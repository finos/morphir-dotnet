namespace Morphir.CodeGeneration.FSharp

open Morphir.IR
open Morphir.IR.V4

/// Checks native v4 nodes before optimization or emission. Unknown inference never overrides a contradiction.
module internal V4Check =
    let private typeAttributes =
        function
        | Type.Variable(a, _)
        | Type.Reference(a, _, _)
        | Type.Tuple(a, _)
        | Type.Record(a, _)
        | Type.ExtensibleRecord(a, _, _)
        | Type.Function(a, _, _)
        | Type.Unit a -> a

    let private valueAttributes =
        function
        | Value.Literal(a, _)
        | Value.Constructor(a, _)
        | Value.Tuple(a, _)
        | Value.List(a, _)
        | Value.Record(a, _)
        | Value.Variable(a, _)
        | Value.Reference(a, _)
        | Value.Field(a, _, _)
        | Value.FieldFunction(a, _)
        | Value.Apply(a, _, _)
        | Value.Lambda(a, _, _)
        | Value.LetDefinition(a, _, _, _)
        | Value.LetRecursion(a, _, _)
        | Value.Destructure(a, _, _, _)
        | Value.IfThenElse(a, _, _, _)
        | Value.PatternMatch(a, _, _)
        | Value.UpdateRecord(a, _, _)
        | Value.Unit a
        | Value.Hole(a, _, _) -> a

    let private atSource location =
        function
        | Some source -> $"{location}@{source.StartLine}:{source.StartColumn}"
        | None -> location

    let private sdk moduleName local = {
        PackagePath =
            PackageName(
                Path [
                    Name["morphir"]
                    Name["s"
                         "d"
                         "k"]
                ]
            )
        ModulePath = ModulePath(Path [ Name[moduleName] ])
        LocalName = Name[local]
    }

    let private reference moduleName local args =
        Type.Reference(TypeAttributes.empty, sdk moduleName local, args)

    let private literalType =
        function
        | BoolLiteral _ -> Some(reference "basics" "bool" [])
        | CharLiteral _ -> Some(reference "char" "char" [])
        | StringLiteral _ -> Some(reference "string" "string" [])
        | IntegerLiteral _ -> Some(reference "basics" "int" [])
        | FloatLiteral _ -> Some(reference "basics" "float" [])
        | DecimalLiteral _ -> Some(reference "decimal" "decimal" [])
        | DocumentLiteral _ -> None

    let rec private substitute bindings =
        function
        | Type.Variable(_, name) as value ->
            Map.tryFind name bindings
            |> Option.defaultValue value
        | Type.Reference(a, q, args) -> Type.Reference(a, q, List.map (substitute bindings) args)
        | Type.Tuple(a, elements) -> Type.Tuple(a, List.map (substitute bindings) elements)
        | Type.Record(a, fields) ->
            Type.Record(
                a,
                fields
                |> List.map (fun (name, t) -> name, substitute bindings t)
            )
        | Type.ExtensibleRecord(a, name, fields) ->
            Type.ExtensibleRecord(
                a,
                name,
                fields
                |> List.map (fun (name, t) -> name, substitute bindings t)
            )
        | Type.Function(a, input, output) ->
            Type.Function(a, substitute bindings input, substitute bindings output)
        | Type.Unit _ as value -> value

    let validate distribution =
        let package, definition =
            match distribution with
            | Library(package, _, definition) -> package, definition
            | _ ->
                Decode.fail
                    "unsupported-distribution"
                    "$"
                    "F# generation requires Library implementations."

        let modules = OrderedMap.toList definition.Modules

        let types =
            modules
            |> List.collect (fun (path, module') ->
                OrderedMap.toList module'.Value.Types
                |> List.map (fun (name, value) ->
                    {
                        PackagePath = package
                        ModulePath = path
                        LocalName = name
                    },
                    value.Value.Value
                )
            )
            |> Map.ofList

        let values =
            modules
            |> List.collect (fun (path, module') ->
                OrderedMap.toList module'.Value.Values
                |> List.map (fun (name, value) ->
                    {
                        PackagePath = package
                        ModulePath = path
                        LocalName = name
                    },
                    value.Value.Value
                )
            )
            |> Map.ofList

        let constructors =
            types
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

        let distinct location names =
            names
            |> List.iter (
                Emit.words
                >> ignore
            )

            if
                (Set.ofList names).Count
                <> names.Length
            then
                Decode.fail "name-collision" location "Duplicate names in a typed node."

        let rec checkType location value =
            let location = atSource location (typeAttributes value).Source

            let fields fields =
                distinct location (List.map fst fields)

                fields
                |> List.iter (
                    snd
                    >> checkType location
                )

            match value with
            | Type.Variable(_, name) ->
                Emit.words name
                |> ignore
            | Type.Reference(_, q, args) ->
                Emit.packageNames q.PackagePath
                |> ignore

                Emit.moduleNames q.ModulePath
                |> ignore

                Emit.words q.LocalName
                |> ignore

                List.iter (checkType location) args

                if Emit.sdk q then
                    if
                        TargetSyntax.sdkType (Emit.sdkName q) (List.replicate args.Length "_")
                        |> Option.isNone
                    then
                        Decode.fail
                            "unsupported-sdk"
                            location
                            $"Unmapped SDK type {FQName.toString q}."
                else
                    match Map.tryFind q types with
                    | None ->
                        Decode.fail
                            "unresolved-reference"
                            location
                            $"Unknown type {FQName.toString q}."
                    | Some definition ->
                        let parameters =
                            match definition with
                            | TypeAliasDefinition(p, _)
                            | CustomTypeDefinition(p, _, _)
                            | IncompleteTypeDefinition(p, _, _) -> p

                        if
                            parameters.Length
                            <> args.Length
                        then
                            Decode.fail
                                "type-arity"
                                location
                                $"Wrong type argument count for {FQName.toString q}."
            | Type.Tuple(_, elements) -> List.iter (checkType location) elements
            | Type.Record(_, entries) -> fields entries
            | Type.ExtensibleRecord _ ->
                Decode.fail
                    "unsupported-open-record"
                    location
                    "Open extensible records cannot be represented faithfully."
            | Type.Function(_, input, output) ->
                checkType location input
                checkType location output
            | Type.Unit _ -> ()

        let rec expand visited value =
            match value with
            | Type.Reference(_, q, args) ->
                match Map.tryFind q types with
                | Some(TypeAliasDefinition(parameters, body)) when parameters.Length = args.Length ->
                    if Set.contains q visited then
                        Decode.fail "type-cycle" "$" "Cyclic type aliases."

                    expand
                        (Set.add q visited)
                        (substitute (Map.ofList (List.zip parameters args)) body)
                | _ -> value
            | _ -> value

        let expand = expand Set.empty

        let rec compatible left right =
            match expand left, expand right with
            | Type.Variable _, _
            | _, Type.Variable _ -> true
            | Type.Unit _, Type.Unit _
            | Type.Tuple(_, []), Type.Unit _
            | Type.Unit _, Type.Tuple(_, []) -> true
            | Type.Reference(_, l, la), Type.Reference(_, r, ra) ->
                let sameName =
                    l = r
                    || (Emit.sdk l
                        && Emit.sdk r
                        && TargetSyntax.sdkType (Emit.sdkName l) [] = TargetSyntax.sdkType
                            (Emit.sdkName r)
                            []
                        && List.isEmpty la
                        && List.isEmpty ra)

                sameName
                && la.Length = ra.Length
                && List.forall2 compatible la ra
            | Type.Tuple(_, left), Type.Tuple(_, right) ->
                left.Length = right.Length
                && List.forall2 compatible left right
            | Type.Record(_, left), Type.Record(_, right) ->
                let left, right = Map.ofList left, Map.ofList right

                Map.count left = Map.count right
                && (left
                    |> Map.forall (fun name t ->
                        Map.tryFind name right
                        |> Option.exists (compatible t)
                    ))
            | Type.Function(_, li, lo), Type.Function(_, ri, ro) ->
                compatible li ri
                && compatible lo ro
            | _ -> false

        let ensure location expected actual =
            match expected, actual with
            | Some expected, Some actual when not (compatible expected actual) ->
                Decode.fail
                    "type-mismatch"
                    location
                    "The expression contradicts its declared or inferred type."
            | _ -> ()

        let output =
            function
            | ExpressionBody(t, _)
            | NativeBody(t, _)
            | ExternalBody(t, _) -> Some t
            | IncompleteBody(t, _, _) -> t

        let signature definition =
            output definition.Body
            |> Option.map (fun output ->
                List.foldBack
                    (fun (_, input) output -> Type.Function(TypeAttributes.empty, input, output))
                    definition.InputTypes
                    output
            )

        let constructorType location q =
            if Emit.sdk q then
                match TargetSyntax.sdkConstructor (Emit.sdkName q) with
                | None ->
                    Decode.fail
                        "unsupported-sdk"
                        location
                        $"Unmapped SDK constructor {FQName.toString q}."
                | Some _ -> None
            else
                match Map.tryFind q constructors with
                | None ->
                    Decode.fail
                        "unresolved-reference"
                        location
                        $"Unknown constructor {FQName.toString q}."
                | Some(owner, parameters, args) ->
                    let result =
                        Type.Reference(
                            TypeAttributes.empty,
                            owner,
                            parameters
                            |> List.map (fun name -> Type.Variable(TypeAttributes.empty, name))
                        )

                    Some(
                        List.foldBack
                            (fun (_, input) output ->
                                Type.Function(TypeAttributes.empty, input, output)
                            )
                            args
                            result
                    )

        let fields expected =
            match
                expected
                |> Option.map expand
            with
            | Some(Type.Record(_, fields)) -> Some fields
            | _ -> None

        let allTypes values =
            if List.forall Option.isSome values then
                Some(List.map Option.get values)
            else
                None

        let specialize input actual output =
            let rec collect bindings input actual =
                match expand input, expand actual with
                | Type.Variable(_, name), actual -> Map.add name actual bindings
                | Type.Reference(_, _, inputs), Type.Reference(_, _, actuals)
                | Type.Tuple(_, inputs), Type.Tuple(_, actuals) when inputs.Length = actuals.Length ->
                    List.fold2 collect bindings inputs actuals
                | _ -> bindings

            substitute (collect Map.empty input actual) output

        let rec bindPattern location env expected pattern =
            let add env name t = Map.add name t env

            match pattern with
            | WildcardPattern _ -> env
            | AsPattern(_, nested, name) ->
                Emit.words name
                |> ignore

                add (bindPattern location env expected nested) name expected
            | TuplePattern(_, patterns) ->
                let inputs =
                    match
                        expected
                        |> Option.map expand
                    with
                    | Some(Type.Tuple(_, types)) when types.Length = patterns.Length ->
                        List.map Some types
                    | _ -> List.replicate patterns.Length None

                List.fold2 (bindPattern location) env inputs patterns
            | ConstructorPattern(_, q, patterns) ->
                let ctorType = constructorType location q

                let rec inputs =
                    function
                    | Some(Type.Function(_, input, output)) ->
                        Some input
                        :: inputs (Some output)
                    | _ -> []

                let inputs = inputs ctorType

                if
                    not (Emit.sdk q)
                    && inputs.Length
                       <> patterns.Length
                then
                    Decode.fail
                        "constructor-arity"
                        location
                        "Constructor pattern arity does not match."

                let inputs =
                    if inputs.Length = patterns.Length then
                        inputs
                    else
                        List.replicate patterns.Length None

                List.fold2 (bindPattern location) env inputs patterns
            | LiteralPattern(_, literal) ->
                ScalarProfile.render location literal
                |> ignore

                ensure location expected (literalType literal)
                env
            | EmptyListPattern _ -> env
            | HeadTailPattern(_, head, tail) ->
                let element =
                    match
                        expected
                        |> Option.map expand
                    with
                    | Some(Type.Reference(_, q, [ element ])) when
                        Emit.sdk q
                        && Emit.sdkName q = "list.list"
                        ->
                        Some element
                    | _ -> None

                bindPattern location (bindPattern location env element head) expected tail
            | UnitPattern _ ->
                ensure location expected (Some(Type.Unit TypeAttributes.empty))
                env

        let rec expression location env expected value =
            let attributes = valueAttributes value
            let location = atSource location attributes.Source

            attributes.InferredType
            |> Option.iter (checkType location)

            ensure location expected attributes.InferredType

            let contextual =
                expected
                |> Option.orElse attributes.InferredType

            let check = expression location env

            let actual =
                match value with
                | Value.Literal(_, literal) ->
                    ScalarProfile.render location literal
                    |> ignore

                    literalType literal
                | Value.Unit _ -> Some(Type.Unit TypeAttributes.empty)
                | Value.Variable(_, name) ->
                    match Map.tryFind name env with
                    | Some t -> t
                    | None ->
                        Decode.fail
                            "unbound-variable"
                            location
                            $"Variable {Emit.camel name} is not bound."
                | Value.Reference(_, q) ->
                    if Emit.sdk q then
                        if
                            TargetSyntax.sdkValue (Emit.sdkName q)
                            |> Option.isNone
                        then
                            Decode.fail
                                "unsupported-sdk"
                                location
                                $"Unmapped SDK value {FQName.toString q}."

                        None
                    else
                        match Map.tryFind q values with
                        | Some definition -> signature definition
                        | None ->
                            Decode.fail
                                "unresolved-reference"
                                location
                                $"Unknown value {FQName.toString q}."
                | Value.Constructor(_, q) -> constructorType location q
                | Value.Tuple(_, elements) ->
                    let types =
                        match
                            contextual
                            |> Option.map expand
                        with
                        | Some(Type.Tuple(_, types)) when types.Length = elements.Length ->
                            List.map Some types
                        | _ -> List.replicate elements.Length None

                    List.map2 check types elements
                    |> allTypes
                    |> Option.map (fun types -> Type.Tuple(TypeAttributes.empty, types))
                | Value.List(_, elements) ->
                    let element =
                        match
                            contextual
                            |> Option.map expand
                        with
                        | Some(Type.Reference(_, q, [ element ])) when
                            Emit.sdk q
                            && Emit.sdkName q = "list.list"
                            ->
                            Some element
                        | _ -> None

                    let inferred =
                        elements
                        |> List.map (check element)

                    let known =
                        element
                        |> Option.orElseWith (fun () -> List.tryPick id inferred)

                    inferred
                    |> List.iter (ensure location known)

                    known
                    |> Option.map (fun t -> reference "list" "list" [ t ])
                | Value.Record(_, entries) ->
                    distinct location (List.map fst entries)
                    let expectedFields = fields contextual

                    expectedFields
                    |> Option.iter (fun expected ->
                        if
                            Set.ofList (List.map fst expected)
                            <> Set.ofList (List.map fst entries)
                        then
                            Decode.fail
                                "type-mismatch"
                                location
                                "Record fields contradict the declared type."
                    )

                    let inferred =
                        entries
                        |> List.map (fun (name, value) ->
                            let expected =
                                expectedFields
                                |> Option.bind (
                                    List.tryFind (
                                        fst
                                        >> (=) name
                                    )
                                )
                                |> Option.map snd

                            name, check expected value
                        )

                    contextual
                    |> Option.orElseWith (fun () ->
                        inferred
                        |> List.map snd
                        |> allTypes
                        |> Option.map (fun types ->
                            Type.Record(
                                TypeAttributes.empty,
                                List.zip (List.map fst entries) types
                            )
                        )
                    )
                | Value.Field(_, target, name) ->
                    let targetType = check None target

                    match fields targetType with
                    | Some fields ->
                        match
                            List.tryFind
                                (fst
                                 >> (=) name)
                                fields
                        with
                        | Some(_, t) -> Some t
                        | None ->
                            Decode.fail "type-mismatch" location "The record has no such field."
                    | None -> None
                | Value.FieldFunction(_, name) ->
                    match
                        contextual
                        |> Option.map expand
                    with
                    | Some(Type.Function(_, input, output)) ->
                        match fields (Some input) with
                        | Some entries ->
                            let actual =
                                entries
                                |> List.tryFind (
                                    fst
                                    >> (=) name
                                )
                                |> Option.map snd

                            if actual.IsNone then
                                Decode.fail "type-mismatch" location "The record has no such field."

                            ensure location (Some output) actual
                            contextual
                        | None -> contextual
                    | _ -> None
                | Value.Apply(_, fn, arg) ->
                    let fnType = check None fn

                    match
                        fnType
                        |> Option.map expand
                    with
                    | Some(Type.Function(_, input, output)) ->
                        let actual = check (Some input) arg

                        Some(
                            actual
                            |> Option.map (fun actual -> specialize input actual output)
                            |> Option.defaultValue output
                        )
                    | Some(Type.Variable _)
                    | None ->
                        check None arg
                        |> ignore

                        None
                    | Some _ ->
                        Decode.fail "type-mismatch" location "Only a function can be applied."
                | Value.Lambda(_, pattern, body) ->
                    let input, output =
                        match
                            contextual
                            |> Option.map expand
                        with
                        | Some(Type.Function(_, input, output)) -> Some input, Some output
                        | _ -> None, None

                    let actual =
                        expression location (bindPattern location env input pattern) output body

                    match input, actual with
                    | Some input, Some output ->
                        Some(Type.Function(TypeAttributes.empty, input, output))
                    | _ -> contextual
                | Value.LetDefinition(_, name, definition, body) ->
                    checkDefinition location env definition.Value

                    expression
                        location
                        (Map.add name (signature definition.Value) env)
                        contextual
                        body
                | Value.LetRecursion(_, definitions, body) ->
                    let definitions = OrderedMap.toList definitions
                    distinct location (List.map fst definitions)

                    let next =
                        definitions
                        |> List.fold
                            (fun env (name, definition) ->
                                Map.add name (signature definition.Value) env
                            )
                            env

                    definitions
                    |> List.iter (fun (_, definition) ->
                        checkDefinition location next definition.Value
                    )

                    expression location next contextual body
                | Value.Destructure(_, pattern, target, body) ->
                    expression
                        location
                        (bindPattern location env (check None target) pattern)
                        contextual
                        body
                | Value.IfThenElse(_, condition, yes, no) ->
                    check (Some(reference "basics" "bool" [])) condition
                    |> ignore

                    let yes = check contextual yes
                    let no = check contextual no
                    ensure location yes no

                    yes
                    |> Option.orElse no
                | Value.PatternMatch(_, target, cases) ->
                    let targetType = check None target

                    let outputs =
                        cases
                        |> List.map (fun (pattern, body) ->
                            expression
                                location
                                (bindPattern location env targetType pattern)
                                contextual
                                body
                        )

                    let known =
                        contextual
                        |> Option.orElseWith (fun () -> List.tryPick id outputs)

                    outputs
                    |> List.iter (ensure location known)

                    known
                | Value.UpdateRecord(_, target, entries) ->
                    distinct location (List.map fst entries)
                    let targetType = check contextual target
                    let targetFields = fields targetType

                    entries
                    |> List.iter (fun (name, value) ->
                        let expected =
                            targetFields
                            |> Option.bind (
                                List.tryFind (
                                    fst
                                    >> (=) name
                                )
                            )
                            |> Option.map snd

                        if
                            targetFields.IsSome
                            && expected.IsNone
                        then
                            Decode.fail "type-mismatch" location "The record has no such field."

                        check expected value
                        |> ignore
                    )

                    targetType
                | Value.Hole _ -> None

            ensure location attributes.InferredType actual
            ensure location expected actual

            actual
            |> Option.orElse contextual

        and checkDefinition location env definition =
            distinct location (List.map fst definition.InputTypes)

            definition.InputTypes
            |> List.iter (
                snd
                >> checkType location
            )

            output definition.Body
            |> Option.iter (checkType location)

            let env =
                definition.InputTypes
                |> List.fold (fun env (name, t) -> Map.add name (Some t) env) env

            match definition.Body with
            | ExpressionBody(output, body) ->
                expression location env (Some output) body
                |> ignore
            | _ -> ()

        for path, module' in modules do
            let location = Emit.modulePath path

            for name, definition in OrderedMap.toList module'.Value.Types do
                Emit.words name
                |> ignore

                match definition.Value.Value with
                | TypeAliasDefinition(parameters, body) ->
                    distinct location parameters
                    checkType location body
                | CustomTypeDefinition(parameters, _, constructors) ->
                    distinct location parameters

                    for name, args in OrderedMap.toList constructors do
                        Emit.words name
                        |> ignore

                        distinct location (List.map fst args)

                        args
                        |> List.iter (
                            snd
                            >> checkType location
                        )
                | IncompleteTypeDefinition _ -> ()

            for name, definition in OrderedMap.toList module'.Value.Values do
                Emit.words name
                |> ignore

                checkDefinition
                    (location
                     + "."
                     + Emit.camel name)
                    Map.empty
                    definition.Value.Value
