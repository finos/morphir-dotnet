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

/// <summary>One constructor of a custom type: a case record of a closed record, an enum member, or a case type of a union.</summary>
type internal PlannedCase = {
    /// <summary>The case record symbol, the enum member symbol, or the case type of a union.</summary>
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
    /// <summary>A closed record, an enum or a union: a custom type.</summary>
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
    /// <summary>
    /// Unions of the package to the names of their constructors, in the order of the case types. A case type is not a key of
    /// <c>ConstructorNames</c>: it is a type of its own, and two unions of different modules can have the same case type.
    /// </summary>
    UnionCases: Dictionary<ISymbol, FQName list>
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

    let private refusal (compiled: Compiled) (slug: string) (node: SyntaxNode) (message: string) =
        Diagnostic.error (CSharpCodes.unsupported slug) message |> Diagnostic.at (locationOf compiled node)

    /// <summary>Whether a property is an auto-property: it has accessors, and no accessor has a body.</summary>
    let private isAutoProperty (property: PropertyDeclarationSyntax) =
        isNull property.ExpressionBody
        && not (isNull property.AccessorList)
        && property.AccessorList.Accessors |> Seq.forall (fun accessor -> isNull accessor.Body && isNull accessor.ExpressionBody)

    /// <summary>
    /// The members of a record that the model has no place for. A positional record has only its parameters as fields,
    /// and a record without parameters has only public auto-properties. Any other member, such as a computed property or
    /// a method, would be lost, so it is refused.
    /// </summary>
    let private recordMemberRefusals (compiled: Compiled) (declaration: RecordDeclarationSyntax) (positional: bool) =
        [ for member' in declaration.Members do
              let refuse (what: string) =
                  refusal
                      compiled
                      "record_member"
                      member'
                      $"The record '{declaration.Identifier.Text}' has {what}. A Morphir record has only fields: give each field as a parameter of the record, and write computations as functions of a static class."

              match member' with
              | :? PropertyDeclarationSyntax as property when
                  not positional
                  && isAutoProperty property
                  && property.Modifiers.Any(SyntaxKind.PublicKeyword)
                  && not (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                  ->
                  ()
              | :? PropertyDeclarationSyntax as property when positional && isAutoProperty property ->
                  yield refuse $"the property '{property.Identifier.Text}' outside its parameters"
              | :? PropertyDeclarationSyntax as property -> yield refuse $"the computed or hidden property '{property.Identifier.Text}'"
              | :? BaseTypeDeclarationSyntax -> ()
              | :? MethodDeclarationSyntax as method -> yield refuse $"the method '{method.Identifier.Text}'"
              | :? FieldDeclarationSyntax -> yield refuse "a field"
              | _ -> yield refuse "a member that is not a field" ]

    /// <summary>
    /// The type declarations that the frontend does not read and that no admission rule refuses: an abstract record, a
    /// record that inherits from another record that is not closed, and the members of a record, a union or a static class
    /// that the model has no place for. Without these refusals the types or members would be left out.
    /// </summary>
    let private shapeRefusals (compiled: Compiled) (declaration: BaseTypeDeclarationSyntax) (symbol: INamedTypeSymbol) =
        [ if symbol.IsUnion then
              // A union is only its cases: a member or an interface would be lost, as on a record.
              match declaration with
              | :? TypeDeclarationSyntax as union ->
                  if not (isNull union.BaseList) then
                      yield
                          refusal
                              compiled
                              "union_member"
                              union.BaseList
                              $"The union '{symbol.Name}' has a base list. A Morphir custom type has only its cases: write computations as functions of a static class."

                  for member' in union.Members do
                      yield
                          refusal
                              compiled
                              "union_member"
                              member'
                              $"The union '{symbol.Name}' has a member. A Morphir custom type has only its cases: write computations as functions of a static class."
              | _ -> ()
          elif symbol.IsRecord && symbol.TypeKind = TypeKind.Class then
              if symbol.IsAbstract && not symbol.IsClosed then
                  yield
                      refusal
                          compiled
                          "abstract_record"
                          declaration
                          $"The abstract record '{symbol.Name}' is not a Morphir type. Write 'closed record {symbol.Name}' so that its cases form a custom type."
              elif not (isNull symbol.BaseType)
                   && symbol.BaseType.SpecialType <> SpecialType.System_Object
                   && not symbol.BaseType.IsClosed
                   && not (symbol.BaseType.IsAbstract && symbol.BaseType.IsRecord) then
                  yield
                      refusal
                          compiled
                          "record_inheritance"
                          declaration
                          $"The record '{symbol.Name}' inherits from '{symbol.BaseType.Name}'. A Morphir record has no base: give every field to the record itself."

              // A partial record has its parameters in one part, and those parameters are the fields of every part.
              let positional =
                  symbol.DeclaringSyntaxReferences
                  |> Seq.exists (fun reference ->
                      match reference.GetSyntax() with
                      | :? RecordDeclarationSyntax as part -> not (isNull part.ParameterList)
                      | _ -> false)

              match declaration with
              | :? RecordDeclarationSyntax as record -> yield! recordMemberRefusals compiled record positional
              | _ -> ()
          elif symbol.IsStatic && symbol.TypeKind = TypeKind.Class then
              match declaration with
              | :? TypeDeclarationSyntax as type' ->
                  for member' in type'.Members do
                      match member' with
                      | :? MethodDeclarationSyntax
                      | :? BaseTypeDeclarationSyntax
                      | :? DelegateDeclarationSyntax -> ()
                      | :? FieldDeclarationSyntax as field when field.Modifiers.Any(SyntaxKind.ConstKeyword) -> ()
                      | _ ->
                          yield
                              refusal
                                  compiled
                                  "static_member"
                                  member'
                                  $"The static class '{symbol.Name}' has a member that is not a method or a constant. Write a value as a 'const' or as a static method."
              | _ -> () ]

    let private shortName (t: ITypeSymbol) = t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)

    /// <summary>
    /// The constructors of a union: one for each case type, with the local name of the Morphir type reference of the case
    /// and that type as its one argument <c>value</c>. A case with no name (a type parameter, a tuple, a function), a case
    /// record of a closed record (a constructor, not a type) and a case with no Morphir type are refused.
    /// </summary>
    let private unionCases (compiled: Compiled) (declaration: BaseTypeDeclarationSyntax) (symbol: INamedTypeSymbol) (location: Morphir.Compilation.Location) =
        let caseSyntax =
            match declaration with
            | :? TypeDeclarationSyntax as union when not (isNull union.ParameterList) ->
                [ for parameter in union.ParameterList.Parameters -> (if isNull parameter.Type then (parameter :> SyntaxNode) else (parameter.Type :> SyntaxNode)) ]
            | _ -> []

        let own = compiled.Compilation.Assembly

        let noName (caseType: ITypeSymbol) (at: Morphir.Compilation.Location) (what: string) =
            Error(
                Diagnostic.error
                    (CSharpCodes.unsupported "union_case")
                    $"The case '{shortName caseType}' of the union '{symbol.Name}' is {what}, so its constructor has no name. Give the case a record type, for example 'sealed record Some<T>(T Value)'."
                |> Diagnostic.at at
            )

        [ for index, caseType in List.indexed (List.ofSeq symbol.UnionCaseTypes) ->
              let at =
                  match List.tryItem index caseSyntax with
                  | Some syntax -> locationOf compiled syntax
                  | None -> location

              let named =
                  match caseType with
                  | :? ITypeParameterSymbol -> noName caseType at "a type parameter"
                  | :? INamedTypeSymbol as named when named.IsTupleType -> noName caseType at "a tuple"
                  | :? INamedTypeSymbol as named when named.TypeKind = TypeKind.Delegate -> noName caseType at "a function"
                  | :? INamedTypeSymbol as named when SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, own) ->
                      if not (isNull named.BaseType) && named.BaseType.IsClosed then
                          Error(
                              Diagnostic.error
                                  (CSharpCodes.unsupported "union_case")
                                  $"The case '{shortName caseType}' of the union '{symbol.Name}' is a case of the closed record '{named.BaseType.Name}': it is a constructor, not a type. Use '{named.BaseType.Name}' or a record as the case."
                              |> Diagnostic.at at
                          )
                      else
                          Ok(Name.fromString named.Name)
                  | :? INamedTypeSymbol as named ->
                      match SdkMapping.tryType named with
                      | Some(fq, _) -> Ok fq.LocalName
                      | None ->
                          Error(
                              Diagnostic.error
                                  CSharpCodes.unsupportedReference
                                  $"The C# symbol '{caseType.ToDisplayString()}' has no Morphir mapping. Use a type of this package or a mapped Morphir SDK type."
                              |> Diagnostic.at at
                          )
                  | _ -> noName caseType at "not a named type"

              named
              |> Result.bind (fun name ->
                  if Name.isEmpty name then
                      Error(nameFailure at caseType.Name)
                  else
                      Ok
                          {
                              Symbol = caseType
                              Name = name
                              Fields = [ Name.fromString "value", "Value", caseType ]
                              FieldLocations = [ at ]
                              Location = at
                          }) ]

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
        let unionNames = newIndex<FQName list> ()
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

        for _, declaration, symbol in declarations do
            diagnostics.AddRange(shapeRefusals compiled declaration symbol)

        // One entry for each type: the parts of a partial type are one declaration. The part with the parameter list of a
        // record comes first, because its parameters are the fields; otherwise the first part in source order.
        let types =
            let seen = HashSet<ISymbol>(comparer)

            [ for _, _, symbol in declarations do
                  if seen.Add symbol then
                      let parts = declarations |> List.filter (fun (_, _, other) -> comparer.Equals(other, symbol))

                      let positional =
                          parts
                          |> List.tryFind (fun (_, declaration, _) ->
                              match declaration with
                              | :? RecordDeclarationSyntax as record -> not (isNull record.ParameterList)
                              | _ -> false)

                      yield Option.defaultValue (List.head parts) positional ]

        // The cases of each closed record, in source order.
        let casesOf =
            let table = Dictionary<ISymbol, ResizeArray<INamedTypeSymbol * RecordDeclarationSyntax * SemanticModel>>(comparer)

            for model, declaration, symbol in types do
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

        // The constructors of each module so far, to find two with one Morphir name.
        let constructorsOf = Dictionary<ModulePath * Name, PlannedCase * INamedTypeSymbol>()

        for model, declaration, symbol in types do
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
                                                let memberLocation =
                                                    match field.DeclaringSyntaxReferences |> Seq.tryHead with
                                                    | Some reference -> locationOf compiled (reference.GetSyntax())
                                                    | None -> location

                                                Some
                                                    {
                                                        Symbol = field
                                                        Name = Name.fromString field.Name
                                                        Fields = []
                                                        FieldLocations = []
                                                        Location = memberLocation
                                                    }
                                            | _ -> None)
                                        |> List.ofSeq

                                    Some(CustomType members)
                                | _ when symbol.IsUnion ->
                                    let cases = unionCases compiled declaration symbol location

                                    for case in cases do
                                        match case with
                                        | Error diagnostic -> diagnostics.Add diagnostic
                                        | Ok _ -> ()

                                    // The cases that convert stay, so that their names are checked against the other constructors.
                                    Some(CustomType [ for case in cases do match case with Ok planned -> yield planned | Error _ -> () ])
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
                                            match constructorsOf.TryGetValue((path, case.Name)) with
                                            | true, (first, firstType) ->
                                                let message =
                                                    let caseText (planned: PlannedCase) =
                                                        match planned.Symbol with
                                                        | :? ITypeSymbol as caseType when symbol.IsUnion || firstType.IsUnion -> shortName caseType
                                                        | other -> other.Name

                                                    if symbol.IsUnion && comparer.Equals(firstType, symbol) then
                                                        $"The cases '{caseText first}' and '{caseText case}' of the union '{symbol.Name}' give one constructor name. The constructors of a module must have different Morphir names."
                                                    elif symbol.IsUnion && firstType.IsUnion then
                                                        $"The unions '{firstType.Name}' and '{symbol.Name}' of one module both have the case '{caseText case}'. The constructors of a module must have different Morphir names: put one of the unions in another module."
                                                    else
                                                        $"The constructors '{caseText first}' of '{firstType.Name}' and '{caseText case}' of '{symbol.Name}' have the same Morphir name in one module."

                                                diagnostics.Add(
                                                    Diagnostic.error CSharpCodes.nameCollision message
                                                    |> Diagnostic.at case.Location
                                                    |> Diagnostic.withRelated (
                                                        ({ Location = first.Location; Message = "The first declaration." }: RelatedInformation)
                                                    )
                                                )
                                            | _ ->
                                                constructorsOf[(path, case.Name)] <- (case, symbol)

                                                // A case type of a union is a type of its own, not a constructor symbol.
                                                if not symbol.IsUnion then
                                                    constructorNames[case.Symbol.OriginalDefinition] <-
                                                        (FQName.fqName package path case.Name, case)

                                        if symbol.IsUnion then
                                            unionNames[symbol.OriginalDefinition] <-
                                                [ for case in constructors -> FQName.fqName package path case.Name ]
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
                UnionCases = unionNames
            }

        Checked.ofDiagnostics planned (List.ofSeq diagnostics)
