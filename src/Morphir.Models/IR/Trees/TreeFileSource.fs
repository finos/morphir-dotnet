namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open Microsoft.Extensions.FileProviders
open Morphir.IR
open Morphir.IR.Files
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>
/// The files that a store reads: their profiles, the limits on their size, and the decoding of their JSON. Nothing
/// here depends on the IR version of a tree.
/// </summary>
module internal TreeFileSource =
    open TreeLayout

    let private error code file message = StoreErrors.error code file message
    let private charge (work: BoundedTreeWorkBudget option) units =
        work |> Option.forall (fun meter -> meter.TryCharge units)

    /// Looking up a file does not open its content. A revision must change when
    /// the source changes and must never identify more than one set of files.
    type SnapshotSource = {
        Revision: unit -> string
        /// Read the lifetime peak charge of constructed discovery indexes without triggering discovery.
        DiscoveryIndexPeakBytes: unit -> int64
        ContainsFile: string -> bool
        FindFile: string -> Morphir.Files.VFile option
        /// The first logical module path in a package after the optional cursor, in ordinal order.
        /// This callback only examines storage metadata.
        NextModule: BoundedTreeWorkBudget option -> int -> string -> string option -> Result<string option, StoreError>
    }


    /// Use the provider's directory metadata and open only the file requested by the reader.
    let private fromFileProviderCore maxEntries cacheBytes revision (provider: IFileProvider) : SnapshotSource =
        let fileAt path =
            let info = provider.GetFileInfo path

            if
                info.Exists
                && not info.IsDirectory
            then
                Some info
            else
                None

        let scanModule retain work pathBudget (prefix: string) after =
            let root = prefix.TrimEnd('/')
            let directories = Stack<string * IEnumerator<IFileInfo>>()
            let mutable remaining = maxEntries
            let mutable nearest = None
            let mutable refusal = None

            let push directory =
                if charge work 1L then
                    directories.Push(
                        directory,
                        provider.GetDirectoryContents(directory).GetEnumerator()
                    )
                else
                    refusal <- Some(StoreErrors.workLimit (Some directory))

            try
                push root

                while directories.Count > 0
                      && Option.isNone refusal do
                    let directory, entries = directories.Peek()

                    if not (charge work 1L) then
                        refusal <- Some(StoreErrors.workLimit (Some directory))
                    elif not (entries.MoveNext()) then
                        directories.Pop()
                        |> ignore

                        entries.Dispose()
                    elif remaining = 0 then
                        refusal <-
                            Some(
                                error
                                    "tree.too_many_files"
                                    (Some prefix)
                                    $"Module lookup exceeded its limit of {maxEntries} directory entries."
                            )
                    else
                        remaining <-
                            remaining
                            - 1

                        let entry = entries.Current
                        let name = entry.Name

                        if entry.IsDirectory then
                            match TreeStems.parseStem name with
                            | Ok(WholeStem canonical) when TreeStems.escapeName canonical = name ->
                                let location =
                                    directory
                                    + "/"
                                    + name

                                if
                                    location.Length
                                    >= pathBudget
                                then
                                    refusal <-
                                        Some(
                                            error
                                                "tree.invalid_module_path"
                                                (Some location)
                                                "The module directory exceeds the path budget."
                                        )
                                else
                                    push location
                            | _ -> ()
                        elif
                            directory
                            <> root
                            && (name = "module.json"
                                || name = "module.yaml"
                                || name = "module.yml")
                        then
                            let logical =
                                directory
                                + "/module"

                            if not (retain logical) then
                                refusal <- Some(error "tree.discovery_cache_limit" (Some prefix) "Module paths exceed the reserved discovery cache.")
                            elif
                                (after
                                 |> Option.forall (fun prior ->
                                     String.CompareOrdinal(logical, prior) > 0
                                 ))
                                && (nearest
                                    |> Option.forall (fun candidate ->
                                        String.CompareOrdinal(logical, candidate) < 0
                                    ))
                            then
                                nearest <- Some logical

                match refusal with
                | Some problem -> Error problem
                | None -> Ok nearest
            finally
                while directories.Count > 0 do
                    let _, entries = directories.Pop()
                    entries.Dispose()

        // One active package. The existing charge is 128 bytes of fixed index
        // overhead plus 160 + 4 * path.Length per distinct logical module path.
        // It models UTF-16 paths, sorted-set nodes and the final reference array
        // concurrently, including a builder that is later refused. It remains
        // the charge after publication, rather than discounting the released set.
        // This is logical storage accounting, not a measurement of CLR allocation.
        // It excludes provider metadata, the traversal stack and enumerators,
        // temporary directory/logical path strings (including duplicate physical
        // spellings), cursor/lookup data and revision/prefix strings held by the
        // source. It does not account for objects awaiting garbage collection.
        // Never publish a partial walk.
        let mutable cached: (string * int * string array) option = None
        let mutable peakIndexBytes = 0L
        let originalRevision = if cacheBytes = 0L then "" else revision ()
        let cacheGate = obj ()
        let nextModule work pathBudget (prefix: string) after =
            if cacheBytes = 0L then scanModule (fun _ -> true) work pathBudget prefix after
            else lock cacheGate (fun () ->
                let changed () =
                    cached <- None
                    Error(error "tree.revision_changed" None "The tree source changed during discovery.")
                if revision () <> originalRevision then changed ()
                else
                    let lookup (paths: string array) =
                        let cost = 1L + int64 (Math.Ceiling(Math.Log(float (paths.Length + 1), 2.0)))
                        if not (charge work cost) then Error(StoreErrors.workLimit (Some prefix))
                        else
                            let index =
                                match after with
                                | None -> 0
                                | Some previous ->
                                    let found = Array.BinarySearch(paths, previous, StringComparer.Ordinal)
                                    if found < 0 then ~~~found else found + 1
                            if index < paths.Length then Ok(Some paths[index]) else Ok None
                    match cached with
                    | Some(package, budget, paths) when package = prefix && budget = pathBudget -> lookup paths
                    | _ ->
                        cached <- None
                        let paths = SortedSet<string>(StringComparer.Ordinal)
                        let mutable retained = 128L
                        // The builder exists even if its fixed overhead is refused.
                        peakIndexBytes <- max peakIndexBytes retained
                        let mutable workRefused = false
                        let retain (path: string) =
                            let cost = 1L + int64 (Math.Ceiling(Math.Log(float (paths.Count + 2), 2.0)))
                            if not (charge work cost) then
                                workRefused <- true
                                false
                            elif paths.Contains path then true
                            else
                                let bytes = 160L + 4L * int64 path.Length
                                if retained > cacheBytes || bytes > cacheBytes - retained then false
                                else
                                    retained <- retained + bytes
                                    paths.Add path |> ignore
                                    peakIndexBytes <- max peakIndexBytes retained
                                    true
                        let result =
                            if cacheBytes < retained then
                                Error(error "tree.discovery_cache_limit" (Some prefix) "The discovery reservation is below its fixed overhead.")
                            else scanModule retain work pathBudget prefix None
                        if revision () <> originalRevision then changed ()
                        elif workRefused then Error(StoreErrors.workLimit (Some prefix))
                        else
                            match result with
                            | Error problem -> Error problem
                            | Ok _ ->
                                // Copy directly into the charged final array. Do not
                                // introduce an intermediate sequence conversion buffer.
                                let ordered = Array.zeroCreate<string> paths.Count
                                paths.CopyTo ordered
                                cached <- Some(prefix, pathBudget, ordered)
                                lookup ordered)

        {
            Revision = revision
            DiscoveryIndexPeakBytes = fun () -> lock cacheGate (fun () -> peakIndexBytes)
            ContainsFile =
                fileAt
                >> Option.isSome
            FindFile =
                fun path ->
                    fileAt path
                    |> Option.map (fun info ->
                        let length = Morphir.Files.VFileProviders.tryGetKnownLength info

                        let source =
                            Morphir.Files.StreamSource(
                                (fun () -> info.CreateReadStream()),
                                ?length = length
                            )

                        Morphir.Files.VFile.ofStream path source
                    )
            NextModule = nextModule
        }


    /// Preserve the public constant-retention scan without a discovery reservation.
    let fromFileProvider maxEntries revision provider =
        fromFileProviderCore maxEntries 0L revision provider

    /// Retain one package's paths within an explicit reservation. Zero selects the scan fallback.
    let fromFileProviderWithDiscoveryCache maxEntries cacheBytes revision provider =
        if cacheBytes < 0L then invalidArg (nameof cacheBytes) "A discovery reservation cannot be negative."
        fromFileProviderCore maxEntries cacheBytes revision provider

    type Profile =
        | Json
        | Yaml

    /// Inspect the physical spellings without opening any of them.
    let private locateSnapshot (source: SnapshotSource) expected logical =
        try
            let found =
                [ Json, ".json"; Yaml, ".yaml"; Yaml, ".yml" ]
                |> List.map (fun (profile, suffix) ->
                    profile,
                    logical
                    + suffix
                )
                |> List.filter (fun (_, location) -> source.ContainsFile location)

            match found with
            | [] -> Error(error "tree.missing_member" (Some logical) "The tree file is missing.")
            | [ profile, location ] when
                expected
                |> Option.exists ((<>) profile)
                ->
                Error(
                    error
                        "tree.unsupported_profile"
                        (Some location)
                        "The file's profile differs from the manifest."
                )
            | [ found ] -> Ok found
            | _ ->
                Error(
                    error
                        "tree.ambiguous_profile"
                        (Some logical)
                        "More than one physical file has this logical path."
                )
        with ex ->
            Error(error "tree.unreadable_file" (Some logical) ex.Message)

    let snapshotLocation work source expected logical =
        if charge work 3L then locateSnapshot source expected logical
        else Error(StoreErrors.workLimit (Some logical))

    let profileOf (location: string) =
        if location.EndsWith(".json", StringComparison.Ordinal) then
            Some Json
        elif
            location.EndsWith(".yaml", StringComparison.Ordinal)
            || location.EndsWith(".yml", StringComparison.Ordinal)
        then
            Some Yaml
        else
            None

    let isManifestName (location: string) =
        location = ManifestFile
        || location = "manifest.yaml"
        || location = "manifest.yml"

    let isNodeFileName (name: string) =
        name = "module.json"
        || name.EndsWith(".type.json", StringComparison.Ordinal)
        || name.EndsWith(".value.json", StringComparison.Ordinal)

    let isNodeFileNameFor (profile: TreeProfile) (name: string) =
        match profile with
        | TreeProfile.Json -> isNodeFileName name
        | TreeProfile.Yaml ->
            name = "module.yaml"
            || name = "module.yml"
            || name.EndsWith(".type.yaml", StringComparison.Ordinal)
            || name.EndsWith(".type.yml", StringComparison.Ordinal)
            || name.EndsWith(".value.yaml", StringComparison.Ordinal)
            || name.EndsWith(".value.yml", StringComparison.Ordinal)

    type internal TreeDecodeContext = {
        Profile: TreeProfile
        MaxFileBytes: int64
        EnforceExpansionBytes: bool
        RemainingTotalBytes: unit -> int64 option
        ConsumeTotalBytes: int64 -> unit
        ContextLimits: ContextLimits
        MaxNodes: int option
        RemainingNodes: unit -> int
        ConsumeNodes: int -> unit
        Cancellation: CancellationToken
    }

    let createDecodeContext
        (profile: TreeProfile)
        (maxFileBytes: int64)
        (maxTotalBytes: int64 option)
        (maxDepth: int)
        (maxNodes: int option)
        (ct: CancellationToken)
        : TreeDecodeContext =
        let mutable remainingNodes = defaultArg maxNodes Int32.MaxValue
        let mutable remainingBytes = defaultArg maxTotalBytes Int64.MaxValue

        {
            Profile = profile
            MaxFileBytes = maxFileBytes
            EnforceExpansionBytes = true
            RemainingTotalBytes = fun () -> if maxTotalBytes.IsSome then Some remainingBytes else None
            ConsumeTotalBytes =
                fun used ->
                    if maxTotalBytes.IsSome then
                        remainingBytes <- max 0L (remainingBytes - max 0L used)
            ContextLimits = ContextLimits.draft1.WithMaxDepth maxDepth
            MaxNodes = maxNodes
            RemainingNodes = fun () -> remainingNodes
            ConsumeNodes =
                fun used ->
                    if maxNodes.IsSome then
                        remainingNodes <- max 0 (remainingNodes - used)
            Cancellation = ct
        }

    /// Legacy JSON-only context that enforces MaxFileBytes on stored file bytes only,
    /// matching historical DocumentTreeStore.Read semantics without capping expanded output.
    let createLegacyDecodeContext (maxFileBytes: int64) : TreeDecodeContext =
        {
            Profile = TreeProfile.Json
            MaxFileBytes = maxFileBytes
            EnforceExpansionBytes = false
            RemainingTotalBytes = fun () -> None
            ConsumeTotalBytes = fun _ -> ()
            ContextLimits = ContextLimits.draft1
            MaxNodes = None
            RemainingNodes = fun () -> Int32.MaxValue
            ConsumeNodes = fun _ -> ()
            Cancellation = CancellationToken.None
        }

    let readFileCap (ctx: TreeDecodeContext) : int64 =
        match ctx.RemainingTotalBytes () with
        | Some remaining -> min ctx.MaxFileBytes remaining
        | None -> ctx.MaxFileBytes

    /// The bytes of a file, refusing one over the limit after at most one lookahead byte.
    let contentBytes
        (limit: int64)
        (location: string)
        (file: Morphir.Files.VFile)
        : Result<byte array, StoreError> =
        let tooLarge () =
            error "tree.file_too_large" (Some location) $"The file has more than {limit} bytes."

        match file.Content with
        | None -> Ok [||]
        | Some content when
            Morphir.Files.VFileContent.tryGetKnownLength content
            |> Option.exists (fun length -> length > limit)
            ->
            Error(tooLarge ())
        | Some content ->
            match Morphir.Files.VFileContent.openRead content with
            | Error problem ->
                Error(
                    error
                        "tree.unreadable_file"
                        (Some location)
                        (Morphir.Files.ContentError.describe problem)
                )
            | Ok opened ->
                try
                    use stream = opened
                    use buffer = new MemoryStream()

                    let readSize remaining =
                        int (min 81919L (max 0L remaining))
                        + 1

                    let chunk = Array.zeroCreate<byte> (readSize limit)
                    let mutable remaining = limit
                    let mutable over = false
                    let mutable count = stream.Read(chunk, 0, chunk.Length)

                    while count > 0
                          && not over do
                        if int64 count > remaining then
                            over <- true
                        else
                            buffer.Write(chunk, 0, count)

                            remaining <-
                                remaining
                                - int64 count

                            count <- stream.Read(chunk, 0, readSize remaining)

                    if over then Error(tooLarge ()) else Ok(buffer.ToArray())
                with ex ->
                    Error(error "tree.unreadable_file" (Some location) ex.Message)

    let snapshotBytes limit (source: SnapshotSource) location (work: BoundedTreeWorkBudget option) =
        let exhausted () = Error(StoreErrors.workLimit (Some location))
        try
            if not (charge work 1L) then exhausted ()
            else
                let cap = work |> Option.map (fun meter -> meter.CapBytes limit) |> Option.defaultValue limit
                if cap <= 0L then exhausted ()
                else
                    match source.FindFile location with
                    | None -> Error(error "tree.missing_member" (Some location) "The tree file is missing.")
                    | Some file ->
                        match contentBytes cap location file with
                        | Ok bytes when charge work (BoundedTreeWorkBudget.Blocks(int64 bytes.Length)) -> Ok bytes
                        | Ok _ -> exhausted ()
                        | Error problem when cap < limit && problem.Code = "tree.file_too_large" -> exhausted ()
                        | Error problem -> Error problem
        with ex ->
            Error(error "tree.unreadable_file" (Some location) ex.Message)

    /// <summary>
    /// Decodes a file's JSON or YAML into its plain expanded <see cref="JsonElement"/> under the active profile,
    /// per-file and aggregate byte, depth, node, and cancellation limits. Gives the expanded root and whether the
    /// file was compacted, with structured diagnostics preserving member locations separately from messages.
    /// </summary>
    let decodeRawDetailed
        (ctx: TreeDecodeContext)
        (location: string)
        (bytes: byte array)
        : Result<JsonElement * bool, TreeDiagnostic list> =
        ctx.Cancellation.ThrowIfCancellationRequested()

        let expandFile (rawNodes: int) (plainBytes: int64) (file: MorphirFile<JsonElement>) =
            match file.Context with
            | None ->
                match ctx.RemainingTotalBytes () with
                | Some remaining when plainBytes > remaining ->
                    Error [
                        StoreErrors.diagnostic
                            "tree.file_too_large"
                            (Some location)
                            "The decoded tree files exceed the aggregate byte limit."
                    ]
                | _ ->
                    ctx.ConsumeTotalBytes plainBytes
                    ctx.ConsumeNodes rawNodes
                    Ok(file.Document, false)
            | Some context ->
                let expandLimit = ctx.MaxNodes |> Option.map (fun _ -> ctx.RemainingNodes ())

                let byteLimit =
                    if not ctx.EnforceExpansionBytes then
                        None
                    else
                        let perFile =
                            if ctx.MaxFileBytes < Int64.MaxValue then
                                Some ctx.MaxFileBytes
                            else
                                None

                        match perFile, ctx.RemainingTotalBytes () with
                        | Some a, Some b -> Some(min a b)
                        | Some a, None -> Some a
                        | None, Some b -> Some b
                        | None, None -> None

                ContextExpansion.expandCountedDetailed
                    ctx.ContextLimits
                    byteLimit
                    expandLimit
                    ctx.Cancellation
                    (MorphirFile.familyOf file.Document)
                    location
                    context
                    file.Document
                |> Result.map (fun (expanded, expandedNodes, expandedBytes) ->
                    ctx.ConsumeTotalBytes(max plainBytes expandedBytes)
                    ctx.ConsumeNodes(max rawNodes expandedNodes)
                    expanded, true
                )

        match ctx.Profile with
        | TreeProfile.Json ->
            let encoded = ReadOnlyMemory bytes
            let nodeLimit = ctx.MaxNodes |> Option.map (fun _ -> ctx.RemainingNodes ())

            ContextExpansion.countJsonNodesCancellable
                ctx.ContextLimits.MaxDepth
                nodeLimit
                ctx.Cancellation
                location
                encoded
            |> Result.mapError (List.map StoreErrors.ofStoreError)
            |> Result.bind (fun rawNodes ->
                MorphirFile.readEnvelope ctx.ContextLimits location encoded
                |> Result.mapError (List.map StoreErrors.ofStoreError)
                |> Result.bind (expandFile rawNodes (int64 bytes.Length))
            )
        | TreeProfile.Yaml ->
            let yamlLimit =
                ctx.MaxNodes
                |> Option.map (fun _ -> ctx.RemainingNodes ())
                |> Option.defaultValue Int32.MaxValue

            TreeYaml.readCountedDetailed
                true
                ctx.ContextLimits.MaxDepth
                yamlLimit
                ctx.Cancellation
                location
                bytes
            |> Result.bind (fun (root, rawNodes, jsonBytes) ->
                MorphirFile.readEnvelopeRoot ContextOrder.WholeFile ctx.ContextLimits location root
                |> Result.mapError (List.map StoreErrors.ofStoreError)
                |> Result.bind (expandFile rawNodes (max (int64 bytes.Length) jsonBytes))
            )

    let decodeWithDetailed
        (ctx: TreeDecodeContext)
        (location: string)
        (read: JsonElement -> Result<'a, V4JsonError>)
        (bytes: byte array)
        : Result<'a * bool, TreeDiagnostic list> =
        decodeRawDetailed ctx location bytes
        |> Result.bind (fun (document, compacted) ->
            read document
            |> Result.map (fun value -> value, compacted)
            |> Result.mapError (fun e -> [
                StoreErrors.located e.Rule (Some location) e.Location e.Message
            ])
        )

    let readFileWithDetailed
        (ctx: TreeDecodeContext)
        (location: string)
        (file: Morphir.Files.VFile)
        (read: JsonElement -> Result<'a, V4JsonError>)
        : Result<'a * bool, TreeDiagnostic list> =
        ctx.Cancellation.ThrowIfCancellationRequested()

        contentBytes (readFileCap ctx) location file
        |> Result.mapError (StoreErrors.ofStoreError >> List.singleton)
        |> Result.bind (decodeWithDetailed ctx location read)

    /// <summary>
    /// Decodes a single YAML document (plain or contextual) into its expanded <see cref="JsonElement"/>
    /// under explicit byte, depth, node, and cancellation limits.
    /// </summary>
    let readSingleYamlDocument
        (file: string)
        (bytes: ReadOnlyMemory<byte>)
        (maxBytes: int64)
        (maxDepth: int)
        (maxNodes: int)
        (ct: CancellationToken)
        : Result<JsonElement, TreeDiagnostic list> =
        ct.ThrowIfCancellationRequested()

        if int64 bytes.Length > maxBytes then
            Error [ StoreErrors.diagnostic "tree.file_too_large" (Some file) $"The file has more than {maxBytes} bytes." ]
        else
            let ctx = createDecodeContext TreeProfile.Yaml maxBytes (Some maxBytes) maxDepth (Some maxNodes) ct
            decodeRawDetailed ctx file (bytes.ToArray()) |> Result.map fst

    /// <summary>
    /// The exact declared <c>formatVersion</c> text of an already-decoded manifest object.
    /// </summary>
    let declaredFormatVersion (root: JsonElement) : string =
        let mutable version = Unchecked.defaultof<JsonElement>

        if root.ValueKind = JsonValueKind.Object && root.TryGetProperty("formatVersion", &version) then
            match version.ValueKind with
            | JsonValueKind.String -> nonNull (version.GetString())
            | _ -> version.GetRawText()
        else
            ""

    /// <summary>
    /// Decodes a file's JSON, skipping a UTF-8 byte order mark: a plain file as it is, and a compacted file
    /// (<c>ir/name-compaction</c>) after its context is checked and its document expanded, for the IR family that the
    /// document's own <c>formatVersion</c> gives. Gives the decoded value and whether the file was compacted.
    /// </summary>
    let decode
        (location: string)
        (read: JsonElement -> Result<'a, V4JsonError>)
        (bytes: byte array)
        : Result<'a * bool, StoreError list> =
        let ctx = createLegacyDecodeContext Int64.MaxValue

        decodeWithDetailed ctx location read bytes
        |> Result.mapError StoreErrors.toStoreErrors

    let readFile limit location file read =
        contentBytes limit location file
        |> Result.mapError List.singleton
        |> Result.bind (decode location read)

    /// <summary>
    /// Whether a manifest states a format version of major 3, which the v3 rules read: the string <c>"3"</c> or one
    /// that starts with <c>"3."</c>, or a number from 3 up to 4, such as <c>3</c> or <c>3.1</c>. The v3 reader refuses
    /// every one of them but <c>"3.1.0"</c>, and none is sent to the v4 reader.
    /// </summary>
    let isClassicManifest (root: JsonElement) =
        MorphirFile.familyOf root = IrFamily.Classic3

    let errorsOf (results: Result<'a, 'e list> list) =
        results
        |> List.collect (
            function
            | Error errors -> errors
            | Ok _ -> []
        )

    let valuesOf (results: Result<'a, 'e list> list) =
        results
        |> List.choose (
            function
            | Ok value -> Some value
            | Error _ -> None
        )

    let one (result: Result<'a, StoreError>) =
        result
        |> Result.mapError List.singleton

    /// <summary>
    /// The tree files of a tree, by location: its active profile, its one manifest, and every other file of that
    /// profile. Refuses too many files, no manifest, ambiguous manifests (including <c>manifest.ion</c>), or files of
    /// a disallowed or mixed profile.
    /// </summary>
    let discoverWithOptions
        (checkIonManifest: bool)
        (maxFiles: int)
        (selection: TreeProfileSelection)
        (files: Morphir.Files.VFileTree)
        : Result<TreeProfile * (string * Morphir.Files.VFile) * (string * Morphir.Files.VFile) list, StoreError list> =
        let allFiles =
            Morphir.Files.VFileTree.toFileMap files
            |> Map.toList

        let treeFiles =
            allFiles
            |> List.choose (fun (location, file) ->
                profileOf location
                |> Option.map (fun profile -> location, file, profile)
            )

        if treeFiles.Length > maxFiles then
            Error [
                error
                    "tree.too_many_files"
                    None
                    $"The tree has {treeFiles.Length} files, and a read accepts at most {maxFiles}."
            ]
        else
            let ionManifest =
                if checkIonManifest then
                    allFiles
                    |> List.tryFind (fun (location, _) -> location = "manifest.ion")
                else
                    None

            let manifests =
                treeFiles
                |> List.filter (fun (location, _, _) -> isManifestName location)

            let manifestNames =
                (manifests |> List.map (fun (location, _, _) -> location))
                @ (match ionManifest with
                   | Some(location, _) -> [ location ]
                   | None -> [])
                |> List.sortWith (fun left right -> String.CompareOrdinal(left, right))

            let jsonFiles =
                treeFiles
                |> List.filter (fun (_, _, profile) -> profile = Json)

            let yamlFiles =
                treeFiles
                |> List.filter (fun (_, _, profile) -> profile = Yaml)

            match manifests, ionManifest with
            | [], None ->
                let expected =
                    match selection with
                    | TreeProfileSelection.JsonOnly -> "manifest.json"
                    | TreeProfileSelection.YamlOnly -> "manifest.yaml"
                    | TreeProfileSelection.Any -> "manifest.json or manifest.yaml"

                Error [
                    error "tree.missing_manifest" None $"The tree has no {expected} at its root."
                ]
            | [], Some(location, _) ->
                Error [
                    error
                        "tree.unsupported_profile"
                        (Some location)
                        "This store reads JSON and YAML trees, and the manifest is in the Ion profile."
                ]
            | _, _ when manifestNames.Length > 1 ->
                let named = String.concat " and " manifestNames

                Error [
                    error
                        "tree.ambiguous_profile"
                        None
                        $"The tree has more than one manifest, {named}; a reader does not choose one."
                ]
            | [ (_, _, Yaml) ], None when selection = TreeProfileSelection.JsonOnly ->
                Error(
                    yamlFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This store reads the JSON profile of a tree, and the file is in the YAML profile."
                    )
                )
            | [ (_, _, Json) ], None when selection = TreeProfileSelection.YamlOnly ->
                Error(
                    jsonFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This store reads the YAML profile of a tree, and the file is in the JSON profile."
                    )
                )
            | [ (_, _, Json) ], None when not yamlFiles.IsEmpty ->
                Error(
                    yamlFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This store reads the JSON profile of a tree, and the file is in the YAML profile."
                    )
                )
            | [ (_, _, Yaml) ], None when not jsonFiles.IsEmpty ->
                Error(
                    jsonFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This tree uses the YAML profile, and the file is in the JSON profile."
                    )
                )
            | [ (manifestLocation, manifestFile, profile) ], None ->
                let others =
                    treeFiles
                    |> List.filter (fun (location, _, _) ->
                        location
                        <> manifestLocation
                    )
                    |> List.map (fun (location, file, _) -> location, file)

                let treeProfile =
                    match profile with
                    | Json -> TreeProfile.Json
                    | Yaml -> TreeProfile.Yaml

                Ok(treeProfile, (manifestLocation, manifestFile), others)
            | _ ->
                Error [
                    error
                        "tree.ambiguous_profile"
                        None
                        "The tree has more than one manifest; a reader does not choose one."
                ]

    /// <summary>
    /// The tree files of a tree, by location: its one JSON manifest and every other JSON file. A tree with too many
    /// files, no manifest, two manifests or a YAML file is refused.
    /// </summary>
    let discover
        (options: ReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<(string * Morphir.Files.VFile) * (string * Morphir.Files.VFile) list, StoreError list> =
        discoverWithOptions false options.MaxFiles TreeProfileSelection.JsonOnly files
        |> Result.map (fun (_, manifest, others) -> manifest, others)
