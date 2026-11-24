namespace Morphir.IR.Trees

#if NETSTANDARD2_0
open Morphir.Compat
#endif

open System
open System.Collections.Generic
open System.Globalization
open System.Security.Cryptography
open System.Text
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

/// A selected body and the original encoded document that supplied it.
type BoundedTreeSelection = {
    Body: BoundedTreeBody
    File: string
    Encoded: ReadOnlyMemory<byte>
}

/// A validated plain JSON file ready for a private export stage.
type BoundedTreeExportDocument = {
    File: string
    Bytes: ReadOnlyMemory<byte>
}

/// Independent output and inventory limits for expanded tree export.
type BoundedTreeExportLimits = {
    MaxDocumentBytes: int64
    MaxTotalBytes: int64
    PageSize: int
}

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

/// An ordered, validated entry from a logical inventory selection.
[<RequireQualifiedAccess>]
type BoundedTreeInventoryEntry =
    | ModuleEntry of BoundedTreeModule
    | MemberEntry of BoundedTreeMember * BoundedTreeRole

/// The entries and opaque continuation for one inventory page.
type BoundedTreeInventoryPage = {
    Entries: BoundedTreeInventoryEntry list
    Continuation: string option
}

/// File-local name encoding accepted by a bounded reader.
[<RequireQualifiedAccess>]
type BoundedTreeEncoding =
    | Plain
    | ContextualDraft1
    | ContextualDraft1Streaming

    /// <summary>Stable encoding label without reflection.</summary>
    override this.ToString() =
        match this with
        | Plain -> "plain"
        | ContextualDraft1 -> "contextual-draft.1"
        | ContextualDraft1Streaming -> "contextual-draft.1-streaming"

/// The physical syntax admitted at the snapshot manifest.
[<RequireQualifiedAccess>]
type BoundedTreeSyntax =
    | Json
    | Yaml

    /// <summary>Stable syntax label without reflection.</summary>
    override this.ToString() =
        match this with
        | Json -> "json"
        | Yaml -> "yaml"

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
            CursorKey: byte array
            ByteLimit: int64
            InventoryByteLimit: int64
            DocumentDepthLimit: int
            DocumentNodeLimit: int option
            Work: BoundedTreeWorkBudget option
            Cancellation: System.Threading.CancellationToken
            Encoding: BoundedTreeEncoding
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

    /// The canonical package identity used by hosts, independent of its physical location.
    member snapshot.PackageId =
        let (PackageName path) = snapshot.Package
        CanonicalNames.pathToString path

    /// The canonical dependency identities in the admitted manifest.
    member snapshot.DependencyIds =
        snapshot.Dependencies
        |> Seq.map (fun (PackageName path) -> CanonicalNames.pathToString path)

    /// The distribution kind admitted by the bounded reader.
    member snapshot.KindName =
        match snapshot.Manifest with
        | BoundedTreeManifest.Classic file ->
            match file.Manifest.Kind with
            | ClassicDistributionKind.LibraryKind -> "Library"
            | ClassicDistributionKind.SpecsKind -> "Specs"
        | BoundedTreeManifest.V4 file ->
            match file.Manifest.Kind with
            | V4DistributionKind.LibraryKind -> "Library"
            | V4DistributionKind.SpecsKind -> "Specs"
            | V4DistributionKind.ApplicationKind -> "Application"

    /// The revision fixed at admission.
    member snapshot.RevisionId = snapshot.Revision

    /// Lifetime peak discovery-index charge in bytes, shared by copies of this snapshot.
    /// Reading this C#-safe property never starts discovery. A disabled index reports zero.
    /// The charge is 128 plus 160 + 4 * UTF-16 path length per distinct admitted module path.
    /// It covers the builder and final array concurrently, including refused partial builders,
    /// and retains that charge after publication. Fixed-overhead refusal can report 128 even
    /// below that reservation because the builder was constructed. Traversal/provider state,
    /// temporary path strings, cursors, revision/prefix strings and pending GC are excluded.
    /// This is logical index accounting, not CLR heap, context or process resident memory.
    member snapshot.PeakDiscoveryIndexChargedBytes = snapshot.Source.DiscoveryIndexPeakBytes ()

    /// The exact tree IR version, after manifest validation.
    member snapshot.IrVersion =
        match snapshot.Manifest with
        | BoundedTreeManifest.Classic _ -> "3.1.0"
        | BoundedTreeManifest.V4 _ -> "4.0.0"

    /// The homogeneous syntax fixed by the manifest.
    member snapshot.Syntax =
        match snapshot.Profile with
        | TreeFileSource.Json -> BoundedTreeSyntax.Json
        | TreeFileSource.Yaml -> BoundedTreeSyntax.Yaml

    /// The name encoding that this snapshot permits.
    member snapshot.NameEncoding = snapshot.Encoding

    /// Restrict the parser for source and expanded documents without changing the snapshot identity.
    member snapshot.WithDocumentLimits(depth: int, nodes: int) =
        if depth <= 0 then invalidArg (nameof depth) "A document depth limit must be positive."
        if nodes <= 0 then invalidArg (nameof nodes) "A document node limit must be positive."
        { snapshot with
            DocumentDepthLimit = min depth snapshot.DocumentDepthLimit
            DocumentNodeLimit = Some(min nodes (defaultArg snapshot.DocumentNodeLimit nodes)) }

    /// Charge reads and enumeration to the caller's operation without replacing the snapshot or its cursors.
    member snapshot.WithWorkBudget(work: BoundedTreeWorkBudget) =
        { snapshot with Work = Some work }

type private ModuleInventory = {
    Path: ModulePath
    Access: Morphir.IR.V4.Access option
    Types: Name list
    Values: Name list
    InlineTypes: Set<Name>
    InlineValues: Set<Name>
    FileNames: Map<Name, string>
}

type private BoundedTreeFile = {
    Input: MorphirFile<JsonElement>
    UnrepresentableEnvelopeMember: string option
    Bytes: byte array
    ContextLimits: ContextLimits
    Nodes: int
    NodeLimit: int option
    Work: BoundedTreeWorkBudget option
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

    let private readRoot encoding depthLimit nodeLimit byteLimit work source expected pathBudget logical =
        let limits = ContextLimits.draft1.WithMaxDepth depthLimit

        let plainOnly location =
            refuse
                "tree.unsupported_context"
                (Some location)
                "This reader accepts plain tree files."

        let order =
            match encoding with
            | BoundedTreeEncoding.Plain -> ContextOrder.WholeFile
            | BoundedTreeEncoding.ContextualDraft1 -> ContextOrder.Bounded
            | BoundedTreeEncoding.ContextualDraft1Streaming -> ContextOrder.Streaming

        let parse profile location (bytes: byte array) =
            match profile with
            | TreeFileSource.Json ->
                let encoded = ReadOnlyMemory bytes
                ContextExpansion.countJsonNodes limits.MaxDepth nodeLimit location encoded
                |> Result.bind (fun nodes ->
                    MorphirFile.readEnvelopeWithOrderAndInspect order limits location encoded
                    |> Result.bind (fun (input, unrepresentable) ->
                        if encoding = BoundedTreeEncoding.Plain && input.Context.IsSome then
                            plainOnly location
                        else
                            Ok(input, unrepresentable, nodes)))
            | TreeFileSource.Yaml when encoding = BoundedTreeEncoding.Plain ->
                TreeYaml.readCounted false limits.MaxDepth (defaultArg nodeLimit Int32.MaxValue) location bytes
                |> Result.bind (fun (document, nodes) ->
                    let mutable schema = Unchecked.defaultof<JsonElement>

                    if document.TryGetProperty("$schema", &schema) then
                        plainOnly location
                    else
                        Ok(
                            {
                                Schema = None
                                Context = None
                                Document = document
                            },
                            None,
                            nodes
                        )
                )
            | TreeFileSource.Yaml ->
                TreeYaml.readCounted true limits.MaxDepth (defaultArg nodeLimit Int32.MaxValue) location bytes
                |> Result.bind (fun (root, nodes) ->
                    MorphirFile.readEnvelopeRootAndInspect order limits location root
                    |> Result.map (fun (input, unrepresentable) -> input, unrepresentable, nodes))

        TreeFileSource.snapshotLocation work source expected logical
        |> TreeFileSource.one
        |> Result.bind (fun (profile, location) ->
            if location.Length > pathBudget then
                refuse
                    "tree.over_budget"
                    (Some location)
                    "The file path exceeds the manifest path budget."
            else
                TreeFileSource.snapshotBytes byteLimit source location work
                |> TreeFileSource.one
                |> Result.bind (fun bytes ->
                    parse profile location bytes
                    |> Result.map (fun (document, unrepresentable, nodes) ->
                        profile, location, {
                            Input = document
                            UnrepresentableEnvelopeMember = unrepresentable
                            Bytes = bytes
                            ContextLimits = limits
                            Nodes = nodes
                            NodeLimit = nodeLimit
                            Work = work
                        }
                    )
                )
        )

    let private decodeAt location decode root =
        decode root
        |> Result.mapError (fun (error: V4JsonError) -> [
            StoreErrors.error error.Rule (Some location) $"{error.Location}: {error.Message}"
        ])

    let private expandRootCounted limit family location projection (file: BoundedTreeFile) =
        match file.Input.Context with
        | None -> Ok(file.Input.Document, file.Nodes)
        | Some context ->
            let checkVersion =
                match family with
                | IrFamily.Classic3 -> ClassicTreeFiles.checkFileVersion
                | IrFamily.V4 -> V4TreeFiles.checkFileVersion

            decodeAt location checkVersion file.Input.Document
            |> Result.bind (fun () ->
                ContextExpansion.expandBounded
                    file.ContextLimits
                    limit
                    file.NodeLimit
                    file.Work
                    family
                    location
                    context
                    projection
                    file.Input.Document
                |> Result.map (fun (document, nodes) -> document, max nodes file.Nodes)
            )

    let private expandRoot limit family location projection file =
        expandRootCounted limit family location projection file |> Result.map fst

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

    let private newCursorKey () =
        use random = RandomNumberGenerator.Create()
        let key = Array.zeroCreate<byte> 32
        random.GetBytes key
        key

    let openTreeWithEncoding
        encoding
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
                    readRoot encoding ContextLimits.draft1.MaxDepth None byteLimit None source None Int32.MaxValue "manifest"
                    |> Result.bind (fun (profile, location, file) ->
                        let decode document =
                            if TreeFileSource.isClassicManifest document then
                                ClassicTreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.Classic
                            else
                                V4TreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.V4

                        expandRoot
                            byteLimit
                            (MorphirFile.familyOf file.Input.Document)
                            location
                            ContextExpansion.Projection.Complete
                            file
                        |> Result.bind (decodeAt location decode)
                        |> Result.bind (validateManifest location)
                        |> Result.map (fun manifest -> {
                            Source = source
                            Revision = revision
                            Identity = obj ()
                            CursorKey = newCursorKey ()
                            ByteLimit = byteLimit
                            InventoryByteLimit = byteLimit
                            DocumentDepthLimit = ContextLimits.draft1.MaxDepth
                            DocumentNodeLimit = None
                            Work = None
                            Cancellation = System.Threading.CancellationToken.None
                            Encoding = encoding
                            Profile = profile
                            Manifest = manifest
                        })
                    )
                )

    let openTree byteLimit source =
        openTreeWithEncoding BoundedTreeEncoding.Plain byteLimit source

    let private family snapshot =
        match snapshot.Manifest with
        | BoundedTreeManifest.Classic _ -> IrFamily.Classic3
        | BoundedTreeManifest.V4 _ -> IrFamily.V4

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

    let private requireInlineOnly work source logical =
        match TreeFileSource.snapshotLocation work source None logical with
        | Ok(_, sidecar) ->
            refuse
                "tree.duplicate_document"
                (Some sidecar)
                "The selected member is both inline and split."
        | Error problem when problem.Code = "tree.missing_member" -> Ok()
        | Error problem -> Error [ problem ]

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
                    snapshot.Encoding
                    snapshot.DocumentDepthLimit
                    snapshot.DocumentNodeLimit
                    snapshot.ByteLimit
                    snapshot.Work
                    snapshot.Source
                    (Some snapshot.Profile)
                    pathBudget
                    logical
                |> Result.bind (fun (_, location, file) ->
                    expandRoot
                        snapshot.InventoryByteLimit
                        (family snapshot)
                        location
                        ContextExpansion.Projection.ModuleInventory
                        file
                    |> Result.bind (moduleInventory snapshot location holdsDefinitions)
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
                                action own location holdsDefinitions pathBudget validated file
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
                            snapshot.Source.NextModule snapshot.Work snapshot.Cancellation pathBudget prefix previous
                            |> TreeFileSource.one
                        with
                        | :? OperationCanceledException -> reraise ()
                        | ex ->
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

    let private selected location (file: BoundedTreeFile) body = {
        Body = body
        File = location
        Encoded = ReadOnlyMemory file.Bytes
    }

    let private inlineField suffix =
        if suffix = ".type" then "types" else "values"

    let private inlineProperty field name (root: JsonElement) =
        // Inventory validates uniqueness by parsed Name. Use the property's original spelling.
        root.GetProperty(field: string).EnumerateObject()
        |> Seq.tryFind (fun property -> CanonicalNames.parseName property.Name = Ok name)

    let private decodeInlineBody snapshot location holdsDefinitions suffix memberLocation item =
        match snapshot.Manifest, suffix with
        | BoundedTreeManifest.Classic _, ".type" ->
            decodeAt location (ClassicTreeFiles.readInlineType holdsDefinitions memberLocation) item
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

    let private inlineBody snapshot location holdsDefinitions suffix name (file: BoundedTreeFile) =
        let field = inlineField suffix
        let root = file.Input.Document

        match inlineProperty field name root with
        | None -> refuse "tree.missing_member" (Some location) "The inline member is missing."
        | Some property ->
            let payload = property.Value

            let memberLocation =
                V4JsonDecoding.memberAt (V4JsonDecoding.memberAt "$" field) property.Name

            let decode =
                decodeInlineBody snapshot location holdsDefinitions suffix memberLocation

            let decoded =
                match file.Input.Context with
                | Some context ->
                    ContextExpansion.expandMember
                        file.ContextLimits
                        snapshot.ByteLimit
                        file.NodeLimit
                        file.Work
                        (family snapshot)
                        location
                        context
                        (suffix = ".type")
                        holdsDefinitions
                        memberLocation
                        payload
                    |> Result.bind decode
                | None ->
                    // Classic attributes retain JsonElements. Keep only the selected slice.
                    use slice =
                        JsonDocument.Parse(
                            payload.GetRawText(),
                            JsonDocumentOptions(MaxDepth = file.ContextLimits.MaxDepth)
                        )

                    decode slice.RootElement

            decoded
            |> Result.map (selected location file)

    let private decodeSplitBody snapshot location holdsDefinitions suffix name document =
        let read decode wrap =
            decodeAt location decode document
            |> Result.bind (fun (stated, document) ->
                if stated = name then
                    Ok(wrap document)
                else
                    refuse
                        "tree.name_mismatch"
                        (Some location)
                        "The file's name does not match the selected member."
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

    let private splitBodyCounted snapshot location holdsDefinitions suffix name file =
        expandRootCounted
            snapshot.ByteLimit
            (family snapshot)
            location
            ContextExpansion.Projection.Complete
            file
        |> Result.bind (fun (document, nodes) ->
            decodeSplitBody snapshot location holdsDefinitions suffix name document
            |> Result.map (fun body -> selected location file body, nodes))

    let private splitBody snapshot location holdsDefinitions suffix name file =
        splitBodyCounted snapshot location holdsDefinitions suffix name file |> Result.map fst

    let private selectMember
        (address: DocumentAddress)
        (snapshot: BoundedTreeSnapshot)
        byteLimit
        nodeLimit
        onInline
        onSplit
        =
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
                        (fun own moduleFile holdsDefinitions pathBudget inventory file ->
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
                                    requireInlineOnly snapshot.Work snapshot.Source logical
                                    |> Result.bind (fun () ->
                                        onInline moduleFile holdsDefinitions suffix name file
                                    )
                                else
                                    readRoot
                                        snapshot.Encoding
                                        snapshot.DocumentDepthLimit
                                        nodeLimit
                                        byteLimit
                                        snapshot.Work
                                        snapshot.Source
                                        (Some snapshot.Profile)
                                        pathBudget
                                        logical
                                    |> Result.bind (fun (_, location, split) ->
                                        onSplit location holdsDefinitions suffix name split
                                    )
                        )
            )

    /// Resolve a member in the admitted module and decode only its selected body.
    let readMemberWithArtifact address snapshot =
        selectMember address snapshot snapshot.ByteLimit snapshot.DocumentNodeLimit (inlineBody snapshot) (splitBody snapshot)

    let private invalidDocumentSelection () =
        refuse
            "tree.invalid_selection"
            None
            "Expected a logical document in the admitted package or its dependencies."

    let private logicalAddress (documentId: string) snapshot =
        let own, dependencies, _, _, pathBudget = ownership snapshot.Manifest
        let ownPrefix = TreeLayout.packageDirectory own None

        let scope =
            if documentId.StartsWith(ownPrefix, StringComparison.Ordinal) then
                Some(ownPrefix, Own)
            else
                dependencies
                |> List.tryPick (fun package ->
                    let prefix = TreeLayout.packageDirectory own (Some package)

                    if documentId.StartsWith(prefix, StringComparison.Ordinal) then
                        Some(prefix, (fun address -> Dependency(package, address)))
                    else
                        None
                )

        match scope with
        | None -> invalidDocumentSelection ()
        | Some(prefix, wrap) ->
            let lastSlash = documentId.LastIndexOf('/')

            let node =
                documentId.Substring(
                    lastSlash
                    + 1
                )

            let moduleId =
                documentId.Substring(0, lastSlash)
                + "/module"

            match modulePathFromMetadata prefix pathBudget None moduleId with
            | Error _ -> invalidDocumentSelection ()
            | Ok path when node = "module" -> Ok(wrap (Module path))
            | Ok path ->
                let memberOf (suffix: string) makeAddress =
                    if node.EndsWith(suffix, StringComparison.Ordinal) then
                        let stem =
                            node.Substring(
                                0,
                                node.Length
                                - suffix.Length
                            )

                        match TreeStems.parseStem stem with
                        | Ok(WholeStem name) when TreeStems.escapeName name = stem ->
                            Some(wrap (makeAddress (path, name)))
                        | _ -> None
                    else
                        None

                memberOf ".type" TypeDef
                |> Option.orElseWith (fun () -> memberOf ".value" ValueDef)
                |> function
                    | Some address -> Ok address
                    | None -> invalidDocumentSelection ()

    // Retain one fixed key per snapshot. Each continuation carries its own position.
    let private cursorSignature
        (snapshot: BoundedTreeSnapshot)
        (selection: string)
        (encoded: string)
        =
        use hash = new HMACSHA256(snapshot.CursorKey)

        hash.ComputeHash(
            Encoding.UTF8.GetBytes(
                selection
                + "\n"
                + encoded
            )
        )

    let private sealCursor snapshot selection (position: string) =
        let encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes position)

        encoded
        + "."
        + Convert.ToBase64String(cursorSignature snapshot selection encoded)

    let private invalidCursor () =
        refuse
            "tree.invalid_cursor"
            None
            "The continuation does not belong to this snapshot and selection."

    let private openCursor snapshot selection (pathBudget: int) (token: string) =
        // The longest position contains a package index and a path within the manifest budget.
        if
            String.IsNullOrEmpty token
            || int64 token.Length > int64 pathBudget
                                    * 4L
                                    + 96L
        then
            invalidCursor ()
        else
            let separator = token.LastIndexOf('.')

            if
                separator < 1
                || token.Length
                   - separator
                   - 1
                   <> 44
            then
                invalidCursor ()
            else
                try
                    let encoded = token.Substring(0, separator)

                    let supplied =
                        Convert.FromBase64String(
                            token.Substring(
                                separator
                                + 1
                            )
                        )

                    let expected = cursorSignature snapshot selection encoded

                    if
                        supplied.Length
                        <> expected.Length
                    then
                        invalidCursor ()
                    else
                        let mutable difference = 0uy

                        for index in
                            0 .. expected.Length
                                 - 1 do
                            difference <-
                                difference
                                ||| (expected[index]
                                     ^^^ supplied[index])

                        if
                            difference
                            <> 0uy
                        then
                            invalidCursor ()
                        else
                            Ok(Encoding.UTF8.GetString(Convert.FromBase64String encoded))
                with :? FormatException ->
                    invalidCursor ()

    let private parseOffset (position: string) =
        match Int32.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, offset -> Ok offset
        | _ -> invalidCursor ()

    let private rootPage limit position (snapshot: BoundedTreeSnapshot) =
        let packages =
            snapshot.Package
            :: snapshot.Dependencies

        let start =
            match position with
            | None -> Ok(0, None)
            | Some(text: string) ->
                let separator = text.IndexOf('\n')

                if separator < 1 then
                    invalidCursor ()
                else
                    let after =
                        text.Substring(
                            separator
                            + 1
                        )

                    parseOffset (text.Substring(0, separator))
                    |> Result.map (fun index -> index, (if after = "" then None else Some after))

        let continuation (index: int) after =
            let offset = index.ToString(CultureInfo.InvariantCulture)

            sealCursor
                snapshot
                "root"
                (offset
                 + "\n"
                 + after)
            |> Some

        let rec collect index previous remaining reversed =
            match List.tryItem index packages with
            | None ->
                Ok
                    {
                        Entries = List.rev reversed
                        Continuation = None
                    }
            | Some _ when remaining = 0 ->
                Ok
                    {
                        Entries = List.rev reversed
                        Continuation = continuation index ""
                    }
            | Some package ->
                let cursor =
                    previous
                    |> Option.map (fun logical ->
                        BoundedTreeModuleCursor(snapshot.Identity, package, logical)
                    )

                modules remaining cursor package snapshot
                |> Result.bind (fun page ->
                    let current =
                        List.fold
                            (fun entries item ->
                                BoundedTreeInventoryEntry.ModuleEntry item
                                :: entries
                            )
                            reversed
                            page.Items

                    match page.Next with
                    | Some(BoundedTreeModuleCursor(_, _, next)) ->
                        Ok
                            {
                                Entries = List.rev current
                                Continuation = continuation index next
                            }
                    | None ->
                        collect
                            (index + 1)
                            None
                            (remaining
                             - page.Items.Length)
                            current
                )

        start
        |> Result.bind (fun (index, previous) -> collect index previous limit [])

    let private memberInventory selection limit position snapshot =
        let start =
            match position with
            | None -> Ok 0
            | Some encoded -> parseOffset encoded

        start
        |> Result.bind (fun offset ->
            logicalAddress selection snapshot
            |> Result.bind (fun address ->
                if (TreeLayout.memberOf address).IsSome then
                    invalidDocumentSelection ()
                else
                    withModule
                        snapshot
                        address
                        (fun own _ holdsDefinitions _ inventory _ ->
                            let page = memberPage own snapshot address offset limit inventory

                            let role =
                                if holdsDefinitions then
                                    BoundedTreeRole.Definition
                                else
                                    BoundedTreeRole.Specification

                            Ok
                                {
                                    Entries =
                                        page.Items
                                        |> List.map (fun item ->
                                            BoundedTreeInventoryEntry.MemberEntry(item, role)
                                        )
                                    Continuation =
                                        page.Next
                                        |> Option.map (fun (BoundedTreeCursor(_, _, next)) ->
                                            sealCursor
                                                snapshot
                                                selection
                                                (next.ToString(CultureInfo.InvariantCulture))
                                        )
                                }
                        )
            )
        )

    let pageInventory (selection: string) limit continuation snapshot =
        atRevision
            snapshot.Source
            snapshot.Revision
            (fun () ->
                let _, _, _, _, pathBudget = ownership snapshot.Manifest

                if
                    limit < 1
                    || limit > 256
                then
                    refuse "tree.invalid_page_limit" None "Page size must be between 1 and 256."
                elif
                    String.IsNullOrEmpty selection
                    || selection.Length > pathBudget
                then
                    invalidDocumentSelection ()
                else
                    let position =
                        match continuation with
                        | None -> Ok None
                        | Some token ->
                            openCursor snapshot selection pathBudget token
                            |> Result.map Some

                    position
                    |> Result.bind (fun current ->
                        if selection = "root" then
                            rootPage limit current snapshot
                        else
                            memberInventory selection limit current snapshot
                    )
            )

    let private withScopedDocument
        documentId
        byteLimit
        nodeLimit
        snapshot
        readRootDocument
        readModuleDocument
        readSplitDocument
        =
        atRevision
            snapshot.Source
            snapshot.Revision
            (fun () ->
                if
                    byteLimit
                    <= 0L
                then
                    refuse
                        "tree.file_too_large"
                        None
                        "No document bytes remain in the delivery budget."
                elif nodeLimit |> Option.exists (fun remaining -> remaining <= 0) then
                    refuse
                        "tree.document_node_limit"
                        None
                        "No decoded maxNodes budget remains for this exchange."
                elif documentId = "manifest" then
                    readRoot
                        snapshot.Encoding
                        snapshot.DocumentDepthLimit
                        nodeLimit
                        byteLimit
                        snapshot.Work
                        snapshot.Source
                        (Some snapshot.Profile)
                        Int32.MaxValue
                        documentId
                    |> Result.bind (fun (_, location, file) -> readRootDocument location file)
                elif String.IsNullOrEmpty documentId then
                    invalidDocumentSelection ()
                else
                    logicalAddress documentId snapshot
                    |> Result.bind (fun address ->
                        match TreeLayout.memberOf address with
                        | None ->
                            withModule
                                { snapshot with ByteLimit = byteLimit; DocumentNodeLimit = nodeLimit }
                                address
                                (fun own location definitions _ inventory file ->
                                    readModuleDocument
                                        own
                                        address
                                        location
                                        definitions
                                        inventory
                                        file
                                )
                        | Some _ ->
                            selectMember
                                address
                                snapshot
                                byteLimit
                                nodeLimit
                                // Inline members have the module's logical document identity.
                                (fun _ _ _ _ _ -> invalidDocumentSelection ())
                                readSplitDocument
                    )
            )

    let private readDocumentWithLimits documentId maxBytes nodeLimit snapshot =
        let original location (file: BoundedTreeFile) =
            if file.NodeLimit.IsNone || file.Input.Context.IsNone then
                Ok(ReadOnlyMemory file.Bytes, file.Nodes)
            else
                expandRootCounted snapshot.ByteLimit (family snapshot) location ContextExpansion.Projection.Complete file
                |> Result.map (fun (_, nodes) -> ReadOnlyMemory file.Bytes, nodes)
        let originalModule _ _ location _ _ file = original location file

        let selected location definitions suffix name file =
            splitBodyCounted snapshot location definitions suffix name file
            |> Result.map (fun (selection, nodes) -> selection.Encoded, nodes)

        withScopedDocument
            documentId
            (min maxBytes snapshot.ByteLimit)
            nodeLimit
            snapshot
            original
            originalModule
            selected

    let readDocumentWithinCounted documentId maxBytes maxNodes snapshot =
        let nodeLimit = Some(min maxNodes (defaultArg snapshot.DocumentNodeLimit maxNodes))
        readDocumentWithLimits documentId maxBytes nodeLimit snapshot

    let readDocumentWithin documentId maxBytes snapshot =
        readDocumentWithLimits documentId maxBytes snapshot.DocumentNodeLimit snapshot
        |> Result.map fst

    let readDocument documentId (snapshot: BoundedTreeSnapshot) =
        readDocumentWithin documentId snapshot.ByteLimit snapshot

    let private withInventoryBudget requested snapshot =
        { snapshot with
            InventoryByteLimit = max requested snapshot.InventoryByteLimit
        }

    let internal withDepthAndCancellation depthLimit cancellationToken (snapshot: BoundedTreeSnapshot) =
        { snapshot with
            DocumentDepthLimit = min depthLimit snapshot.DocumentDepthLimit
            Cancellation = cancellationToken
        }

    let internal readExpandedDocumentWithSource rejectUnrepresentableEnvelope documentId outputBytes snapshot =
        if
            outputBytes
            <= 0L
        then
            refuse "tree.file_too_large" None "No document bytes remain in the export budget."
        else
            let _, _, _, _, pathBudget = ownership snapshot.Manifest

            let serialize location (document: JsonElement) =
                let output = System.IO.Path.ChangeExtension(location, ".json")

                if output.Length > pathBudget then
                    refuse
                        "tree.over_budget"
                        (Some location)
                        "The expanded path exceeds the manifest path budget."
                else
                    let text = document.GetRawText()

                    if int64 (Encoding.UTF8.GetByteCount text) > outputBytes then
                        refuse
                            "tree.file_too_large"
                            (Some location)
                            "Expanded names exceed the document byte limit."
                    else
                        Ok(
                            location,
                            {
                                File = output
                                Bytes = ReadOnlyMemory(Encoding.UTF8.GetBytes text)
                            }
                        )

            let checkEnvelope location (file: BoundedTreeFile) =
                if rejectUnrepresentableEnvelope then
                    match file.UnrepresentableEnvelopeMember with
                    | Some memberName ->
                        refuse
                            "data.lossy_conversion"
                            (Some location)
                            $"Converting document tree to single-document JSON would drop '{memberName}' in {location}; lossy conversion is refused by default."
                    | None -> Ok()
                else
                    Ok()

            let expand location file =
                checkEnvelope location file
                |> Result.bind (fun () ->
                    expandRoot
                        outputBytes
                        (family snapshot)
                        location
                        ContextExpansion.Projection.Complete
                        file
                )

            let root location file =
                expand location file
                |> Result.bind (serialize location)

            let moduleFile own address location definitions inventory file =
                let directory = TreeLayout.directoryOf own address

                let validateInline (document: JsonElement) suffix =
                    let field = inlineField suffix
                    let fieldLocation = V4JsonDecoding.memberAt "$" field

                    let check (property: JsonProperty) =
                        match CanonicalNames.parseName property.Name with
                        | Error _ -> invalidDocumentSelection ()
                        | Ok name ->
                            let logical =
                                directory
                                + memberStem inventory name
                                + suffix

                            let memberLocation = V4JsonDecoding.memberAt fieldLocation property.Name

                            requireInlineOnly snapshot.Work snapshot.Source logical
                            |> Result.bind (fun () ->
                                decodeInlineBody
                                    snapshot
                                    location
                                    definitions
                                    suffix
                                    memberLocation
                                    property.Value
                                |> Result.map ignore
                            )

                    match document.TryGetProperty(field) with
                    | true, members when members.ValueKind = JsonValueKind.Object ->
                        members.EnumerateObject()
                        |> Seq.fold
                            (fun prior property ->
                                prior
                                |> Result.bind (fun () -> check property)
                            )
                            (Ok())
                    | _ -> Ok()

                expand location file
                |> Result.bind (fun document ->
                    validateInline document ".type"
                    |> Result.bind (fun () -> validateInline document ".value")
                    |> Result.bind (fun () -> serialize location document)
                )

            let split location definitions suffix name file =
                expand location file
                |> Result.bind (fun document ->
                    decodeSplitBody snapshot location definitions suffix name document
                    |> Result.bind (fun _ -> serialize location document)
                )

            withScopedDocument documentId snapshot.ByteLimit snapshot.DocumentNodeLimit (withInventoryBudget outputBytes snapshot) root moduleFile split

    let readExpandedDocument documentId outputBytes snapshot =
        readExpandedDocumentWithSource false documentId outputBytes snapshot
        |> Result.map snd

    let internal writeExpandedTreeWithSources rejectUnrepresentableEnvelope limits (stageFile: string -> BoundedTreeExportDocument -> Result<unit, StoreError list>) snapshot =
        if
            limits.MaxDocumentBytes
            <= 0L
            || limits.MaxTotalBytes
               <= 0L
        then
            refuse "tree.file_too_large" None "Export limits must be positive."
        elif
            limits.PageSize < 1
            || limits.PageSize > 256
        then
            refuse "tree.invalid_page_limit" None "Page size must be between 1 and 256."
        else
            let snapshot = withInventoryBudget limits.MaxDocumentBytes snapshot

            atRevision
                snapshot.Source
                snapshot.Revision
                (fun () ->
                    let mutable remaining = limits.MaxTotalBytes

                    let stage documentId =
                        readExpandedDocumentWithSource
                            rejectUnrepresentableEnvelope
                            documentId
                            (min remaining limits.MaxDocumentBytes)
                            snapshot
                        |> Result.bind (fun (sourceLocation, file) ->
                            remaining <-
                                remaining
                                - int64 file.Bytes.Length

                            stageFile sourceLocation file
                        )

                    let writePages selection stageEntry =
                        let mutable continuation = None
                        let mutable pending = true
                        let mutable result = Ok()

                        while pending
                              && Result.isOk result do
                            match
                                pageInventory selection limits.PageSize continuation snapshot
                            with
                            | Error errors -> result <- Error errors
                            | Ok page ->
                                result <-
                                    page.Entries
                                    |> List.fold
                                        (fun prior entry ->
                                            prior
                                            |> Result.bind (fun () -> stageEntry entry)
                                        )
                                        (Ok())

                                continuation <- page.Continuation
                                pending <- continuation.IsSome

                        result

                    let writeModule (item: BoundedTreeModule) =
                        let moduleId = item.DocumentId

                        stage moduleId
                        |> Result.bind (fun () ->
                            writePages
                                moduleId
                                (function
                                | BoundedTreeInventoryEntry.MemberEntry(memberInfo, _) when
                                    memberInfo.DocumentId = moduleId
                                    ->
                                    Ok()
                                | BoundedTreeInventoryEntry.MemberEntry(memberInfo, _) ->
                                    stage memberInfo.DocumentId
                                | _ -> invalidDocumentSelection ()
                                )
                        )

                    stage "manifest"
                    |> Result.bind (fun () ->
                        writePages
                            "root"
                            (function
                            | BoundedTreeInventoryEntry.ModuleEntry item -> writeModule item
                            | _ -> invalidDocumentSelection ()
                            )
                    )
                )

    let writeExpandedTree limits stageFile snapshot =
        writeExpandedTreeWithSources false limits (fun _ doc -> stageFile doc) snapshot

    let openTreeDetectedBounded
        (byteLimit: int64)
        (depthLimit: int)
        (nodeLimit: int option)
        (workBudget: BoundedTreeWorkBudget option)
        (cancellationToken: System.Threading.CancellationToken)
        (source: TreeFileSource.SnapshotSource)
        : Result<BoundedTreeSnapshot, StoreError list> =
        if byteLimit <= 0L then
            refuse "tree.invalid_limit" None "A document byte limit must be positive."
        elif depthLimit <= 0 then
            refuse "tree.invalid_limit" None "A document depth limit must be positive."
        else
            cancellationToken.ThrowIfCancellationRequested()
            let effectiveDepth = min ContextLimits.draft1.MaxDepth depthLimit
            let revision = source.Revision()

            atRevision
                source
                revision
                (fun () ->
                    readRoot
                        BoundedTreeEncoding.ContextualDraft1
                        effectiveDepth
                        nodeLimit
                        byteLimit
                        workBudget
                        source
                        None
                        Int32.MaxValue
                        "manifest"
                    |> Result.bind (fun (profile, location, file) ->
                        let mutable foundContextual = file.Input.Context.IsSome

                        let decode document =
                            if TreeFileSource.isClassicManifest document then
                                ClassicTreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.Classic
                            else
                                V4TreeFiles.readManifest document
                                |> Result.map BoundedTreeManifest.V4

                        expandRoot
                            byteLimit
                            (MorphirFile.familyOf file.Input.Document)
                            location
                            ContextExpansion.Projection.Complete
                            file
                        |> Result.bind (decodeAt location decode)
                        |> Result.bind (validateManifest location)
                        |> Result.bind (fun manifest ->
                            let candidate: BoundedTreeSnapshot = {
                                Source = source
                                Revision = revision
                                Identity = obj ()
                                CursorKey = newCursorKey ()
                                ByteLimit = byteLimit
                                InventoryByteLimit = byteLimit
                                DocumentDepthLimit = effectiveDepth
                                DocumentNodeLimit = nodeLimit
                                Work = workBudget
                                Cancellation = cancellationToken
                                Encoding = BoundedTreeEncoding.ContextualDraft1
                                Profile = profile
                                Manifest = manifest
                            }

                            let checkDocument documentId =
                                cancellationToken.ThrowIfCancellationRequested()

                                let inspectFile (doc: BoundedTreeFile) =
                                    if doc.Input.Context.IsSome then
                                        foundContextual <- true

                                    Ok()

                                withScopedDocument
                                    documentId
                                    candidate.ByteLimit
                                    candidate.DocumentNodeLimit
                                    candidate
                                    (fun _ doc -> inspectFile doc)
                                    (fun _ _ _ _ _ doc -> inspectFile doc)
                                    (fun _ _ _ _ doc -> inspectFile doc)

                            let scanPages selection handleEntry =
                                let mutable continuation = None
                                let mutable pending = true
                                let mutable result = Ok()

                                while pending && Result.isOk result do
                                    cancellationToken.ThrowIfCancellationRequested()

                                    match pageInventory selection 64 continuation candidate with
                                    | Error errors -> result <- Error errors
                                    | Ok page ->
                                        result <-
                                            page.Entries
                                            |> List.fold
                                                (fun prior entry ->
                                                    prior
                                                    |> Result.bind (fun () -> handleEntry entry)
                                                )
                                                (Ok())

                                        continuation <- page.Continuation
                                        pending <- continuation.IsSome

                                result

                            let scanModule (item: BoundedTreeModule) =
                                let moduleId = item.DocumentId

                                checkDocument moduleId
                                |> Result.bind (fun () ->
                                    scanPages
                                        moduleId
                                        (function
                                        | BoundedTreeInventoryEntry.MemberEntry(memberInfo, _) when
                                            memberInfo.DocumentId = moduleId
                                            ->
                                            Ok()
                                        | BoundedTreeInventoryEntry.MemberEntry(memberInfo, _) ->
                                            checkDocument memberInfo.DocumentId
                                        | _ -> invalidDocumentSelection ())
                                )

                            scanPages
                                "root"
                                (function
                                | BoundedTreeInventoryEntry.ModuleEntry item -> scanModule item
                                | _ -> invalidDocumentSelection ())
                            |> Result.map (fun () ->
                                { candidate with
                                    Encoding =
                                        if foundContextual then
                                            BoundedTreeEncoding.ContextualDraft1
                                        else
                                            BoundedTreeEncoding.Plain
                                    DocumentNodeLimit = None
                                    Work = None
                                    Cancellation = System.Threading.CancellationToken.None
                                }
                            )
                        )
                    )
                )

    let openTreeDetected
        byteLimit
        (source: TreeFileSource.SnapshotSource)
        : Result<BoundedTreeSnapshot, StoreError list> =
        openTreeDetectedBounded
            byteLimit
            ContextLimits.draft1.MaxDepth
            None
            None
            System.Threading.CancellationToken.None
            source

    let readMember address snapshot =
        readMemberWithArtifact address snapshot
        |> Result.map (fun selection -> selection.Body)

    let resolveMemberAlias address (path: IReadOnlyList<string>) snapshot =
        let tail = List.ofSeq path
        let selectedFamily = family snapshot

        let resolve
            location
            (file: BoundedTreeFile)
            holdsDefinitions
            suffix
            memberLocation
            payload
            =
            match file.Input.Context with
            | None ->
                refuse
                    "context.not_an_alias"
                    (Some location)
                    "The selected file has no file-local context."
            | Some context ->
                ContextExpansion.resolveMemberAlias
                    snapshot.ByteLimit
                    selectedFamily
                    location
                    context
                    (suffix = ".type")
                    holdsDefinitions
                    memberLocation
                    tail
                    payload

        let inlineAlias location holdsDefinitions suffix name (file: BoundedTreeFile) =
            let field = inlineField suffix

            match inlineProperty field name file.Input.Document with
            | Some property ->
                let memberLocation =
                    V4JsonDecoding.memberAt (V4JsonDecoding.memberAt "$" field) property.Name

                resolve location file holdsDefinitions suffix memberLocation property.Value
            | None -> refuse "tree.missing_member" (Some location) "The inline member is missing."

        let splitAlias location holdsDefinitions suffix name (file: BoundedTreeFile) =
            let header =
                match selectedFamily with
                | IrFamily.Classic3 -> ClassicTreeFiles.readNodeHeader holdsDefinitions
                | IrFamily.V4 -> V4TreeFiles.readNodeHeader holdsDefinitions

            expandRoot
                snapshot.ByteLimit
                selectedFamily
                location
                ContextExpansion.Projection.MemberHeader
                file
            |> Result.bind (decodeAt location header)
            |> Result.bind (fun (stated, ()) ->
                if
                    stated
                    <> name
                then
                    refuse
                        "tree.name_mismatch"
                        (Some location)
                        "The file's name does not match the selected member."
                else
                    let field = if holdsDefinitions then "def" else "spec"

                    resolve
                        location
                        file
                        holdsDefinitions
                        suffix
                        (V4JsonDecoding.memberAt "$" field)
                        (file.Input.Document.GetProperty field)
            )

        selectMember address snapshot snapshot.ByteLimit snapshot.DocumentNodeLimit inlineAlias splitAlias
