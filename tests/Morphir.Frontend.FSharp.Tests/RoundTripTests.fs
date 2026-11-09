// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The IR → F# → IR round trip, a morphir-dotnet check and not a kit verdict. The F# backend
/// generates F# from the IR of each successful corpus case, the frontend compiles that F# again,
/// and the second IR must equal the first.
module Morphir.Frontend.FSharp.Tests.RoundTripTests

open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Conformance

/// Removes the source spans from the attributes of a v4 model. The generated F# has other
/// lines and columns than the original source, so spans cannot be equal. Classic IR has no spans.
let rec private withoutSpans (node: JsonNode | null) =
    match node with
    | :? JsonObject as item ->
        match item["attributes"] with
        | :? JsonObject as attributes ->
            attributes.Remove "source"
            |> ignore
        | _ -> ()

        for property in List.ofSeq item do
            withoutSpans property.Value
    | :? JsonArray as items ->
        for element in List.ofSeq items do
            withoutSpans element
    | _ -> ()

let private comparable (json: string) =
    let node = JsonNode.Parse json
    withoutSpans node

    match node with
    | null -> json
    | node -> node.ToJsonString()

let private observe input =
    match
        (CorpusTests.adapter.Observe input CancellationToken.None)
            .GetAwaiter()
            .GetResult()
    with
    | Ok observation -> Ok observation
    | Error failure -> Error failure.Message

/// The round trip of one case variant, or the first difference.
let private roundTrip (case: ConformanceCase) =
    let diagnostics (observation: CompileObservation) =
        observation.Diagnostics
        |> List.map (fun key ->
            $"{key.Severity} {key.Code} {key.Uri}:{key.StartLine}:{key.StartCharacter}"
        )
        |> String.concat "; "

    match observe case.Input with
    | Error message -> Error $"The source does not compile: {message}"
    | Ok { ModelJson = None } as first -> Error $"The source gives no model: {first}"
    | Ok({ ModelJson = Some original }) ->
        match GeneratedCalls.generate case.Input.IrVersion original with
        | Error message -> Error message
        | Ok sources ->
            let input =
                { case.Input with
                    Documents = [
                        for source in sources ->
                            {
                                Uri = source.File
                                Text = source.Source
                            }
                    ]
                }

            match observe input with
            | Error message -> Error $"The generated F# does not compile: {message}"
            | Ok observation ->
                match observation.ModelJson with
                | None -> Error $"The frontend refuses the generated F#: {diagnostics observation}"
                | Some recompiled ->
                    match
                        JsonComparison.firstDifference (comparable original) (comparable recompiled)
                    with
                    | Some difference -> Error $"The recompiled IR differs: {difference}"
                    | None -> Ok()

[<Tests>]
let tests =
    testSequenced
    <| testList "IR → F# → IR round trip" [
        test "The recompiled IR equals the original IR for every successful corpus case" {
            let cases =
                CorpusTests.suite.Value.Cases
                |> List.filter (fun case ->
                    case.Expected.Success
                    && case.Exclusion.IsNone
                    && case.Input.InputKind = "files"
                )

            Expect.equal
                (cases
                 |> List.map (fun case -> case.Input.IrVersion)
                 |> List.distinct
                 |> List.sort)
                [ "1"; "2"; "3"; "4" ]
                "Every IR version is checked"

            let failures = [
                for case in cases do
                    match roundTrip case with
                    | Ok() -> ()
                    | Error difference ->
                        yield $"{case.Id} (IR {case.Input.IrVersion}): {difference}"
            ]

            if not failures.IsEmpty then
                failtestf "%d round trip(s) fail:\n%s" failures.Length (String.concat "\n" failures)
        }
    ]
