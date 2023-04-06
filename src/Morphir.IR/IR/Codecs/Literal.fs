module Morphir.IR.Codecs.Literal

open Json
open Morphir.IR.Literal
open Morphir.SDK

let encoder =
    function
    | BoolLiteral v -> Encode.list id [ Encode.string "BoolLiteral"; Encode.bool v ]
    | CharLiteral v ->
        Encode.list id [ Encode.string "CharLiteral"; Encode.string (String.fromChar v) ]
    | StringLiteral v -> Encode.list id [ Encode.string "StringLiteral"; Encode.string v ]
    | WholeNumberLiteral v -> Encode.list id [ Encode.string "WholeNumberLiteral"; Encode.int64 v ]
    | FloatLiteral v -> Encode.list id [ Encode.string "FloatLiteral"; Encode.float v ]
    | DecimalLiteral v ->
        Encode.list id [ Encode.string "DecimalLiteral"; Encode.string (Decimal.toString v) ]
