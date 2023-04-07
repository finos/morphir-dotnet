[<AutoOpen>]
module Morphir.IR.Codecs.Prelude

open Json
open Json.Decode

let inline encodeUnit () = Encode.object []
let decodeUnit: Decoder<unit> = Decode.succeed ()
