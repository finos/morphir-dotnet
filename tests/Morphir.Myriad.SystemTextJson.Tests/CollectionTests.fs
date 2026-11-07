// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.CollectionTests

open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open Morphir.Myriad.SystemTextJson.Tests.Compatibility
open Morphir.Myriad.SystemTextJson.Tests.Collections
open Morphir.Myriad.SystemTextJson.Tests.PairMaps

let private bag =
    {
        Maybe = Some 1
        MaybeValue = ValueNone
        Items = [ 1; 2 ]
        Words = [| "a"; "b" |]
        Tags = set [ "y"; "x" ]
        Buffer = ResizeArray [ 3 ]
        ReadOnly = [| 4; 5 |]
        Collection = [| "c" |]
        Pair = (6, "six")
        Nested = [ [ 1 ]; []; [ 2; 3 ] ]
        Lookup = Map [ "one", 1; "two", 2 ]
        ByNumber = Map [ 1, "one" ]
        Dictionary = Dictionary(dict [ "yes", true ])
    }

let private expected =
    """{"Maybe":1,"MaybeValue":null,"Items":[1,2],"Words":["a","b"],"Tags":["x","y"],"Buffer":[3],"ReadOnly":[4,5],"Collection":["c"],"Pair":[6,"six"],"Nested":[[1],[],[2,3]],"Lookup":{"one":1,"two":2},"ByNumber":[[1,"one"]],"Dictionary":{"yes":true}}"""

let private refused (json: string) =
    try
        JsonSerializer.Deserialize(json, CollectionJson.Default.Bag) |> ignore
        failtestf "the JSON was accepted: %s" json
    with :? JsonException ->
        ()

[<Tests>]
let tests =
    testList "collections" [
        test "every collection shape has the expected JSON" {
            Expect.equal (JsonSerializer.Serialize(bag, CollectionJson.Default.Bag)) expected "the JSON"
        }

        test "every collection shape gives the same JSON as FSharp.SystemTextJson" {
            expectSameAsReference (JsonFSharpOptions.Default()) (JsonSerializer.Serialize(bag, CollectionJson.Default.Bag)) bag
        }

        test "every collection shape reads back" {
            let back = JsonSerializer.Deserialize(expected, CollectionJson.Default.Bag)
            Expect.equal (JsonSerializer.Serialize(back, CollectionJson.Default.Bag)) expected "the JSON of the value read back"
        }

        test "a named collection type is a context member" {
            let json = JsonSerializer.Serialize([ bag ], CollectionJson.Default.Bags)
            Expect.equal json ("[" + expected + "]") "the JSON"
            Expect.hasLength (JsonSerializer.Deserialize(json, CollectionJson.Default.Bags)) 1 "the list read back"
        }

        test "a tuple must have exactly its number of items" {
            refused (expected.Replace("""[6,"six"]""", "[6]"))
            refused (expected.Replace("""[6,"six"]""", """[6,"six",7]"""))
        }

        test "a null item in a list of a type that is not an option is refused" {
            refused (expected.Replace("[1,2]", "[1,null]"))
        }

        test "a key that appears twice in a map object is refused" {
            refused (expected.Replace("""{"one":1,"two":2}""", """{"one":1,"one":2}"""))
        }

        test "a map with ArrayOfPairs is an array of pairs, also for string keys" {
            let value = { Entries = Map [ "a", 1 ] }
            let json = JsonSerializer.Serialize(value, PairJson.Default.Index)
            Expect.equal json """{"Entries":[["a",1]]}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, PairJson.Default.Index)) value "the value read back"
            expectSameAsReference (JsonFSharpOptions.Default().WithMapFormat(MapFormat.ArrayOfPairs)) json value
        }
    ]
