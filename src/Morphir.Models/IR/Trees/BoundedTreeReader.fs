namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Text.Json
open Morphir.IR
open Morphir.IR.Files
open Morphir.Json.V4

type BoundedTreeMember = {
    Address: DocumentAddress
    DocumentId: string
}

[<RequireQualifiedAccess>]
type BoundedTreeBody =
    | ClassicType of ClassicTypeDocument<JsonElement>
    | ClassicValue of ClassicValueDocument<JsonElement, JsonElement>
    | V4Type of V4TypeDocument
    | V4Value of V4ValueDocument

type BoundedTreeCursor = private BoundedTreeCursor of obj * DocumentAddress * int

type BoundedTreeMemberPage = {
    Items: BoundedTreeMember list
    Next: BoundedTreeCursor option
}

[<RequireQualifiedAccess>]
type BoundedTreeRole =
    | Definition
    | Specification

type BoundedTreeModule = {
    Address: DocumentAddress
    DocumentId: string
    Role: BoundedTreeRole
    Access: Morphir.IR.V4.Access option
}

type BoundedTreeModuleCursor = private BoundedTreeModuleCursor of obj * PackageName * string

type BoundedTreeModulePage = {
    Items: BoundedTreeModule list
    Next: BoundedTreeModuleCursor option
}

[<RequireQualifiedAccess>]
type internal BoundedTreeManifest =
    | Classic of ClassicManifestFile
    | V4 of V4ManifestFile

type BoundedTreeSnapshot =
    private
        {
            Source: TreeFileSource.SnapshotSource
            Revision: string
            Identity: obj
            ByteLimit: int64
            Profile: TreeFileSource.Profile
            Manifest: BoundedTreeManifest
        }

    /// The package named by the admitted manifest.
    member snapshot.Package =
        match snapshot.Manifest with
        | BoundedTreeManifest.Classic file -> file.Manifest.Package
        | BoundedTreeManifest.V4 file -> file.Manifest.Package

    /// The dependency packages named by the admitted manifest.
    member snapshot.Dependencies =
        match snapshot.Manifest with
        | BoundedTreeManifest.Classic file -> file.Manifest.Dependencies
        | BoundedTreeManifest.V4 file -> file.Manifest.Dependencies

type private ModuleInventory = {
    Path: ModulePath
    Access: Morphir.IR.V4.Access option
    Types: Name list
    Values: Name list
    InlineTypes: Set<Name>
    InlineValues: Set<Name>
    FileNames: Map<Name, string>
}

[<RequireQualifiedAccess>]
module internal BoundedTreeReader =
    let private refuse code file message =
        Result.Error [ StoreErrors.error code file message ]

    let private atRevision (source: TreeFileSource.SnapshotSource) revision read =
        let changed () =
            refuse "tree.revision_changed" None "The tree source changed during the read."

        if
            source.Revision()
            <> revision
        then
            changed ()
        else
            let result = read ()

            if
                source.Revision()
                <> revision
            then
                changed ()
            else
                result

    let private readRoot byteLimit source expected pathBudget logical =
        let plainOnly location =
            refuse
                "tree.unsupported_context"
                (Some location)
                "This reader accepts plain tree files."

        let parse profile location (bytes: byte array) =
            let memory = ReadOnlyMemory bytes

            match profile with
            | TreeFileSource.Json ->
                MorphirFile.readEnvelope ContextLimits.draft1 location memory
                |> Result.bind (fun input ->
                    if input.Context.IsSome then
                        plainOnly location
                    else
                        Ok input.Document
                )
            | TreeFileSource.Yaml ->
                TreeYaml.read ContextLimits.draft1.MaxDepth location bytes
                |> Result.bind (fun document ->
                    let mutable schema = Unchecked.defaultof<JsonElement>

                    if document.TryGetProperty("$schema", &schema) then
                        plainOnly location
                    else
                        Ok document
                )

        TreeFileSource.snapshotLocation source expected logical
        |> TreeFileSource.one
        |> Result.bind (fun (profile, location) ->
            if location.Length > pathBudget then
                refuse
                    "tree.over_budget"
                    (Some location)
                    "The file path exceeds the manifest path budget."
            else
                TreeFileSource.snapshotBytes byteLimit source location
                |> TreeFileSource.one
                |> Result.bind (parse profile location)
                |> Result.map (fun document -> profile, location, document)
        )

    let private decodeAt location decode root =
        decode root
        |> Result.mapError (fun (error: V4JsonError) -> [
            StoreErrors.error error.Rule (Some location) $"{error.Location}: {error.Message}"
        ])


    let private validateManifest location manifest =
        match manifest with
        | BoundedTreeManifest.V4 file ->
            match file.Manifest.Kind, file.Manifest.EntryPoints with
            | V4DistributionKind.ApplicationKind, [] ->
                refuse
                    "tree.missing_entry_points"
                    (Some location)
                    "An Application needs at least one entry point."
            | V4DistributionKind.ApplicationKind, _
            | _, [] -> Ok manifest
            | _ ->
                refuse
                    "tree.misplaced_entry_points"
                    (Some location)
                    "Only an Application can have entry points."
        | BoundedTreeManifest.Classic _ -> Ok manifest

    let openTree
        byteLimit
        (source: TreeFileSource.SnapshotSource)
        : Result<BoundedTreeSnapshot, StoreError list> =
        if
            byteLimit
            <= 0L
        then
            refuse "tree.invalid_limit" None "A document byte limit must be positive."
        else
            let revision = source.Revision()

            atRevision
                source
                revision
                (fun () ->
                    readRoot byteLimit source None Int32.MaxValue "manifest"
                    |> Result.bind (fun (profile, location, root) ->
                        let decode document =
                            if TreeFileSource.isClassicManifest document then
                                ClassicTreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.Classic
                            else
                                V4TreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.V4

                        decodeAt location decode root
                        |> Result.bind (validateManifest location)
                        |> Result.map (fun manifest -> {
                            Source = source
                            Revision = revision
                            Identity = obj ()
                            ByteLimit = byteLimit
                            Profile = profile
                            Manifest = manifest
                        })
                    )
                )

    let private ownership manifest =
        match manifest with
        | BoundedTreeManifest.Classic file ->
            file.Manifest.Package,
            file.Manifest.Dependencies,
            file.Manifest.Kind = ClassicDistributionKind.LibraryKind,
            false,
            file.PathBudget
        | BoundedTreeManifest.V4 file ->
            file.Manifest.Package,
            file.Manifest.Dependencies,
            file.Manifest.Kind
            <> V4DistributionKind.SpecsKind,
            file.Manifest.Kind = V4DistributionKind.ApplicationKind,
            file.PathBudget

    let private moduleInventory snapshot location holdsDefinitions root =
        let read decode = decodeAt location decode root

        let inventory access types values (file: TreeModuleFile<_, unit, unit>) = {
            Path = file.Path
            Access = access file.Document
            Types = types file.Document
            Values = values file.Document
            InlineTypes =
                file.InlineTypes
                |> List.map fst
                |> Set.ofList
            InlineValues =
                file.InlineValues
                |> List.map fst
                |> Set.ofList
            FileNames = Map.ofList file.FileNames
        }

        match snapshot.Manifest with
        | BoundedTreeManifest.Classic _ ->
            read (ClassicTreeFiles.readModuleInventory holdsDefinitions)
            |> Result.map (
                inventory
                    (fun (doc: ClassicModuleDocument) ->
                        doc.Access
                        |> Option.map (
                            function
                            | Morphir.IR.Classic.AccessControlled.Private -> Morphir.IR.V4.Private
                            | Morphir.IR.Classic.AccessControlled.Public -> Morphir.IR.V4.Public
                        )
                    )
                    (fun (doc: ClassicModuleDocument) -> doc.TypeNames)
                    (fun doc -> doc.ValueNames)
            )
        | BoundedTreeManifest.V4 _ ->
            read (V4TreeFiles.readModuleInventory holdsDefinitions)
            |> Result.map (
                inventory
                    (fun (doc: V4ModuleDocument) -> doc.Access)
                    (fun doc -> doc.TypeNames)
                    (fun doc -> doc.ValueNames)
            )

    let private memberStem inventory name =
        Map.tryFind name inventory.FileNames
        |> Option.defaultWith (fun () -> TreeStems.escapeName name)

    let private validateInventory location inventory =
        let unique names inlines =
            let namesSeen = HashSet<Name>()
            let stemsSeen = HashSet<string>(StringComparer.OrdinalIgnoreCase)

            names
            |> List.forall (fun name ->
                namesSeen.Add name
                && (Set.contains name inlines
                    || stemsSeen.Add(memberStem inventory name))
            )

        if
            unique inventory.Types inventory.InlineTypes
            && unique inventory.Values inventory.InlineValues
        then
            Ok inventory
        else
            refuse
                "duplicate_member"
                (Some location)
                "The module lists a duplicate member or conflicting file stem."

    let private withModule snapshot moduleAddress action =
        let own, dependencies, ownDefinitions, dependencyDefinitions, pathBudget =
            ownership snapshot.Manifest

        let selection =
            match moduleAddress with
            | Own(Module path) -> Ok(path, ownDefinitions)
            | Dependency(package, Module path) when List.contains package dependencies ->
                Ok(path, dependencyDefinitions)
            | _ ->
                refuse
                    "tree.invalid_selection"
                    None
                    "Expected a module in the admitted package or its dependencies."

        selection
        |> Result.bind (fun (path, holdsDefinitions) ->
            let (ModulePath names) = path

            if
                CanonicalNames.parsePath (CanonicalNames.pathToString names)
                <> Ok names
            then
                refuse "tree.invalid_selection" None "Expected a canonical module path."
            else
                let logical = DocumentAddress.logicalPath own moduleAddress

                readRoot
                    snapshot.ByteLimit
                    snapshot.Source
                    (Some snapshot.Profile)
                    pathBudget
                    logical
                |> Result.bind (fun (_, location, root) ->
                    moduleInventory snapshot location holdsDefinitions root
                    |> Result.bind (fun inventory ->
                        if
                            inventory.Path
                            <> path
                        then
                            refuse
                                "tree.wrong_module_path"
                                (Some location)
                                "The stated module does not match the selected module."
                        else
                            validateInventory location inventory
                            |> Result.bind (fun validated ->
                                action own location holdsDefinitions pathBudget validated root
                            )
                    )
                )
        )

    let private modulePathFromMetadata (prefix: string) pathBudget after (logical: string) =
        let badPath () =
            refuse
                "tree.invalid_module_path"
                (Some logical)
                "The source returned a misplaced, malformed or unordered module path."

        let suffix = "/module"

        if
            not (logical.StartsWith(prefix, StringComparison.Ordinal))
            || not (logical.EndsWith(suffix, StringComparison.Ordinal))
            || logical.Length
               <= prefix.Length
                  + suffix.Length
            || logical.Length > pathBudget
                                - ".yml".Length
            || (after
                |> Option.exists (fun previous ->
                    String.CompareOrdinal(previous, logical)
                    >= 0
                ))
        then
            badPath ()
        else
            let relative =
                logical.Substring(
                    prefix.Length,
                    logical.Length
                    - prefix.Length
                    - suffix.Length
                )

            let segments = relative.Split('/')

            let names =
                segments
                |> Array.choose (fun stem ->
                    match TreeStems.parseStem stem with
                    | Ok(WholeStem name) when TreeStems.escapeName name = stem -> Some name
                    | _ -> None
                )

            if names.Length = segments.Length then
                Ok(ModulePath(Path(List.ofArray names)))
            else
                badPath ()

    let modules
        (limit: int)
        (cursor: BoundedTreeModuleCursor option)
        (package: PackageName)
        (snapshot: BoundedTreeSnapshot)
        : Result<BoundedTreeModulePage, StoreError list> =
        atRevision
            snapshot.Source
            snapshot.Revision
            (fun () ->
                let own, dependencies, _, _, pathBudget = ownership snapshot.Manifest

                let after =
                    match cursor with
                    | None -> Ok None
                    | Some(BoundedTreeModuleCursor(identity, selected, previous)) when
                        Object.ReferenceEquals(identity, snapshot.Identity)
                        && selected = package
                        ->
                        Ok(Some previous)
                    | _ ->
                        refuse
                            "tree.invalid_cursor"
                            None
                            "The cursor belongs to another snapshot or package."

                if
                    limit < 1
                    || limit > 256
                then
                    refuse "tree.invalid_page_limit" None "Page size must be between 1 and 256."
                elif
                    package
                    <> own
                    && not (List.contains package dependencies)
                then
                    refuse
                        "tree.invalid_selection"
                        None
                        "Expected the admitted package or one of its dependencies."
                else
                    let dependency = if package = own then None else Some package
                    let prefix = TreeLayout.packageDirectory own dependency

                    let toAddress path =
                        match dependency with
                        | None -> Own(Module path)
                        | Some name -> Dependency(name, Module path)

                    let next previous =
                        try
                            snapshot.Source.NextModule pathBudget prefix previous
                            |> TreeFileSource.one
                        with ex ->
                            refuse "tree.unreadable_file" (Some prefix) ex.Message

                    let rec collect remaining previous reversed =
                        next previous
                        |> Result.bind (
                            function
                            | None ->
                                Ok
                                    {
                                        Items = List.rev reversed
                                        Next = None
                                    }
                            | Some logical ->
                                modulePathFromMetadata prefix pathBudget previous logical
                                |> Result.bind (fun path ->
                                    if remaining = 0 then
                                        let continuation =
                                            previous
                                            |> Option.map (fun current ->
                                                BoundedTreeModuleCursor(
                                                    snapshot.Identity,
                                                    package,
                                                    current
                                                )
                                            )

                                        Ok
                                            {
                                                Items = List.rev reversed
                                                Next = continuation
                                            }
                                    else
                                        let address = toAddress path

                                        withModule
                                            snapshot
                                            address
                                            (fun _ _ holdsDefinitions _ inventory _ ->
                                                Ok
                                                    {
                                                        Address = address
                                                        DocumentId = logical
                                                        Role =
                                                            if holdsDefinitions then
                                                                BoundedTreeRole.Definition
                                                            else
                                                                BoundedTreeRole.Specification
                                                        Access = inventory.Access
                                                    }
                                            )
                                        |> Result.bind (fun item ->
                                            collect
                                                (remaining
                                                 - 1)
                                                (Some logical)
                                                (item
                                                 :: reversed)
                                        )
                                )
                        )

                    after
                    |> Result.bind (fun previous -> collect limit previous [])
            )

    let private memberPage
        own
        snapshot
        moduleAddress
        offset
        limit
        inventory
        : BoundedTreeMemberPage =
        let scope address =
            match moduleAddress with
            | Dependency(package, _) -> Dependency(package, address)
            | _ -> Own address

        let memberEntry makeAddress inlines name =
            let address = scope (makeAddress (inventory.Path, name))

            let documentId =
                if Set.contains name inlines then
                    DocumentAddress.logicalPath own moduleAddress
                else
                    DocumentAddress.logicalPath own address

            {
                Address = address
                DocumentId = documentId
            }

        let page =
            seq {
                yield! Seq.map (memberEntry TypeDef inventory.InlineTypes) inventory.Types
                yield! Seq.map (memberEntry ValueDef inventory.InlineValues) inventory.Values
            }
            |> Seq.indexed
            |> Seq.skipWhile (fun (index, _) -> index < offset)
            |> Seq.truncate (limit + 1)
            |> Seq.map snd
            |> List.ofSeq

        let next =
            if page.Length > limit then
                Some(
                    BoundedTreeCursor(
                        snapshot.Identity,
                        moduleAddress,
                        offset
                        + limit
                    )
                )
            else
                None

        {
            Items = List.truncate limit page
            Next = next
        }

    let members
        (limit: int)
        (cursor: BoundedTreeCursor option)
        (moduleAddress: DocumentAddress)
        (snapshot: BoundedTreeSnapshot)
        : Result<BoundedTreeMemberPage, StoreError list> =
        atRevision
            snapshot.Source
            snapshot.Revision
            (fun () ->
                if
                    limit < 1
                    || limit > 256
                then
                    refuse "tree.invalid_page_limit" None "Page size must be between 1 and 256."
                else
                    let offset =
                        match cursor with
                        | None -> Ok 0
                        | Some(BoundedTreeCursor(identity, selected, start)) when
                            Object.ReferenceEquals(identity, snapshot.Identity)
                            && selected = moduleAddress
                            ->
                            Ok start
                        | _ ->
                            refuse
                                "tree.invalid_cursor"
                                None
                                "The cursor belongs to another snapshot or selection."

                    offset
                    |> Result.bind (fun start ->
                        withModule
                            snapshot
                            moduleAddress
                            (fun own _ _ _ inventory _ ->
                                Ok(memberPage own snapshot moduleAddress start limit inventory)
                            )
                    )
            )

    let private inlineBody snapshot location holdsDefinitions suffix name (root: JsonElement) =
        let field = if suffix = ".type" then "types" else "values"

        // The codec validates uniqueness by parsed Name. Read the property with its original spelling.
        let property =
            root.GetProperty(field).EnumerateObject()
            |> Seq.tryFind (fun property -> CanonicalNames.parseName property.Name = Ok name)

        match property with
        | None -> refuse "tree.missing_member" (Some location) "The inline member is missing."
        | Some property ->
            let payload = property.Value

            let memberLocation =
                V4JsonDecoding.memberAt (V4JsonDecoding.memberAt "$" field) property.Name

            // Classic attributes retain JsonElements. Reparse the selected slice so
            // the returned attributes do not retain other inline bodies in the module.
            use selected =
                JsonDocument.Parse(
                    payload.GetRawText(),
                    JsonDocumentOptions(MaxDepth = ContextLimits.draft1.MaxDepth)
                )

            let item = selected.RootElement

            match snapshot.Manifest, suffix with
            | BoundedTreeManifest.Classic _, ".type" ->
                decodeAt
                    location
                    (ClassicTreeFiles.readInlineType holdsDefinitions memberLocation)
                    item
                |> Result.map BoundedTreeBody.ClassicType
            | BoundedTreeManifest.Classic _, _ ->
                decodeAt
                    location
                    (ClassicTreeFiles.readInlineValue holdsDefinitions memberLocation)
                    item
                |> Result.map BoundedTreeBody.ClassicValue
            | BoundedTreeManifest.V4 _, ".type" ->
                decodeAt location (V4TreeFiles.readInlineType holdsDefinitions memberLocation) item
                |> Result.map BoundedTreeBody.V4Type
            | BoundedTreeManifest.V4 _, _ ->
                decodeAt location (V4TreeFiles.readInlineValue holdsDefinitions memberLocation) item
                |> Result.map BoundedTreeBody.V4Value

    let private splitBody snapshot logical pathBudget holdsDefinitions suffix name =
        let read decode wrap =
            readRoot snapshot.ByteLimit snapshot.Source (Some snapshot.Profile) pathBudget logical
            |> Result.bind (fun (_, location, root) ->
                decodeAt location decode root
                |> Result.bind (fun (stated, document) ->
                    if stated = name then
                        Ok(wrap document)
                    else
                        refuse
                            "tree.name_mismatch"
                            (Some location)
                            "The file's name does not match the selected member."
                )
            )

        match snapshot.Manifest, suffix with
        | BoundedTreeManifest.Classic _, ".type" ->
            read (ClassicTreeFiles.readType holdsDefinitions) BoundedTreeBody.ClassicType
        | BoundedTreeManifest.Classic _, _ ->
            read (ClassicTreeFiles.readValue holdsDefinitions) BoundedTreeBody.ClassicValue
        | BoundedTreeManifest.V4 _, ".type" ->
            read (V4TreeFiles.readType holdsDefinitions) BoundedTreeBody.V4Type
        | BoundedTreeManifest.V4 _, _ ->
            read (V4TreeFiles.readValue holdsDefinitions) BoundedTreeBody.V4Value

    /// Resolve a member in the admitted module and decode only its selected body.
    let readMember
        (address: DocumentAddress)
        (snapshot: BoundedTreeSnapshot)
        : Result<BoundedTreeBody, StoreError list> =
        atRevision
            snapshot.Source
            snapshot.Revision
            (fun () ->
                match TreeLayout.memberOf address with
                | None ->
                    refuse
                        "tree.invalid_selection"
                        None
                        "A selected body must be a type or a value."
                | Some(name, suffix) ->
                    let moduleAddress = TreeLayout.moduleOf address

                    withModule
                        snapshot
                        moduleAddress
                        (fun own moduleFile holdsDefinitions pathBudget inventory root ->
                            let names, inlines =
                                if suffix = ".type" then
                                    inventory.Types, inventory.InlineTypes
                                else
                                    inventory.Values, inventory.InlineValues

                            if not (List.contains name names) then
                                refuse
                                    "tree.missing_member"
                                    (Some moduleFile)
                                    "The selected member is not listed in its module."
                            else
                                let logical =
                                    TreeLayout.directoryOf own address
                                    + memberStem inventory name
                                    + suffix

                                if Set.contains name inlines then
                                    match
                                        TreeFileSource.snapshotLocation
                                            snapshot.Source
                                            None
                                            logical
                                    with
                                    | Ok(_, sidecar) ->
                                        refuse
                                            "tree.duplicate_document"
                                            (Some sidecar)
                                            "The selected member is both inline and split."
                                    | Error problem when problem.Code = "tree.missing_member" ->
                                        inlineBody
                                            snapshot
                                            moduleFile
                                            holdsDefinitions
                                            suffix
                                            name
                                            root
                                    | Error problem -> Error [ problem ]
                                else
                                    splitBody
                                        snapshot
                                        logical
                                        pathBudget
                                        holdsDefinitions
                                        suffix
                                        name
                        )
            )
