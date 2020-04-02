module Morphir.SDK.String

open Morphir.SDK

let inline isEmpty string =
    System.String.IsNullOrEmpty(string)

let length = function
    | null -> 0
    | (str:string) -> str.Length

let repeat (str:string) (x:int) =
    String.replicate x str

let replace (before:string) (after:string) (str:string) =
    str.Replace(before, after)

let append (str1:string) (str2:string) =
    str1 + str2

let split (sep:string) (str:string) =
    str.Split sep

let join (sep:string) (chunks:Morphir.SDK.List.List<string>) =
    System.String.Join(sep, chunks)

let concat (strings: string list) =
    join "" strings

let words (str:string) =
    str.Split "\s"

let lines (str:string) =
    str.Split "\n"

let slice (startIndex:int) (endIndex: int) (str:string) =
    str.[startIndex..endIndex]

let left (n:int) (str:string) =
    if n < 1 then ""
    else slice 0 n str

let right (n:int) (str:string) =
    if n < 1 then ""
    else slice -n (length str) str

let dropLeft (n:int) (str:string) =
    if n < 1 then str
    else slice n (length str) str

let dropRight (n:int) (str:string) =
    if n < 1 then str
    else slice 0 -n str

let contains (substring:string) (str:string) =
    str.Contains substring

let startsWith (substring:string) (str:string) =
    str.StartsWith substring

let endsWith (substring:string) (str:string) =
    str.EndsWith substring

let rec indexesHelp (substring:string) (str:string) (result: int list) =
    let idx = str.IndexOf substring
    if (idx = -1) then result
    else indexesHelp substring (right idx str) (result @ [idx])

let indexes (substring:string) (str:string) =
    indexesHelp substring str []

let indices (substring:string) (str:string) =
    indexes substring str

let inline toLower (str:string) =
    str.ToLowerInvariant()

let inline toUpper (str:string) =
    str.ToUpperInvariant()

let cons (ch:char) (string:string) =
    sprintf "%c%s" ch string

let uncons (string:string) =
    match string  with
    | null -> Maybe.Nothing
    | "" -> Maybe.Nothing
    | str when (str.Length = 1) -> Maybe.Just  (str.[0], System.String.Empty)
    | str -> Maybe.Just (str.[0], str.[1..])
