module Morphir.SDK.Tests.DecimalTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Decimal" [
        test "toString converts decimal to string" {
            Decimal.toString 42.5m
            |> Expect.equal "Should be '42.5'" "42.5"
        }

        test "fromString parses valid decimal" {
            Decimal.fromString "42.5"
            |> Expect.equal "Should be Some 42.5" (Some 42.5m)
        }

        test "fromString returns None for invalid" {
            Decimal.fromString "abc"
            |> Expect.equal "Should be None" None
        }

        test "fromInt converts int to decimal" {
            Decimal.fromInt 42
            |> Expect.equal "Should be 42m" 42m
        }

        test "round rounds to decimal places" {
            Decimal.round 2 42.567m
            |> Expect.equal "Should be 42.57" 42.57m
        }

        test "abs gets absolute value" {
            Decimal.abs -42.5m
            |> Expect.equal "Should be 42.5" 42.5m
        }

        test "add adds decimals" {
            Decimal.add 10.5m 5.25m
            |> Expect.equal "Should be 15.75" 15.75m
        }

        test "compare compares decimals" {
            Decimal.compare 10m 5m
            |> Expect.equal "Should be GT" GT
        }
    ]
