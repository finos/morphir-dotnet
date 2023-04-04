[<RequireQualifiedAccess>]
module Morphir.IR.Codecs.Type

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
    | Type.Tuple (attr, elements) ->
        Encode.list id [
            Encode.string "Tuple"
            encodeAttributes attr
            Encode.list (encoder encodeAttributes) elements
        ]
    | _ ->
        raise (
            System.NotImplementedException($"Encoding of type {tpe} is not implemented yet.")
        )

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
