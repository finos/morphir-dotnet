module Morphir.SDK.Tests.ListTests

open Expecto
open Expecto.Flip
open Morphir.SDK

[<Tests>]
let tests =
    testList "List" [
        test "map transforms elements" {
            List.map ((*) 2) [1; 2; 3]
            |> Expect.equal "Should be [2; 4; 6]" [2; 4; 6]
        }

        test "filter keeps matching elements" {
            List.filter (fun x -> x % 2 = 0) [1; 2; 3; 4; 5]
            |> Expect.equal "Should be [2; 4]" [2; 4]
        }

        test "foldl accumulates from left" {
            List.foldl (fun acc x -> acc + x) 0 [1; 2; 3]
            |> Expect.equal "Should be 6" 6
        }

        test "head returns first element" {
            List.head [1; 2; 3]
            |> Expect.equal "Should be Some 1" (Some 1)
        }

        test "head returns None for empty list" {
            List.head []
            |> Expect.equal "Should be None" None
        }

        test "tail returns remaining elements" {
            List.tail [1; 2; 3]
            |> Expect.equal "Should be Some [2; 3]" (Some [2; 3])
        }

        test "append concatenates lists" {
            List.append [1; 2] [3; 4]
            |> Expect.equal "Should be [1; 2; 3; 4]" [1; 2; 3; 4]
        }

        test "contains checks membership" {
            List.contains 2 [1; 2; 3]
            |> Expect.isTrue "Should contain 2"
        }

        test "unique removes duplicates" {
            List.unique [1; 2; 2; 3; 1]
            |> Expect.equal "Should be [1; 2; 3]" [1; 2; 3]
        }

        test "sum adds all elements" {
            List.sum [1; 2; 3; 4]
            |> Expect.equal "Should be 10" 10
        }

        test "intersperse adds separator" {
            List.intersperse 0 [1; 2; 3]
            |> Expect.equal "Should be [1; 0; 2; 0; 3]" [1; 0; 2; 0; 3]
        }
    ]
