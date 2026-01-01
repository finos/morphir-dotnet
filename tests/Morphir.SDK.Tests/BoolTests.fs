module Morphir.SDK.Tests.BoolTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Bool" [
        test "not negates true" {
            Bool.not true
            |> Expect.isFalse "Should be false"
        }

        test "not negates false" {
            Bool.not false
            |> Expect.isTrue "Should be true"
        }

        test "xor returns true for different values" {
            Bool.xor true false
            |> Expect.isTrue "Should be true"
        }

        test "xor returns false for same values" {
            Bool.xor true true
            |> Expect.isFalse "Should be false"
        }
    ]
