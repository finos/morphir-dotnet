module rec Morphir.IR.Codecs.Value

open Json
open Morphir.IR.Value
open Morphir.SDK

let rec encoder
    (encodeTypeAttributes: 'ta -> Value)
    (encodeValueAttributes: 'va -> Value)
    (value: Value<'ta, 'va>)
    : Value =
    match value with
    | Literal (attributes, value) ->
        Encode.list id [
            Encode.string "literal"
            encodeValueAttributes attributes
            Literal.encoder value
        ]
    | Constructor (attributes, fullyQualifiedName) ->
        Encode.list id [
            Encode.string "constructor"
            encodeValueAttributes attributes
            FQName.encoder fullyQualifiedName
        ]
    | Tuple (attributes, elements) ->
        Encode.list id [
            Encode.string "tuple"
            encodeValueAttributes attributes
            Encode.list (encoder encodeTypeAttributes encodeValueAttributes) elements
        ]
    | List (attributes, items) ->
        Encode.list id [
            Encode.string "list"
            encodeValueAttributes attributes
            Encode.list (encoder encodeTypeAttributes encodeValueAttributes) items
        ]
    | Record (attributes, fields) ->
        Encode.list id [
            Encode.string "record"
            encodeValueAttributes attributes
            Encode.list
                (fun (name, value) ->
                    Encode.list id [
                        Name.encoder name
                        encoder encodeTypeAttributes encodeValueAttributes value
                    ]
                )
                fields
        ]
    | Variable (attributes, name) ->
        Encode.list id [
            Encode.string "variable"
            encodeValueAttributes attributes
            Name.encoder name
        ]
    | Reference (attributes, fullyQualifiedName) ->
        Encode.list id [
            Encode.string "reference"
            encodeValueAttributes attributes
            FQName.encoder fullyQualifiedName
        ]
    | Field (attributes, subjectValue, fieldName) ->
        Encode.list id [
            Encode.string "field"
            encodeValueAttributes attributes
            encoder encodeTypeAttributes encodeValueAttributes subjectValue
            Name.encoder fieldName
        ]
    | FieldFunction (attributes, fieldName) ->
        Encode.list id [
            Encode.string "field_function"
            encodeValueAttributes attributes
            Name.encoder fieldName
        ]
    | Apply (attributes, ``function``, argument) ->
        Encode.list id [
            Encode.string "apply"
            encodeValueAttributes attributes
            encoder encodeTypeAttributes encodeValueAttributes ``function``
            encoder encodeTypeAttributes encodeValueAttributes argument
        ]
    | Lambda (attributes, argumentPattern, body) ->
        Encode.list id [
            Encode.string "lambda"
            encodeValueAttributes attributes
            encodePattern encodeValueAttributes argumentPattern
            encoder encodeTypeAttributes encodeValueAttributes body
        ]
    | LetDefinition (attributes, valueName, valueDefinition, inValue) ->
        Encode.list id [
            Encode.string "let_definition"
            encodeValueAttributes attributes
            Name.encoder valueName
            encodeDefinition encodeTypeAttributes encodeValueAttributes valueDefinition
            encoder encodeTypeAttributes encodeValueAttributes inValue
        ]
    | LetRecursion (attributes, valueDefinitions, inValue) ->
        Encode.list id [
            Encode.string "let_recursion"
            encodeValueAttributes attributes
            valueDefinitions
            |> Dict.toList
            |> Encode.list (fun (name, definition) ->
                Encode.list id [
                    Name.encoder name
                    encodeDefinition encodeTypeAttributes encodeValueAttributes definition
                ]
            )
            encoder encodeTypeAttributes encodeValueAttributes inValue
        ]
    | Destructure (attributes, pattern, valueToDestruct, inValue) ->
        Encode.list id [
            Encode.string "destructure"
            encodeValueAttributes attributes
            encodePattern encodeValueAttributes pattern
            encoder encodeTypeAttributes encodeValueAttributes valueToDestruct
            encoder encodeTypeAttributes encodeValueAttributes inValue
        ]
    | IfThenElse (attributes, condition, thenBranch, elseBranch) ->
        Encode.list id [
            Encode.string "if_then_else"
            encodeValueAttributes attributes
            encoder encodeTypeAttributes encodeValueAttributes condition
            encoder encodeTypeAttributes encodeValueAttributes thenBranch
            encoder encodeTypeAttributes encodeValueAttributes elseBranch
        ]
    | PatternMatch (attributes, branchOutOn, cases) ->
        Encode.list id [
            Encode.string "pattern_match"
            encodeValueAttributes attributes
            encoder encodeTypeAttributes encodeValueAttributes branchOutOn
            Encode.list
                (fun (pattern, value) ->
                    Encode.list id [
                        encodePattern encodeValueAttributes pattern
                        encoder encodeTypeAttributes encodeValueAttributes value
                    ]
                )
                cases
        ]
    | UpdateRecord (attributes, valueToUpdate, fieldsToUpdate) ->
        Encode.list id [
            Encode.string "update_record"
            encodeValueAttributes attributes
            encoder encodeTypeAttributes encodeValueAttributes valueToUpdate
            fieldsToUpdate
            |> Dict.toList
            |> Encode.list (fun (name, value) ->
                Encode.list id [
                    Name.encoder name
                    encoder encodeTypeAttributes encodeValueAttributes value
                ]
            )
        ]
    | Unit attributes -> Encode.list id [ Encode.string "unit"; encodeValueAttributes attributes ]


let decoder
    (decodeTypeAttributes: Decode.Decoder<'ta>)
    (decodeValueAttributes: Decode.Decoder<'va>)
    : Decode.Decoder<Value<'ta, 'va>> =
    Decode.index 0 Decode.string
    |> Decode.andThen (fun kind ->
        match kind with
        | "literal" ->
            Decode.map2 literal
                (Decode.index 1 decodeValueAttributes)
                (Decode.index 2 Literal.decoder)
        | "constructor" ->
            Decode.map2 constructor
                (Decode.index 1 decodeValueAttributes)
                (Decode.index 2 FQName.decoder)
        | other ->
            Decode.fail ($"Unknown pattern value: {other}")
    )

let rec encodePattern (encodeAttributes: 'va -> Value) (pattern: Pattern<'va>) : Value =
    match pattern with
    | WildcardPattern attributes ->
        Encode.list id [ Encode.string "wildcard_pattern"; encodeAttributes attributes ]
    | AsPattern (attributes, pattern, name) ->
        Encode.list id [
            Encode.string "as_pattern"
            encodeAttributes attributes
            encodePattern encodeAttributes pattern
            Name.encoder name
        ]
    | TuplePattern (attributes, elementPatterns) ->
        Encode.list id [
            Encode.string "tuple_pattern"
            encodeAttributes attributes
            Encode.list (encodePattern encodeAttributes) elementPatterns
        ]
    | ConstructorPattern (attributes, constructorName, argumentPatterns) ->
        Encode.list id [
            Encode.string "constructor_pattern"
            encodeAttributes attributes
            FQName.encoder constructorName
            Encode.list (encodePattern encodeAttributes) argumentPatterns
        ]
    | EmptyListPattern attributes ->
        Encode.list id [ Encode.string "empty_list_pattern"; encodeAttributes attributes ]
    | HeadTailPattern (attributes, headPattern, tailPattern) ->
        Encode.list id [
            Encode.string "head_tail_pattern"
            encodeAttributes attributes
            encodePattern encodeAttributes headPattern
            encodePattern encodeAttributes tailPattern
        ]
    | LiteralPattern (attributes, value) ->
        Encode.list id [
            Encode.string "literal_pattern"
            encodeAttributes attributes
            Literal.encoder value
        ]
    | UnitPattern attributes ->
        Encode.list id [ Encode.string "unit_pattern"; encodeAttributes attributes ]

let encodeDefinition
    (encodeTypeAttributes: 'ta -> Value)
    (encodeValueAttributes: 'va -> Value)
    (def: Definition<'ta, 'va>)
    : Value =
    Encode.object [
        "inputTypes",
        def.InputTypes
        |> Encode.list (fun (argName, a, argType) ->
            Encode.list id [
                Name.encoder argName
                encodeValueAttributes a
                Type.encoder encodeTypeAttributes argType
            ]
        )
        "outputType", Type.encoder encodeTypeAttributes def.OutputType
        "body", encoder encodeTypeAttributes encodeValueAttributes def.Body
    ]

let rec decodeDefinition
    (decodeTypeAttributes: Decode.Decoder<'ta>)
    (decodeValueAttributes: Decode.Decoder<'va>)
    : Decode.Decoder<Definition<'ta, 'va>> =
    Decode.map3
        definition
        (Decode.field
            "inputTypes"
            (Decode.list (
                Decode.map3
                    (fun argName a argType -> argName, a, argType)
                    (Decode.index 0 Name.decoder)
                    (Decode.index 1 decodeValueAttributes)
                    (Decode.index 2 (Type.decoder decodeTypeAttributes))
            )))
        (Decode.field "outputType" (Type.decoder decodeTypeAttributes))
        (Decode.field "body" (decoder decodeTypeAttributes decodeValueAttributes))
