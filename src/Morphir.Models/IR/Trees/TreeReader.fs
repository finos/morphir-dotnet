namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Text.Json
open Morphir.IR
open Morphir.Json.V4

/// <summary>How one IR version reads the module, type and value files of a tree.</summary>
type internal TreeFileReaders<'moduleDoc, 'typ, 'value> = {
    /// <summary>Reads a module file, of a package definition when the flag is set.</summary>
    Module: bool -> JsonElement -> Result<TreeModuleFile<'moduleDoc, 'typ, 'value>, V4JsonError>
    /// <summary>Reads a type file, of a package definition when the flag is set: its name and its document.</summary>
    Type: bool -> JsonElement -> Result<Name * 'typ, V4JsonError>
    /// <summary>Reads a value file, of a package definition when the flag is set: its name and its document.</summary>
    Value: bool -> JsonElement -> Result<Name * 'value, V4JsonError>
    /// <summary>The names of a module document's types and of its values, in document order.</summary>
    Listed: 'moduleDoc -> Name list * Name list
}

/// <summary>What a read found in the files of a tree besides the manifest.</summary>
type internal TreeFound<'moduleDoc, 'typ, 'value> = {
    /// <summary>Each document at its address.</summary>
    Documents: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typ, 'value>>
    /// <summary>The file of each document, the manifest's included.</summary>
    Locations: Map<DocumentAddress, string>
    /// <summary>A notice for each file the read ignored.</summary>
    Ignored: TreeNotice list
    /// <summary>The documents whose files were compacted, by the location of their file.</summary>
    Compacted: (string * DocumentAddress) list
}

/// <summary>
/// Reads the documents of a tree from its files, for either IR version: where each file belongs, the path budget,
/// the module, type and value files, the stems that <c>fileNames</c> records, and the documents that are missing or in
/// two places. The version gives its codecs (<see cref="TreeFileReaders`3"/>) and joins what is found.
/// </summary>
module internal TreeReader =
    open TreeLayout
    open TreeFileSource

    let private error code file message = StoreErrors.diagnostic code file message

    type private NodeKind =
        | TypeNode
        | ValueNode

    /// The optional package metadata file of the parent's v4 tree page ("Package Metadata"), which is not part of
    /// the model.
    let private isPackageMetadataFile (profile: TreeProfile) (name: string) =
        match profile with
        | TreeProfile.Json -> name = "package.json"
        | TreeProfile.Yaml -> name = "package.yaml" || name = "package.yml"

    let private canonicalExtension (profile: TreeProfile) =
        match profile with
        | TreeProfile.Json -> ".json"
        | TreeProfile.Yaml -> ".yaml"

    /// Where a file of a module is: its scope, its module, and what it is.
    type private Placed =
        /// A package metadata file directly below a package directory, which a read ignores with a notice.
        | PackageMetadata
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

    /// Where a tree file below pkg/ or deps/ belongs, none for a file that is not part of the tree, or errors.
    let private place
        (profile: TreeProfile)
        (ownStems: string list)
        (location: string)
        : Result<Placed option, TreeDiagnostic list> =
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
            | [] when isPackageMetadataFile profile name -> Ok(Some PackageMetadata)
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

                    match profile with
                    | TreeProfile.Json ->
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
                    | TreeProfile.Yaml ->
                        if name = "module.yaml" || name = "module.yml" then
                            Ok(Some(ModuleFile(scope (Module modulePath))))
                        elif name.EndsWith(".type.yaml", StringComparison.Ordinal) then
                            node ".type.yaml" TypeNode
                        elif name.EndsWith(".type.yml", StringComparison.Ordinal) then
                            node ".type.yml" TypeNode
                        elif name.EndsWith(".value.yaml", StringComparison.Ordinal) then
                            node ".value.yaml" ValueNode
                        elif name.EndsWith(".value.yml", StringComparison.Ordinal) then
                            node ".value.yml" ValueNode
                        else
                            Error [
                                error
                                    "tree.unknown_file"
                                    (Some location)
                                    "A YAML file in a module directory is module.yaml, a .type.yaml or a .value.yaml file."
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
                // A versioned segment is named by its own directory, which every file below it shares.
                match
                    rest
                    |> List.tryFindIndex (fun segment ->
                        segment.StartsWith("@", StringComparison.Ordinal)
                        && segment
                           <> TreeStems.BareVersionSegment
                    )
                with
                | Some index ->
                    let directory =
                        "deps"
                        :: List.take (index + 1) rest
                        |> String.concat "/"

                    Error [
                        error
                            "tree.versioned_dependency"
                            (Some(
                                directory
                                + "/"
                            ))
                            reason
                    ]
                | None ->
                    Error [
                        error
                            "tree.misplaced_file"
                            (Some(
                                String.concat "/" directories
                                + "/"
                            ))
                            reason
                    ]
        | _ when isNodeFileNameFor profile name ->
            Error [
                error
                    "tree.outside_tree"
                    (Some location)
                    "A module, type or value file is below pkg/ or deps/."
            ]
        | _ -> Ok None

    /// The address of a type or value within the package of a module's address.
    let private within (moduleAddress: DocumentAddress) (inner: ModuleDocumentAddress) =
        match moduleAddress with
        | Dependency(package, _) -> Dependency(package, inner)
        | Manifest
        | Own _ -> Own inner

    /// <summary>
    /// The documents of a tree from its files other than the manifest, or every error found: files out of place, paths
    /// over the manifest's path budget, files that do not decode, a module file whose path is not its directory's, a
    /// node file whose stem does not spell its name, a document in two places, and a name that a module file lists
    /// with no file (<c>missing_member</c> at the missing file's path). A package metadata file is ignored with a notice. Errors that several files share, such as a
    /// versioned dependency directory, are given once.
    /// </summary>
    let private readDocuments
        (ctx: TreeDecodeContext)
        (manifestLocation: string)
        (pathBudget: int)
        (own: PackageName)
        (holds: DocumentAddress -> bool)
        (readers: TreeFileReaders<'moduleDoc, 'typ, 'value>)
        (files: (string * Morphir.Files.VFile) list)
        : Result<TreeFound<'moduleDoc, 'typ, 'value>, TreeDiagnostic list> =
        let (PackageName ownPath) = own
        let ownStems = stems ownPath
        let extension = canonicalExtension ctx.Profile

        let placed =
            files
            |> List.map (fun (location, file) ->
                place ctx.Profile ownStems location
                |> Result.map (Option.map (fun found -> location, file, found))
            )

        let found =
            valuesOf placed
            |> List.choose id

        // Refuse ambiguous physical spellings (for example both .yaml and .yml for one logical document)
        // before opening or decoding either file.
        let ambiguousFiles =
            found
            |> List.choose (fun (location, _, where) ->
                match where with
                | PackageMetadata -> None
                | ModuleFile address -> Some(Choice1Of2 address, location)
                | NodeFile(scope, modulePath, stem, kind) ->
                    Some(Choice2Of2(scope (Module modulePath), stem, kind), location)
            )
            |> List.groupBy fst
            |> List.filter (fun (_, group) -> group.Length > 1)
            |> List.map (fun (_, group) ->
                let places =
                    group
                    |> List.map snd
                    |> List.sortWith (fun left right -> String.CompareOrdinal(left, right))

                let logical =
                    let first = places.Head
                    let dot = first.LastIndexOf '.'
                    if dot > 0 then first.Substring(0, dot) else first

                let named = String.concat " and " places

                error
                    "tree.ambiguous_profile"
                    (Some logical)
                    $"More than one physical file has this logical path: {named}."
            )

        if not ambiguousFiles.IsEmpty then
            Error ambiguousFiles
        else

        let isTreeFile where =
            match where with
            | PackageMetadata -> false
            | ModuleFile _
            | NodeFile _ -> true

        // Only the files of the tree count against the budget, not a file that the read ignores.
        let overBudget =
            found
            |> List.filter (fun (location, _, where) ->
                isTreeFile where
                && location.Length > pathBudget
            )
            |> List.map (fun (location, _, _) ->
                error
                    "tree.over_budget"
                    (Some location)
                    $"The path has {location.Length} characters, and the manifest's path budget is {pathBudget}."
            )

        let ignored =
            found
            |> List.choose (fun (location, _, where) ->
                match where with
                | PackageMetadata -> Some(TreeNotice.IgnoredFile location)
                | ModuleFile _
                | NodeFile _ -> None
            )

        let modules =
            found
            |> List.choose (fun (location, file, where) ->
                match where with
                | ModuleFile address -> Some(location, file, address)
                | PackageMetadata
                | NodeFile _ -> None
            )
            |> List.map (fun (location, file, address) ->
                readFileWithDetailed ctx location file (readers.Module(holds address))
                |> Result.bind (fun (moduleFile, compacted) ->
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
                    | _ -> Ok((address, location, moduleFile), compacted)
                )
            )

        let moduleFiles =
            valuesOf modules
            |> List.map fst

        let cutStems =
            moduleFiles
            |> List.map (fun (address, _, moduleFile) -> address, Map.ofList moduleFile.FileNames)
            |> Map.ofList

        /// The stem of a name's file in a module: the cut stem its module file records, or the name's escape.
        let stemOf moduleAddress (nodeName: Name) =
            Map.tryFind moduleAddress cutStems
            |> Option.bind (Map.tryFind nodeName)
            |> Option.defaultWith (fun () -> TreeStems.escapeName nodeName)

        let nodes =
            found
            |> List.choose (fun (location, file, where) ->
                match where with
                | NodeFile(scope, modulePath, stem, kind) ->
                    Some(location, file, scope, modulePath, stem, kind)
                | PackageMetadata
                | ModuleFile _ -> None
            )
            |> List.map (fun (location, file, scope, modulePath, stem, kind) ->
                let named (nodeName: Name) document =
                    let expected = stemOf (scope (Module modulePath)) nodeName

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
                    readFileWithDetailed
                        ctx
                        location
                        file
                        (readers.Type(holds (scope (Module modulePath))))
                    |> Result.bind (fun ((nodeName, document), compacted) ->
                        named nodeName (TypeDoc document)
                        |> Result.map (fun (nodeName, document) ->
                            (scope (TypeDef(modulePath, nodeName)), location, document), compacted
                        )
                    )
                | ValueNode ->
                    readFileWithDetailed
                        ctx
                        location
                        file
                        (readers.Value(holds (scope (Module modulePath))))
                    |> Result.bind (fun ((nodeName, document), compacted) ->
                        named nodeName (ValueDoc document)
                        |> Result.map (fun (nodeName, document) ->
                            (scope (ValueDef(modulePath, nodeName)), location, document), compacted
                        )
                    )
            )

        let inlineDocuments =
            moduleFiles
            |> List.collect (fun (address, location, moduleFile) ->
                let at = within address

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
            @ (valuesOf nodes
               |> List.map fst)
            @ inlineDocuments

        // A compacted module file holds its inline documents too, so they are listed with it.
        let compactedModules =
            valuesOf modules
            |> List.filter snd
            |> List.map (fun ((_, location, _), _) -> location)
            |> Set.ofList

        let compacted =
            (valuesOf nodes
             |> List.filter snd
             |> List.map (fun ((address, location, _), _) -> location, address))
            @ (documents
               |> List.filter (fun (_, location, _) -> compactedModules.Contains location)
               |> List.map (fun (address, location, _) -> location, address))

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

        // A listed name with no file at its path and no inline entry. A file at that path that does not decode has
        // its own error.
        let present =
            found
            |> List.map (fun (location, _, _) -> location)
            |> Set.ofList

        let hasPresent (basePath: string) =
            match ctx.Profile with
            | TreeProfile.Json -> present.Contains(basePath + ".json")
            | TreeProfile.Yaml ->
                present.Contains(basePath + ".yaml")
                || present.Contains(basePath + ".yml")

        let inlined =
            inlineDocuments
            |> List.map (fun (address, _, _) -> address)
            |> Set.ofList

        let missing =
            moduleFiles
            |> List.collect (fun (address, location, moduleFile) ->
                let typeNames, valueNames = readers.Listed moduleFile.Document
                let directory = directoryOf own address

                [
                    for kind, suffix, inner, names in
                        [
                            "type",
                            ".type",
                            (fun listed -> TypeDef(moduleFile.Path, listed)),
                            typeNames
                            "value",
                            ".value",
                            (fun listed -> ValueDef(moduleFile.Path, listed)),
                            valueNames
                        ] do
                        for listed in names do
                            let basePath =
                                directory
                                + stemOf address listed
                                + suffix

                            let expected = basePath + extension

                            if
                                not (inlined.Contains(within address (inner listed)))
                                && not (hasPresent basePath)
                            then
                                error
                                    "missing_member"
                                    (Some expected)
                                    $"The module file {location} lists the {kind} '{CanonicalNames.nameToString listed}', and the tree has no file for it."
                ]
            )

        let fileErrors =
            overBudget
            @ (errorsOf placed
               |> List.distinctBy (fun (d: TreeDiagnostic) -> d.Code, d.File, d.Location, d.Message))
            @ errorsOf modules
            @ errorsOf nodes
            @ duplicates
            @ missing

        match fileErrors with
        | _ :: _ -> Error fileErrors
        | [] ->
            Ok
                {
                    Documents =
                        documents
                        |> List.map (fun (address, _, document) -> address, document)
                        |> Map.ofList
                    Locations =
                        documents
                        |> List.map (fun (address, location, _) -> address, location)
                        |> Map.ofList
                        |> Map.add Manifest manifestLocation
                    Ignored = ignored
                    Compacted = compacted
                }

    /// The errors of a join of what a read found, each at the file of its document, or at the file its logical path
    /// gives for a document that has none.
    let private joinErrors
        (profile: TreeProfile)
        (own: PackageName)
        (found: TreeFound<_, _, _>)
        (errors: TreeError list)
        : TreeDiagnostic list =
        let extension = canonicalExtension profile

        errors
        |> List.map (fun (e: TreeError) ->
            error
                e.Code
                (e.Address
                 |> Option.map (fun address ->
                     Map.tryFind address found.Locations
                     |> Option.defaultWith (fun () ->
                         DocumentAddress.logicalPath own address
                         + extension
                     )
                 ))
                e.Message
        )

    /// <summary>
    /// Reads a tree from its files other than the manifest under <paramref name="ctx"/>, returning the stored tree,
    /// its joined distribution, and its read report.
    /// </summary>
    let readTreeWith
        (ctx: TreeDecodeContext)
        (manifestLocation: string)
        (manifestCompacted: bool)
        (pathBudget: int)
        (own: PackageName)
        (holds: DocumentAddress -> bool)
        (readers: TreeFileReaders<'moduleDoc, 'typ, 'value>)
        (build: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typ, 'value>> -> 'tree)
        (join: System.Threading.CancellationToken -> 'tree -> Result<'joined, TreeError list>)
        (stored: 'tree -> StoredTree)
        (notices: TreeNotice list)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * 'joined * ReadReport * string list, TreeDiagnostic list> =
        readDocuments ctx manifestLocation pathBudget own holds readers files
        |> Result.bind (fun found ->
            ctx.Cancellation.ThrowIfCancellationRequested()
            let tree = build found.Documents

            match join ctx.Cancellation tree with
            | Ok joined ->
                ctx.Cancellation.ThrowIfCancellationRequested()
                let sortedCompacted = found.Compacted |> List.sortBy fst

                let compactedLocations =
                    (if manifestCompacted then [ manifestLocation ] else [])
                    @ (sortedCompacted |> List.map fst)

                Ok(
                    stored tree,
                    joined,
                    {
                        Notices =
                            notices
                            @ found.Ignored
                        CompactedFiles =
                            (if manifestCompacted then [ Manifest ] else [])
                            @ (sortedCompacted |> List.map snd)
                    },
                    compactedLocations
                )
            | Error errors -> Error(joinErrors ctx.Profile own found errors)
        )

    /// <summary>
    /// Reads a JSON tree from its files other than the manifest: its documents, the tree that <paramref name="build"/>
    /// makes of them, and that tree's <paramref name="join"/>, whose errors are each at the file of their document. The
    /// report has the manifest's <paramref name="notices"/>, then one for each file the read ignored.
    /// </summary>
    let readTree
        (manifestCompacted: bool)
        (options: ReadOptions)
        (pathBudget: int)
        (own: PackageName)
        (holds: DocumentAddress -> bool)
        (readers: TreeFileReaders<'moduleDoc, 'typ, 'value>)
        (build: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typ, 'value>> -> 'tree)
        (join: 'tree -> Result<'joined, TreeError list>)
        (stored: 'tree -> StoredTree)
        (notices: TreeNotice list)
        (files: (string * Morphir.Files.VFile) list)
        : Result<StoredTree * ReadReport, StoreError list> =
        let ctx = createLegacyDecodeContext options.MaxFileBytes

        readTreeWith ctx ManifestFile manifestCompacted pathBudget own holds readers build (fun _ tree -> join tree) stored notices files
        |> Result.map (fun (tree, _, report, _) -> tree, report)
        |> Result.mapError StoreErrors.toStoreErrors
