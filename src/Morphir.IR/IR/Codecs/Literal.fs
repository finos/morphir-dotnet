module Morphir.IR.Codecs.Literal

open Json
open System.Text
open Morphir.IR.Literal
open Morphir.SDK
open Morphir.SDK.Maybe

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

let decoder: Decode.Decoder<Literal> =
    Decode.index 0 Decode.string
    |> Decode.andThen (
        function
        | "BoolLiteral" -> Decode.map boolLiteral (Decode.index 1 Decode.bool)
        | "CharLiteral" ->
            Decode.map
                charLiteral
                (Decode.index 1 Decode.string
                 |> Decode.andThen (fun str ->
                     match String.uncons str with
                     | Just (ch, _) -> Decode.succeed ch
                     | _ -> Decode.fail "Single char expected"
                 ))
        | "StringLiteral" -> Decode.map stringLiteral (Decode.index 1 Decode.string)
        | "WholeNumberLiteral" -> Decode.map wholeNumberLiteral (Decode.index 1 Decode.int64)
        | "FloatLiteral" -> Decode.map floatLiteral (Decode.index 1 Decode.float)
        | "DecimalLiteral" ->
            Decode.map
                decimalLiteral
                (Decode.index 1 Decode.string
                 |> Decode.andThen (fun str ->
                     match Decimal.fromString str with
                     | Just dec -> Decode.succeed dec
                     | Nothing -> Decode.fail $"Failed to create decimal from string: {str}"
                 ))
        | other -> Decode.fail $"Unknown literal type: {other}"
    )
