module Morphir.IR.Path

open Morphir.IR.Name
open Morphir.SDK.List
open Morphir.SDK

/// <summary>
/// Type that represents a path as a list of names.
/// </summary>
type Path = List<Name>

let inline fromList (names: List<Name>) : Path = names

let inline toList (names: Path) : List<Name> = names

let toString nameToString sep path =
    path
    |> toList
    |> List.map nameToString
    |> String.join sep

let fromString string =
    let separatorRegex =
        Regex.fromString "[^\\w\\s]+"
        |> Maybe.withDefault Regex.never in

    Regex.split separatorRegex string
    |> List.map Name.fromString
    |> fromList

let rec isPrefixOf (prefix: Path) (path: Path) =
    match (path, prefix) with
    // empty path is a prefix of any other path
    | ([], _) -> true
    // empty path has no prefixes except the empty prefix captured above
    | (_, []) -> false
    // for any other case compare the head and recurse
    | (pathHead :: pathTail, prefixHead :: prefixTail) ->
        if prefixHead = pathHead then
            isPrefixOf prefixTail pathTail
        else
            false

module Codec =
    open Thoth.Json.Net
    open Morphir.IR.Name.Codec

    let encodePath (path: Path) : JsonValue =
        path
        |> toList
        |> List.map encodeName
        |> Encode.list

    let decodePath: Decoder<Path> =
        Decode.list decodeName
        |> Decode.map fromList
