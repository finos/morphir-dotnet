namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.IO
open System.Text.Json
open Morphir.IR
open Morphir.IR.V4
open Morphir.Json.V4

/// <summary>How a store compacts the names of the files it writes.</summary>
type CompactMode =
    /// <summary>Names are written through a name-compaction context (<c>ir/name-compaction</c>).</summary>
    | Names

/// <summary>How a store writes a tree.</summary>
type WriteOptions = {
    /// <summary>
    /// The longest physical path, in characters from the tree's root with the extension, that the store writes. A
    /// longer path has its stem cut, and the module file records the cut in <c>fileNames</c> (decision 0012).
    /// </summary>
    PathBudget: int
    /// <summary>The compaction of names, or none for plain files.</summary>
    Compact: CompactMode option
}

/// <summary>The limits of a read.</summary>
type ReadOptions = {
    /// <summary>The most tree files (JSON or YAML) that a read accepts.</summary>
    MaxFiles: int
    /// <summary>The most bytes of one file that a read accepts.</summary>
    MaxFileBytes: int64
}

/// <summary>What a read found besides the tree.</summary>
type ReadReport = {
    /// <summary>Content that the read dropped, such as the manifest's <c>version</c> and <c>created</c>.</summary>
    Notices: TreeNotice list
    /// <summary>The documents whose files were compacted. A plain tree has none.</summary>
    CompactedFiles: DocumentAddress list
}

/// <summary>Why a store did not read or write a tree: a stable code, the file it concerns, and a message.</summary>
type StoreError =
    {
        /// <summary>
        /// The stable code: a <c>tree.</c> code for a rule of the tree, such as <c>tree.stem_collision</c>, or the
        /// rule of the JSON codec, such as <c>unknown_member</c>.
        /// </summary>
        Code: string
        /// <summary>The file, as a physical path from the tree's root, or none for an error of the whole tree.</summary>
        File: string option
        /// <summary>What is wrong.</summary>
        Message: string
    }

    /// <summary>The code, the file and the message.</summary>
    override this.ToString() =
        match this.File with
        | Some file -> $"{this.Code}: {file}: {this.Message}"
        | None -> $"{this.Code}: {this.Message}"

/// <summary>A tree that a store reads or writes: a v4 tree or a classic v3 tree.</summary>
type StoredTree =
    /// <summary>A v4 tree.</summary>
    | V4Tree of V4DocumentTree
    /// <summary>A classic v3 tree, with the attributes as <c>ClassicJson</c> reads them.</summary>
    | ClassicTree of ClassicDocumentTree<JsonElement, JsonElement>

/// <summary>Functions for <see cref="WriteOptions"/>.</summary>
[<RequireQualifiedAccess>]
module WriteOptions =
    /// <summary>The path budget of a tree when the caller gives none: 4000 characters (decision 0012).</summary>
    [<Literal>]
    let DefaultPathBudget = 4000

    /// <summary>A path budget of 4000 and no compaction.</summary>
    let defaults: WriteOptions = {
        PathBudget = DefaultPathBudget
        Compact = None
    }

/// <summary>Functions for <see cref="ReadOptions"/>.</summary>
[<RequireQualifiedAccess>]
module ReadOptions =
    /// <summary>At most a million files of at most 256 MiB each.</summary>
    let defaults: ReadOptions = {
        MaxFiles = 1_000_000
        MaxFileBytes =
            256L
            * 1024L
            * 1024L
    }

/// <summary>
/// Maps a document tree to and from the files of the parent's document-tree specifications, on a
/// <see cref="VFileTree"/>. The store makes no file-system call: the same tree comes from or goes to the disk, an
/// embedded resource, an archive or memory.
/// </summary>
/// <remarks>
/// <para>
/// A v4 tree is written in the JSON profile: <c>manifest.json</c>, <c>module.json</c> for each module, and
/// <c>&lt;stem&gt;.type.json</c> and <c>&lt;stem&gt;.value.json</c> for each type and value, below
/// <c>pkg/&lt;package&gt;/</c> or <c>deps/&lt;package&gt;/@/</c>. Each stem is the escape of its name
/// (<see cref="TreeStems"/>). A path longer than the path budget has its stem cut, and its module file maps the
/// name to the stem in <c>fileNames</c>. A top-level <c>$meta</c> is ignored on read and never written.
/// </para>
/// <para>
/// A refused write or read gives every error it finds and no tree. The codes of the tree: those of
/// <c>V4DocumentTree.join</c>; <c>tree.stem_collision</c>, two documents whose files would have one path on a file
/// system that ignores case; <c>invalid_distribution_shape</c>, a module with annotations, which a 4.0.0 module
/// file cannot hold, or a path that no cut fits in the budget; <c>tree.missing_manifest</c>;
/// <c>tree.ambiguous_profile</c>, a JSON and a YAML manifest; <c>tree.unsupported_profile</c>, a YAML file, which
/// this store does not read; <c>tree.outside_package</c>, a file below <c>pkg/</c> of another package;
/// <c>tree.outside_tree</c>, a module, type or value file outside <c>pkg/</c> and <c>deps/</c>;
/// <c>tree.versioned_dependency</c>, a dependency segment that carries a version;
/// <c>tree.misplaced_file</c>; <c>tree.unknown_file</c>; <c>tree.invalid_stem</c>; <c>tree.path_mismatch</c>, a
/// module file whose path is not its directory's; <c>tree.name_mismatch</c>, a file whose stem does not spell
/// its name; <c>tree.duplicate_document</c>; <c>tree.over_budget</c>, a path longer than the manifest's budget;
/// <c>tree.too_many_files</c>; <c>tree.file_too_large</c>; <c>tree.unreadable_file</c>; <c>invalid_json</c>;
/// <c>tree.unsupported_option</c>; and <c>tree.classic_not_supported</c>, as classic v3 trees come later. A
/// codec error keeps the codec's rule as its code.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module DocumentTreeStore =
    /// The extension of the JSON profile.
    [<Literal>]
    let private Extension = ".json"

    [<Literal>]
    let private ManifestFile = "manifest.json"

    /// <summary>
    /// The physical path of each document of a tree that <c>write</c> made, set on the tree it returns.
    /// </summary>
    let locations: Morphir.Files.Property<Map<DocumentAddress, string>> =
        Morphir.Files.Property.create "morphir.document-tree.locations"

    let private error code file message : StoreError = {
        Code = code
        File = file
        Message = message
    }

    let private classicNotSupported =
        error
            "tree.classic_not_supported"
            None
            "This store reads and writes v4 trees; classic v3 trees come with its next version."

    let private stems (Path names) =
        names
        |> List.map TreeStems.escapeName

    /// The module that holds an address of a type or value, or the address itself.
    let private moduleOf (address: DocumentAddress) =
        match address with
        | Own(TypeDef(path, _))
        | Own(ValueDef(path, _)) -> Own(Module path)
        | Dependency(package, TypeDef(path, _))
        | Dependency(package, ValueDef(path, _)) -> Dependency(package, Module path)
        | Manifest
        | Own(Module _)
        | Dependency(_, Module _) -> address

    /// The name and the logical suffix of a type or value address.
    let private memberOf (address: DocumentAddress) =
        match address with
        | Own(TypeDef(_, name))
        | Dependency(_, TypeDef(_, name)) -> Some(name, ".type")
        | Own(ValueDef(_, name))
        | Dependency(_, ValueDef(_, name)) -> Some(name, ".value")
        | Manifest
        | Own(Module _)
        | Dependency(_, Module _) -> None

    /// The directory of an address's file, with a trailing slash, or "" for the manifest.
    let private directoryOf (own: PackageName) (address: DocumentAddress) =
        let modulePath (inner: ModuleDocumentAddress) =
            match inner with
            | Module(ModulePath path)
            | TypeDef(ModulePath path, _)
            | ValueDef(ModulePath path, _) -> stems path

        let segments =
            match address with
            | Manifest -> []
            | Own inner ->
                let (PackageName package) = own

                "pkg"
                :: stems package
                @ modulePath inner
            | Dependency(PackageName package, inner) ->
                "deps"
                :: stems package
                @ TreeStems.BareVersionSegment
                  :: modulePath inner

        segments
        |> List.map (fun segment ->
            segment
            + "/"
        )
        |> String.concat ""

    let private byOrdinal (paths: ('a * string) list) =
        paths
        |> List.sortWith (fun (_, first) (_, second) -> String.CompareOrdinal(first, second))

    // Words of names, for messages that name two names with one spelling.
    let private words (Name parts) =
        "["
        + (parts
           |> List.map (fun part -> $"'{part}'")
           |> String.concat ", ")
        + "]"

    let private moduleWords (ModulePath(Path names)) =
        names
        |> List.map words
        |> String.concat "/"

    let private describe (address: DocumentAddress) =
        let inner, package =
            match address with
            | Manifest -> None, None
            | Own inner -> Some inner, None
            | Dependency(PackageName package, inner) -> Some inner, Some package

        let ofPackage =
            match package with
            | Some(Path names) ->
                " of the dependency "
                + (names
                   |> List.map words
                   |> String.concat "/")
            | None -> ""

        match inner with
        | None -> "the manifest"
        | Some(Module path) -> $"the module {moduleWords path}{ofPackage}"
        | Some(TypeDef(path, name)) ->
            $"the type {words name} of the module {moduleWords path}{ofPackage}"
        | Some(ValueDef(path, name)) ->
            $"the value {words name} of the module {moduleWords path}{ofPackage}"

    /// Documents whose files would have one path on a file system that ignores case.
    let private collisions (paths: (DocumentAddress * string) list) =
        paths
        |> List.groupBy (fun (_, path) -> path.ToUpperInvariant())
        |> List.filter (fun (_, group) -> group.Length > 1)
        |> List.map (fun (_, group) ->
            let path = snd group.Head

            let named =
                group
                |> List.map (
                    fst
                    >> describe
                )
                |> String.concat " and "

            error
                "tree.stem_collision"
                (Some path)
                $"The names of {named} have one file, {path}, on a file system that ignores case; each document needs a file of its own."
        )

    /// The physical path of each document, with the stems that the budget cut by module, or every error.
    let private layout
        (budget: int)
        (own: PackageName)
        (whole: (DocumentAddress * string) list)
        : Result<Map<DocumentAddress, string> * Map<DocumentAddress, Map<Name, string>>, StoreError list> =
        let overBudget address path =
            error
                "invalid_distribution_shape"
                (Some path)
                $"The path of {describe address} has {String.length path} characters, and the path budget is {budget}; only a type or value stem can be cut."

        let tooLongModules =
            whole
            |> List.filter (fun (address, path) ->
                Option.isNone (memberOf address)
                && path.Length > budget
            )

        let blocked =
            tooLongModules
            |> List.map fst
            |> Set.ofList

        let placed =
            whole
            |> List.choose (fun (address, path) ->
                match memberOf address with
                | _ when
                    path.Length
                    <= budget
                    ->
                    Some(Ok(address, path, None))
                | None -> None
                | Some _ when Set.contains (moduleOf address) blocked -> None
                | Some(name, suffix) ->
                    let directory = directoryOf own address

                    let ending =
                        suffix
                        + Extension

                    match
                        TreeStems.cut
                            (budget
                             - directory.Length
                             - ending.Length)
                            name
                    with
                    | Ok stem ->
                        Some(
                            Ok(
                                address,
                                directory
                                + stem
                                + ending,
                                Some(name, stem)
                            )
                        )
                    | Error reason ->
                        Some(
                            Error(
                                error
                                    "invalid_distribution_shape"
                                    (Some path)
                                    $"The path of {describe address} has {path.Length} characters, and no cut stem fits the path budget of {budget}: {reason}"
                            )
                        )
            )

        let errors =
            (tooLongModules
             |> List.map (fun (address, path) -> overBudget address path))
            @ (placed
               |> List.choose (
                   function
                   | Error e -> Some e
                   | Ok _ -> None
               ))

        let chosen =
            placed
            |> List.choose (
                function
                | Ok found -> Some found
                | Error _ -> None
            )

        let cutCollisions =
            chosen
            |> List.map (fun (address, path, _) -> address, path)
            |> collisions
            |> List.map (fun found ->
                { found with
                    Code = "invalid_distribution_shape"
                    Message = $"A cut stem collides with another stem: {found.Message}"
                }
            )

        match
            errors
            @ cutCollisions
        with
        | [] ->
            let paths =
                chosen
                |> List.map (fun (address, path, _) -> address, path)
                |> Map.ofList

            let fileNames =
                chosen
                |> List.choose (fun (address, _, cut) ->
                    cut
                    |> Option.map (fun entry -> moduleOf address, entry)
                )
                |> List.groupBy fst
                |> List.map (fun (moduleAddress, entries) ->
                    moduleAddress, Map.ofList (List.map snd entries)
                )
                |> Map.ofList

            Ok(paths, fileNames)
        | found -> Error found

    let private encoded path (written: Result<byte array, V4WriteError>) =
        match written with
        | Ok bytes -> Ok(path, V4TreeFiles.toText bytes)
        | Error e -> Error(error e.Rule (Some path) $"{e.Location}: {e.Message}")

    let private fileName (path: string) =
        path.Substring(
            path.LastIndexOf '/'
            + 1
        )

    let private writeV4
        (options: WriteOptions)
        (tree: V4DocumentTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        let own = tree.Manifest.Package
        let documents = Map.toList tree.Documents

        let whole =
            (Manifest, ManifestFile)
            :: (documents
                |> List.map (fun (address, _) ->
                    address,
                    DocumentAddress.logicalPath own address
                    + Extension
                ))

        let wholePath address =
            whole
            |> List.tryFind (fun (candidate, _) -> candidate = address)
            |> Option.map snd

        let joinErrors =
            match V4DocumentTree.join tree with
            | Ok _ -> []
            | Error errors ->
                errors
                |> List.map (fun (e: TreeError) ->
                    error
                        e.Code
                        (e.Address
                         |> Option.bind wholePath)
                        e.Message
                )

        let annotationErrors =
            documents
            |> List.choose (fun (address, document) ->
                match document with
                | ModuleDoc m when not m.Annotations.IsEmpty ->
                    Some(
                        error
                            "invalid_distribution_shape"
                            (wholePath address)
                            $"Module annotations cannot be written to a document tree: {describe address} has {m.Annotations.Length}, and a 4.0.0 module file has no place for them."
                    )
                | _ -> None
            )

        let budgetErrors =
            if options.PathBudget < V4TreeFiles.MinimumPathBudget then
                [
                    error
                        "tree.unsupported_option"
                        None
                        $"The path budget is {options.PathBudget}; a tree's budget is at least {V4TreeFiles.MinimumPathBudget}."
                ]
            else
                []

        match
            budgetErrors
            @ joinErrors
            @ annotationErrors
            @ collisions whole
        with
        | _ :: _ as errors -> Error errors
        | [] ->
            layout options.PathBudget own whole
            |> Result.bind (fun (paths, fileNames) ->
                let pathOf address = paths[address]

                let files =
                    encoded
                        ManifestFile
                        (V4TreeFiles.writeManifest options.PathBudget tree.Manifest)
                    :: (documents
                        |> List.map (fun (address, document) ->
                            let path = pathOf address

                            match address, document with
                            | (Own(Module modulePath) | Dependency(_, Module modulePath)),
                              ModuleDoc m ->
                                let cut =
                                    Map.tryFind address fileNames
                                    |> Option.defaultValue Map.empty

                                let entries =
                                    m.TypeNames
                                    @ m.ValueNames
                                    |> List.choose (fun name ->
                                        Map.tryFind name cut
                                        |> Option.map (fun stem -> name, stem)
                                    )

                                encoded path (V4TreeFiles.writeModule modulePath m entries)
                            | _, TypeDoc t ->
                                let name, _ = (memberOf address).Value
                                encoded path (V4TreeFiles.writeType name t)
                            | _, ValueDoc v ->
                                let name, _ = (memberOf address).Value
                                encoded path (V4TreeFiles.writeValue name v)
                            // join refuses a document that does not fit its address, so this is not reached.
                            | _, ModuleDoc _ ->
                                Error(
                                    error
                                        "tree.mismatched_document"
                                        (Some path)
                                        $"The document at {address} does not fit its address."
                                )
                        ))

                match
                    files
                    |> List.choose (
                        function
                        | Error e -> Some e
                        | Ok _ -> None
                    )
                with
                | _ :: _ as errors -> Error errors
                | [] ->
                    let written =
                        files
                        |> List.choose (
                            function
                            | Ok file -> Some file
                            | Error _ -> None
                        )

                    // The manifest first, then every document in the ordinal order of its logical path.
                    let order =
                        ManifestFile
                        :: (documents
                            |> List.map (fun (address, _) ->
                                address, DocumentAddress.logicalPath own address
                            )
                            |> byOrdinal
                            |> List.map (fun (address, _) -> pathOf address))

                    let byPath = Map.ofList written

                    Morphir.Files.VFileTree.fromFileMap (
                        written
                        |> List.map (fun (path, text) ->
                            path, Morphir.Files.VFile.ofText (fileName path) text
                        )
                        |> Map.ofList
                    )
                    |> Morphir.Files.VFileTree.set
                        Morphir.Files.VFileTree.declaredOrder
                        (order
                         |> List.filter byPath.ContainsKey)
                    |> Morphir.Files.VFileTree.set locations (Map.add Manifest ManifestFile paths)
                    |> Ok
            )

    /// <summary>
    /// Writes a tree to files. The tree must join (<c>V4DocumentTree.join</c>); a module with annotations, two
    /// documents whose files would have one path, and a path that no cut fits are refused, before any file is
    /// encoded. The returned tree has the manifest first and then each document in the ordinal order of its logical
    /// path as its declared order, and <see cref="locations"/> gives each document's file. A refused write gives
    /// every error and no file. C# calls this <c>Write</c>.
    /// </summary>
    [<CompiledName("Write")>]
    let write
        (options: WriteOptions)
        (tree: StoredTree)
        : Result<Morphir.Files.VFileTree, StoreError list> =
        match options.Compact, tree with
        | Some _, _ ->
            Error [
                error
                    "tree.unsupported_option"
                    None
                    "Compacted trees are written by a later version of this store."
            ]
        | None, V4Tree v4 -> writeV4 options v4
        | None, ClassicTree _ -> Error [ classicNotSupported ]

    // Reading.

    type private Profile =
        | Json
        | Yaml

    let private profileOf (location: string) =
        if location.EndsWith(".json", StringComparison.Ordinal) then
            Some Json
        elif
            location.EndsWith(".yaml", StringComparison.Ordinal)
            || location.EndsWith(".yml", StringComparison.Ordinal)
        then
            Some Yaml
        else
            None

    let private isManifestName (location: string) =
        location = ManifestFile
        || location = "manifest.yaml"
        || location = "manifest.yml"

    let private isNodeFileName (name: string) =
        name = "module.json"
        || name.EndsWith(".type.json", StringComparison.Ordinal)
        || name.EndsWith(".value.json", StringComparison.Ordinal)

    /// The bytes of a file, refusing one over the limit without reading past it.
    let private contentBytes
        (limit: int64)
        (location: string)
        (file: Morphir.Files.VFile)
        : Result<byte array, StoreError> =
        let tooLarge () =
            error "tree.file_too_large" (Some location) $"The file has more than {limit} bytes."

        match file.Content with
        | None -> Ok [||]
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
                    let chunk = Array.zeroCreate<byte> 81920
                    let mutable total = 0L
                    let mutable over = false
                    let mutable count = stream.Read(chunk, 0, chunk.Length)

                    while count > 0
                          && not over do
                        total <-
                            total
                            + int64 count

                        if total > limit then
                            over <- true
                        else
                            buffer.Write(chunk, 0, count)
                            count <- stream.Read(chunk, 0, chunk.Length)

                    if over then Error(tooLarge ()) else Ok(buffer.ToArray())
                with ex ->
                    Error(error "tree.unreadable_file" (Some location) ex.Message)

    /// Decodes a file's JSON, skipping a UTF-8 byte order mark.
    let private decode
        (location: string)
        (read: JsonElement -> Result<'a, V4JsonError>)
        (bytes: byte array)
        : Result<'a, StoreError> =
        let start =
            if
                bytes.Length
                >= 3
                && bytes[0] = 0xEFuy
                && bytes[1] = 0xBBuy
                && bytes[2] = 0xBFuy
            then
                3
            else
                0

        try
            use document =
                JsonDocument.Parse(
                    ReadOnlyMemory(
                        bytes,
                        start,
                        bytes.Length
                        - start
                    ),
                    JsonDocumentOptions(MaxDepth = V4JsonDecoding.MaxDepth)
                )

            read document.RootElement
            |> Result.mapError (fun e -> error e.Rule (Some location) $"{e.Location}: {e.Message}")
        with :? JsonException as problem ->
            Error(error "invalid_json" (Some location) problem.Message)

    let private readFile limit location file read =
        contentBytes limit location file
        |> Result.bind (decode location read)

    /// Whether a manifest states a classic v3 format version.
    let private isClassicManifest (root: JsonElement) =
        let mutable version = Unchecked.defaultof<JsonElement>

        root.ValueKind = JsonValueKind.Object
        && root.TryGetProperty("formatVersion", &version)
        && (
            match version.ValueKind with
            | JsonValueKind.String ->
                (nonNull (version.GetString())).StartsWith("3.", StringComparison.Ordinal)
            | JsonValueKind.Number -> version.GetRawText() = "3"
            | _ -> false
        )

    type private NodeKind =
        | TypeNode
        | ValueNode

    /// Where a file of a module is: its scope, its module, and what it is.
    type private Placed =
        | ModuleFile of DocumentAddress
        | NodeFile of
            scope: (ModuleDocumentAddress -> DocumentAddress) *
            modulePath: ModulePath *
            stem: string *
            kind: NodeKind

    /// The names of directory stems; a directory stem is never cut.
    let private directoryNames (location: string) (directories: string list) =
        let parsed =
            directories
            |> List.map (fun directory ->
                match TreeStems.parseStem directory with
                | Ok(WholeStem name) -> Ok name
                | Ok(CutStem _) ->
                    Error(
                        error
                            "tree.invalid_stem"
                            (Some location)
                            $"The directory '{directory}' is a cut stem; only a type or value stem is cut."
                    )
                | Error reason -> Error(error "tree.invalid_stem" (Some location) reason)
            )

        match
            parsed
            |> List.choose (
                function
                | Error e -> Some e
                | Ok _ -> None
            )
        with
        | [] ->
            Ok(
                parsed
                |> List.choose (
                    function
                    | Ok name -> Some name
                    | Error _ -> None
                )
            )
        | errors -> Error errors

    /// Where a JSON file below pkg/ or deps/ belongs, none for a file that is not part of the tree, or errors.
    let private place
        (ownStems: string list)
        (location: string)
        : Result<Placed option, StoreError list> =
        let segments =
            location.Split('/')
            |> List.ofArray

        let directories =
            List.take
                (segments.Length
                 - 1)
                segments

        let name = List.last segments

        let inModule
            (scope: ModuleDocumentAddress -> DocumentAddress)
            (moduleDirectories: string list)
            =
            match moduleDirectories with
            | [] ->
                Error [
                    error
                        "tree.misplaced_file"
                        (Some location)
                        "A tree file below a package belongs to a module directory."
                ]
            | _ ->
                directoryNames location moduleDirectories
                |> Result.bind (fun names ->
                    let modulePath = ModulePath(Path names)

                    let node (suffix: string) kind =
                        Ok(
                            Some(
                                NodeFile(
                                    scope,
                                    modulePath,
                                    name.Substring(
                                        0,
                                        name.Length
                                        - suffix.Length
                                    ),
                                    kind
                                )
                            )
                        )

                    if name = "module.json" then
                        Ok(Some(ModuleFile(scope (Module modulePath))))
                    elif name.EndsWith(".type.json", StringComparison.Ordinal) then
                        node ".type.json" TypeNode
                    elif name.EndsWith(".value.json", StringComparison.Ordinal) then
                        node ".value.json" ValueNode
                    else
                        Error [
                            error
                                "tree.unknown_file"
                                (Some location)
                                "A JSON file in a module directory is module.json, a .type.json or a .value.json file."
                        ]
                )

        match directories with
        | "pkg" :: rest ->
            if
                rest.Length
                >= ownStems.Length
                && List.take ownStems.Length rest = ownStems
            then
                inModule Own (List.skip ownStems.Length rest)
            else
                Error [
                    error
                        "tree.outside_package"
                        (Some location)
                        "The file is below pkg/ and not below the directory of the manifest's package."
                ]
        | "deps" :: rest ->
            match TreeStems.splitDependency rest with
            | Ok split ->
                directoryNames location split.PackageDirectories
                |> Result.bind (fun packageNames ->
                    let package = PackageName(Path packageNames)
                    inModule (fun inner -> Dependency(package, inner)) split.ModuleDirectories
                )
            | Error reason ->
                let code =
                    if
                        rest
                        |> List.exists (fun segment ->
                            segment.StartsWith("@", StringComparison.Ordinal)
                            && segment
                               <> TreeStems.BareVersionSegment
                        )
                    then
                        "tree.versioned_dependency"
                    else
                        "tree.misplaced_file"

                let directory =
                    String.concat "/" directories
                    + "/"

                Error [ error code (Some directory) reason ]
        | _ when isNodeFileName name ->
            Error [
                error
                    "tree.outside_tree"
                    (Some location)
                    "A module, type or value file is below pkg/ or deps/."
            ]
        | _ -> Ok None

    let private errorsOf (results: Result<'a, StoreError list> list) =
        results
        |> List.collect (
            function
            | Error errors -> errors
            | Ok _ -> []
        )

    let private valuesOf (results: Result<'a, StoreError list> list) =
        results
        |> List.choose (
            function
            | Ok value -> Some value
            | Error _ -> None
        )

    let private one (result: Result<'a, StoreError>) =
        result
        |> Result.mapError List.singleton

    let private readV4
        (options: ReadOptions)
        (manifestFile: V4ManifestFile)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * ReadReport, StoreError list> =
        let manifest = manifestFile.Manifest
        let own = manifest.Package
        let (PackageName ownPath) = own
        let ownStems = stems ownPath
        let holds = V4DocumentTree.holdsDefinitions manifest.Kind

        let overBudget =
            files
            |> List.filter (fun (location, _) -> location.Length > manifestFile.PathBudget)
            |> List.map (fun (location, _) ->
                error
                    "tree.over_budget"
                    (Some location)
                    $"The path has {location.Length} characters, and the manifest's path budget is {manifestFile.PathBudget}."
            )

        let placed =
            files
            |> List.map (fun (location, file) ->
                place ownStems location
                |> Result.map (Option.map (fun found -> location, file, found))
            )

        let found =
            valuesOf placed
            |> List.choose id

        let modules =
            found
            |> List.choose (fun (location, file, where) ->
                match where with
                | ModuleFile address -> Some(location, file, address)
                | NodeFile _ -> None
            )
            |> List.map (fun (location, file, address) ->
                readFile
                    options.MaxFileBytes
                    location
                    file
                    (V4TreeFiles.readModule (holds address))
                |> one
                |> Result.bind (fun moduleFile ->
                    match address with
                    | Own(Module path)
                    | Dependency(_, Module path) when
                        path
                        <> moduleFile.Path
                        ->
                        let (ModulePath stated) = moduleFile.Path
                        let (ModulePath expected) = path

                        Error [
                            error
                                "tree.path_mismatch"
                                (Some location)
                                $"The module file states the path '{CanonicalNames.pathToString stated}', and its directory is the module '{CanonicalNames.pathToString expected}'."
                        ]
                    | _ -> Ok(address, location, moduleFile)
                )
            )

        let moduleFiles = valuesOf modules

        let cutStems =
            moduleFiles
            |> List.map (fun (address, _, moduleFile) -> address, Map.ofList moduleFile.FileNames)
            |> Map.ofList

        let nodes =
            found
            |> List.choose (fun (location, file, where) ->
                match where with
                | NodeFile(scope, modulePath, stem, kind) ->
                    Some(location, file, scope, modulePath, stem, kind)
                | ModuleFile _ -> None
            )
            |> List.map (fun (location, file, scope, modulePath, stem, kind) ->
                let named (nodeName: Name) document =
                    let expected =
                        Map.tryFind (scope (Module modulePath)) cutStems
                        |> Option.bind (Map.tryFind nodeName)
                        |> Option.defaultWith (fun () -> TreeStems.escapeName nodeName)

                    if stem = expected then
                        Ok(nodeName, document)
                    else
                        Error [
                            error
                                "tree.name_mismatch"
                                (Some location)
                                $"The file's stem is '{stem}', and the stem of its name '{CanonicalNames.nameToString nodeName}' is '{expected}'."
                        ]

                match kind with
                | TypeNode ->
                    readFile options.MaxFileBytes location file V4TreeFiles.readType
                    |> one
                    |> Result.bind (fun (nodeName, document) -> named nodeName (TypeDoc document))
                    |> Result.map (fun (nodeName, document) ->
                        scope (TypeDef(modulePath, nodeName)), location, document
                    )
                | ValueNode ->
                    readFile options.MaxFileBytes location file V4TreeFiles.readValue
                    |> one
                    |> Result.bind (fun (nodeName, document) -> named nodeName (ValueDoc document))
                    |> Result.map (fun (nodeName, document) ->
                        scope (ValueDef(modulePath, nodeName)), location, document
                    )
            )

        let inlineDocuments =
            moduleFiles
            |> List.collect (fun (address, location, moduleFile) ->
                let at inner =
                    match address with
                    | Dependency(package, _) -> Dependency(package, inner)
                    | Manifest
                    | Own _ -> Own inner

                (moduleFile.InlineTypes
                 |> List.map (fun (name, document) ->
                     at (TypeDef(moduleFile.Path, name)), location, TypeDoc document
                 ))
                @ (moduleFile.InlineValues
                   |> List.map (fun (name, document) ->
                       at (ValueDef(moduleFile.Path, name)), location, ValueDoc document
                   ))
            )

        let documents =
            (moduleFiles
             |> List.map (fun (address, location, moduleFile) ->
                 address, location, ModuleDoc moduleFile.Document
             ))
            @ valuesOf nodes
            @ inlineDocuments

        let duplicates =
            documents
            |> List.groupBy (fun (address, _, _) -> address)
            |> List.filter (fun (_, group) -> group.Length > 1)
            |> List.map (fun (address, group) ->
                let (_, location, _) = group.Head

                let places =
                    group
                    |> List.map (fun (_, place, _) -> place)
                    |> String.concat ", "

                error
                    "tree.duplicate_document"
                    (Some location)
                    $"The tree has {describe address} in more than one place: {places}."
            )

        let fileErrors =
            overBudget
            @ errorsOf placed
            @ errorsOf modules
            @ errorsOf nodes
            @ duplicates

        match fileErrors with
        | _ :: _ -> Error fileErrors
        | [] ->
            let tree: V4DocumentTree = {
                Manifest = manifest
                Documents =
                    documents
                    |> List.map (fun (address, _, document) -> address, document)
                    |> Map.ofList
            }

            let locationOf =
                documents
                |> List.map (fun (address, location, _) -> address, location)
                |> Map.ofList
                |> Map.add Manifest ManifestFile

            match V4DocumentTree.join tree with
            | Ok _ ->
                Ok(
                    V4Tree tree,
                    {
                        Notices = manifestFile.Dropped
                        CompactedFiles = []
                    }
                )
            | Error errors ->
                Error(
                    errors
                    |> List.map (fun (e: TreeError) ->
                        error
                            e.Code
                            (e.Address
                             |> Option.map (fun address ->
                                 Map.tryFind address locationOf
                                 |> Option.defaultWith (fun () ->
                                     DocumentAddress.logicalPath own address
                                     + Extension
                                 )
                             ))
                            e.Message
                    )
                )

    /// <summary>
    /// Reads a tree from files. The files are taken by their location in the tree, whatever order the tree lists
    /// them in. A JSON file is part of the tree, a <c>.yaml</c> or <c>.yml</c> file is refused as this store reads
    /// the JSON profile only, and any other file is ignored. The tree read must join (<c>V4DocumentTree.join</c>).
    /// A v4 manifest's <c>version</c> and <c>created</c> are dropped with a notice each; its <c>layout</c> and
    /// <c>pathBudget</c> describe the tree and are dropped silently. A refused read gives every error it finds and
    /// no tree. C# calls this <c>Read</c>.
    /// </summary>
    [<CompiledName("Read")>]
    let read
        (options: ReadOptions)
        (files: Morphir.Files.VFileTree)
        : Result<StoredTree * ReadReport, StoreError list> =
        let treeFiles =
            Morphir.Files.VFileTree.toFileMap files
            |> Map.toList
            |> List.choose (fun (location, file) ->
                profileOf location
                |> Option.map (fun profile -> location, file, profile)
            )

        if treeFiles.Length > options.MaxFiles then
            Error [
                error
                    "tree.too_many_files"
                    None
                    $"The tree has {treeFiles.Length} files, and a read accepts at most {options.MaxFiles}."
            ]
        else
            let manifests =
                treeFiles
                |> List.filter (fun (location, _, _) -> isManifestName location)

            let yamlFiles =
                treeFiles
                |> List.filter (fun (_, _, profile) -> profile = Yaml)

            match manifests with
            | [] ->
                Error [
                    error "tree.missing_manifest" None "The tree has no manifest.json at its root."
                ]
            | _ :: _ :: _ ->
                let named =
                    manifests
                    |> List.map (fun (location, _, _) -> location)
                    |> String.concat " and "

                Error [
                    error
                        "tree.ambiguous_profile"
                        None
                        $"The tree has more than one manifest, {named}; a reader does not choose one."
                ]
            | [ (_, _, Yaml) ]
            | [ (_, _, Json) ] when not yamlFiles.IsEmpty ->
                Error(
                    yamlFiles
                    |> List.map (fun (location, _, _) ->
                        error
                            "tree.unsupported_profile"
                            (Some location)
                            "This store reads the JSON profile of a tree, and the file is in the YAML profile."
                    )
                )
            | [ (manifestLocation, manifestFile, _) ] ->
                let others =
                    treeFiles
                    |> List.filter (fun (location, _, _) ->
                        location
                        <> manifestLocation
                    )
                    |> List.map (fun (location, file, _) -> location, file)

                contentBytes options.MaxFileBytes manifestLocation manifestFile
                |> Result.bind (fun bytes ->
                    let classic =
                        decode manifestLocation (fun root -> Ok(isClassicManifest root)) bytes

                    match classic with
                    | Ok true -> Error classicNotSupported
                    | Ok false -> decode manifestLocation V4TreeFiles.readManifest bytes
                    | Error e -> Error e
                )
                |> one
                |> Result.bind (fun manifest -> readV4 options manifest others)
