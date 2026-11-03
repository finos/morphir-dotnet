module Morphir.Configuration.Tests.DocumentMergeTests

open Expecto
open Morphir.Configuration

[<Tests>]
let tests =
    testList "Document merge" [
        test "a shorter array replaces the complete lower array" {
            let lower = ConfigValue.Array [ConfigValue.String "a"; ConfigValue.String "b"]
            let higher = ConfigValue.Array [ConfigValue.String "c"]
            Expect.equal (ValueMerge.merge lower higher) higher "only higher array elements survive"
        }
    ]
