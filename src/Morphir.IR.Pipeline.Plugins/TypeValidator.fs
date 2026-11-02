namespace Morphir.IR.Pipeline.Plugins

open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Pipeline

/// <summary>
/// Type validation plugin that reports definite type mismatches in a classic IR package.
/// </summary>
/// <remarks>
/// Reports definite primitive mismatches, non-function applications and absent closed-record fields.
/// Unknown external types remain silent. The package-aware entry resolves its own aliases on demand.
/// The F# compiler remains the full check for generated code. Unbound variables are checked separately.
/// </remarks>
[<RequireQualifiedAccess>]
module TypeValidator =

    /// <summary>
    /// Creates an FQName from a colon-separated string (Package:Module:Name).
    /// </summary>
    let private makeFQName (str: string) : FQName = FQName.fromString str ":"

    /// <summary>
    /// Type environment mapping variable names to their types.
    /// </summary>
    type TypeEnvironment = Map<Name, Type<unit>>

    /// <summary>
    /// Creates an empty type environment.
    /// </summary>
    let emptyEnv: TypeEnvironment = Map.empty

    /// <summary>
    /// Adds a binding to the type environment.
    /// </summary>
    let addBinding (name: Name) (typ: Type<unit>) (env: TypeEnvironment) : TypeEnvironment =
        Map.add name typ env

    /// <summary>
    /// Looks up a variable in the type environment.
    /// </summary>
    let lookupVar (name: Name) (env: TypeEnvironment) : Type<unit> option = Map.tryFind name env

    /// <summary>
    /// Infers the type of a literal value.
    /// </summary>
    let inferLiteralType (lit: Literal) : Type<unit> =
        match lit with
        | Literal.BoolLiteral _ -> Type.Reference((), makeFQName "Morphir.SDK:Basics:Bool", [])
        | Literal.CharLiteral _ -> Type.Reference((), makeFQName "Morphir.SDK:Char:Char", [])
        | Literal.StringLiteral _ -> Type.Reference((), makeFQName "Morphir.SDK:String:String", [])
        | Literal.WholeNumberLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Basics:Int", [])
        | Literal.FloatLiteral _ -> Type.Reference((), makeFQName "Morphir.SDK:Basics:Float", [])
        | Literal.DecimalLiteral _ ->
            Type.Reference((), makeFQName "Morphir.SDK:Decimal:Decimal", [])

    /// <summary>
    /// Checks if two types are equal (structural equality ignoring attributes).
    /// </summary>
    let rec typesEqual (t1: Type<unit>) (t2: Type<unit>) : bool =
        match t1, t2 with
        | Type.Variable(_, n1), Type.Variable(_, n2) -> n1 = n2
        | Type.Reference(_, fq1, args1), Type.Reference(_, fq2, args2) ->
            fq1 = fq2
            && List.length args1 = List.length args2
            && List.forall2 typesEqual args1 args2
        | Type.Tuple(_, elems1), Type.Tuple(_, elems2) ->
            List.length elems1 = List.length elems2
            && List.forall2 typesEqual elems1 elems2
        | Type.Function(_, input1, output1), Type.Function(_, input2, output2) ->
            typesEqual input1 input2
            && typesEqual output1 output2
        | Type.Unit _, Type.Unit _ -> true
        | _ -> false

    /// <summary>Removes attributes, which do not affect whether types match.</summary>
    let rec private withoutAttributes (typ: Type<'a>) : Type<unit> =
        let fields (items: Field<'a> list) : Field<unit> list =
            items
            |> List.map (fun field -> {
                Name = field.Name
                Type = withoutAttributes field.Type
            })

        match typ with
        | Type.Variable(_, name) -> Type.Variable((), name)
        | Type.Reference(_, name, args) ->
            Type.Reference(
                (),
                name,
                args
                |> List.map withoutAttributes
            )
        | Type.Tuple(_, items) ->
            Type.Tuple(
                (),
                items
                |> List.map withoutAttributes
            )
        | Type.Record(_, items) -> Type.Record((), fields items)
        | Type.ExtensibleRecord(_, name, items) -> Type.ExtensibleRecord((), name, fields items)
        | Type.Function(_, input, output) ->
            Type.Function((), withoutAttributes input, withoutAttributes output)
        | Type.Unit _ -> Type.Unit()

    /// <summary>The kind of an SDK primitive type. Other types have no kind and count as unknown.</summary>
    let private primitiveKind (typ: Type<unit>) : string option =
        match typ with
        | Type.Reference(_, name, []) when ClassicPackage.isSdk name ->
            match ClassicPackage.sdkName name with
            // Numeric literals are written the same way for each of these, so they cannot conflict.
            | "basics.int"
            | "basics.float"
            | "decimal.decimal" -> Some "number"
            | "basics.bool"
            | "bool.bool" -> Some "Bool"
            | "string.string" -> Some "String"
            | "char.char" -> Some "Char"
            | _ -> None
        | _ -> None

    /// <summary>True only when both types are known primitives that cannot match.</summary>
    let private definitelyDiffer (first: Type<unit> option) (second: Type<unit> option) : bool =
        match
            first
            |> Option.bind primitiveKind,
            second
            |> Option.bind primitiveKind
        with
        | Some a, Some b -> a <> b
        | _ -> false

    let private typeName (typ: Type<unit> option) =
        match typ with
        | Some(Type.Reference(_, name, _)) -> FQName.toString name
        | _ -> "an unknown type"

    /// <summary>The names a pattern binds, whose types are unknown here.</summary>
    let rec private patternNames (pattern: Pattern<'a>) : Name list =
        match pattern with
        | Pattern.AsPattern(_, nested, name) ->
            name
            :: patternNames nested
        | Pattern.TuplePattern(_, patterns)
        | Pattern.ConstructorPattern(_, _, patterns) ->
            patterns
            |> List.collect patternNames
        | Pattern.HeadTailPattern(_, head, tail) ->
            patternNames head
            @ patternNames tail
        | Pattern.WildcardPattern _
        | Pattern.EmptyListPattern _
        | Pattern.LiteralPattern _
        | Pattern.UnitPattern _ -> []

    let private shadow (names: Name list) (env: TypeEnvironment) : TypeEnvironment =
        names
        |> List.fold (fun current name -> Map.remove name current) env

    /// <summary>
    /// Infers the type of a value, reporting definite mismatches on the file. Returns None when the
    /// type is unknown.
    /// </summary>
    /// <param name="location">The value being checked, for messages</param>
    /// <param name="env">The variables in scope with known types</param>
    /// <param name="value">The value to check</param>
    /// <param name="file">The file to report on</param>
    let rec private inferWith
        (resolve: Type<unit> -> Type<unit>)
        (location: string)
        (env: TypeEnvironment)
        (value: Value<'ta, 'va>)
        (file: VFile)
        : Type<unit> option * VFile =
        let mismatch (text: string) (current: VFile) =
            current
            |> VFile.report
                { VMessage.create Error text with
                    Location = Some location
                    RuleId = Some "type-mismatch"
                    Source = Some "type-validator"
                }

        // Checks items only for the errors inside them.
        let visit (items: Value<'ta, 'va> list) (current: VFile) =
            items
            |> List.fold (fun inner item -> snd (inferWith resolve location env item inner)) current

        match value with
        | Value.Literal(_, lit) -> Some(inferLiteralType lit), file
        | Value.Unit _ -> Some(Type.Unit()), file
        | Value.Variable(_, name) ->
            lookupVar name env
            |> Option.map resolve,
            file
        | Value.Tuple(_, items) -> None, visit items file
        | Value.Record(_, fields) ->
            None,
            visit
                (fields
                 |> Map.toList
                 |> List.map snd)
                file
        | Value.Field(_, record, name) ->
            let target, checkedFile = inferWith resolve location env record file

            match
                target
                |> Option.map resolve
            with
            | Some(Type.Record(_, fields)) ->
                match
                    fields
                    |> List.tryFind (fun field -> field.Name = name)
                with
                | Some field -> Some(resolve field.Type), checkedFile
                | None -> None, mismatch "The record has no such field." checkedFile
            | _ -> None, checkedFile
        | Value.UpdateRecord(_, target, fields) ->
            None,
            visit
                (target
                 :: (fields
                     |> Map.toList
                     |> List.map snd))
                file
        | Value.List(_, items) ->
            let types, finalFile =
                items
                |> List.mapFold (fun inner item -> inferWith resolve location env item inner) file

            let reported =
                match List.tryHead types with
                | Some first ->
                    types
                    |> List.fold
                        (fun inner other ->
                            if definitelyDiffer first other then
                                inner
                                |> mismatch
                                    $"List elements have different types: {typeName first} and {typeName other}."
                            else
                                inner
                        )
                        finalFile
                | None -> finalFile

            None, reported
        | Value.Apply(_, fn, argument) ->
            let fnType, afterFn = inferWith resolve location env fn file
            let argumentType, afterArgument = inferWith resolve location env argument afterFn

            match fnType with
            | Some(Type.Function(_, input, output)) ->
                let input, output = resolve input, resolve output

                if definitelyDiffer (Some input) argumentType then
                    Some output,
                    afterArgument
                    |> mismatch
                        $"Function argument has type {typeName argumentType}, expected {typeName (Some input)}."
                else
                    Some output, afterArgument
            | Some(Type.Unit _) -> None, mismatch "Only a function can be applied." afterArgument
            | Some known when
                primitiveKind known
                |> Option.isSome
                ->
                None, mismatch "Only a function can be applied." afterArgument
            | _ -> None, afterArgument
        | Value.IfThenElse(_, condition, yes, no) ->
            let conditionType, afterCondition = inferWith resolve location env condition file

            let boolType: Type<unit> =
                Type.Reference((), makeFQName "Morphir.SDK:Basics:Bool", [])

            let checkedCondition =
                if definitelyDiffer conditionType (Some boolType) then
                    afterCondition
                    |> mismatch $"If condition has type {typeName conditionType}, expected Bool."
                else
                    afterCondition

            let yesType, afterYes = inferWith resolve location env yes checkedCondition
            let noType, afterNo = inferWith resolve location env no afterYes

            if definitelyDiffer yesType noType then
                None,
                afterNo
                |> mismatch
                    $"If branches have different types: {typeName yesType} and {typeName noType}."
            else
                (match yesType with
                 | Some _ -> yesType
                 | None -> noType),
                afterNo
        | Value.Lambda(_, pattern, body) ->
            None, snd (inferWith resolve location (shadow (patternNames pattern) env) body file)
        | Value.LetDefinition(_, name, definition, body) ->
            let afterDefinition = validateWith resolve location env definition file

            let bound =
                if List.isEmpty definition.InputTypes then
                    addBinding name (withoutAttributes definition.OutputType) env
                else
                    Map.remove name env

            inferWith resolve location bound body afterDefinition
        | Value.LetRecursion(_, definitions, body) ->
            let scope =
                shadow
                    (definitions
                     |> Map.toList
                     |> List.map fst)
                    env

            let afterDefinitions =
                definitions
                |> Map.toList
                |> List.fold
                    (fun inner (_, definition) ->
                        validateWith resolve location scope definition inner
                    )
                    file

            inferWith resolve location scope body afterDefinitions
        | Value.Destructure(_, pattern, target, body) ->
            let afterTarget = snd (inferWith resolve location env target file)
            inferWith resolve location (shadow (patternNames pattern) env) body afterTarget
        | Value.PatternMatch(_, target, cases) ->
            let afterTarget = snd (inferWith resolve location env target file)

            None,
            cases
            |> List.fold
                (fun inner (pattern, body) ->
                    snd (inferWith resolve location (shadow (patternNames pattern) env) body inner)
                )
                afterTarget
        | Value.Constructor _
        | Value.Reference _
        | Value.FieldFunction _ -> None, file

    /// <summary>
    /// Checks a value definition against its declared output type.
    /// </summary>
    /// <param name="location">The value being checked, for messages</param>
    /// <param name="env">The variables in scope with known types</param>
    /// <param name="definition">The definition to check</param>
    /// <param name="file">The file to report on</param>
    and private validateWith
        (resolve: Type<unit> -> Type<unit>)
        (location: string)
        (env: TypeEnvironment)
        (definition: ValueDefinition<'ta, 'va>)
        (file: VFile)
        : VFile =
        let scope =
            definition.InputTypes
            |> List.fold
                (fun current (name, _, typ) ->
                    addBinding name (resolve (withoutAttributes typ)) current
                )
                env

        let inferred, checkedBody = inferWith resolve location scope definition.Body file
        let declared = Some(resolve (withoutAttributes definition.OutputType))

        if definitelyDiffer inferred declared then
            checkedBody
            |> VFile.report
                { VMessage.create
                      Error
                      $"Value has type {typeName inferred}, declared {typeName declared}." with
                    Location = Some location
                    RuleId = Some "type-mismatch"
                    Source = Some "type-validator"
                }
        else
            checkedBody

    /// Infers conservatively without resolving model aliases.
    let inferValueType location env value file = inferWith id location env value file

    /// Validates a definition without requiring package identity.
    let validateDefinition location env definition file =
        validateWith id location env definition file

    /// <summary>
    /// Creates a type validator plugin for a classic IR package, with extra bindings in scope. The
    /// model is always kept, so later plugins can report more.
    /// </summary>
    /// <param name="env">Variables in scope for every value</param>
    let createWithEnv (env: TypeEnvironment) : Plugin<Package.PackageDefinition<'ta, 'va>> =
        Plugin.create
            "type-validator"
            (fun package file ->
                let _, finalFile =
                    ClassicPackage.mapDefinitions
                        (fun location definition current ->
                            definition, validateDefinition location env definition current
                        )
                        package
                        file

                Some package, finalFile
            )

    /// <summary>
    /// Creates a type validator plugin for a classic IR package.
    /// </summary>
    let create () : Plugin<Package.PackageDefinition<'ta, 'va>> = createWithEnv emptyEnv

    /// Validates known record aliases using explicit package identity; external types stay unknown.
    let createForPackage (packageName: PackageName) : Plugin<Package.PackageDefinition<'ta, 'va>> =
        Plugin.create
            "type-validator"
            (fun package file ->
                let aliases =
                    [
                        for modulePath, controlled in Map.toList package.Modules do
                            for name, definition in Map.toList controlled.Value.Types do
                                match Documented.value definition.Value with
                                | Type.TypeAliasDefinition(parameters, target) ->
                                    let qualified: FQName = {
                                        PackagePath = packageName
                                        ModulePath = modulePath
                                        LocalName = name
                                    }

                                    yield qualified, (parameters, withoutAttributes target)
                                | _ -> ()
                    ]
                    |> Map.ofList

                let rec substitute bindings typ =
                    let nested = substitute bindings

                    match typ with
                    | Type.Variable(_, name) ->
                        Map.tryFind name bindings
                        |> Option.defaultValue typ
                    | Type.Reference(a, name, args) ->
                        Type.Reference(a, name, List.map nested args)
                    | Type.Tuple(a, items) -> Type.Tuple(a, List.map nested items)
                    | Type.Function(a, input, output) ->
                        Type.Function(a, nested input, nested output)
                    | Type.Record(a, fields) ->
                        Type.Record(
                            a,
                            fields
                            |> List.map (fun field -> { field with Type = nested field.Type })
                        )
                    | Type.ExtensibleRecord(a, name, fields) ->
                        Type.ExtensibleRecord(
                            a,
                            name,
                            fields
                            |> List.map (fun field -> { field with Type = nested field.Type })
                        )
                    | Type.Unit _ -> typ

                let rec resolve seen typ =
                    match typ with
                    | Type.Reference(_, name, args) ->
                        match Map.tryFind name aliases with
                        | Some(parameters, target) when
                            parameters.Length = args.Length
                            && not (Set.contains name seen)
                            ->
                            substitute (Map.ofList (List.zip parameters args)) target
                            |> resolve (Set.add name seen)
                        | _ -> typ
                    // Resolve only the head needed by inference. Expanding unused fields or generic
                    // arguments can multiply alias graphs without adding definite type facts.
                    | _ -> typ

                let _, checkedFile =
                    ClassicPackage.mapDefinitions
                        (fun location definition current ->
                            definition,
                            validateWith (resolve Set.empty) location emptyEnv definition current
                        )
                        package
                        file

                Some package, checkedFile
            )
