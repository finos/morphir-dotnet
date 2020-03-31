module Morphir.SDK.String

open Morphir.SDK
open Morphir.SDK

let inline isEmpty string =
    System.String.IsNullOrEmpty(string)

let length = function
    | null -> 0
    | (str:string) -> str.Length

let inline toLower (str:string) =
    str.ToLowerInvariant()

let inline toUpper (str:string) =
    str.ToUpperInvariant()

let uncons (string:string) =
    match string  with
    | null -> Maybe.Nothing
    | "" -> Maybe.Nothing
    | str when (str.Length = 1) -> Maybe.Just  (str.[0], System.String.Empty)
    | str -> Maybe.Just (str.[0], str.[1..])

