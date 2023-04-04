[<RequireQualifiedAccess>]
module Morphir.IR.Codecs.Name

open Thoth.Json.Net
open Json
open Morphir.IR.Name

let inline encoder (name:Name): Value =
    Encode.list Encode.string name

let decoder: Decoder<Name> =
    Decode.list Decode.string
    |> Decode.map fromList

