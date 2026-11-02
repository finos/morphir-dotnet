namespace Morphir.Ion

open System.Collections.Generic
open Morphir.IR
open Morphir.Ion.IonNodes

/// A type or value with its scope removed: <c>package</c>, <c>module</c> and <c>name</c> are already checked.
type internal NormalizedMember = {
    MemberAnnotations: string list
    MemberName: Name
    MemberFields: Members
    MemberPath: IonPath
}

/// A module with its members inline, whichever layout stated them.
type internal NormalizedModule = {
    ModuleAnnotations: string list
    ModuleName: ModulePath
    /// The module's own members, without <c>package</c>, <c>name</c>, <c>types</c> and <c>values</c>.
    ModuleFields: Members
    ModuleTypes: NormalizedMember list
    ModuleValues: NormalizedMember list
    ModulePath: IonPath
}

/// A dependency after its fragments merged.
type internal NormalizedPackage = {
    PackageName: PackageName
    PackageModules: NormalizedModule list
}

/// One document shape for every layout, so that the version decoders never see a layout (design IRF-1).
type internal NormalizedDocument = {
    Header: IrHeader
    Dependencies: NormalizedPackage list
    Modules: NormalizedModule list
}

/// Layout normalization with its merge rules.
module internal NormalizedDocument =
    let private invalidDocument path message = fail "invalid_document" path message

    /// Collects the members of one module, refusing a name stated twice.
    type private MemberList(kind: string) =
        let items = ResizeArray<NormalizedMember>()
        let names = HashSet<Name>()

        member _.Add(item: NormalizedMember) =
            if not (names.Add item.MemberName) then
                fail
                    "duplicate_name"
                    item.MemberPath
                    $"The {kind} '{CanonicalNames.nameToString item.MemberName}' is already defined."

            items.Add item

        member _.ToList() = List.ofSeq items

    /// A module while its fragments arrive.
    type private ModuleBuilder(annotations: string list, name: ModulePath, path: IonPath) =
        member val Fields: Members = [] with get, set
        member val Types = MemberList "type"
        member val Values = MemberList "value"

        member this.Build() : NormalizedModule = {
            ModuleAnnotations = annotations
            ModuleName = name
            ModuleFields = this.Fields
            ModuleTypes = this.Types.ToList()
            ModuleValues = this.Values.ToList()
            ModulePath = path
        }

    /// A dependency while its fragments arrive.
    type private PackageBuilder(name: PackageName) =
        let modules = ResizeArray<ModuleBuilder>()
        let index = Dictionary<ModulePath, ModuleBuilder>()
        member _.Name = name
        member _.Modules = modules
        member _.Index = index

    /// The kinds of top-level and inline element the draft names.
    type private ElementKind =
        | PackageSpecElement
        | PackageDefElement
        | ModuleSpecElement
        | ModuleDefElement
        | TypeDefElement
        | ValueDefElement
        | TypeSpecElement
        | ValueSpecElement

    let private classify (names: string list) =
        match names with
        | [ "package"; "spec" ] -> Some PackageSpecElement
        | [ "package"; "def" ] -> Some PackageDefElement
        | [ "module"; "spec" ] -> Some ModuleSpecElement
        | [ ("public" | "private"); "def"; "module" ] -> Some ModuleDefElement
        | [ ("public" | "private"); "def"; ("alias" | "custom" | "incomplete"); "type" ] ->
            Some TypeDefElement
        | [ ("public" | "private"); "def"; "value" ]
        | [ ("public" | "private"); "def"; ("native" | "external" | "incomplete"); "value" ] ->
            Some ValueDefElement
        | [ "public"; "spec"; ("opaque" | "alias" | "custom" | "derived"); "type" ] ->
            Some TypeSpecElement
        | [ "public"; "spec"; "value" ] -> Some ValueSpecElement
        | _ -> None

    let private isV4Only (names: string list) =
        match names with
        | [ "package"; "def" ]
        | [ _; "def"; "incomplete"; "type" ]
        | [ _; "def"; ("native" | "external" | "incomplete"); "value" ] -> true
        | _ -> false

    /// Refuses a node the document's version or kind does not allow, whatever position it is in.
    let private gate (header: IrHeader) path (names: string list) =
        if
            header.Family = ClassicFamily
            && isV4Only names
        then
            fail "unsupported_node" path $"{spell names} is a v4 node, and this is a v3 document."

        match names, header.Kind with
        | ("public" | "private") :: "def" :: _, SpecsKind ->
            fail
                "definition_in_specs"
                path
                $"A specs distribution has no definitions, found {spell names}."
        | _ -> ()

    let private sameSpelling (a: IonValue) (b: IonValue) =
        match a.Data, b.Data with
        | IonData.String x, IonData.String y -> x = y
        | _ -> false

    /// Combines the module-level members of two fragments of one module specification.
    let private mergeFields path (existing: Members) (incoming: Members) =
        incoming
        |> List.fold
            (fun (fields: Members) (name, item) ->
                match tryMember name fields, name with
                | None, _ ->
                    fields
                    @ [ name, item ]
                | Some earlier, "doc" when sameSpelling earlier item -> fields
                | Some _, "doc" ->
                    invalidDocument
                        (memberAt path "doc")
                        "Two fragments of one module state different docs."
                | Some earlier, "annotations" ->
                    match earlier.Data, item.Data with
                    | IonData.List first, IonData.List second ->
                        fields
                        |> List.map (fun (key, value) ->
                            if key = "annotations" then
                                key,
                                { value with
                                    Data =
                                        IonData.List(
                                            first
                                            @ second
                                        )
                                }
                            else
                                key, value
                        )
                    | _ -> fail "invalid_type" (memberAt path "annotations") "Expected a list."
                | Some _, _ ->
                    fail "unknown_member" (memberAt path name) $"Unknown member '{name}'."
            )
            existing

    /// Checks a member's stated scope against where it was found.
    let private checkStated path name expected (describe: string) (fields: Members) =
        match tryMember name fields with
        | None -> ()
        | Some value ->
            let here = memberAt path name

            if
                pathText here (text here value)
                <> expected
            then
                invalidDocument
                    here
                    $"This {describe} belongs to {CanonicalNames.pathToString expected}."

    let private packagePath (PackageName path) = path
    let private modulePathOf (ModulePath path) = path

    let private toMember path (names: string list) (fields: Members) = {
        MemberAnnotations = names
        MemberName = required path "name" nameString fields
        MemberFields = without [ "package"; "module"; "name" ] fields
        MemberPath = path
    }

    /// Adds the inline types and values of a module.
    let private addInline
        (header: IrHeader)
        (owner: ModuleBuilder)
        (package: PackageName)
        (moduleName: ModulePath)
        (typeKind: ElementKind)
        (valueKind: ElementKind)
        path
        (fields: Members)
        =
        let add listName (target: MemberList) expected =
            for itemPath, item in optionalItems path listName fields do
                let names = annotationNames itemPath item
                gate header itemPath names

                if
                    classify names
                    <> Some expected
                then
                    fail "invalid_node" itemPath $"{listName} cannot hold {spell names}."

                let itemFields = members itemPath item
                checkStated itemPath "package" (packagePath package) "member" itemFields
                checkStated itemPath "module" (modulePathOf moduleName) "member" itemFields
                target.Add(toMember itemPath names itemFields)

        add "types" owner.Types typeKind
        add "values" owner.Values valueKind

    /// The fields a module keeps for its decoder.
    let private moduleFields (fields: Members) =
        without [ "package"; "name"; "types"; "values" ] fields

    /// Reads one fragment of a module specification into a dependency, which merges it with earlier ones.
    let private addSpecFragment
        header
        (dependency: PackageBuilder)
        path
        (names: string list)
        (fields: Members)
        =
        checkStated path "package" (packagePath dependency.Name) "module" fields

        let moduleName =
            ModulePath(
                required path "name" (fun here item -> pathText here (text here item)) fields
            )

        let builder =
            match dependency.Index.TryGetValue moduleName with
            | true, existing ->
                existing.Fields <- mergeFields path existing.Fields (moduleFields fields)
                existing
            | false, _ ->
                let created = ModuleBuilder(names, moduleName, path)
                created.Fields <- moduleFields fields
                dependency.Index[moduleName] <- created
                dependency.Modules.Add created
                created

        addInline
            header
            builder
            dependency.Name
            moduleName
            TypeSpecElement
            ValueSpecElement
            path
            fields

    /// Adds a definition module to a list of modules. These do not merge.
    let private addDefModule
        header
        (modules: ResizeArray<ModuleBuilder>)
        (index: Dictionary<ModulePath, ModuleBuilder>)
        (package: PackageName)
        path
        (names: string list)
        (fields: Members)
        =
        checkStated path "package" (packagePath package) "module" fields

        let moduleName =
            ModulePath(
                required path "name" (fun here item -> pathText here (text here item)) fields
            )

        if index.ContainsKey moduleName then
            fail
                "duplicate_name"
                path
                $"The module '{CanonicalNames.pathToString (modulePathOf moduleName)}' is already defined."

        let builder = ModuleBuilder(names, moduleName, path)
        builder.Fields <- moduleFields fields
        index[moduleName] <- builder
        modules.Add builder
        addInline header builder package moduleName TypeDefElement ValueDefElement path fields

    /// Folds the body elements of a document, in document order, into dependencies and modules.
    let private normalizeBody (header: IrHeader) (elements: Node list) =
        let dependencies = ResizeArray<PackageBuilder>()
        let dependencyIndex = Dictionary<PackageName, PackageBuilder>()
        let modules = ResizeArray<ModuleBuilder>()
        let moduleIndex = Dictionary<ModulePath, ModuleBuilder>()

        let dependencyNamed (name: PackageName) =
            match dependencyIndex.TryGetValue name with
            | true, existing -> existing
            | false, _ ->
                let created = PackageBuilder name
                dependencyIndex[name] <- created
                dependencies.Add created
                created

        /// A package a specification fragment names by a package member.
        let statedDependency path (fields: Members) =
            let here = memberAt path "package"

            match tryMember "package" fields with
            | None -> None
            | Some value ->
                let name = PackageName(pathText here (text here value))

                if name = header.Package then
                    invalidDocument
                        here
                        "A specification fragment outside the distribution's own package names that package."

                Some(dependencyNamed name)

        let packageNameOf path (fields: Members) =
            PackageName(
                required path "name" (fun here item -> pathText here (text here item)) fields
            )

        let addElement (path, element: IonValue) =
            let names = annotationNames path element
            gate header path names

            match classify names, header.Kind with
            | None, _ ->
                invalidDocument path $"{spell names} is not a top-level element of a distribution."
            | Some PackageSpecElement, ApplicationKind ->
                fail
                    "unsupported_node"
                    path
                    "An application writes its dependencies as package::def."
            | Some PackageDefElement, (LibraryKind | SpecsKind) ->
                fail
                    "unsupported_node"
                    path
                    "Only an application writes its dependencies as package::def."
            | Some PackageSpecElement, (LibraryKind | SpecsKind) ->
                let fields = members path element

                if hasMember "annotations" fields then
                    fail
                        "invalid_node"
                        (memberAt path "annotations")
                        "A package specification has no annotations."

                closed path [ "name"; "modules" ] fields
                let name = packageNameOf path fields

                if
                    header.Family = ClassicFamily
                    && name = header.Package
                then
                    invalidDocument
                        path
                        "A v3 package::spec cannot name the distribution's own package."

                let dependency = dependencyNamed name

                for itemPath, item in optionalItems path "modules" fields do
                    let itemNames = annotationNames itemPath item

                    if
                        itemNames
                        <> [ "module"; "spec" ]
                    then
                        fail
                            "invalid_node"
                            itemPath
                            $"package::spec modules are module::spec, found {spell itemNames}."

                    addSpecFragment header dependency itemPath itemNames (members itemPath item)
            | Some PackageDefElement, ApplicationKind ->
                let fields = members path element

                if hasMember "annotations" fields then
                    fail
                        "invalid_node"
                        (memberAt path "annotations")
                        "A package definition has no annotations."

                closed path [ "name"; "modules" ] fields
                let dependency = dependencyNamed (packageNameOf path fields)

                for itemPath, item in optionalItems path "modules" fields do
                    let itemNames = annotationNames itemPath item

                    if
                        classify itemNames
                        <> Some ModuleDefElement
                    then
                        fail
                            "invalid_node"
                            itemPath
                            $"package::def modules are definitions, found {spell itemNames}."

                    addDefModule
                        header
                        dependency.Modules
                        dependency.Index
                        dependency.Name
                        itemPath
                        itemNames
                        (members itemPath item)
            | Some ModuleSpecElement, _ ->
                let fields = members path element

                match statedDependency path fields, header.Kind with
                | Some _, ApplicationKind ->
                    fail "unsupported_node" path "An application's dependencies are package::def."
                | Some dependency, (LibraryKind | SpecsKind) ->
                    addSpecFragment header dependency path names fields
                | None, SpecsKind ->
                    // A specs distribution's own modules do not merge.
                    let moduleName =
                        ModulePath(
                            required
                                path
                                "name"
                                (fun here item -> pathText here (text here item))
                                fields
                        )

                    if moduleIndex.ContainsKey moduleName then
                        fail
                            "duplicate_name"
                            path
                            $"The module '{CanonicalNames.pathToString (modulePathOf moduleName)}' is already defined."

                    let builder = ModuleBuilder(names, moduleName, path)
                    builder.Fields <- moduleFields fields
                    moduleIndex[moduleName] <- builder
                    modules.Add builder

                    addInline
                        header
                        builder
                        header.Package
                        moduleName
                        TypeSpecElement
                        ValueSpecElement
                        path
                        fields
                | None, (LibraryKind | ApplicationKind) ->
                    fail
                        "unsupported_node"
                        path
                        "A library or application writes its own modules as definitions."
            | Some ModuleDefElement, (LibraryKind | ApplicationKind | SpecsKind) ->
                addDefModule
                    header
                    modules
                    moduleIndex
                    header.Package
                    path
                    names
                    (members path element)
            | Some((TypeDefElement | ValueDefElement) as kind), _ ->
                let fields = members path element
                checkStated path "package" (packagePath header.Package) "definition" fields

                let moduleName =
                    match tryMember "module" fields with
                    | Some value ->
                        let here = memberAt path "module"
                        ModulePath(pathText here (text here value))
                    | None -> invalidDocument path "A top-level definition states its module."

                match moduleIndex.TryGetValue moduleName with
                | true, owner ->
                    let target = if kind = TypeDefElement then owner.Types else owner.Values

                    target.Add(toMember path names fields)
                | false, _ ->
                    invalidDocument
                        path
                        $"The module '{CanonicalNames.pathToString (modulePathOf moduleName)}' has not appeared."
            | Some(TypeSpecElement | ValueSpecElement), ApplicationKind ->
                fail "unsupported_node" path "An application's dependencies are package::def."
            | Some((TypeSpecElement | ValueSpecElement) as kind), (LibraryKind | SpecsKind) ->
                let fields = members path element

                let dependency =
                    match statedDependency path fields with
                    | Some found -> found
                    | None ->
                        invalidDocument
                            path
                            "A top-level specification names a dependency; a module's own sit inline."

                let moduleName =
                    match tryMember "module" fields with
                    | Some value ->
                        let here = memberAt path "module"
                        ModulePath(pathText here (text here value))
                    | None -> invalidDocument path "A top-level specification states its module."

                let owner =
                    match dependency.Index.TryGetValue moduleName with
                    | true, existing -> existing
                    | false, _ ->
                        let created = ModuleBuilder([ "module"; "spec" ], moduleName, path)
                        dependency.Index[moduleName] <- created
                        dependency.Modules.Add created
                        created

                let target = if kind = TypeSpecElement then owner.Types else owner.Values

                target.Add(toMember path names fields)

        elements
        |> List.iter addElement

        {
            Header = header
            Dependencies =
                dependencies
                |> Seq.map (fun dependency -> {
                    PackageName = dependency.Name
                    PackageModules =
                        dependency.Modules
                        |> Seq.map (fun builder -> builder.Build())
                        |> List.ofSeq
                })
                |> List.ofSeq
            Modules =
                modules
                |> Seq.map (fun builder -> builder.Build())
                |> List.ofSeq
        }

    /// Refuses a list member in a datagram's header, where each item has its own top-level value.
    let private refuseHeaderList path name (fields: Members) =
        match tryMember name fields with
        | None -> ()
        | Some item ->
            match item.Data with
            | IonData.List [] -> ()
            | _ ->
                invalidDocument
                    (memberAt path name)
                    $"A datagram writes each of {name} as its own top-level value."

    /// A record expands to the elements a datagram holds at the top level.
    let private recordElements path (fields: Members) =
        let items name =
            match tryMember name fields with
            | None -> []
            | Some item ->
                match item.Annotations, item.Data with
                | [], IonData.List values ->
                    let here = memberAt path name

                    values
                    |> List.mapi (fun i value -> itemAt here i, value)
                | _ -> invalidDocument (memberAt path name) $"{name} is a list."

        let dependencies = items "dependencies"

        for itemPath, item in dependencies do
            match annotationNames itemPath item with
            | [ "package"; ("spec" | "def") ] -> ()
            | names ->
                invalidDocument itemPath $"dependencies hold package elements, found {spell names}."

        dependencies
        @ items "modules"

    let private checkFooter path (footer: IonValue) =
        match annotationNames path footer, footer.Data with
        | [ "morphir_footer" ], IonData.Struct [] -> ()
        | [ "morphir_footer" ], _ -> invalidDocument path "morphir_footer is an empty struct."
        | _ -> invalidDocument path "A datagram ends with morphir_footer::{}."

    /// Reduces a datagram or a record to the one document shape.
    let normalize (values: IonValue list) : NormalizedDocument =
        match values with
        | [] -> invalidDocument Root "A document starts with a morphir:: header."
        | first :: rest ->
            let headerPath = itemAt Root 0

            match first.Annotations with
            | [ IonSymbol.Text "morphir" ] -> ()
            | _ -> invalidDocument headerPath "A document starts with a morphir:: header."

            let fields = IrHeader.fields headerPath first
            let header = IrHeader.read headerPath fields

            match rest with
            | [] ->
                if
                    header.Family = ClassicFamily
                    && header.Kind = SpecsKind
                then
                    fail
                        "unsupported_node"
                        headerPath
                        "A v3 specs distribution is read from its datagram only."

                normalizeBody header (recordElements headerPath fields)
            | _ ->
                refuseHeaderList headerPath "dependencies" fields
                refuseHeaderList headerPath "modules" fields

                let last =
                    values.Length
                    - 1

                checkFooter (itemAt Root last) (List.last values)

                let body =
                    rest
                    |> List.take (last - 1)
                    |> List.mapi (fun i value -> itemAt Root (i + 1), value)

                normalizeBody header body
