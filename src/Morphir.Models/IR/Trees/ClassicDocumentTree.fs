namespace Morphir.IR.Trees

open Morphir.IR
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.Documented
open Morphir.IR.Classic.Distribution

/// <summary>The kind of a classic v3 distribution.</summary>
[<RequireQualifiedAccess>]
type ClassicDistributionKind =
    /// <summary>A <c>Library</c>: own definitions, dependency specifications.</summary>
    | LibraryKind
    /// <summary>A <c>Specs</c>: own specifications, dependency specifications.</summary>
    | SpecsKind

/// <summary>
/// The <c>formatVersion</c> of the single file that a classic v3 tree joins to. Every file of the tree itself says
/// <c>"3.1.0"</c>; that label is the store's concern.
/// </summary>
type ClassicLabel =
    /// <summary>The integer <c>3</c>, which a single-file <c>Library</c> has.</summary>
    | Integer3
    /// <summary>The release <c>"3.1.0"</c>, which a single-file <c>Specs</c> needs.</summary>
    | SemVer310

/// <summary>The manifest of a classic v3 tree: its kind, its package, its dependencies and its single-file label.</summary>
type ClassicManifest = {
    /// <summary>The kind of distribution.</summary>
    Kind: ClassicDistributionKind
    /// <summary>The own package, whose documents are at <c>Own</c> addresses.</summary>
    Package: PackageName
    /// <summary>Every dependency package in model order, including one with no modules.</summary>
    Dependencies: PackageName list
    /// <summary>
    /// The label of the single file the tree joins to: <c>Integer3</c> for a <c>Library</c> and <c>SemVer310</c>
    /// for a <c>Specs</c>. <c>join</c> refuses any other pairing.
    /// </summary>
    SingleFileVersion: ClassicLabel
}

/// <summary>
/// A classic v3 module document: what the module file of the parent v3 tree specification holds besides its path,
/// that is the module's access, its documentation and the names of its types and values in document order. The
/// types and values themselves are documents of their own. A classic module has no annotations.
/// </summary>
type ClassicModuleDocument = {
    /// <summary>The module's access in a package definition, and none in a package specification.</summary>
    Access: Access option
    /// <summary>The module's documentation.</summary>
    Doc: string option
    /// <summary>The module's types in document order.</summary>
    TypeNames: Name list
    /// <summary>The module's values in document order.</summary>
    ValueNames: Name list
}

/// <summary>
/// A classic type document: a definition in a package definition, a specification in a package specification.
/// The attributes are kept as they are, so a document read from JSON keeps every attribute slot.
/// </summary>
[<RequireQualifiedAccess>]
type ClassicTypeDocument<'ta> =
    /// <summary>A type of a package definition: the <c>def</c> payload of a type file.</summary>
    | DefinedType of AccessControlled<Documented<Classic.Type.TypeDefinition<'ta>>>
    /// <summary>A type of a package specification: the <c>spec</c> payload of a type file.</summary>
    | SpecifiedType of Documented<Classic.Type.TypeSpecification<'ta>>

/// <summary>
/// A classic value document: a definition in a package definition, a specification in a package specification.
/// The attributes are kept as they are, including the inferred types that morphir-elm writes on expressions.
/// </summary>
[<RequireQualifiedAccess>]
type ClassicValueDocument<'ta, 'va> =
    /// <summary>A value of a package definition: the <c>def</c> payload of a value file.</summary>
    | DefinedValue of AccessControlled<Documented<Classic.ValueDefinition<'ta, 'va>>>
    /// <summary>A value of a package specification: the <c>spec</c> payload of a value file.</summary>
    | SpecifiedValue of Documented<Classic.ValueSpecification<'ta>>

/// <summary>
/// A classic v3 model as a document tree, with type attributes <c>'ta</c> and value attributes <c>'va</c>. A tree
/// of a model read with <c>ClassicJson.readDistribution</c> has <c>JsonElement</c> attributes.
/// </summary>
type ClassicDocumentTree<'ta, 'va> =
    DocumentTree<ClassicManifest, ClassicModuleDocument, ClassicTypeDocument<'ta>, ClassicValueDocument<'ta, 'va>>

/// <summary>Splits a classic v3 model into a document tree and joins a tree back into the model.</summary>
/// <remarks>
/// <para>
/// The tree is the classic instance of the v4 tree layout, as the parent v3 tree specification defines it: a
/// <c>Library</c> holds its own definitions and its dependencies' specifications, and a <c>Specs</c> holds
/// specifications only. Each type and value document holds the case its package needs: <c>DefinedType</c> and
/// <c>DefinedValue</c> in a definition, <c>SpecifiedType</c> and <c>SpecifiedValue</c> in a specification.
/// Attributes pass through unchanged, whatever their type.
/// </para>
/// <para>
/// <c>join</c> refuses an inconsistent tree with every error it finds, and never gives a partial model. The codes:
/// <c>tree.missing_definition</c>, a module document lists a type or value that has no document;
/// <c>tree.unlisted_definition</c>, a type or value document that no module document lists;
/// <c>tree.mismatched_document</c>, a document that does not fit its address or its package: a value document at
/// a type address, a specification in a definition, a module definition with no access, a module specification
/// with an access, or a document at the manifest address;
/// <c>tree.duplicate_name</c>, a module document lists a name twice;
/// <c>tree.duplicate_dependency</c>, the manifest lists a dependency twice;
/// <c>tree.undeclared_dependency</c>, documents of a dependency that the manifest does not list;
/// <c>tree.mismatched_label</c>, a manifest whose single-file label does not fit its kind.
/// </para>
/// </remarks>
[<RequireQualifiedAccess>]
module ClassicDocumentTree =
    /// The documents of one package, at addresses within that package.
    type private PackageDocuments<'ta, 'va> =
        (ModuleDocumentAddress * TreeDocument<ClassicModuleDocument, ClassicTypeDocument<'ta>, ClassicValueDocument<'ta, 'va>>) list

    /// <summary>
    /// Whether the document at an address in a tree of the kind holds a definition rather than a specification:
    /// only the own package of a <c>Library</c> holds definitions. A definition is a <c>DefinedType</c> or a
    /// <c>DefinedValue</c>, and its module document has an access; a specification is a <c>SpecifiedType</c> or a
    /// <c>SpecifiedValue</c>, and its module document has none. The manifest holds neither, and gives false. A
    /// store uses this rule to decode a document as <c>split</c> makes it, and <c>join</c> checks every document
    /// against it. C# calls this <c>HoldsDefinitions</c>.
    /// </summary>
    [<CompiledName("HoldsDefinitions")>]
    let holdsDefinitions (kind: ClassicDistributionKind) (address: DocumentAddress) : bool =
        match kind, address with
        | ClassicDistributionKind.LibraryKind, Own _ -> true
        | ClassicDistributionKind.SpecsKind, Own _
        | _, Dependency _
        | _, Manifest -> false

    /// <summary>
    /// The label of the single file that a tree of the kind joins to: <c>Integer3</c> for a <c>Library</c>, which
    /// needs nothing newer than 3.0.0, and <c>SemVer310</c> for a <c>Specs</c>, which 3.1.0 introduced. C# calls
    /// this <c>SingleFileVersionOf</c>.
    /// </summary>
    [<CompiledName("SingleFileVersionOf")>]
    let singleFileVersionOf (kind: ClassicDistributionKind) : ClassicLabel =
        match kind with
        | ClassicDistributionKind.LibraryKind -> Integer3
        | ClassicDistributionKind.SpecsKind -> SemVer310

    let private moduleDocument access doc (types: Map<Name, _>) (values: Map<Name, _>) =
        ModuleDoc
            {
                Access = access
                Doc = doc
                TypeNames = List.ofSeq types.Keys
                ValueNames = List.ofSeq values.Keys
            }

    let private splitDefinition
        (definition: Classic.Package.PackageDefinition<'ta, 'va>)
        : PackageDocuments<'ta, 'va> =
        Map.toList definition.Modules
        |> List.collect (fun (path, controlled) ->
            let m = controlled.Value

            (Module path, moduleDocument (Some controlled.Access) m.Doc m.Types m.Values)
            :: (Map.toList m.Types
                |> List.map (fun (name, t) ->
                    TypeDef(path, name), TypeDoc(ClassicTypeDocument.DefinedType t)
                ))
            @ (Map.toList m.Values
               |> List.map (fun (name, v) ->
                   ValueDef(path, name), ValueDoc(ClassicValueDocument.DefinedValue v)
               ))
        )

    let private splitSpecification
        (specification: Classic.Package.PackageSpecification<'ta>)
        : PackageDocuments<'ta, 'va> =
        Map.toList specification.Modules
        |> List.collect (fun (path, m) ->
            (Module path, moduleDocument None m.Doc m.Types m.Values)
            :: (Map.toList m.Types
                |> List.map (fun (name, t) ->
                    TypeDef(path, name), TypeDoc(ClassicTypeDocument.SpecifiedType t)
                ))
            @ (Map.toList m.Values
               |> List.map (fun (name, v) ->
                   ValueDef(path, name), ValueDoc(ClassicValueDocument.SpecifiedValue v)
               ))
        )

    /// <summary>
    /// Splits a classic model into a tree: the manifest, and one document per module, type and value of the
    /// package and of each dependency. A module document lists its types and values in model order, and the
    /// manifest lists the dependencies in model order and records the single-file label of the kind. C# calls this
    /// <c>Split</c>.
    /// </summary>
    [<CompiledName("Split")>]
    let split (distribution: Distribution<'ta, 'va>) : ClassicDocumentTree<'ta, 'va> =
        let tree kind package (dependencies: Map<PackageName, Classic.Package.PackageSpecification<'ta>>) ownDocuments = {
            Manifest = {
                Kind = kind
                Package = package
                Dependencies = List.ofSeq dependencies.Keys
                SingleFileVersion = singleFileVersionOf kind
            }
            Documents =
                (ownDocuments
                 |> List.map (fun (address, document) -> Own address, document))
                @ (Map.toList dependencies
                   |> List.collect (fun (dependency, contents) ->
                       splitSpecification contents
                       |> List.map (fun (address, document) ->
                           Dependency(dependency, address), document
                       )
                   ))
                |> Map.ofList
        }

        match distribution with
        | Library(package, dependencies, definition) ->
            tree ClassicDistributionKind.LibraryKind package dependencies (splitDefinition definition)
        | Specs(package, dependencies, specification) ->
            tree ClassicDistributionKind.SpecsKind package dependencies (splitSpecification specification)

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

    /// The case of a document as text, for messages.
    let private describe
        (document: TreeDocument<ClassicModuleDocument, ClassicTypeDocument<'ta>, ClassicValueDocument<'ta, 'va>>)
        =
        match document with
        | ModuleDoc _ -> "a module document"
        | TypeDoc(ClassicTypeDocument.DefinedType _) -> "a type definition"
        | TypeDoc(ClassicTypeDocument.SpecifiedType _) -> "a type specification"
        | ValueDoc(ClassicValueDocument.DefinedValue _) -> "a value definition"
        | ValueDoc(ClassicValueDocument.SpecifiedValue _) -> "a value specification"

    /// <summary>
    /// The errors of documents that do not fit their address or their package. The case must match the address. A
    /// type or value must be a definition where <c>holdsDefinitions</c> says so, and a specification elsewhere. A
    /// module definition has an access, and a module specification has none. No document is at the manifest
    /// address. Every rule about the case of a document is here, so the readers of <c>joinPackage</c> leave a
    /// document that does not fit to this check.
    /// </summary>
    let private documentErrors (tree: ClassicDocumentTree<'ta, 'va>) : TreeError list =
        let kind = tree.Manifest.Kind

        tree.Documents
        |> Map.toList
        |> List.choose (fun (address, document) ->
            let holds = holdsDefinitions kind address
            let wanted = if holds then "a definition" else "a specification"

            let fault =
                match address, document with
                | Manifest, _ -> Some "the manifest of a tree is not one of its documents"
                | (Own(Module _) | Dependency(_, Module _)), ModuleDoc m ->
                    match holds, m.Access with
                    | true, None ->
                        Some "a module definition has an access, and this module document has none"
                    | false, Some _ ->
                        Some
                            "a module specification has no access, and this module document has one"
                    | _ -> None
                | (Own(TypeDef _) | Dependency(_, TypeDef _)), TypeDoc t ->
                    match holds, t with
                    | true, ClassicTypeDocument.DefinedType _
                    | false, ClassicTypeDocument.SpecifiedType _ -> None
                    | _ ->
                        Some $"its package holds {wanted}, and the document is {describe document}"
                | (Own(ValueDef _) | Dependency(_, ValueDef _)), ValueDoc v ->
                    match holds, v with
                    | true, ClassicValueDocument.DefinedValue _
                    | false, ClassicValueDocument.SpecifiedValue _ -> None
                    | _ ->
                        Some $"its package holds {wanted}, and the document is {describe document}"
                | _ ->
                    Some
                        $"the address needs another kind of document, and the document is {describe document}"

            fault
            |> Option.map (fun reason ->
                error
                    "tree.mismatched_document"
                    (Some address)
                    $"The document at {address} does not fit: {reason}."
            )
        )

    /// How the modules of one package are made of its documents. A reader gives none for a document that
    /// documentErrors reports.
    type private PackageShape<'ta, 'va, 'typ, 'value, 'moduleOf> = {
        TypeOf: ClassicTypeDocument<'ta> -> 'typ option
        ValueOf: ClassicValueDocument<'ta, 'va> -> 'value option
        ModuleOf: ClassicModuleDocument -> (Map<Name, 'typ> -> Map<Name, 'value> -> 'moduleOf) option
    }

    let private definitions<'ta, 'va>
        : PackageShape<'ta, 'va, AccessControlled<Documented<Classic.Type.TypeDefinition<'ta>>>, AccessControlled<Documented<Classic.ValueDefinition<'ta, 'va>>>, AccessControlled<Classic.Module.ModuleDefinition<'ta, 'va>>> = {
        TypeOf =
            function
            | ClassicTypeDocument.DefinedType t -> Some t
            | ClassicTypeDocument.SpecifiedType _ -> None
        ValueOf =
            function
            | ClassicValueDocument.DefinedValue v -> Some v
            | ClassicValueDocument.SpecifiedValue _ -> None
        ModuleOf =
            fun m ->
                m.Access
                |> Option.map (fun access types values -> {
                    Access = access
                    Value = {
                        Types = types
                        Values = values
                        Doc = m.Doc
                    }
                })
    }

    let private specifications<'ta, 'va>
        : PackageShape<'ta, 'va, Documented<Classic.Type.TypeSpecification<'ta>>, Documented<Classic.ValueSpecification<'ta>>, Classic.Module.ModuleSpecification<'ta>> = {
        TypeOf =
            function
            | ClassicTypeDocument.SpecifiedType t -> Some t
            | ClassicTypeDocument.DefinedType _ -> None
        ValueOf =
            function
            | ClassicValueDocument.SpecifiedValue v -> Some v
            | ClassicValueDocument.DefinedValue _ -> None
        ModuleOf =
            fun m ->
                match m.Access with
                | None ->
                    Some(fun types values -> {
                        Types = types
                        Values = values
                        Doc = m.Doc
                    })
                | Some _ -> None
    }

    /// The result for a document that does not fit. documentErrors reports it, and join always includes those
    /// errors, so the package fails here with no error of its own and the error is not reported twice.
    let private reportedByDocumentErrors<'a> : Result<'a, TreeError list> = Error []

    /// The modules of one package, or every error of its documents in the ordinal order of their logical paths.
    let private joinPackage
        (shape: PackageShape<'ta, 'va, 'typ, 'value, 'moduleOf>)
        (scope: ModuleDocumentAddress -> DocumentAddress)
        (logicalPath: DocumentAddress -> string)
        (documents: PackageDocuments<'ta, 'va>)
        : Result<Map<ModulePath, 'moduleOf>, TreeError list> =
        let byAddress = Map.ofList documents

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
            (read: TreeDocument<ClassicModuleDocument, ClassicTypeDocument<'ta>, ClassicValueDocument<'ta, 'va>> -> 'item option)
            (names: Name list)
            : Result<Map<Name, 'item>, TreeError list> =
            let repeated =
                names
                |> List.countBy id
                |> List.filter (fun (_, count) -> count > 1)
                |> List.map (fun (name, _) ->
                    error
                        "tree.duplicate_name"
                        (Some(scope (Module path)))
                        $"The document of {scope (Module path)} lists the {what} '{nameText name}' more than once."
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
                        | None -> reportedByDocumentErrors
                )
                |> collect

            match repeated, entries with
            | [], Ok entries -> Ok(Map.ofList entries)
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
                match address, document with
                | Module path, ModuleDoc m -> Some(path, m)
                | _ -> None
            )
            |> byLogicalPath (
                fst
                >> Module
            )

        let modules =
            moduleDocuments
            |> List.map (fun (path, m) ->
                let types =
                    members path "type" (fun name -> TypeDef(path, name)) readType m.TypeNames

                let values =
                    members path "value" (fun name -> ValueDef(path, name)) readValue m.ValueNames

                match shape.ModuleOf m, types, values with
                | Some make, Ok types, Ok values -> Ok(path, make types values)
                // A module document that does not fit is reported by documentErrors.
                | _ ->
                    Error(
                        errorsOf types
                        @ errorsOf values
                    )
            )
            |> collect

        let listed =
            moduleDocuments
            |> List.collect (fun (path, m) ->
                (m.TypeNames
                 |> List.map (fun name -> TypeDef(path, name)))
                @ (m.ValueNames
                   |> List.map (fun name -> ValueDef(path, name)))
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

        // Each module path is one key of the documents, so no module is made twice.
        match modules, unlisted with
        | Ok modules, [] -> Ok(Map.ofList modules)
        | _ ->
            Error(
                errorsOf modules
                @ unlisted
            )

    /// The errors of the manifest itself, and of documents that belong to no package of it.
    let private manifestErrors
        (tree: ClassicDocumentTree<'ta, 'va>)
        (logicalPath: DocumentAddress -> string)
        =
        let manifest = tree.Manifest

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

        let mismatchedLabel =
            let wanted = singleFileVersionOf manifest.Kind

            if manifest.SingleFileVersion = wanted then
                []
            else
                let text label =
                    match label with
                    | Integer3 -> "3"
                    | SemVer310 -> "\"3.1.0\""

                let kind =
                    match manifest.Kind with
                    | ClassicDistributionKind.LibraryKind -> "Library"
                    | ClassicDistributionKind.SpecsKind -> "Specs"

                [
                    error
                        "tree.mismatched_label"
                        (Some Manifest)
                        $"The manifest of a {kind} tree records the single-file formatVersion {text manifest.SingleFileVersion}, and a {kind} joins to {text wanted}."
                ]

        let undeclared =
            tree.Documents
            |> Map.keys
            |> List.ofSeq
            |> List.choose (fun address ->
                match address with
                | Dependency(package, _) when not (List.contains package manifest.Dependencies) ->
                    Some(package, address)
                | Manifest
                | Own _
                | Dependency _ -> None
            )
            |> List.groupBy fst
            |> List.map (fun (PackageName path, documents) ->
                error
                    "tree.undeclared_dependency"
                    (Some(
                        documents
                        |> List.map snd
                        |> List.minBy logicalPath
                    ))
                    $"The tree has documents of the dependency '{CanonicalNames.pathToString path}', and the manifest does not list it."
            )

        repeatedDependencies
        @ mismatchedLabel
        @ undeclared

    /// <summary>
    /// Joins a tree into a model. Types, values, modules and dependencies are maps in a classic model, so their
    /// order is the model's own. An inconsistent tree is refused with every error found, its document errors in
    /// the ordinal order of their logical paths; the remarks of this module list the codes. The single file the
    /// model belongs in has the label <c>tree.Manifest.SingleFileVersion</c>. C# calls this <c>Join</c>.
    /// </summary>
    [<CompiledName("Join")>]
    let join (tree: ClassicDocumentTree<'ta, 'va>) : Result<Distribution<'ta, 'va>, TreeError list> =
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

        let dependencies =
            manifest.Dependencies
            |> List.distinct
            |> List.map (fun package ->
                joinPackage
                    specifications
                    (fun inner -> Dependency(package, inner))
                    logicalPath
                    (ofDependency package)
                |> Result.map (fun modules ->
                    package, ({ Modules = modules }: Classic.Package.PackageSpecification<'ta>)
                )
            )
            |> collect
            |> Result.map Map.ofList

        let assemble ownResult build =
            match
                manifestErrors tree logicalPath
                @ documentErrors tree,
                ownResult,
                dependencies
            with
            | [], Ok ownModules, Ok dependencyMap -> Ok(build ownModules dependencyMap)
            | found, _, _ ->
                Error(
                    found
                    @ errorsOf ownResult
                    @ errorsOf dependencies
                )

        match manifest.Kind with
        | ClassicDistributionKind.LibraryKind ->
            assemble
                (joinPackage definitions Own logicalPath own)
                (fun ownModules dependencyMap ->
                    Library(
                        manifest.Package,
                        dependencyMap,
                        ({ Modules = ownModules }: Classic.Package.PackageDefinition<'ta, 'va>)
                    )
                )
        | ClassicDistributionKind.SpecsKind ->
            assemble
                (joinPackage specifications Own logicalPath own)
                (fun ownModules dependencyMap ->
                    Specs(
                        manifest.Package,
                        dependencyMap,
                        ({ Modules = ownModules }: Classic.Package.PackageSpecification<'ta>)
                    )
                )
