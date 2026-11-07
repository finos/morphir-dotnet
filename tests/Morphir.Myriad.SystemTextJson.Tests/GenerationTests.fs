// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.GenerationTests

open System.Collections.Generic
open Expecto
open Morphir.Myriad.SystemTextJson.Generator

let private parameters (pairs: (string * string) list) =
    Dictionary<string, string>(dict pairs) :> IDictionary<string, string>

let private settings pairs =
    match ContextSettings.parse (parameters pairs) with
    | Ok settings -> settings
    | Error errors -> failtestf "settings refused: %A" (errors |> List.map GenerationError.describe)

let private emptySource = "namespace Sample.Types\n\nmodule Marker =\n    let value = 0\n"

[<Tests>]
let tests =
    testList "generation" [
        testList "context settings" [
            test "name and namespace are read" {
                let parsed = settings [ "Name", "AppJson"; "Namespace", "Sample.Json" ]
                Expect.equal parsed.Name.Value "AppJson" "name"
                Expect.equal parsed.Namespace.Value "Sample.Json" "namespace"
            }

            test "a missing name is refused" {
                match ContextSettings.parse (parameters [ "Namespace", "Sample.Json" ]) with
                | Ok _ -> failtest "a context with no name was accepted"
                | Error errors ->
                    Expect.exists (errors |> List.map GenerationError.describe) (fun text -> text.Contains "Name") "the error names the setting"
            }

            test "a name that is not an F# identifier is refused" {
                for name in [ ""; "1Json"; "App Json"; "App.Json"; "App-Json" ] do
                    match ContextSettings.parse (parameters [ "Name", name; "Namespace", "Sample.Json" ]) with
                    | Ok _ -> failtestf "the name '%s' was accepted" name
                    | Error _ -> ()
            }

            test "a namespace that is not dotted identifiers is refused" {
                for ns in [ ""; "."; "Sample..Json"; "Sample.1Json"; ".Sample" ] do
                    match ContextSettings.parse (parameters [ "Name", "AppJson"; "Namespace", ns ]) with
                    | Ok _ -> failtestf "the namespace '%s' was accepted" ns
                    | Error _ -> ()
            }

            test "an unknown setting is refused" {
                match ContextSettings.parse (parameters [ "Name", "AppJson"; "Namespace", "Sample.Json"; "Colour", "blue" ]) with
                | Ok _ -> failtest "an unknown setting was accepted"
                | Error errors ->
                    Expect.exists (errors |> List.map GenerationError.describe) (fun text -> text.Contains "Colour") "the error names the setting"
            }
        ]

        testList "empty context" [
            test "an input with no type gives a context with no type info" {
                match Generation.generate (settings [ "Name", "AppJson"; "Namespace", "Sample.Json" ]) "Types.fs" emptySource with
                | Error errors -> failtestf "generation failed: %A" (errors |> List.map GenerationError.describe)
                | Ok code ->
                    Expect.stringContains code "namespace Sample.Json" "the generated namespace"
                    Expect.stringContains code "open Sample.Types" "the input namespace is opened"
                    Expect.stringContains code "type AppJson" "the context type"
                    Expect.stringContains code "inherit JsonSerializerContext" "the context base type"
            }

            test "an input that does not parse fails with the file name" {
                match Generation.generate (settings [ "Name", "AppJson"; "Namespace", "Sample.Json" ]) "Broken.fs" "namespace Sample\n type = =" with
                | Ok _ -> failtest "a broken input was accepted"
                | Error errors ->
                    Expect.exists (errors |> List.map GenerationError.describe) (fun text -> text.Contains "Broken.fs") "the error names the file"
            }
        ]
    ]
