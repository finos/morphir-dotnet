module Morphir.SDK.Tests.SetTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "Set" [
        test "empty creates empty set" {
            Set.empty
            |> Set.isEmpty
            |> Expect.isTrue "Should be empty"
        }

        test "fromList creates set from list" {
            Set.fromList [1; 2; 3; 2; 1]
            |> Set.size
            |> Expect.equal "Should have 3 unique elements" 3
        }

        test "insert adds element" {
            Set.empty
            |> Set.insert 1
            |> Set.contains 1
            |> Expect.isTrue "Should contain 1"
        }

        test "remove deletes element" {
            Set.fromList [1; 2; 3]
            |> Set.remove 2
            |> Set.contains 2
            |> Expect.isFalse "Should not contain 2"
        }

        test "union combines sets" {
            let set1 = Set.fromList [1; 2]
            let set2 = Set.fromList [2; 3]
            Set.union set1 set2
            |> Set.toList
            |> Expect.equal "Should be [1; 2; 3]" [1; 2; 3]
        }

        test "intersect finds common elements" {
            let set1 = Set.fromList [1; 2; 3]
            let set2 = Set.fromList [2; 3; 4]
            Set.intersect set1 set2
            |> Set.toList
            |> Expect.equal "Should be [2; 3]" [2; 3]
        }
    ]
