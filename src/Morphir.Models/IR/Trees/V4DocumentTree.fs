namespace Morphir.IR.Trees

open System.Threading
open Morphir.IR
open Morphir.IR.V4

/// <summary>The manifest of a v4 tree: what kind of distribution it is, its package and its dependencies.</summary>
type V4Manifest = {
    /// <summary>The kind of distribution.</summary>
    Kind: V4DistributionKind
    /// <summary>The own package, whose documents are at <c>Own</c> addresses.</summary>
    Package: PackageName
    /// <summary>Every dependency package in model order, including one with no modules.</summary>
    Dependencies: PackageName list
    /// <summary>The entry points in order, which only an <c>Application</c> has.</summary>
    EntryPoints: (string * EntryPoint) list
}

/// <summary>The kind of a v4 distribution.</summary>
and [<RequireQualifiedAccess>] V4DistributionKind =
    /// <summary>A <c>Library</c>: own definitions, dependency specifications.</summary>
    | LibraryKind
    /// <summary>A <c>Specs</c>: own specifications, dependency specifications.</summary>
    | SpecsKind
    /// <summary>An <c>Application</c>: own definitions, dependency definitions, entry points.</summary>
    | ApplicationKind

/// <summary>
/// A v4 module document: the module's own members, and the names of its types and values in document order. The
/// types and values themselves are documents of their own.
/// </summary>
type V4ModuleDocument = {
    /// <summary>The module's access in a package definition, and none in a package specification.</summary>
    Access: Access option
    /// <summary>The module's documentation.</summary>
    Doc: string option
    /// <summary>The annotations of a module specification; a module definition has none.</summary>
    Annotations: Annotation list
    /// <summary>The module's types in document order.</summary>
    TypeNames: Name list
    /// <summary>The module's values in document order.</summary>
    ValueNames: Name list
}

/// <summary>A v4 type document: a definition in a package definition, a specification in a package specification.</summary>
type V4TypeDocument =
    /// <summary>A type of a package definition.</summary>
    | DefinedType of AccessControlled<Documented<TypeDefinition>>
    /// <summary>A type of a package specification.</summary>
    | SpecifiedType of Documented<TypeSpecification>

/// <summary>A v4 value document: a definition in a package definition, a specification in a package specification.</summary>
type V4ValueDocument =
    /// <summary>A value of a package definition.</summary>
    | DefinedValue of AccessControlled<Documented<ValueDefinition>>
    /// <summary>A value of a package specification.</summary>
    | SpecifiedValue of Documented<ValueSpecification>

/// <summary>A v4 model as a document tree.</summary>
type V4DocumentTree = DocumentTree<V4Manifest, V4ModuleDocument, V4TypeDocument, V4ValueDocument>

/// <summary>Splits a v4 model into a document tree and joins a tree back into the model.</summary>
/// <remarks>
/// <para>
/// A <c>Library</c> holds its own definitions and its dependencies' specifications, a <c>Specs</c> holds
/// specifications only, and an <c>Application</c> holds definitions only. Each type and value document holds the
/// case its package needs: <c>DefinedType</c> and <c>DefinedValue</c> in a definition, <c>SpecifiedType</c> and
/// <c>SpecifiedValue</c> in a specification.
/// </para>
/// <para>
/// <c>join</c> refuses an inconsistent tree with every error it finds, and never gives a partial model. The codes:
/// <c>tree.missing_definition</c>, a module document lists a type or value that has no document;
/// <c>tree.unlisted_definition</c>, a type or value document that no module document lists;
/// <c>tree.mismatched_document</c>, a document that does not fit its address or its package: a value document at
/// a type address, a specification in a definition, a module definition with no access or with annotations, a
/// module specification with an access, or a document at the manifest address;
/// <c>tree.duplicate_name</c>, a module document lists a name twice;
/// <c>tree.duplicate_dependency</c>, the manifest lists a dependency twice;
/// <c>tree.undeclared_dependency</c>, documents of a dependency that the manifest does not list;
/// <c>tree.misplaced_entry_points</c>, entry points in the manifest of a <c>Library</c> or a <c>Specs</c>.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module V4DocumentTree =
    /// The documents of one package, at addresses within that package.
    type private PackageDocuments =
        (ModuleDocumentAddress * TreeDocument<V4ModuleDocument, V4TypeDocument, V4ValueDocument>) list

    /// <summary>
    /// Whether the document at an address in a tree of the kind holds a definition rather than a specification: a
    /// <c>Library</c> holds its own definitions and its dependencies' specifications, a <c>Specs</c> holds
    /// specifications only, and an <c>Application</c> holds definitions only. A definition is a
    /// <c>DefinedType</c> or a <c>DefinedValue</c>, and its module document has an access; a specification is a
    /// <c>SpecifiedType</c> or a <c>SpecifiedValue</c>, and its module document has none. The manifest holds
    /// neither, and gives false. A store uses this rule to decode a document as <c>split</c> makes it, and
    /// <c>join</c> checks every document against it. C# calls this <c>HoldsDefinitions</c>.
    /// </summary>
    [<CompiledName("HoldsDefinitions")>]
    let holdsDefinitions (kind: V4DistributionKind) (address: DocumentAddress) : bool =
        match kind, address with
        | _, Manifest -> false
        | (V4DistributionKind.LibraryKind | V4DistributionKind.ApplicationKind), Own _ -> true
        | V4DistributionKind.SpecsKind, Own _ -> false
        | V4DistributionKind.ApplicationKind, Dependency _ -> true
        | (V4DistributionKind.LibraryKind | V4DistributionKind.SpecsKind), Dependency _ -> false

    let private moduleDocument access annotations doc typeNames valueNames =
        ModuleDoc
            {
                Access = access
                Doc = doc
                Annotations = annotations
                TypeNames = typeNames
                ValueNames = valueNames
            }

    let private splitDefinition (definition: PackageDefinition) : PackageDocuments =
        OrderedMap.toList definition.Modules
        |> List.collect (fun (path, controlled) ->
            let m = controlled.Value

            (Module path,
             moduleDocument
                 (Some controlled.Access)
                 []
                 m.Doc
                 (OrderedMap.keys m.Types)
                 (OrderedMap.keys m.Values))
            :: (OrderedMap.toList m.Types
                |> List.map (fun (name, t) -> TypeDef(path, name), TypeDoc(DefinedType t)))
            @ (OrderedMap.toList m.Values
               |> List.map (fun (name, v) -> ValueDef(path, name), ValueDoc(DefinedValue v)))
        )

    let private splitSpecification (specification: PackageSpecification) : PackageDocuments =
        OrderedMap.toList specification.Modules
        |> List.collect (fun (path, m) ->
            (Module path,
             moduleDocument
                 None
                 m.Annotations
                 m.Doc
                 (OrderedMap.keys m.Types)
                 (OrderedMap.keys m.Values))
            :: (OrderedMap.toList m.Types
                |> List.map (fun (name, t) -> TypeDef(path, name), TypeDoc(SpecifiedType t)))
            @ (OrderedMap.toList m.Values
               |> List.map (fun (name, v) -> ValueDef(path, name), ValueDoc(SpecifiedValue v)))
        )

    /// <summary>
    /// Splits a model into a tree: the manifest, and one document per module, type and value of the package and of
    /// each dependency. A module document lists its types and values in model order, and the manifest lists the
    /// dependencies in model order. C# calls this <c>Split</c>.
    /// </summary>
    [<CompiledName("Split")>]
    let split (distribution: Distribution) : V4DocumentTree =
        let tree kind package dependencies entryPoints ownDocuments dependencyDocuments = {
            Manifest = {
                Kind = kind
                Package = package
                Dependencies = OrderedMap.keys dependencies
                EntryPoints = entryPoints
            }
            Documents =
                (ownDocuments
                 |> List.map (fun (address, document) -> Own address, document))
                @ (OrderedMap.toList dependencies
                   |> List.collect (fun (dependency, contents) ->
                       dependencyDocuments contents
                       |> List.map (fun (address, document) ->
                           Dependency(dependency, address), document
                       )
                   ))
                |> Map.ofList
        }

        match distribution with
        | Library(package, dependencies, definition) ->
            tree V4DistributionKind.LibraryKind package dependencies [] (splitDefinition definition) splitSpecification
        | Specs(package, dependencies, specification) ->
            tree
                V4DistributionKind.SpecsKind
                package
                dependencies
                []
                (splitSpecification specification)
                splitSpecification
        | Application(package, dependencies, definition, entryPoints) ->
            tree
                V4DistributionKind.ApplicationKind
                package
                dependencies
                entryPoints
                (splitDefinition definition)
                splitDefinition

    let private family: TreeJoin.TreeFamily<V4ModuleDocument, V4TypeDocument, V4ValueDocument> = {
        TypeNames = fun m -> m.TypeNames
        ValueNames = fun m -> m.ValueNames
        IsDefinedType =
            function
            | DefinedType _ -> true
            | SpecifiedType _ -> false
        IsDefinedValue =
            function
            | DefinedValue _ -> true
            | SpecifiedValue _ -> false
        ModuleFault =
            fun holds m ->
                match holds, m.Access, m.Annotations with
                | true, None, _ ->
                    Some "a module definition has an access, and this module document has none"
                | true, Some _, _ :: _ ->
                    Some "a module definition has no annotations, and this module document has some"
                | false, Some _, _ ->
                    Some "a module specification has no access, and this module document has one"
                | _ -> None
    }

    /// How the modules of one package are made of its documents. A reader gives none for a document that
    /// documentErrors reports.
    type private PackageShape<'typ, 'value, 'moduleOf when 'typ: equality and 'value: equality> =
        TreeJoin.PackageShape<V4ModuleDocument, V4TypeDocument, V4ValueDocument, 'typ, 'value, OrderedMap<Name, 'typ>, OrderedMap<Name, 'value>, 'moduleOf>

    let private definitions
        : PackageShape<AccessControlled<Documented<TypeDefinition>>, AccessControlled<Documented<ValueDefinition>>, AccessControlled<ModuleDefinition>> = {
        TypeOf =
            function
            | DefinedType t -> Some t
            | SpecifiedType _ -> None
        ValueOf =
            function
            | DefinedValue v -> Some v
            | SpecifiedValue _ -> None
        TypeMap = OrderedMap.ofList
        ValueMap = OrderedMap.ofList
        ModuleOf =
            fun m ->
                match m.Access, m.Annotations with
                | Some access, [] ->
                    Some(fun types values -> {
                        Access = access
                        Value = {
                            Types = types
                            Values = values
                            Doc = m.Doc
                        }
                    })
                | _ -> None
    }

    let private specifications
        : PackageShape<Documented<TypeSpecification>, Documented<ValueSpecification>, ModuleSpecification> = {
        TypeOf =
            function
            | SpecifiedType t -> Some t
            | DefinedType _ -> None
        ValueOf =
            function
            | SpecifiedValue v -> Some v
            | DefinedValue _ -> None
        TypeMap = OrderedMap.ofList
        ValueMap = OrderedMap.ofList
        ModuleOf =
            fun m ->
                match m.Access with
                | None ->
                    Some(fun types values -> {
                        Annotations = m.Annotations
                        Types = types
                        Values = values
                        Doc = m.Doc
                    })
                | Some _ -> None
    }

    /// The modules of one package in the ordinal order of their logical paths, or every error of its documents.
    let private joinPackage ct shape scope logicalPath documents =
        TreeJoin.joinPackage ct family shape OrderedMap.ofList scope logicalPath documents

    /// The errors of the manifest itself, and of documents that belong to no package of it.
    let private manifestErrors (ct: CancellationToken) (tree: V4DocumentTree) (logicalPath: DocumentAddress -> string) =
        let manifest = tree.Manifest

        let misplacedEntryPoints =
            match manifest.Kind, manifest.EntryPoints with
            | V4DistributionKind.ApplicationKind, _
            | _, [] -> []
            | _ -> [
                TreeJoin.error
                    "tree.misplaced_entry_points"
                    (Some Manifest)
                    "The manifest has entry points, and only an Application has them."
              ]

        TreeJoin.manifestErrors ct manifest.Dependencies misplacedEntryPoints tree.Documents logicalPath

    let internal joinCancellable (ct: CancellationToken) (tree: V4DocumentTree) : Result<Distribution, TreeError list> =
        ct.ThrowIfCancellationRequested()
        let manifest = tree.Manifest
        let logicalPath = DocumentAddress.logicalPath manifest.Package
        let documents = Map.toList tree.Documents
        let own = TreeJoin.ownDocuments documents

        let dependencies shape (wrap: OrderedMap<ModulePath, 'moduleOf> -> 'package) =
            TreeJoin.joinDependencies
                ct
                (fun package ->
                    joinPackage
                        ct
                        shape
                        (fun inner -> Dependency(package, inner))
                        logicalPath
                        (TreeJoin.dependencyDocuments package documents)
                    |> Result.map wrap
                )
                OrderedMap.ofList
                manifest.Dependencies

        let assemble ownResult dependencyResult build =
            TreeJoin.assemble
                (manifestErrors ct tree logicalPath
                 @ TreeJoin.documentErrors ct family (holdsDefinitions manifest.Kind) tree.Documents)
                ownResult
                dependencyResult
                build

        let definitionOf modules : PackageDefinition = { Modules = modules }
        let specificationOf modules : PackageSpecification = { Modules = modules }

        match manifest.Kind with
        | V4DistributionKind.LibraryKind ->
            assemble
                (joinPackage ct definitions Own logicalPath own)
                (dependencies specifications specificationOf)
                (fun ownModules dependencyMap ->
                    Library(manifest.Package, dependencyMap, definitionOf ownModules)
                )
        | V4DistributionKind.SpecsKind ->
            assemble
                (joinPackage ct specifications Own logicalPath own)
                (dependencies specifications specificationOf)
                (fun ownModules dependencyMap ->
                    Specs(manifest.Package, dependencyMap, specificationOf ownModules)
                )
        | V4DistributionKind.ApplicationKind ->
            assemble
                (joinPackage ct definitions Own logicalPath own)
                (dependencies definitions definitionOf)
                (fun ownModules dependencyMap ->
                    Application(
                        manifest.Package,
                        dependencyMap,
                        definitionOf ownModules,
                        manifest.EntryPoints
                    )
                )

    /// <summary>
    /// Joins a tree into a model. Modules are in the ordinal order of their <c>DocumentAddress.logicalPath</c>,
    /// types and values in the order their module document lists them, and dependencies in the order the manifest
    /// lists them. An inconsistent tree is refused with every error found; the remarks of this module list the
    /// codes. C# calls this <c>Join</c>.
    /// </summary>
    [<CompiledName("Join")>]
    let join (tree: V4DocumentTree) : Result<Distribution, TreeError list> =
        joinCancellable CancellationToken.None tree
