module Morphir.Extensions.Tests.NonEmptySetTests

open System.Collections.Generic
open Expecto
open Morphir.Extensions

[<Tests>]
let tests =
    testList "NonEmptySet" [
        test "create keeps every element in order" {
            let set = NonEmptySet.create 3 [ 1; 2 ]
            Expect.equal (List.ofSeq set) [ 1; 2; 3 ] "elements are ordered"
            Expect.equal set.Count 3 "count"
        }

        test "duplicates collapse" {
            Expect.equal (NonEmptySet.create 1 [ 1; 1 ]).Count 1 "one element"
        }

        test "an empty sequence is refused" {
            Expect.equal
                (NonEmptySet.ofSeq "targets" ([]: int list))
                (Error(ParseFailure.Empty "targets"))
                "refused"
        }

        test "a non-empty sequence is accepted" {
            match NonEmptySet.ofSeq "targets" [ 2; 1 ] with
            | Ok set -> Expect.equal (List.ofSeq set) [ 1; 2 ] "accepted"
            | Error failure -> failtest $"refused: {failure}"
        }

        test "equality does not depend on input order" {
            Expect.equal (NonEmptySet.create 2 [ 1 ]) (NonEmptySet.create 1 [ 2 ]) "equal"

            Expect.equal
                (hash (NonEmptySet.create 2 [ 1 ]))
                (hash (NonEmptySet.create 1 [ 2 ]))
                "same hash"
        }

        test "sets compare by their elements" {
            Expect.isLessThan
                (compare (NonEmptySet.create 1 []) (NonEmptySet.create 2 []))
                0
                "ordered"
        }

        test "map keeps the set non-empty" {
            let mapped =
                NonEmptySet.map (fun value -> value % 2) (NonEmptySet.create 1 [ 2; 3 ])

            Expect.equal (List.ofSeq mapped) [ 0; 1 ] "mapped and collapsed"
        }

        test "it is a read-only collection for C# callers" {
            let collection = NonEmptySet.create "b" [ "a" ] :> IReadOnlyCollection<string>
            Expect.equal collection.Count 2 "count"
        }

        test "the C# factory builds the same set" {
            Expect.equal (NonEmptySet<int>.Create(2, 1)) (NonEmptySet.create 1 [ 2 ]) "same set"
        }

        test "parse failures describe the rule" {
            Expect.equal
                (string (ParseFailure.Empty "extension id"))
                "extension id is empty"
                "empty"

            Expect.equal
                (string (ParseFailure.TooLong("extension id", 238)))
                "extension id is longer than 238 characters"
                "too long"
        }
    ]
