// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.Collections.Generic
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Morphir.Compilation
open Morphir.IR

/// <summary>One constructor of a custom type: a case record of a closed record, or an enum member.</summary>
type internal PlannedCase = {
    /// <summary>The case record symbol, or the enum member symbol.</summary>
    Symbol: ISymbol
    Name: Name
    /// <summary>The fields in constructor order: the Morphir name, the C# member name and the C# type.</summary>
    Fields: (Name * string * ITypeSymbol) list
    /// <summary>The location of each field, in the same order: the C# type syntax of the field.</summary>
    FieldLocations: Morphir.Compilation.Location list
    Location: Morphir.Compilation.Location
}

/// <summary>What a planned type becomes in the model.</summary>
type internal PlannedTypeKind =
    /// <summary>A plain record: a type alias of a record type.</summary>
    | RecordAlias of fields: (Name * string * ITypeSymbol) list * locations: Morphir.Compilation.Location list
    /// <summary>A closed record or an enum: a custom type.</summary>
    | CustomType of constructors: PlannedCase list

/// <summary>A type of a module.</summary>
type internal PlannedType = {
    Symbol: INamedTypeSymbol
    Name: Name
    Parameters: Name list
    Kind: PlannedTypeKind
    Access: Morphir.Compilation.Building.Access
    Location: Morphir.Compilation.Location
}

/// <summary>A value of a module: a static method or a constant.</summary>
type internal PlannedValue = {
    Symbol: ISymbol
    Name: Name
    Access: Morphir.Compilation.Building.Access
    Location: Morphir.Compilation.Location
    /// <summary>The method declaration or the variable declarator of the constant.</summary>
    Syntax: SyntaxNode
}

/// <summary>A module of the package.</summary>
type internal PlannedModule = {
    Path: ModulePath
    Access: Morphir.Compilation.Building.Access
    Location: Morphir.Compilation.Location
    Types: PlannedType list
    Values: PlannedValue list
}

/// <summary>The plan of a package: its modules and the index from C# symbols to Morphir names.</summary>
type internal Plan = {
    Package: PackageName
    Compiled: Compiled
    Modules: PlannedModule list
    /// <summary>Type symbols of the package to the name of their type.</summary>
    TypeNames: Dictionary<ISymbol, FQName * PlannedType>
    /// <summary>Case records and enum members to the name of their constructor.</summary>
    ConstructorNames: Dictionary<ISymbol, FQName * PlannedCase>
    /// <summary>Methods and constants of the package to the name of their value.</summary>
    ValueNames: Dictionary<ISymbol, FQName>
}

/// <summary>Plans the modules of a package from a bound compilation.</summary>
module internal Planning =

    let private access (symbol: ISymbol) =
        match symbol.DeclaredAccessibility with
        | Accessibility.Public -> Morphir.Compilation.Building.Access.Public
        | _ -> Morphir.Compilation.Building.Access.Private

    let private comparer = SymbolEqualityComparer.Default

    /// <summary>
    /// The Morphir name of a type parameter. By C# convention a type parameter has the prefix <c>T</c> before a word that starts
    /// with a capital letter (<c>TKey</c>), and the backend writes the type parameter <c>a</c> as <c>TA</c>. The prefix is not part
    /// of the Morphir name. A type parameter that is only <c>T</c> keeps its name.
    /// </summary>
    let typeParameterName (name: string) : Name =
        if name.Length > 1 && name[0] = 'T' && System.Char.IsUpper name[1] then
            Name.fromString (name.Substring 1)
        else
            Name.fromString name

    let private newIndex<'T> () = Dictionary<ISymbol, 'T>(comparer)

    let private namesOf (symbol: INamespaceSymbol) : Name list =
        let rec parts (current: INamespaceSymbol) acc =
            if isNull current || current.IsGlobalNamespace then
                acc
            else
                parts current.ContainingNamespace (Name.fromString current.Name :: acc)

        parts symbol []

    let private nameFailure (location: Morphir.Compilation.Location) (identifier: string) =
        Diagnostic.error CSharpCodes.unsupportedName $"The C# identifier '{identifier}' gives no Morphir name."
        |> Diagnostic.at location

    /// <summary>The Morphir module path of a namespace and a chain of static classes, relative to the package.</summary>
    let private modulePath (package: PackageName) (ns: Name list) (classes: Name list) =
        let (PackageName(Path packageNames)) = package

        let rec strip (prefix: Name list) (names: Name list) =
            match prefix, names with
            | [], rest -> Some rest
            | p :: ps, n :: ns when p = n -> strip ps ns
            | _ -> None

        match strip packageNames ns with
        | None -> Error "outside"
        | Some rest ->
            match rest @ classes with
            | [] -> Error "empty"
            | names -> Ok(ModulePath(Path names))

    let private locationOf (compiled: Compiled) (node: SyntaxNode) =
        CSharpCompiler.locationOfNode compiled node
        |> Option.defaultValue (Morphir.Compilation.Location.Create("morphir:unknown", Range.Create(Position.Create(0, 0), Position.Create(0, 0))))

    let private fieldsOfRecord (compiled: Compiled) (model: SemanticModel) (declaration: RecordDeclarationSyntax) (symbol: INamedTypeSymbol) =
        match declaration.ParameterList with
        | null ->
            let properties =
                symbol.GetMembers()
                |> Seq.choose (function
                    | :? IPropertySymbol as property when
                        not property.IsImplicitlyDeclared
                        && not property.IsStatic
                        && property.DeclaredAccessibility = Accessibility.Public
                        ->
                        Some property
                    | _ -> None)
                |> List.ofSeq

            let fields = [ for property in properties -> Name.fromString property.Name, property.Name, property.Type ]

            let locations =
                [ for property in properties ->
                      match property.DeclaringSyntaxReferences |> Seq.tryHead with
                      | Some reference -> locationOf compiled (reference.GetSyntax())
                      | None -> locationOf compiled declaration ]

            fields, locations
        | parameters ->
            let items =
                parameters.Parameters
                |> Seq.map (fun parameter ->
                    let declared = model.GetDeclaredSymbol parameter
                    let at = locationOf compiled (if isNull parameter.Type then (parameter :> SyntaxNode) else (parameter.Type :> SyntaxNode))
                    (Name.fromString declared.Name, declared.Name, declared.Type), at)
                |> List.ofSeq

            List.map fst items, List.map snd items

    /// <summary>Plans a package. Exposed modules follow the request: none listed means every public module.</summary>
    let plan
        (package: PackageName)
        (exposed: string list option)
        (compiled: Compiled)
        (cancellationToken: Threading.CancellationToken)
        : Checked<Plan> =
        let diagnostics = ResizeArray<Morphir.Compilation.Diagnostic>()
        let typeNames = newIndex<FQName * PlannedType> ()
        let constructorNames = newIndex<FQName * PlannedCase> ()
        let valueNames = newIndex<FQName>()
        let modules = Dictionary<ModulePath, PlannedModule>()
        let order = ResizeArray<ModulePath>()

        let ensureModule (path: ModulePath) (location: Morphir.Compilation.Location) (moduleAccess: Morphir.Compilation.Building.Access) =
            match modules.TryGetValue path with
            | true, existing ->
                if moduleAccess = Morphir.Compilation.Building.Access.Public
                   && existing.Access = Morphir.Compilation.Building.Access.Private then
                    modules[path] <- { existing with Access = moduleAccess }
            | _ ->
                order.Add path
                modules[path] <-
                    {
                        Path = path
                        Access = moduleAccess
                        Location = location
                        Types = []
                        Values = []
                    }

        let modulePathOf (location: Morphir.Compilation.Location) (ns: Name list) (classes: Name list) =
            match modulePath package ns classes with
            | Ok path -> Some path
            | Error _ ->
                let packageText = Path.toString Name.toKebabCase "/" (let (PackageName p) = package in p)

                diagnostics.Add(
                    Diagnostic.error
                        CSharpCodes.moduleOutsidePackage
                        $"The namespace is not below the package path '{packageText}'. Put types in a namespace below the package path, or in a static class."
                    |> Diagnostic.at location
                )

                None

        // All named types with their declaration, document by document, in source order.
        let declarations =
            [ for source in compiled.Sources do
                  let model = compiled.Compilation.GetSemanticModel(source.Tree, false)

                  for node in source.Tree.GetRoot(cancellationToken).DescendantNodes() do
                      match node with
                      | :? BaseTypeDeclarationSyntax as declaration ->
                          match model.GetDeclaredSymbol(declaration, cancellationToken) with
                          | null -> ()
                          | symbol -> yield model, declaration, symbol
                      | _ -> () ]

        // The cases of each closed record, in source order.
        let casesOf =
            let table = Dictionary<ISymbol, ResizeArray<INamedTypeSymbol * RecordDeclarationSyntax * SemanticModel>>(comparer)

            for model, declaration, symbol in declarations do
                match declaration, symbol.BaseType with
                | (:? RecordDeclarationSyntax as record), (null | _ as baseType) when not (isNull baseType) && baseType.IsClosed ->
                    let key = baseType.OriginalDefinition :> ISymbol

                    match table.TryGetValue key with
                    | true, list -> list.Add((symbol, record, model))
                    | _ ->
                        let list = ResizeArray()
                        list.Add((symbol, record, model))
                        table[key] <- list
                | _ -> ()

            table

        let isCase (symbol: INamedTypeSymbol) =
            not (isNull symbol.BaseType)
            && symbol.BaseType.IsClosed

        for model, declaration, symbol in declarations do
            let location = locationOf compiled declaration

            // A type is planned when it sits directly in a namespace or in a static class. Case records are
            // constructors of their closed record, and other nested types belong to no module.
            let container = symbol.ContainingType

            let placement =
                if isNull container then
                    Some(namesOf symbol.ContainingNamespace, [])
                elif container.IsStatic then
                    let rec chain (t: INamedTypeSymbol) acc =
                        if isNull t then acc else chain t.ContainingType (Name.fromString t.Name :: acc)

                    Some(namesOf container.ContainingNamespace, chain container [])
                else
                    None

            match placement with
            | Some(ns, classes) when
                not (isCase symbol)
                && symbol.TypeKind <> TypeKind.Interface
                && symbol.TypeKind <> TypeKind.Delegate
                ->
                let isStaticClass = symbol.IsStatic && symbol.TypeKind = TypeKind.Class

                if isStaticClass then
                    // A static class is a module: a type of this kind has no entry of its own.
                    match modulePathOf location ns (classes @ [ Name.fromString symbol.Name ]) with
                    | Some path -> ensureModule path location (access symbol)
                    | None -> ()
                else
                    match modulePathOf location ns classes with
                    | None -> ()
                    | Some path ->
                        let typeName = Name.fromString symbol.Name

                        if Name.isEmpty typeName then
                            diagnostics.Add(nameFailure location symbol.Name)
                        else
                            let parameters = [ for p in symbol.TypeParameters -> typeParameterName p.Name ]

                            let kind =
                                match symbol.TypeKind with
                                | TypeKind.Enum ->
                                    let members =
                                        symbol.GetMembers()
                                        |> Seq.choose (function
                                            | :? IFieldSymbol as field when field.HasConstantValue ->
                                                Some
                                                    {
                                                        Symbol = field
                                                        Name = Name.fromString field.Name
                                                        Fields = []
                                                        FieldLocations = []
                                                        Location = location
                                                    }
                                            | _ -> None)
                                        |> List.ofSeq

                                    Some(CustomType members)
                                | _ when symbol.IsRecord && symbol.TypeKind = TypeKind.Class ->
                                    match declaration with
                                    | :? RecordDeclarationSyntax as record when symbol.IsClosed ->
                                        let cases =
                                            match casesOf.TryGetValue(symbol.OriginalDefinition) with
                                            | true, list ->
                                                [ for caseSymbol, caseDeclaration, caseModel in list ->
                                                      {
                                                          Symbol = caseSymbol
                                                          Name = Name.fromString caseSymbol.Name
                                                          Fields = fst (fieldsOfRecord compiled caseModel caseDeclaration caseSymbol)
                                                          FieldLocations = snd (fieldsOfRecord compiled caseModel caseDeclaration caseSymbol)
                                                          Location = locationOf compiled caseDeclaration
                                                      } ]
                                            | _ -> []

                                        if List.isEmpty cases then
                                            diagnostics.Add(
                                                Diagnostic.error
                                                    (CSharpCodes.unsupported "closed_without_cases")
                                                    $"The closed record '{symbol.Name}' has no case. A Morphir custom type needs at least one constructor."
                                                |> Diagnostic.at (locationOf compiled record)
                                            )

                                            None
                                        else
                                            Some(CustomType cases)
                                    | :? RecordDeclarationSyntax as record ->
                                        Some(RecordAlias(fieldsOfRecord compiled model record symbol))
                                    | _ -> None
                                | _ -> None

                            match kind with
                            | None -> ()
                            | Some kind ->
                                ensureModule path location (Morphir.Compilation.Building.Access.Public)

                                let planned =
                                    {
                                        Symbol = symbol
                                        Name = typeName
                                        Parameters = parameters
                                        Kind = kind
                                        Access = access symbol
                                        Location = location
                                    }

                                let existing = modules[path]

                                match existing.Types |> List.tryFind (fun t -> t.Name = typeName) with
                                | Some first ->
                                    diagnostics.Add(
                                        Diagnostic.error
                                            CSharpCodes.nameCollision
                                            $"The types '{first.Symbol.Name}' and '{symbol.Name}' have the same Morphir name."
                                        |> Diagnostic.at location
                                        |> Diagnostic.withRelated (
                                            ({ Location = first.Location; Message = "The first declaration." }: RelatedInformation)
                                        )
                                    )
                                | None ->
                                    modules[path] <- { existing with Types = existing.Types @ [ planned ] }

                                    let fq = FQName.fqName package path typeName
                                    typeNames[symbol.OriginalDefinition] <- (fq, planned)

                                    match kind with
                                    | CustomType constructors ->
                                        for case in constructors do
                                            constructorNames[case.Symbol.OriginalDefinition] <-
                                                (FQName.fqName package path case.Name, case)
                                    | RecordAlias _ -> ()
            | _ -> ()

        // Values: static methods and constants of the static classes.
        for model, declaration, symbol in declarations do
            if symbol.IsStatic && symbol.TypeKind = TypeKind.Class then
                let ns = namesOf symbol.ContainingNamespace

                let rec chain (t: INamedTypeSymbol) acc =
                    if isNull t then acc else chain t.ContainingType (Name.fromString t.Name :: acc)

                match modulePathOf (locationOf compiled declaration) ns (chain symbol []) with
                | None -> ()
                | Some path ->
                    ensureModule path (locationOf compiled declaration) (access symbol)

                    for member' in (declaration :?> TypeDeclarationSyntax).Members do
                        let add (memberSymbol: ISymbol) (identifier: string) (syntax: SyntaxNode) =
                            let location = locationOf compiled syntax
                            let name = Name.fromString identifier

                            if Name.isEmpty name then
                                diagnostics.Add(nameFailure location identifier)
                            else
                                let existing = modules[path]

                                match existing.Values |> List.tryFind (fun v -> v.Name = name) with
                                | Some first ->
                                    diagnostics.Add(
                                        Diagnostic.error
                                            CSharpCodes.nameCollision
                                            $"The values '{first.Symbol.Name}' and '{identifier}' have the same Morphir name."
                                        |> Diagnostic.at location
                                        |> Diagnostic.withRelated (
                                            ({ Location = first.Location; Message = "The first declaration." }: RelatedInformation)
                                        )
                                    )
                                | None ->
                                    let planned =
                                        {
                                            Symbol = memberSymbol
                                            Name = name
                                            Access = access memberSymbol
                                            Location = location
                                            Syntax = syntax
                                        }

                                    modules[path] <- { existing with Values = existing.Values @ [ planned ] }
                                    valueNames[memberSymbol.OriginalDefinition] <- FQName.fqName package path name

                        match member' with
                        | :? MethodDeclarationSyntax as method ->
                            match model.GetDeclaredSymbol(method, cancellationToken) with
                            | null -> ()
                            | methodSymbol -> add methodSymbol methodSymbol.Name method
                        | :? FieldDeclarationSyntax as field when field.Modifiers.Any(SyntaxKind.ConstKeyword) ->
                            for variable in field.Declaration.Variables do
                                match model.GetDeclaredSymbol(variable, cancellationToken) with
                                | null -> ()
                                | fieldSymbol -> add fieldSymbol fieldSymbol.Name variable
                        | _ -> ()

        let exposedPublic (path: ModulePath) (current: Morphir.Compilation.Building.Access) =
            match exposed with
            | None -> current
            | Some names ->
                let (ModulePath p) = path
                let text = Path.toString Name.toTitleCase "." p

                if List.contains text names then
                    current
                else
                    Morphir.Compilation.Building.Access.Private

        let finalModules =
            [ for path in order -> let planned = modules[path] in { planned with Access = exposedPublic path planned.Access } ]

        let planned =
            {
                Package = package
                Compiled = compiled
                Modules = finalModules
                TypeNames = typeNames
                ConstructorNames = constructorNames
                ValueNames = valueNames
            }

        Checked.ofDiagnostics planned (List.ofSeq diagnostics)
