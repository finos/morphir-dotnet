module Morphir.IR.Name

open Morphir.SDK

open Morphir.SDK.Regex
type Name = string list

let fromString (string: string): Name =
    let wordPattern =
        Regex.fromString "([a-zA-Z][a-z]*|[0-9]+)"
        |> Maybe.withDefault Regex.never

    failwith "Not Implemented"
let inline fromList (words: string list): Name = words
