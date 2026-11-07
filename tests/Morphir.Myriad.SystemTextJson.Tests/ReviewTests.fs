// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Cases found in the review of #519.
module Morphir.Myriad.SystemTextJson.Tests.ReviewTests

open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open Morphir.Myriad.SystemTextJson.Generator
open Morphir.Myriad.SystemTextJson.Tests.Compatibility
open Morphir.Myriad.SystemTextJson.Tests.SkippedOptions

let private settings (pairs: (string * string) list) =
    match ContextSettings.parse (Dictionary<string, string>(dict pairs)) with
    | Ok settings -> settings
    | Error _ -> failwith "the test settings are not valid"

[<Tests>]
let tests =
    testList "review of #519" [
        test "a skippable option is its value alone without UnwrapOption, as in FSharp.SystemTextJson, and reads back" {
            let value = { V = Some 1; W = ValueSome 2; Name = "n" }
            let json = JsonSerializer.Serialize(value, SkippedJson.Default.Maybe)
            Expect.equal json """{"V":1,"W":2,"Name":"n"}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, SkippedJson.Default.Maybe)) value "the value read back"
            expectSameAsReference (JsonFSharpOptions(JsonUnionEncoding.AdjacentTag).WithSkippableOptionFields(SkippableOptionFields.Always)) json value

            let absent = { V = None; W = ValueNone; Name = "n" }
            Expect.equal (JsonSerializer.Serialize(absent, SkippedJson.Default.Maybe)) """{"Name":"n"}""" "absent options are left out"
            Expect.equal (JsonSerializer.Deserialize("""{"Name":"n"}""", SkippedJson.Default.Maybe)) absent "and read back as absent"
        }

        test "an untagged case whose options are all absent is written as {} and read back as that case" {
            for value in [ Measured None; Measured(Some 3); Labelled "l" ] do
                let json = JsonSerializer.Serialize(value, SkippedJson.Default.Sized)
                Expect.equal (JsonSerializer.Deserialize(json, SkippedJson.Default.Sized)) value $"the value read back from {json}"

            Expect.equal (JsonSerializer.Serialize(Measured None, SkippedJson.Default.Sized)) "{}" "the empty object"
        }

        test "two untagged cases that can both be written as {} are refused at build time" {
            let source =
                "namespace Sample\n\nopen Morphir.Myriad.SystemTextJson\n\n[<JsonUnion(BaseEncoding = JsonUnionEncoding.Untagged)>]\ntype U =\n    | A of a: int option\n    | B of b: string\n    | C\n"

            match Generation.generate (settings [ "Name", "AppJson"; "Namespace", "Sample.Json"; "SkippableOptionFields", "Always" ]) "Types.fs" source with
            | Ok _ -> failtest "the ambiguous union was accepted"
            | Error errors ->
                let text = errors |> List.map GenerationError.describe |> String.concat "\n"
                Expect.stringContains text "A" "names A"
                Expect.stringContains text "C" "names C"
                Expect.stringContains text "{}" "says why"
        }

        test "a number abbreviation member reads numbers from strings when the context allows it" {
            let info = LenientNumbers.LenientJson.Default.Count :> System.Text.Json.Serialization.Metadata.JsonTypeInfo
            Expect.equal (JsonSerializer.Deserialize("\"42\"", info) |> unbox<int64>) 42L "a number in a string"
            Expect.equal (JsonSerializer.Deserialize("42", info) |> unbox<int64>) 42L "a number"
        }
    ]
