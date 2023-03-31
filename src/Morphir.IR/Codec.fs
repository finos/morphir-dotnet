module Morphir.Codec

open Json

let encodeUnit () : Encode.Value = Encode.object []

let decodeUnit: Decode.Decoder<unit> = Decode.succeed ()
