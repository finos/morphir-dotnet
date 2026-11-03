module Morphir.Configuration.Tests.DocumentMergeTests

open Expecto
open Morphir.Configuration

[<Tests>]
let tests =
    testList "Document merge" [
        test "typed-host normalization rejects non-mapping roots" {
            let source = {
                Path = "embedded.json5"
                Format = DocumentFormat.Json
                Role = SourceRole.RootPrimary
                Order = 0
                Profile = None
            }

            for value in [ ConfigValue.Null; ConfigValue.String "scalar"; ConfigValue.Array [] ] do
                let document = Normalization.normalize source value Map.empty

                Expect.isTrue
                    (document.Diagnostics
                     |> List.exists (fun error ->
                         error.Code = "config.document_shape"
                         && error.Source = source
                     ))
                    "Typed hosts must receive the same root-shape error as text readers"
        }
        test "ordinary null cannot unset a lower value" {
            let lower = ConfigValue.Object(Map.ofList [ "a", ConfigValue.Integer 1L ])
            let higher = ConfigValue.Object(Map.ofList [ "a", ConfigValue.Null ])

            Expect.equal
                (ValueMerge.merge lower higher)
                lower
                "ordinary null is absent, not an unset"
        }
        test "a shorter array replaces the complete lower array" {
            let lower = ConfigValue.Array [ ConfigValue.String "a"; ConfigValue.String "b" ]
            let higher = ConfigValue.Array [ ConfigValue.String "c" ]
            Expect.equal (ValueMerge.merge lower higher) higher "only higher array elements survive"
        }
    ]
