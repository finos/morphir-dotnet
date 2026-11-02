namespace Morphir.CodeGeneration.FSharp

open Morphir.IR.V4

/// Admission for the delivered v4 expression profile, before any optimization or source generation.
module internal V4Support =
    let private extensions location =
        function
        | None
        | Some(DocumentObject _) -> () // Opaque optional metadata has no executable meaning here.
        | Some _ ->
            Decode.fail
                "invalid-extension-data"
                location
                "Extension metadata must be a document object."

    let private typeAttributes location attributes =
        extensions location attributes.Extensions

        match attributes.Constraints with
        | None -> ()
        | Some(DocumentObject values) when OrderedMap.count values = 0 -> ()
        | Some _ ->
            Decode.fail
                "unsupported-constraints"
                location
                "Nonempty type constraints have no declared F# mapping."

    let rec private typ location value =
        match value with
        | Type.Variable(a, _)
        | Type.Unit a -> typeAttributes location a
        | Type.Reference(a, _, arguments)
        | Type.Tuple(a, arguments) ->
            typeAttributes location a
            List.iter (typ location) arguments
        | Type.Record(a, fields)
        | Type.ExtensibleRecord(a, _, fields) ->
            typeAttributes location a

            fields
            |> List.iter (
                snd
                >> typ location
            )
        | Type.Function(a, input, output) ->
            typeAttributes location a
            typ location input
            typ location output

    let private valueAttributes location (attributes: ValueAttributes) =
        extensions location attributes.Extensions

        attributes.InferredType
        |> Option.iter (typ location)

    let rec private pattern location value =
        match value with
        | WildcardPattern a
        | EmptyListPattern a
        | UnitPattern a -> valueAttributes location a
        | AsPattern(a, nested, _) ->
            valueAttributes location a
            pattern location nested
        | TuplePattern(a, patterns)
        | ConstructorPattern(a, _, patterns) ->
            valueAttributes location a
            List.iter (pattern location) patterns
        | HeadTailPattern(a, head, tail) ->
            valueAttributes location a
            pattern location head
            pattern location tail
        | LiteralPattern(a, literal) ->
            valueAttributes location a

            ScalarProfile.render location literal
            |> ignore

    let rec private expression location value =
        let many = List.iter (expression location)

        match value with
        | Value.Hole _ ->
            Decode.fail "unsupported-hole" location "A hole is not an executable expression."
        | Value.Literal(a, literal) ->
            valueAttributes location a

            ScalarProfile.render location literal
            |> ignore
        | Value.Unit a
        | Value.Variable(a, _)
        | Value.Reference(a, _)
        | Value.Constructor(a, _)
        | Value.FieldFunction(a, _) -> valueAttributes location a
        | Value.Tuple(a, values)
        | Value.List(a, values) ->
            valueAttributes location a
            many values
        | Value.Record(a, fields) ->
            valueAttributes location a

            fields
            |> List.iter (
                snd
                >> expression location
            )
        | Value.Field(a, target, _) ->
            valueAttributes location a
            expression location target
        | Value.Apply(a, fn, arg) ->
            valueAttributes location a
            many [ fn; arg ]
        | Value.Lambda(a, p, body) ->
            valueAttributes location a
            pattern location p
            expression location body
        | Value.LetDefinition(a, _, value, body) ->
            valueAttributes location a
            definition location value.Value
            expression location body
        | Value.LetRecursion(a, definitions, body) ->
            valueAttributes location a

            OrderedMap.toList definitions
            |> List.iter (
                snd
                >> fun value -> definition location value.Value
            )

            expression location body
        | Value.Destructure(a, p, target, body) ->
            valueAttributes location a
            pattern location p
            many [ target; body ]
        | Value.IfThenElse(a, condition, yes, no) ->
            valueAttributes location a
            many [ condition; yes; no ]
        | Value.PatternMatch(a, target, cases) ->
            valueAttributes location a
            expression location target

            cases
            |> List.iter (fun (p, body) ->
                pattern location p
                expression location body
            )
        | Value.UpdateRecord(a, target, fields) ->
            valueAttributes location a
            expression location target

            fields
            |> List.iter (
                snd
                >> expression location
            )

    and private definition location definition =
        match definition.Body with
        | NativeBody _ ->
            Decode.fail
                "unsupported-native-body"
                location
                "Native hints are not F# implementations."
        | ExternalBody _ ->
            Decode.fail
                "unsupported-external-body"
                location
                "External bindings require a supported target binding adapter."
        | IncompleteBody _ ->
            Decode.fail
                "unsupported-incomplete-body"
                location
                "An incomplete body cannot become executable source."
        | ExpressionBody(output, body) ->
            definition.InputTypes
            |> List.iter (
                snd
                >> typ location
            )

            typ location output
            expression location body

    let validate =
        function
        | Specs _
        | Application _ ->
            Decode.fail
                "unsupported-distribution"
                "$"
                "F# generation requires Library implementations."
        | Library(_, dependencies, package) ->
            for dependencyName, dependency in OrderedMap.toList dependencies do
                for path, module' in OrderedMap.toList dependency.Modules do
                    let location =
                        "dependencies."
                        + Emit.packagePath dependencyName
                        + "."
                        + Emit.modulePath path

                    for name, specification in OrderedMap.toList module'.Types do
                        let location =
                            location
                            + "."
                            + Emit.pascal name

                        match specification.Value with
                        | TypeAliasSpecification(_, _, body)
                        | DerivedTypeSpecification(_, _, body, _, _) -> typ location body
                        | CustomTypeSpecification(_, _, constructors) ->
                            for _, arguments in OrderedMap.toList constructors do
                                arguments
                                |> List.iter (
                                    snd
                                    >> typ location
                                )
                        | OpaqueTypeSpecification _ -> ()

                    for name, specification in OrderedMap.toList module'.Values do
                        let location =
                            location
                            + "."
                            + Emit.camel name

                        specification.Value.Inputs
                        |> List.iter (
                            snd
                            >> typ location
                        )

                        typ location specification.Value.Output

            for path, module' in OrderedMap.toList package.Modules do
                let location = Emit.modulePath path

                for name, controlled in OrderedMap.toList module'.Value.Types do
                    let location =
                        location
                        + "."
                        + Emit.pascal name

                    match controlled.Value.Value with
                    | IncompleteTypeDefinition _ ->
                        Decode.fail
                            "unsupported-incomplete-type"
                            location
                            "An incomplete type cannot become executable source."
                    | TypeAliasDefinition(_, body) -> typ location body
                    | CustomTypeDefinition(_, _, constructors) ->
                        OrderedMap.toList constructors
                        |> List.iter (fun (_, args) ->
                            args
                            |> List.iter (
                                snd
                                >> typ location
                            )
                        )

                for name, controlled in OrderedMap.toList module'.Value.Values do
                    definition
                        (location
                         + "."
                         + Emit.camel name)
                        controlled.Value.Value
