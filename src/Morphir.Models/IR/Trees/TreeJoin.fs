namespace Morphir.IR.Trees

open System.Threading
open Morphir.IR

/// <summary>
/// The parts of a join that every tree family shares: the v4 tree and the classic v3 tree. A family gives what
/// differs, which is its document types (<c>TreeFamily</c>), how the modules of a package are made of
/// documents (<c>PackageShape</c>), and its map types. Each family's own module keeps its types, its
/// split, its manifest rules and the match on its distribution kinds.
/// </summary>
module internal TreeJoin =
    /// <summary>A tree error.</summary>
    let error code address message : TreeError = {
        Code = code
        Address = address
        Message = message
    }

    /// <summary>The errors of a result, or none.</summary>
    let errorsOf (result: Result<'a, TreeError list>) =
        match result with
        | Error errors -> errors
        | Ok _ -> []

    /// <summary>Every value of a list of results, or every error when there is one.</summary>
    let collect (results: Result<'a, TreeError list> list) : Result<'a list, TreeError list> =
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

    /// <summary>A name as text, for messages.</summary>
    let nameText (name: Name) = CanonicalNames.nameToString name

    /// <summary>What a family's documents are: the names a module document lists, and which case a type or value
    /// document is.</summary>
    type TreeFamily<'moduleDoc, 'typeDoc, 'valueDoc> = {
        /// <summary>The types a module document lists, in document order.</summary>
        TypeNames: 'moduleDoc -> Name list
        /// <summary>The values a module document lists, in document order.</summary>
        ValueNames: 'moduleDoc -> Name list
        /// <summary>Whether a type document is a definition rather than a specification.</summary>
        IsDefinedType: 'typeDoc -> bool
        /// <summary>Whether a value document is a definition rather than a specification.</summary>
        IsDefinedValue: 'valueDoc -> bool
        /// <summary>
        /// What is wrong with a module document in a package that holds definitions (true) or specifications
        /// (false), or none.
        /// </summary>
        ModuleFault: bool -> 'moduleDoc -> string option
    }

    /// <summary>The case of a document as text, for messages.</summary>
    let describe
        (family: TreeFamily<'moduleDoc, 'typeDoc, 'valueDoc>)
        (document: TreeDocument<'moduleDoc, 'typeDoc, 'valueDoc>)
        =
        match document with
        | ModuleDoc _ -> "a module document"
        | TypeDoc t when family.IsDefinedType t -> "a type definition"
        | TypeDoc _ -> "a type specification"
        | ValueDoc v when family.IsDefinedValue v -> "a value definition"
        | ValueDoc _ -> "a value specification"

    /// <summary>
    /// The errors of documents that do not fit their address or their package. The case must match the address. A
    /// type or value must be a definition where <paramref name="holds"/> says so, and a specification elsewhere. A
    /// module document must have no fault that the family names. No document is at the manifest address. Every rule
    /// about the case of a document is here, so the readers of <c>joinPackage</c> leave a document that does not
    /// fit to this check.
    /// </summary>
    let documentErrors
        (ct: CancellationToken)
        (family: TreeFamily<'moduleDoc, 'typeDoc, 'valueDoc>)
        (holds: DocumentAddress -> bool)
        (documents: Map<DocumentAddress, TreeDocument<'moduleDoc, 'typeDoc, 'valueDoc>>)
        : TreeError list =
        ct.ThrowIfCancellationRequested()

        documents
        |> Map.toList
        |> List.choose (fun (address, document) ->
            ct.ThrowIfCancellationRequested()
            let holds = holds address
            let wanted = if holds then "a definition" else "a specification"

            let mismatch () =
                Some $"its package holds {wanted}, and the document is {describe family document}"

            let fault =
                match address, document with
                | Manifest, _ -> Some "the manifest of a tree is not one of its documents"
                | (Own(Module _) | Dependency(_, Module _)), ModuleDoc m -> family.ModuleFault holds m
                | (Own(TypeDef _) | Dependency(_, TypeDef _)), TypeDoc t ->
                    if family.IsDefinedType t = holds then None else mismatch ()
                | (Own(ValueDef _) | Dependency(_, ValueDef _)), ValueDoc v ->
                    if family.IsDefinedValue v = holds then None else mismatch ()
                | _ ->
                    Some
                        $"the address needs another kind of document, and the document is {describe family document}"

            fault
            |> Option.map (fun reason ->
                error
                    "tree.mismatched_document"
                    (Some address)
                    $"The document at {address} does not fit: {reason}."
            )
        )

    /// <summary>
    /// How the modules of one package are made of its documents, and the maps that hold its types and values. A
    /// reader gives none for a document that <c>documentErrors</c> reports. A map builder gives the key that is
    /// there twice when it cannot build the map.
    /// </summary>
    type PackageShape<'moduleDoc, 'typeDoc, 'valueDoc, 'typ, 'value, 'typeMap, 'valueMap, 'moduleOf> = {
        /// <summary>The type of a type document, or none for one of the other case.</summary>
        TypeOf: 'typeDoc -> 'typ option
        /// <summary>The value of a value document, or none for one of the other case.</summary>
        ValueOf: 'valueDoc -> 'value option
        /// <summary>The map of a module's types, in listed order.</summary>
        TypeMap: (Name * 'typ) list -> Result<'typeMap, Name>
        /// <summary>The map of a module's values, in listed order.</summary>
        ValueMap: (Name * 'value) list -> Result<'valueMap, Name>
        /// <summary>How a module is made of its document, its types and its values, or none for a document that
        /// does not fit.</summary>
        ModuleOf: 'moduleDoc -> ('typeMap -> 'valueMap -> 'moduleOf) option
    }

    /// <summary>
    /// The result for a document that does not fit. documentErrors reports it, and join always includes those
    /// errors, so the package fails here with no error of its own and the error is not reported twice.
    /// </summary>
    let reportedByDocumentErrors<'a> : Result<'a, TreeError list> = Error []

    /// <summary>The modules of one package in the ordinal order of their logical paths, or every error of its
    /// documents.</summary>
    let joinPackage
        (ct: CancellationToken)
        (family: TreeFamily<'moduleDoc, 'typeDoc, 'valueDoc>)
        (shape: PackageShape<'moduleDoc, 'typeDoc, 'valueDoc, 'typ, 'value, 'typeMap, 'valueMap, 'moduleOf>)
        (moduleMap: (ModulePath * 'moduleOf) list -> Result<'moduleMap, ModulePath>)
        (scope: ModuleDocumentAddress -> DocumentAddress)
        (logicalPath: DocumentAddress -> string)
        (documents: (ModuleDocumentAddress * TreeDocument<'moduleDoc, 'typeDoc, 'valueDoc>) list)
        : Result<'moduleMap, TreeError list> =
        ct.ThrowIfCancellationRequested()
        let byAddress = Map.ofList documents

        let byLogicalPath (addressOf: 'a -> ModuleDocumentAddress) (items: 'a list) =
            ct.ThrowIfCancellationRequested()

            try
                items
                |> List.sortWith (fun a b ->
                    ct.ThrowIfCancellationRequested()

                    System.String.CompareOrdinal(
                        logicalPath (scope (addressOf a)),
                        logicalPath (scope (addressOf b))
                    )
                )
            with :? System.InvalidOperationException as ex ->
                match ex.InnerException with
                | :? System.OperationCanceledException as canceled ->
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(canceled).Throw()
                    reraise ()
                | _ -> reraise ()

        /// The members of one kind that a module document lists, each read from its own document.
        let members
            (path: ModulePath)
            (what: string)
            (addressOf: Name -> ModuleDocumentAddress)
            (read: TreeDocument<'moduleDoc, 'typeDoc, 'valueDoc> -> 'item option)
            (build: (Name * 'item) list -> Result<'map, Name>)
            (names: Name list)
            : Result<'map, TreeError list> =
            ct.ThrowIfCancellationRequested()

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
                    ct.ThrowIfCancellationRequested()
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
            | [], Ok entries ->
                // The names are distinct here, so the map always builds.
                build entries
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
                ct.ThrowIfCancellationRequested()

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
                ct.ThrowIfCancellationRequested()

                let types =
                    members
                        path
                        "type"
                        (fun name -> TypeDef(path, name))
                        readType
                        shape.TypeMap
                        (family.TypeNames m)

                let values =
                    members
                        path
                        "value"
                        (fun name -> ValueDef(path, name))
                        readValue
                        shape.ValueMap
                        (family.ValueNames m)

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
                ct.ThrowIfCancellationRequested()

                (family.TypeNames m
                 |> List.map (fun name -> TypeDef(path, name)))
                @ (family.ValueNames m
                   |> List.map (fun name -> ValueDef(path, name)))
            )
            |> Set.ofList

        let unlisted =
            documents
            |> List.map fst
            |> List.filter (fun address ->
                ct.ThrowIfCancellationRequested()

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

        // Each module path is one key of the documents, so the map always builds.
        let made =
            modules
            |> Result.bind (fun modules ->
                moduleMap modules
                |> Result.mapError (fun path -> [
                    error
                        "tree.mismatched_document"
                        (Some(scope (Module path)))
                        $"The tree has {scope (Module path)} twice."
                ])
            )

        match made, unlisted with
        | Ok modules, [] -> Ok modules
        | _ ->
            Error(
                errorsOf made
                @ unlisted
            )

    /// <summary>The documents of the own package, at addresses within it.</summary>
    let ownDocuments (documents: (DocumentAddress * 'document) list) =
        documents
        |> List.choose (fun (address, document) ->
            match address with
            | Own inner -> Some(inner, document)
            | Manifest
            | Dependency _ -> None
        )

    /// <summary>The documents of one dependency, at addresses within it.</summary>
    let dependencyDocuments (package: PackageName) (documents: (DocumentAddress * 'document) list) =
        documents
        |> List.choose (fun (address, document) ->
            match address with
            | Dependency(p, inner) when p = package -> Some(inner, document)
            | Manifest
            | Own _
            | Dependency _ -> None
        )

    let private listedTwice (PackageName package) =
        error
            "tree.duplicate_dependency"
            (Some Manifest)
            $"The manifest lists the dependency '{CanonicalNames.pathToString package}' more than once."

    /// <summary>
    /// Each dependency the manifest lists, joined by <paramref name="joinOne"/>, in a map built by
    /// <paramref name="dependencyMap"/>, or every error.
    /// </summary>
    let joinDependencies
        (ct: CancellationToken)
        (joinOne: PackageName -> Result<'package, TreeError list>)
        (dependencyMap: (PackageName * 'package) list -> Result<'map, PackageName>)
        (dependencies: PackageName list)
        : Result<'map, TreeError list> =
        ct.ThrowIfCancellationRequested()

        dependencies
        |> List.distinct
        |> List.map (fun package ->
            ct.ThrowIfCancellationRequested()

            joinOne package
            |> Result.map (fun contents -> package, contents)
        )
        |> collect
        |> Result.bind (fun packages ->
            // List.distinct leaves each package once, so the map always builds.
            dependencyMap packages
            |> Result.mapError (fun package -> [ listedTwice package ])
        )

    /// <summary>
    /// The errors of a manifest's dependencies and of documents that belong to no package of it: a dependency
    /// listed twice, then the family's own manifest errors, then the documents of each undeclared dependency.
    /// </summary>
    let manifestErrors
        (ct: CancellationToken)
        (dependencies: PackageName list)
        (familyErrors: TreeError list)
        (documents: Map<DocumentAddress, 'document>)
        (logicalPath: DocumentAddress -> string)
        : TreeError list =
        ct.ThrowIfCancellationRequested()

        let repeatedDependencies =
            dependencies
            |> List.countBy id
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (
                fst
                >> listedTwice
            )

        let undeclared =
            documents
            |> Map.keys
            |> List.ofSeq
            |> List.choose (fun address ->
                ct.ThrowIfCancellationRequested()

                match address with
                | Dependency(package, _) when not (List.contains package dependencies) ->
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
        @ familyErrors
        @ undeclared

    /// <summary>
    /// The model, or every error: the tree's own errors first, then those of the own package, then those of the
    /// dependencies.
    /// </summary>
    let assemble
        (treeErrors: TreeError list)
        (ownResult: Result<'own, TreeError list>)
        (dependencyResult: Result<'dependencies, TreeError list>)
        (build: 'own -> 'dependencies -> 'model)
        : Result<'model, TreeError list> =
        match treeErrors, ownResult, dependencyResult with
        | [], Ok ownModules, Ok dependencyMap -> Ok(build ownModules dependencyMap)
        | found, _, _ ->
            Error(
                found
                @ errorsOf ownResult
                @ errorsOf dependencyResult
            )
