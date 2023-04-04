[<RequireQualifiedAccess>]
module Morphir.IR.Codecs.Path

open Json
open Decode
open Morphir.IR.Path

let inline encoder (path: Path) : Value =
    Encode.list Name.encoder path

let decoder: Decoder<Path> =
    Decode.list Name.decoder
    |> Decode.map fromList
