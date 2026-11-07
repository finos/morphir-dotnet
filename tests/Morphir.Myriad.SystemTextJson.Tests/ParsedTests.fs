// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.ParsedTests

open System.IO
open System.Text.Json
open Expecto
open Morphir.Extensions
open Morphir.Myriad.SystemTextJson.Tests.Parsed
open Morphir.Myriad.SystemTextJson.Tests.Converters

let private refused (json: string) =
    try
        JsonSerializer.Deserialize(json, ParsedJson.Default.Manifest) |> ignore
        failtestf "the JSON was accepted: %s" json
        Unchecked.defaultof<JsonException>
    with :? JsonException as error ->
        error

let private code text =
    match Code.Parse text with
    | Ok code -> code
    | Error reason -> failwith reason

let private manifest =
    {
        Id = ExtensionId.Create "morphir.csharp"
        Version = ReleaseVersion.Create "1.2.3-beta.1"
        Replaces = None
        Owners = Map [ ExtensionId.Create "acme.lint", "Acme" ]
        Codes = [ code "abc" ]
    }

let private json =
    """{"Id":"morphir.csharp","Version":"1.2.3-beta.1","Replaces":null,"Owners":{"acme.lint":"Acme"},"Codes":["abc"]}"""

[<Tests>]
let tests =
    testList "parsed values and user converters" [
        test "parsed values are written as their text and read through their parser" {
            Expect.equal (JsonSerializer.Serialize(manifest, ParsedJson.Default.Manifest)) json "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, ParsedJson.Default.Manifest)) manifest "the value read back"
        }

        test "invalid text fails with the parser's text, and no value is built" {
            let expected =
                match ExtensionId.Parse "A..b" with
                | Ok _ -> failtest "the test id is valid"
                | Error failure -> failure.ToString()

            let error = refused (json.Replace("\"morphir.csharp\"", "\"A..b\""))
            Expect.stringContains error.Message expected "the parser's text"
        }

        test "a parsed value declared in the input uses its own parser" {
            let error = refused (json.Replace("[\"abc\"]", "[\"abcd\"]"))
            Expect.stringContains error.Message "'abcd' is not a code of three letters." "the parser's text"
            Expect.equal (JsonSerializer.Serialize(code "xyz", ParsedJson.Default.Code)) "\"xyz\"" "the context member"
        }

        test "an invalid map key fails through the key's parser" {
            let error = refused (json.Replace("\"acme.lint\"", "\"Not An Id\""))
            Expect.stringContains error.Message "Not An Id" "the key"
        }

        test "a user converter on a member and on a type is used, and the other members are generated" {
            let value = { Label = "hello"; Plain = "x"; Pair = { A = 1; B = 2 } }
            let json = JsonSerializer.Serialize(value, ConverterJson.Default.Labelled)
            Expect.equal json """{"Label":"HELLO","Plain":"x","Pair":"1,2"}""" "the JSON"
            Expect.equal (JsonSerializer.Deserialize(json, ConverterJson.Default.Labelled)) value "the value read back"
            Expect.equal (JsonSerializer.Serialize({ A = 3; B = 4 }, ConverterJson.Default.Pair)) "\"3,4\"" "the context member"
        }

        test "the generated code creates user converters with no reflection" {
            let folder = Path.Combine(__SOURCE_DIRECTORY__, "obj")
            let generated = Directory.GetFiles(folder, "ConverterJson.g.fs", SearchOption.AllDirectories) |> Array.head
            let text = File.ReadAllText generated
            Expect.stringContains text "UpperCaseConverter()" "the converter is created directly"

            for word in [ "Activator"; "GetConstructor"; "CreateInstance"; "MakeGenericType" ] do
                Expect.isFalse (text.Contains word) $"no {word}"
        }
    ]
