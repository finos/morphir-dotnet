module Morphir.IR.Name

open Morphir.SDK

type Name = Morphir.SDK.List.List<string>

let inline fromList (words:string list):Name =
    words

let fromString (string: string): Name =
    let wordPattern =
        Regex.fromString "([a-zA-Z][a-z]*|[0-9]+)"
        |> Maybe.withDefault Regex.never

    Regex.find wordPattern string
        |> List.map (fun me -> me.Match)
        |> List.map String.toLower
        |> fromList

let toList  (name:Name) : List<string> =
    name
