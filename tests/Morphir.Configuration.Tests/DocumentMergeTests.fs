module Morphir.Configuration.Tests.DocumentMergeTests

open Expecto
open Morphir.Configuration

[<Tests>]
let tests =
    testList "Document merge" [
        test "ordinary null cannot unset a lower value" {
            let lower = ConfigValue.Object (Map.ofList ["a", ConfigValue.Integer 1L])
            let higher = ConfigValue.Object (Map.ofList ["a", ConfigValue.Null])
            Expect.equal (ValueMerge.merge lower higher) lower "ordinary null is absent, not an unset"
        }
        test "a shorter array replaces the complete lower array" {
            let lower = ConfigValue.Array [ConfigValue.String "a"; ConfigValue.String "b"]
            let higher = ConfigValue.Array [ConfigValue.String "c"]
            Expect.equal (ValueMerge.merge lower higher) higher "only higher array elements survive"
        }
    ]
