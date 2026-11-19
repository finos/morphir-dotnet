// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The generated code for a sample input, kept as a snapshot so that each change to the generator shows as a diff.
/// To accept a change, run the tests with UPDATE_SNAPSHOTS=1 and review the snapshot diff.
module Morphir.Myriad.SystemTextJson.Tests.SnapshotTests

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.Myriad.SystemTextJson.Generator

let private folder = Path.Combine(__SOURCE_DIRECTORY__, "Snapshots")

let private generate () =
    let settings =
        match ContextSettings.parse (Dictionary<string, string>(dict [ "Name", "SampleJson"; "Namespace", "Sample.Json"; "Naming", "CamelCase" ])) with
        | Ok settings -> settings
        | Error _ -> failwith "the snapshot settings are not valid"

    match Generation.generate settings "Sample.fs" (File.ReadAllText(Path.Combine(folder, "Sample.fs.txt"))) with
    | Ok code ->
        // The version of the plugin changes with each release; the snapshot does not depend on it.
        Regex.Replace(code, "(Morphir\\.Myriad\\.SystemTextJson[\" ,]+)[0-9]+\\.[0-9]+\\.[0-9]+[^\\s\"]*", "${1}VERSION")
    | Error errors -> failwithf "generation failed: %A" (errors |> List.map GenerationError.describe)

[<Tests>]
let tests =
    testList "snapshots" [
        test "the generated code for the sample input equals the snapshot" {
            let snapshot = Path.Combine(folder, "Sample.g.fs.snapshot")
            let received = Path.Combine(folder, "Sample.g.fs.received")
            let code = generate ()

            if Environment.GetEnvironmentVariable "UPDATE_SNAPSHOTS" = "1" then
                File.WriteAllText(snapshot, code)

            let expected = if File.Exists snapshot then File.ReadAllText(snapshot).Replace("\r\n", "\n") else ""

            if code <> expected then
                File.WriteAllText(received, code)
                failtest $"The generated code differs from {snapshot}. Compare it with {received}; to accept the change, run the tests with UPDATE_SNAPSHOTS=1."
            elif File.Exists received then
                File.Delete received
        }
    ]
    |> testLabel "PlatformSpecific"
