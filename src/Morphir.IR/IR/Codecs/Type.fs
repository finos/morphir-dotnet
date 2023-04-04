[<RequireQualifiedAccess>]
module rec Morphir.IR.Codecs.Type

open Json
open Morphir.IR
open Morphir.IR.Type

let rec encoder (encodeAttributes: 'a -> Value) (tpe: Type<'a>) : Encode.Value =
    match tpe with
    | Type.Unit attr -> Encode.list id [ Encode.string "Unit"; encodeAttributes attr ]
    | Type.Variable (attr, name) ->
        Encode.list id [
            Encode.string "Variable"
            encodeAttributes attr
            Name.encoder name
        ]
    | Type.Reference(attributes, typeName, typeParameters) ->
        Encode.list id [
            Encode.string "Reference"
            encodeAttributes attributes
            FQName.encoder typeName
            Encode.list (encoder encodeAttributes) typeParameters
        ]
    | Type.Tuple (attr, elements) ->
        Encode.list id [
            Encode.string "Tuple"
            encodeAttributes attr
            Encode.list (encoder encodeAttributes) elements
        ]
    | Type.Record (attr, fieldTypes) ->
        Encode.list id [
            Encode.string "Record"
            encodeAttributes attr
            Encode.list (encodeField encodeAttributes) fieldTypes
        ]
    | Type.ExtensibleRecord(attributes, variableName, fieldTypes) ->
        Encode.list id [
            Encode.string "ExtensibleRecord"
            encodeAttributes attributes
            Name.encoder variableName
            Encode.list (encodeField encodeAttributes) fieldTypes
        ]
    | Type.Function (attr, argumentType, returnType) ->
        Encode.list id [
            Encode.string "Function"
            encodeAttributes attr
            encoder encodeAttributes argumentType
            encoder encodeAttributes returnType
        ]

let rec decoder (decodeAttributes: Decode.Decoder<'a>) : Decode.Decoder<Type<'a>> =
    Decode.index 0 Decode.string
    |> Decode.andThen (
        function
        | "Variable" ->
            Decode.map2
                Type.variable
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 Name.Codec.decodeName)
        | "Tuple" ->
            Decode.map2
                Type.tuple
                (Decode.index 1 decodeAttributes)
                (Decode.index 2 (Decode.list (decoder decodeAttributes)))
        | "Unit" -> Decode.map Type.unit (Decode.index 1 decodeAttributes)
        | kind -> Decode.fail $"Unknown kind: {kind}"
    )

let encodeField encodeAttributes field: Value =
    Encode.object [
        "name", Name.encoder field.Name
        "tpe", encoder encodeAttributes field.Type
    ]

let decodeField decodeAttributes: Decode.Decoder<Field<'a>> =
    Decode.map2
        field
        (Decode.field "name" Name.Codec.decodeName)
        (Decode.field "tpe" (decoder decodeAttributes))

