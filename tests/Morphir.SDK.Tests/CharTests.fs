module Morphir.SDK.Tests.CharTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Char" [
        test "isUpper checks uppercase" {
            Char.isUpper 'A'
            |> Expect.isTrue "Should be true"
        }

        test "isLower checks lowercase" {
            Char.isLower 'a'
            |> Expect.isTrue "Should be true"
        }

        test "isDigit checks digits" {
            Char.isDigit '5'
            |> Expect.isTrue "Should be true"
        }

        test "toUpper converts to uppercase" {
            Char.toUpper 'a'
            |> Expect.equal "Should be 'A'" 'A'
        }

        test "toLower converts to lowercase" {
            Char.toLower 'A'
            |> Expect.equal "Should be 'a'" 'a'
        }

        test "toCode gets char code" {
            Char.toCode 'A'
            |> Expect.equal "Should be 65" 65
        }

        test "fromCode creates char from code" {
            Char.fromCode 65
            |> Expect.equal "Should be Some 'A'" (Some 'A')
        }
    ]
