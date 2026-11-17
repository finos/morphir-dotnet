namespace Morphir.IR.Trees

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
and V4DistributionKind =
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
            tree LibraryKind package dependencies [] (splitDefinition definition) splitSpecification
        | Specs(package, dependencies, specification) ->
            tree
                SpecsKind
                package
                dependencies
                []
                (splitSpecification specification)
                splitSpecification
        | Application(package, dependencies, definition, entryPoints) ->
            tree
                ApplicationKind
                package
                dependencies
                entryPoints
                (splitDefinition definition)
                splitDefinition

    let private error code address message : TreeError = {
        Code = code
        Address = address
        Message = message
    }

    let private errorsOf (result: Result<'a, TreeError list>) =
        match result with
        | Error errors -> errors
        | Ok _ -> []

    /// Every value of a list of results, or every error when there is one.
    let private collect
        (results: Result<'a, TreeError list> list)
        : Result<'a list, TreeError list> =
        match
            results
            |> List.collect errorsOf
        with
        | [] ->
            Ok(
                results
                |> List.choose (
                    function
                    | Ok value -> Some value
                    | Error _ -> None
                )
            )
        | errors -> Error errors

    let private nameText (name: Name) = CanonicalNames.nameToString name

    /// What the documents of one package must hold, and how its modules are made of them.
    type private PackageShape<'typ, 'value, 'moduleOf when 'typ: equality and 'value: equality> = {
        /// "definition" or "specification", for messages.
        What: string
        TypeOf: V4TypeDocument -> 'typ option
        ValueOf: V4ValueDocument -> 'value option
        /// Checks the members of a module document itself, and gives the function that makes the module of its
        /// types and values, or why the document does not fit.
        ModuleOf:
            V4ModuleDocument
                -> Result<OrderedMap<Name, 'typ> -> OrderedMap<Name, 'value> -> 'moduleOf, string>
    }

    let private definitions
        : PackageShape<AccessControlled<Documented<TypeDefinition>>, AccessControlled<Documented<ValueDefinition>>, AccessControlled<ModuleDefinition>> = {
        What = "definition"
        TypeOf =
            function
            | DefinedType t -> Some t
            | SpecifiedType _ -> None
        ValueOf =
            function
            | DefinedValue v -> Some v
            | SpecifiedValue _ -> None
        ModuleOf =
            fun m ->
                match m.Access, m.Annotations with
                | None, _ ->
                    Error "a module definition has an access, and this module document has none"
                | Some _, _ :: _ ->
                    Error
                        "a module definition has no annotations, and this module document has some"
                | Some access, [] ->
                    Ok(fun types values -> {
                        Access = access
                        Value = {
                            Types = types
                            Values = values
                            Doc = m.Doc
                        }
                    })
    }

    let private specifications
        : PackageShape<Documented<TypeSpecification>, Documented<ValueSpecification>, ModuleSpecification> = {
        What = "specification"
        TypeOf =
            function
            | SpecifiedType t -> Some t
            | DefinedType _ -> None
        ValueOf =
            function
            | SpecifiedValue v -> Some v
            | DefinedValue _ -> None
        ModuleOf =
            fun m ->
                match m.Access with
                | Some _ ->
                    Error "a module specification has no access, and this module document has one"
                | None ->
                    Ok(fun types values -> {
                        Annotations = m.Annotations
                        Types = types
                        Values = values
                        Doc = m.Doc
                    })
    }

    /// The modules of one package in the ordinal order of their logical paths, or every error of its documents.
    let private joinPackage
        (shape: PackageShape<'typ, 'value, 'moduleOf>)
        (scope: ModuleDocumentAddress -> DocumentAddress)
        (logicalPath: DocumentAddress -> string)
        (documents: PackageDocuments)
        : Result<OrderedMap<ModulePath, 'moduleOf>, TreeError list> =
        let byAddress = Map.ofList documents

        let mismatched address message =
            Error [ error "tree.mismatched_document" (Some(scope address)) message ]

        let byLogicalPath (addressOf: 'a -> ModuleDocumentAddress) (items: 'a list) =
            items
            |> List.sortWith (fun a b ->
                System.String.CompareOrdinal(
                    logicalPath (scope (addressOf a)),
                    logicalPath (scope (addressOf b))
                )
            )

        /// The members of one kind that a module document lists, each read from its own document.
        let members
            (path: ModulePath)
            (what: string)
            (addressOf: Name -> ModuleDocumentAddress)
            (read: TreeDocument<V4ModuleDocument, V4TypeDocument, V4ValueDocument> -> 'item option)
            (names: Name list)
            : Result<OrderedMap<Name, 'item>, TreeError list> =
            let listedTwice name =
                error
                    "tree.duplicate_name"
                    (Some(scope (Module path)))
                    $"The document of {scope (Module path)} lists the {what} '{nameText name}' more than once."

            let repeated =
                names
                |> List.countBy id
                |> List.filter (fun (_, count) -> count > 1)
                |> List.map (
                    fst
                    >> listedTwice
                )

            let entries =
                names
                |> List.distinct
                |> List.map (fun name ->
                    let address = addressOf name

                    match Map.tryFind address byAddress with
                    | None ->
                        Error [
                            error
                                "tree.missing_definition"
                                (Some(scope address))
                                $"The document of {scope (Module path)} lists the {what} '{nameText name}', and the tree has no document for it."
                        ]
                    | Some document ->
                        match read document with
                        | Some value -> Ok(name, value)
                        | None ->
                            mismatched
                                address
                                $"The document at {scope address} is not a {what} {shape.What}."
                )
                |> collect

            match repeated, entries with
            | [], Ok entries ->
                // The names are distinct here, so the map always builds.
                OrderedMap.ofList entries
                |> Result.mapError (fun name -> [ listedTwice name ])
            | _ ->
                Error(
                    repeated
                    @ errorsOf entries
                )

        let readType document =
            match document with
            | TypeDoc t -> shape.TypeOf t
            | ModuleDoc _
            | ValueDoc _ -> None

        let readValue document =
            match document with
            | ValueDoc v -> shape.ValueOf v
            | ModuleDoc _
            | TypeDoc _ -> None

        let moduleDocuments =
            documents
            |> List.choose (fun (address, document) ->
                match address with
                | Module path -> Some(path, document)
                | TypeDef _
                | ValueDef _ -> None
            )
            |> byLogicalPath (
                fst
                >> Module
            )

        let modules =
            moduleDocuments
            |> List.map (fun (path, document) ->
                match document with
                | ModuleDoc m ->
                    let made =
                        shape.ModuleOf m
                        |> Result.mapError (fun reason -> [
                            error
                                "tree.mismatched_document"
                                (Some(scope (Module path)))
                                $"The document of {scope (Module path)} does not fit its package: {reason}."
                        ])

                    let types =
                        members path "type" (fun name -> TypeDef(path, name)) readType m.TypeNames

                    let values =
                        members
                            path
                            "value"
                            (fun name -> ValueDef(path, name))
                            readValue
                            m.ValueNames

                    match made, types, values with
                    | Ok make, Ok types, Ok values -> Ok(path, make types values)
                    | _ ->
                        Error(
                            errorsOf made
                            @ errorsOf types
                            @ errorsOf values
                        )
                | TypeDoc _
                | ValueDoc _ ->
                    mismatched
                        (Module path)
                        $"The document at {scope (Module path)} is not a module document."
            )
            |> collect

        let listed =
            moduleDocuments
            |> List.collect (fun (path, document) ->
                match document with
                | ModuleDoc m ->
                    (m.TypeNames
                     |> List.map (fun name -> TypeDef(path, name)))
                    @ (m.ValueNames
                       |> List.map (fun name -> ValueDef(path, name)))
                | TypeDoc _
                | ValueDoc _ -> []
            )
            |> Set.ofList

        let unlisted =
            documents
            |> List.map fst
            |> List.filter (fun address ->
                match address with
                | Module _ -> false
                | TypeDef _
                | ValueDef _ -> not (Set.contains address listed)
            )
            |> byLogicalPath id
            |> List.map (fun address ->
                error
                    "tree.unlisted_definition"
                    (Some(scope address))
                    $"The tree has a document for {scope address}, and no module document lists it."
            )

        match modules, unlisted with
        | Ok modules, [] ->
            // Each module path is one key of the documents, so the paths are distinct.
            OrderedMap.ofList modules
            |> Result.mapError (fun path -> [
                error
                    "tree.mismatched_document"
                    (Some(scope (Module path)))
                    $"The tree has {scope (Module path)} twice."
            ])
        | _ ->
            Error(
                errorsOf modules
                @ unlisted
            )

    /// The errors of the manifest itself, and of documents that belong to no package of it.
    let private treeErrors (tree: V4DocumentTree) (logicalPath: DocumentAddress -> string) =
        let manifest = tree.Manifest

        let addresses =
            tree.Documents
            |> Map.keys
            |> List.ofSeq

        let repeatedDependencies =
            manifest.Dependencies
            |> List.countBy id
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (fun (PackageName package, _) ->
                error
                    "tree.duplicate_dependency"
                    (Some Manifest)
                    $"The manifest lists the dependency '{CanonicalNames.pathToString package}' more than once."
            )

        let misplacedEntryPoints =
            match manifest.Kind, manifest.EntryPoints with
            | ApplicationKind, _
            | _, [] -> []
            | _ -> [
                error
                    "tree.misplaced_entry_points"
                    (Some Manifest)
                    "The manifest has entry points, and only an Application has them."
              ]

        let manifestDocument =
            if List.contains Manifest addresses then
                [
                    error
                        "tree.mismatched_document"
                        (Some Manifest)
                        "The tree holds a document at the manifest address, which is the tree's own manifest."
                ]
            else
                []

        let undeclared =
            addresses
            |> List.filter (fun address ->
                match address with
                | Dependency(package, _) -> not (List.contains package manifest.Dependencies)
                | Manifest
                | Own _ -> false
            )
            |> List.groupBy (fun address ->
                match address with
                | Dependency(package, _) -> Some package
                | Manifest
                | Own _ -> None
            )
            |> List.choose (fun (package, documents) ->
                package
                |> Option.map (fun (PackageName path) ->
                    error
                        "tree.undeclared_dependency"
                        (Some(List.minBy logicalPath documents))
                        $"The tree has documents of the dependency '{CanonicalNames.pathToString path}', and the manifest does not list it."
                )
            )

        repeatedDependencies
        @ misplacedEntryPoints
        @ manifestDocument
        @ undeclared

    /// <summary>
    /// Joins a tree into a model. Modules are in the ordinal order of their <c>DocumentAddress.logicalPath</c>,
    /// types and values in the order their module document lists them, and dependencies in the order the manifest
    /// lists them. An inconsistent tree is refused with every error found; the remarks of this module list the
    /// codes. C# calls this <c>Join</c>.
    /// </summary>
    [<CompiledName("Join")>]
    let join (tree: V4DocumentTree) : Result<Distribution, TreeError list> =
        let manifest = tree.Manifest
        let logicalPath = DocumentAddress.logicalPath manifest.Package
        let documents = Map.toList tree.Documents

        let own =
            documents
            |> List.choose (fun (address, document) ->
                match address with
                | Own inner -> Some(inner, document)
                | Manifest
                | Dependency _ -> None
            )

        let ofDependency package =
            documents
            |> List.choose (fun (address, document) ->
                match address with
                | Dependency(p, inner) when p = package -> Some(inner, document)
                | Manifest
                | Own _
                | Dependency _ -> None
            )

        let dependencies shape (wrap: OrderedMap<ModulePath, 'moduleOf> -> 'package) =
            manifest.Dependencies
            |> List.distinct
            |> List.map (fun package ->
                joinPackage
                    shape
                    (fun inner -> Dependency(package, inner))
                    logicalPath
                    (ofDependency package)
                |> Result.map (fun modules -> package, wrap modules)
            )
            |> collect
            |> Result.bind (fun packages ->
                // The packages are distinct, so the map always builds.
                OrderedMap.ofList packages
                |> Result.mapError (fun _ -> [])
            )

        let assemble ownResult dependencyResult build =
            match treeErrors tree logicalPath, ownResult, dependencyResult with
            | [], Ok ownModules, Ok dependencyMap -> Ok(build ownModules dependencyMap)
            | found, _, _ ->
                Error(
                    found
                    @ errorsOf ownResult
                    @ errorsOf dependencyResult
                )

        let definitionOf modules : PackageDefinition = { Modules = modules }
        let specificationOf modules : PackageSpecification = { Modules = modules }

        match manifest.Kind with
        | LibraryKind ->
            assemble
                (joinPackage definitions Own logicalPath own)
                (dependencies specifications specificationOf)
                (fun ownModules dependencyMap ->
                    Library(manifest.Package, dependencyMap, definitionOf ownModules)
                )
        | SpecsKind ->
            assemble
                (joinPackage specifications Own logicalPath own)
                (dependencies specifications specificationOf)
                (fun ownModules dependencyMap ->
                    Specs(manifest.Package, dependencyMap, specificationOf ownModules)
                )
        | ApplicationKind ->
            assemble
                (joinPackage definitions Own logicalPath own)
                (dependencies definitions definitionOf)
                (fun ownModules dependencyMap ->
                    Application(
                        manifest.Package,
                        dependencyMap,
                        definitionOf ownModules,
                        manifest.EntryPoints
                    )
                )
