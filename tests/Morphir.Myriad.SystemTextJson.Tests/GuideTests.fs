// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Keeps docs/content/docs/guides/myriad-json.md true: its F# samples compile, and its JSON and messages are real.
module Morphir.Myriad.SystemTextJson.Tests.GuideTests

open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open System.Text.RegularExpressions
open Expecto
open Morphir.Myriad.SystemTextJson.Generator
open Morphir.Myriad.SystemTextJson.Tests.Compatibility
open Guide
open Guide.Json

let private guide =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../docs/content/docs/guides/myriad-json.md")).Replace("\r\n", "\n")

let private samples =
    [ "Guide/Types.fs"; "Guide/Usage.fs" ]
    |> List.map (fun file -> File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, file)).Replace("\r\n", "\n"))
    |> String.concat "\n"

let private blocks (language: string) =
    Regex.Matches(guide, $"```{language}\n(.*?)```", RegexOptions.Singleline)
    |> Seq.map (fun m -> m.Groups[1].Value.TrimEnd())
    |> List.ofSeq

[<Tests>]
let tests =
    testList "guide" [
        test "every F# sample of the guide is in the compiled sample files" {
            let fsharp = blocks "fsharp"
            Expect.isNonEmpty fsharp "the guide has F# samples"

            for block in fsharp do
                Expect.isTrue (samples.Contains block) $"the sample is compiled:\n{block}"
        }

        test "the JSON in the guide is what the generated code writes" {
            let person = Usage.writePerson ()
            Expect.equal person """{"name":"Ada","age":36,"nickname":null}""" "writePerson"
            Expect.stringContains guide person "the guide shows it"

            let settings = JsonSerializer.Serialize({ MaxItems = 3; Colour = "red" }, AppJson.Default.Settings)
            Expect.equal settings """{"max_items":3,"colour":"red"}""" "Settings"
            Expect.stringContains guide settings "the guide shows it"
            Expect.equal (JsonSerializer.Deserialize("""{"max_items":3,"color":"red"}""", AppJson.Default.Settings)) { MaxItems = 3; Colour = "red" } "the second name"

            let circle = JsonSerializer.Serialize(Circle 1.5, AppJson.Default.Shape)
            Expect.equal circle """{"kind":"Circle","radius":1.5}""" "Circle"
            Expect.stringContains guide circle "the guide shows it"
            Expect.equal (Usage.readShape circle) (Circle 1.5) "readShape"

            Expect.equal (JsonSerializer.Deserialize("18446744073709551615", AppJson.Default.RequestId)) (Number 18446744073709551615UL) "a number id"
            Expect.equal (JsonSerializer.Deserialize("\"req-1\"", AppJson.Default.RequestId)) (Text "req-1") "a text id"
        }

        test "the union encoding table equals FSharp.SystemTextJson" {
            let values: Corpus.Shape list = [ Corpus.NoArgs; Corpus.OneArg 1; Corpus.TwoArgs(3, "t") ]

            let rows =
                Regex.Matches(guide, @"^\| `([A-Za-z, ]+)` \| `([^`]+)` \| `([^`]+)` \| `([^`]+)` \|$", RegexOptions.Multiline)
                |> List.ofSeq

            Expect.hasLength rows 6 "six encoding rows"

            for row in rows do
                let encoding =
                    row.Groups[1].Value.Split(", ")
                    |> Array.map (fun name -> System.Enum.Parse<JsonUnionEncoding> name)
                    |> Array.reduce (|||)

                for index, value in List.indexed values do
                    Expect.equal row.Groups[index + 2].Value (reference (JsonFSharpOptions(encoding)) value) $"{row.Groups[1].Value} for {value}"
        }

        test "the build error in the guide is the generator's message" {
            let settings =
                match ContextSettings.parse (Dictionary<string, string>(dict [ "Name", "AppJson"; "Namespace", "Fixture.Json" ])) with
                | Ok settings -> settings
                | Error _ -> failwith "the settings are not valid"

            match Generation.generate settings "Types.fs" "namespace Fixture\n\ntype Page<'T> = { Items: 'T list }\n" with
            | Ok _ -> failtest "a generic type was accepted"
            | Error errors ->
                let line = errors |> List.map GenerationError.toBuildLine |> List.exactlyOne
                Expect.isTrue (blocks "text" |> List.contains line) $"the guide shows: {line}"
        }

        test "the nested run-time error in the guide is the generated message" {
            let options =
                JsonSerializerOptions(TypeInfoResolver = JsonTypeInfoResolver.Combine(Records.RecordJson.Default, Addresses.AddressJson.Default))

            let info = options.GetTypeInfo typeof<Records.Household> :?> JsonTypeInfo<Records.Household>

            try
                JsonSerializer.Deserialize("""{"Head":{"Name":"Ada","Age":"old","Active":true},"Home":{"Street":"M","Number":1}}""", info) |> ignore
                failtest "the JSON was accepted"
            with :? JsonException as error ->
                Expect.isTrue (blocks "text" |> List.contains error.Message) $"the guide shows: {error.Message}"
        }
    ]
