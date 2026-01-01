module Morphir.SDK.Tests.DictTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Dict" [
        test "empty creates empty dictionary" {
            Dict.empty
            |> Dict.isEmpty
            |> Expect.isTrue "Should be empty"
        }

        test "fromList creates dictionary from pairs" {
            Dict.fromList [("a", 1); ("b", 2)]
            |> Dict.size
            |> Expect.equal "Should have 2 entries" 2
        }

        test "get retrieves value by key" {
            let dict = Dict.fromList [("a", 1); ("b", 2)]
            Dict.get "a" dict
            |> Expect.equal "Should be Some 1" (Some 1)
        }

        test "get returns None for missing key" {
            let dict = Dict.fromList [("a", 1)]
            Dict.get "b" dict
            |> Expect.equal "Should be None" None
        }

        test "insert adds new key-value pair" {
            let dict = Dict.empty |> Dict.insert "a" 1
            Dict.get "a" dict
            |> Expect.equal "Should be Some 1" (Some 1)
        }

        test "remove deletes key" {
            let dict = Dict.fromList [("a", 1); ("b", 2)] |> Dict.remove "a"
            Dict.contains "a" dict
            |> Expect.isFalse "Should not contain 'a'"
        }

        test "contains checks for key" {
            let dict = Dict.fromList [("a", 1)]
            Dict.contains "a" dict
            |> Expect.isTrue "Should contain 'a'"
        }
    ]
