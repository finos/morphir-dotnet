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

/// <summary>Reads a v4 document tree from the files of the parent's v4 tree specification.</summary>
module internal V4TreeReader =
    open TreeLayout
    open TreeFileSource

    let private error code file message = StoreErrors.error code file message

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

    let readV4
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
