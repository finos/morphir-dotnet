module Morphir.Codecs

open Json

module Default =
    open Morphir.IR

    let encodeUnit () : Encode.Value = Encode.object []

    let decodeUnit: Decode.Decoder<unit> = Decode.succeed ()


    let encodeType (encodeAttributes: 'a -> Encode.Value) (tpe: Type.Type<'a>) : Encode.Value =
        match tpe with
        | Type.Unit attr -> Encode.list id [ Encode.string "Unit"; encodeAttributes attr ]
        | Type.Variable (attr, name) ->
            Encode.list id [
                Encode.string "Variable"
                encodeAttributes attr
                Name.Codec.encodeName name
            ]
        | _ ->
            raise (
                System.NotImplementedException($"Encoding of type {tpe} is not implemented yet.")
            )

    let rec decodeType (decodeAttributes: Decode.Decoder<'a>) : Decode.Decoder<Type.Type<'a>> =
        let rec decodeType' = decodeType decodeAttributes
        let rec lazyDecodeType = Decode.lazyily (fun _ -> decodeType')
        Decode.index 0 Decode.string
        |> Decode.andThen (
            function
            | "Variable" ->
                Decode.map2
                    Type.variable
                    (Decode.index 1 decodeAttributes)
                    (Decode.index 2 Name.Codec.decodeName)
            | "Tuple" -> Decode.map2 Type.tuple (Decode.index 1 decodeAttributes) (Decode.index 2 (Decode.list lazyDecodeType))
            | "Unit" -> Decode.map Type.unit (Decode.index 1 decodeAttributes)
            | kind -> Decode.fail $"Unknown kind: {kind}"
        )
