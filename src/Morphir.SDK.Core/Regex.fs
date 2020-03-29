module Morphir.SDK.Regex

open Morphir.SDK.Maybe

type private RE = System.Text.RegularExpressions.Regex

type Regex =
    | Regex of RE
    | Never

type Match =
    { Match: string
      Index:int
      Number:int
      Submatches: Maybe<string> list }

let never:Regex = Regex.Never

let fromString (string:string) =
    Regex.Regex (RE(string)) |> Maybe.just

let find pattern (string:string) : Match list = failwith "Not Implemented"

