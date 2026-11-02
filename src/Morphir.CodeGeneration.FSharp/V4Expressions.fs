namespace Morphir.CodeGeneration.FSharp

open Morphir.IR
open Morphir.IR.V4

/// Native v4 expression traversal with shared F# names, scalar profile and SDK spellings.
module internal V4Expressions =
    type Context = {
        Package: PackageName
        Module: ModulePath
        Location: string
        Types: Map<FQName, TypeDefinition>
        Values: Map<FQName, ValueDefinition>
        Typ: Type -> string
        Qualify: FQName -> string -> string
    }

    let private output =
        function
        | ExpressionBody(t, _)
        | NativeBody(t, _)
        | ExternalBody(t, _) -> Some t
        | IncompleteBody(t, _, _) -> t

    let rec private substitute bindings value =
        let again = substitute bindings

        match value with
        | Type.Variable(_, name) ->
            Map.tryFind name bindings
            |> Option.defaultValue value
        | Type.Reference(a, q, args) -> Type.Reference(a, q, List.map again args)
        | Type.Tuple(a, elements) -> Type.Tuple(a, List.map again elements)
        | Type.Record(a, fields) ->
            Type.Record(
                a,
                fields
                |> List.map (fun (name, t) -> name, again t)
            )
        | Type.ExtensibleRecord(a, name, fields) ->
            Type.ExtensibleRecord(
                a,
                name,
                fields
                |> List.map (fun (name, t) -> name, again t)
            )
        | Type.Function(a, input, output) -> Type.Function(a, again input, again output)
        | Type.Unit _ -> value

    let rec private references value =
        let many = List.collect references

        let body definition =
            match definition.Body with
            | ExpressionBody(_, value) -> references value
            | _ -> []

        match value with
        | Value.Reference(_, name) -> [ name ]
        | Value.Tuple(_, elements)
        | Value.List(_, elements) -> many elements
        | Value.Record(_, fields) ->
            fields
            |> List.collect (
                snd
                >> references
            )
        | Value.Field(_, target, _)
        | Value.Lambda(_, _, target) -> references target
        | Value.Apply(_, fn, arg)
        | Value.Destructure(_, _, fn, arg) -> many [ fn; arg ]
        | Value.LetDefinition(_, _, definition, value) ->
            body definition.Value
            @ references value
        | Value.LetRecursion(_, definitions, value) ->
            (OrderedMap.toList definitions
             |> List.collect (fun (_, definition) -> body definition.Value))
            @ references value
        | Value.IfThenElse(_, condition, yes, no) -> many [ condition; yes; no ]
        | Value.PatternMatch(_, target, cases) ->
            references target
            @ (cases
               |> List.collect (
                   snd
                   >> references
               ))
        | Value.UpdateRecord(_, target, fields) ->
            references target
            @ (fields
               |> List.collect (
                   snd
                   >> references
               ))
        | Value.Literal _
        | Value.Constructor _
        | Value.Variable _
        | Value.FieldFunction _
        | Value.Unit _
        | Value.Hole _ -> []

    let declarations
        (context: Context)
        (entries: (Name * AccessControlled<Documented<ValueDefinition>>) list)
        =
        let locals =
            entries
            |> List.map fst
            |> Set.ofList

        let localName name =
            if Set.contains name locals then
                "__morphirLocal_"
                + Emit.camel name
            else
                Emit.ident (Emit.camel name)

        let fail code message =
            Decode.fail code context.Location message

        let constructors =
            context.Types
            |> Map.toList
            |> List.collect (fun (q, definition) ->
                match definition with
                | CustomTypeDefinition(parameters, _, constructors) ->
                    OrderedMap.toList constructors
                    |> List.map (fun (name, args) ->
                        { q with LocalName = name }, (q, parameters, args)
                    )
                | _ -> []
            )
            |> Map.ofList

        let constructor q =
            if Emit.sdk q then
                match TargetSyntax.sdkConstructor (Emit.sdkName q) with
                | Some value -> value
                | None -> fail "unsupported-sdk" $"Unmapped SDK constructor {FQName.toString q}."
            else
                match Map.tryFind q constructors with
                | Some(_, _, args) ->
                    context.Qualify q (Emit.ident (Emit.pascal q.LocalName)), args.Length
                | None -> fail "unresolved-reference" $"Unknown constructor {FQName.toString q}."

        let reference q =
            if Emit.sdk q then
                match TargetSyntax.sdkValue (Emit.sdkName q) with
                | Some value -> value
                | None -> fail "unsupported-sdk" $"Unmapped SDK value {FQName.toString q}."
            elif Map.containsKey q context.Values then
                context.Qualify q (Emit.ident (Emit.camel q.LocalName))
            else
                fail "unresolved-reference" $"Unknown value {FQName.toString q}."

        let signature definition =
            output definition.Body
            |> Option.map (fun result ->
                List.foldBack
                    (fun (_, input) output -> Type.Function(TypeAttributes.empty, input, output))
                    definition.InputTypes
                    result
            )

        let rec recordFields visited expected =
            match expected with
            | Some(Type.Record(_, fields)) -> false, fields
            | Some(Type.Reference(_, q, arguments)) ->
                if Set.contains q visited then
                    fail "type-cycle" "Cyclic record aliases."

                match Map.tryFind q context.Types with
                | Some(TypeAliasDefinition(parameters, body)) when
                    parameters.Length = arguments.Length
                    ->
                    let _, fields =
                        recordFields
                            (Set.add q visited)
                            (Some(substitute (Map.ofList (List.zip parameters arguments)) body))

                    true, fields
                | _ -> false, []
            | _ -> false, []

        let record expected = recordFields Set.empty expected

        let rec inferred env value =
            match value with
            | Value.Variable(_, name) ->
                Map.tryFind name env
                |> Option.flatten
            | Value.Reference(_, q) ->
                Map.tryFind q context.Values
                |> Option.bind signature
            | Value.Apply(_, fn, _) ->
                match inferred env fn with
                | Some(Type.Function(_, _, output)) -> Some output
                | _ -> None
            | Value.Field(_, target, name) ->
                record (inferred env target)
                |> snd
                |> List.tryFind (
                    fst
                    >> (=) name
                )
                |> Option.map snd
            | Value.Unit _ -> Some(Type.Unit TypeAttributes.empty)
            | _ -> None

        let constructorArguments expected q =
            match Map.tryFind q constructors, expected with
            | Some(owner, parameters, args), Some(Type.Reference(_, target, arguments)) when
                owner = target
                && parameters.Length = arguments.Length
                ->
                let bindings = Map.ofList (List.zip parameters arguments)

                args
                |> List.map (
                    snd
                    >> substitute bindings
                    >> Some
                )
            | Some(_, _, args), _ ->
                args
                |> List.map (
                    snd
                    >> Some
                )
            | _ -> []

        let rec bindPattern env expected pattern =
            match pattern with
            | AsPattern(_, nested, name) ->
                bindPattern env expected nested
                |> Map.add name expected
            | TuplePattern(_, patterns) ->
                let types =
                    match expected with
                    | Some(Type.Tuple(_, types)) when types.Length = patterns.Length ->
                        List.map Some types
                    | _ -> List.replicate patterns.Length None

                List.fold2 bindPattern env types patterns
            | ConstructorPattern(_, q, patterns) ->
                let types = constructorArguments expected q

                let types =
                    if types.Length = patterns.Length then
                        types
                    else
                        List.replicate patterns.Length None

                List.fold2 bindPattern env types patterns
            | HeadTailPattern(_, head, tail) ->
                let element =
                    match expected with
                    | Some(Type.Reference(_, _, [ element ])) -> Some element
                    | _ -> None

                bindPattern (bindPattern env element head) expected tail
            | WildcardPattern _
            | EmptyListPattern _
            | LiteralPattern _
            | UnitPattern _ -> env

        let rec pattern value =
            match value with
            | WildcardPattern _ -> "_"
            | AsPattern(_, WildcardPattern _, name) -> localName name
            | AsPattern(_, nested, name) ->
                "("
                + pattern nested
                + " as "
                + localName name
                + ")"
            | TuplePattern(_, [])
            | UnitPattern _ -> "()"
            | TuplePattern(_, patterns) ->
                "("
                + (patterns
                   |> List.map pattern
                   |> String.concat ", ")
                + ")"
            | ConstructorPattern(_, q, patterns) ->
                let name, arity = constructor q

                if
                    arity
                    <> patterns.Length
                then
                    fail
                        "constructor-arity"
                        "Constructor pattern arity does not match its definition."

                if arity = 0 then
                    name
                else
                    name
                    + "("
                    + (patterns
                       |> List.map pattern
                       |> String.concat ", ")
                    + ")"
            | EmptyListPattern _ -> "[]"
            | HeadTailPattern(_, head, tail) ->
                "("
                + pattern head
                + " :: "
                + pattern tail
                + ")"
            | LiteralPattern(_, literal) -> ScalarProfile.render context.Location literal

        let fieldType fields name =
            fields
            |> List.tryFind (
                fst
                >> (=) name
            )
            |> Option.map snd

        let rec expression env expected value =
            let expr = expression env None

            match value with
            | Value.Literal(_, literal) -> ScalarProfile.render context.Location literal
            | Value.Unit _
            | Value.Tuple(_, []) -> "()"
            | Value.Variable(_, name) ->
                if not (Map.containsKey name env) then
                    fail "unbound-variable" $"Variable {Emit.camel name} is not bound."

                localName name
            | Value.Reference(_, q) -> reference q
            | Value.Constructor(_, q) ->
                let name, arity = constructor q

                if arity < 2 then
                    name
                else
                    let arguments = [
                        for index in 0 .. arity - 1 ->
                            "__morphirArg"
                            + string index
                    ]

                    "(fun "
                    + String.concat " " arguments
                    + " -> "
                    + name
                    + "("
                    + String.concat ", " arguments
                    + "))"
            | Value.Tuple(_, elements) ->
                let expected =
                    match expected with
                    | Some(Type.Tuple(_, types)) when types.Length = elements.Length ->
                        List.map Some types
                    | _ -> List.replicate elements.Length None

                "("
                + (List.map2 (expression env) expected elements
                   |> String.concat ", ")
                + ")"
            | Value.List(_, elements) ->
                let elementType =
                    match expected with
                    | Some(Type.Reference(_, q, [ element ])) when
                        Emit.sdk q
                        && Emit.sdkName q = "list.list"
                        ->
                        Some element
                    | _ -> None

                "["
                + (elements
                   |> List.map (expression env elementType)
                   |> String.concat "; ")
                + "]"
            | Value.Record(attributes, fields) ->
                let expected =
                    attributes.InferredType
                    |> Option.orElse expected

                let nominal, types = record expected

                let fields =
                    fields
                    |> List.map (fun (name, value) ->
                        Emit.ident (Emit.camel name)
                        + " = "
                        + expression env (fieldType types name) value
                    )
                    |> String.concat "; "

                if nominal then
                    "({ "
                    + fields
                    + " } : "
                    + context.Typ(Option.get expected)
                    + ")"
                else
                    "{| "
                    + fields
                    + " |}"
            | Value.Field(_, target, name) ->
                "("
                + expr target
                + ")."
                + Emit.ident (Emit.camel name)
            | Value.FieldFunction(_, name) ->
                match expected with
                | Some(Type.Function(_, input, _)) ->
                    "(fun (__morphirRecord : "
                    + context.Typ input
                    + ") -> __morphirRecord."
                    + Emit.ident (Emit.camel name)
                    + ")"
                | _ ->
                    "(fun __morphirRecord -> __morphirRecord."
                    + Emit.ident (Emit.camel name)
                    + ")"
            | Value.Apply _ ->
                let rec collect value arguments =
                    match value with
                    | Value.Apply(_, fn, arg) ->
                        collect
                            fn
                            (arg
                             :: arguments)
                    | _ -> value, arguments

                let head, arguments = collect value []

                let types =
                    match head with
                    | Value.Constructor(_, q) -> constructorArguments expected q
                    | _ ->
                        let rec inputs =
                            function
                            | Some(Type.Function(_, input, output)) ->
                                Some input
                                :: inputs (Some output)
                            | _ -> []

                        inputs (inferred env head)

                let rendered =
                    arguments
                    |> List.mapi (fun index argument ->
                        expression
                            env
                            (List.tryItem index types
                             |> Option.flatten)
                            argument
                    )

                match head with
                | Value.Constructor(_, q) when
                    snd (constructor q) > 1
                    && snd (constructor q) = arguments.Length
                    ->
                    fst (constructor q)
                    + "("
                    + String.concat ", " rendered
                    + ")"
                | _ ->
                    "("
                    + expr head
                    + " "
                    + (rendered
                       |> List.map (fun argument ->
                           "("
                           + argument
                           + ")"
                       )
                       |> String.concat " ")
                    + ")"
            | Value.Lambda(_, p, body) ->
                let input, output =
                    match expected with
                    | Some(Type.Function(_, input, output)) -> Some input, Some output
                    | _ -> None, None

                let binding =
                    match input with
                    | Some input ->
                        "("
                        + pattern p
                        + " : "
                        + context.Typ input
                        + ")"
                    | None -> pattern p

                "(fun "
                + binding
                + " -> "
                + expression (bindPattern env input p) output body
                + ")"
            | Value.IfThenElse(_, condition, yes, no) ->
                "(if "
                + expr condition
                + " then "
                + expression env expected yes
                + " else "
                + expression env expected no
                + ")"
            | Value.PatternMatch(_, target, cases) ->
                if List.isEmpty cases then
                    fail "invalid-pattern-match" "A match needs at least one branch."

                let targetType = inferred env target

                "(match "
                + expr target
                + " with "
                + (cases
                   |> List.map (fun (p, body) ->
                       "| "
                       + pattern p
                       + " -> "
                       + expression (bindPattern env targetType p) expected body
                   )
                   |> String.concat " ")
                + ")"
            | Value.LetDefinition _
            | Value.LetRecursion _
            | Value.Destructure _
            | Value.UpdateRecord _
            | Value.Hole _ ->
                fail "unsupported-v4-expression" "This v4 expression is not implemented yet."

        let definitions =
            entries
            |> Map.ofList

        let graph =
            definitions
            |> Map.map (fun _ controlled ->
                let references =
                    match controlled.Value.Value.Body with
                    | ExpressionBody(_, value) -> references value
                    | _ -> []

                references
                |> List.filter (fun q ->
                    q.PackagePath = context.Package
                    && q.ModulePath = context.Module
                    && Set.contains q.LocalName locals
                )
                |> List.map (fun q -> q.LocalName)
                |> Set.ofList
            )

        DependencyOrder.groups graph
        |> List.collect (fun group ->
            let recursive =
                group.Count > 1
                || group
                   |> Set.exists (fun name -> Set.contains name graph[name])

            group
            |> Set.toList
            |> List.mapi (fun index name ->
                let controlled = Map.find name definitions
                let definition = controlled.Value.Value

                match definition.Body with
                | ExpressionBody(output, body) ->
                    if
                        recursive
                        && List.isEmpty definition.InputTypes
                    then
                        match body with
                        | Value.Lambda _ -> ()
                        | _ ->
                            fail
                                "initialization-cycle"
                                "Recursive non-functions cannot be initialized faithfully."

                    let env =
                        definition.InputTypes
                        |> List.map (fun (name, t) -> name, Some t)
                        |> Map.ofList

                    let arguments =
                        definition.InputTypes
                        |> List.map (fun (name, t) ->
                            "("
                            + localName name
                            + " : "
                            + context.Typ t
                            + ")"
                        )
                        |> String.concat " "

                    let prefix =
                        if not recursive then "let "
                        elif index = 0 then "let rec "
                        else "and "

                    prefix
                    + (if controlled.Access = Private then "internal " else "")
                    + Emit.ident (Emit.camel name)
                    + " "
                    + arguments
                    + " : "
                    + context.Typ output
                    + " = "
                    + expression env (Some output) body
                | _ ->
                    fail
                        "unsupported-v4-body"
                        "This v4 definition has no supported expression body."
            )
        )
