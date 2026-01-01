module Morphir.SDK.Tests.TupleTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Tuple" [
        test "pair creates tuple" {
            Tuple.pair 1 2
            |> Expect.equal "Should be (1, 2)" (1, 2)
        }

        test "first gets first element" {
            Tuple.first (1, 2)
            |> Expect.equal "Should be 1" 1
        }

        test "second gets second element" {
            Tuple.second (1, 2)
            |> Expect.equal "Should be 2" 2
        }

        test "mapFirst transforms first element" {
            Tuple.mapFirst ((*) 2) (3, "hello")
            |> Expect.equal "Should be (6, hello)" (6, "hello")
        }

        test "mapSecond transforms second element" {
            Tuple.mapSecond String.toUpper (3, "hello")
            |> Expect.equal "Should be (3, HELLO)" (3, "HELLO")
        }

        test "mapBoth transforms both elements" {
            Tuple.mapBoth ((*) 2) String.toUpper (3, "hello")
            |> Expect.equal "Should be (6, HELLO)" (6, "HELLO")
        }
    ]
