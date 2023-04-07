[<RequireQualifiedAccess>]
module Morphir.IR.Codecs.Documented

    open Json
    open Morphir.IR.Documented

    let inline encoder encodeValue (d:Documented<'a>):Value =
        Encode.object [
            "doc", Encode.string d.Doc
            "value", encodeValue d.Value
        ]

    let inline decoder decodeValue: Decode.Decoder<Documented<'a>> =
        Decode.oneOf [
            Decode.map2 documented (Decode.field "doc" Decode.string) (Decode.field "value" decodeValue)
            Decode.map (fun value -> documented "" value) decodeValue
        ]
