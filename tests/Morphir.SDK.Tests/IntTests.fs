module Morphir.SDK.Tests.IntTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Int" [
        test "toString converts int to string" {
            Int.toString 42
            |> Expect.equal "Should be '42'" "42"
        }

        test "fromString parses valid int" {
            Int.fromString "42"
            |> Expect.equal "Should be Some 42" (Some 42)
        }

        test "fromString returns None for invalid" {
            Int.fromString "abc"
            |> Expect.equal "Should be None" None
        }

        test "toFloat converts int to float" {
            Int.toFloat 42
            |> Expect.equal "Should be 42.0" 42.0
        }

        test "fromFloat truncates float to int" {
            Int.fromFloat 42.9
            |> Expect.equal "Should be 42" 42
        }
    ]
