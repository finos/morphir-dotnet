// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.PipelineTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open Expecto
open Morphir.IR.Model
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Backend
open Morphir.Lang.CSharp.Emit

let private fixture (name: string) =
    File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "MorphirIR", name))

let private run (bytes: byte[]) =
    CSharpPipeline.Generate(CSharpPipelineRequest.Create(ReadOnlySpan bytes), CancellationToken.None)

let private files (result: CSharpPipelineResult) =
    result.Files |> Seq.map (fun file -> file.RelativePath, file.Text) |> List.ofSeq

let private codes (result: CSharpPipelineResult) =
    result.Diagnostics |> Seq.map (fun diagnostic -> diagnostic.Code) |> List.ofSeq

let private decodeV4 (bytes: byte[]) : VersionedModel =
    use document = JsonDocument.Parse(bytes)

    match Morphir.Json.V4.V4Json.readDistribution document.RootElement with
    | Ok decoded -> { SourceVersion = SourceFormatVersion.Create "4"; Distribution = DistributionModel.V4 decoded.Distribution }
    | Error error -> failwith error.Message

[<Tests>]
let tests =
    testList "CSharpPipeline" [
        test "A current model gives the files of the backend, in the order of their paths" {
            let bytes = fixture "daily-rate-v4.json"
            let result = run bytes
            Expect.isTrue result.Succeeded $"%A{codes result}"
            let direct = CSharpBackend.Generate(decodeV4 bytes, GenerationOptions.Default, CancellationToken.None)
            Expect.equal (files result) (direct.Sources |> List.map (fun source -> source.RelativePath, source.Text)) "the files of the backend"
            Expect.contains (files result |> List.map fst) CSharpPipeline.PolyfillPath "the polyfill file with no target framework"
        }

        test "A classic model is upgraded to the current IR and gives the files of the current model" {
            let classic = run (fixture "daily-rate-v3.json")
            let current = run (fixture "daily-rate-v4.json")
            Expect.isTrue classic.Succeeded $"%A{codes classic}"
            Expect.equal (files classic) (files current) "the classic and the current model of one package give the same files"
        }

        test "A byte order mark does not change the files" {
            let bytes = fixture "daily-rate-v4.json"
            let marked = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] bytes
            Expect.equal (files (run marked)) (files (run bytes)) "the files with and without the mark"
        }

        test "The namespace override is the namespace of the files" {
            let request =
                match NamespaceOverride.TryCreate "Pricing.Generated" with
                | Ok value -> CSharpPipelineRequest.Create(ReadOnlySpan(fixture "daily-rate-v4.json")).WithNamespace value
                | Error error -> failwith error.Message

            let result = CSharpPipeline.Generate(request, CancellationToken.None)
            Expect.isTrue result.Succeeded $"%A{codes result}"
            Expect.exists (files result) (fun (_, text) -> text.Contains "namespace Pricing.Generated") "a file in the namespace"
        }

        testList "An invalid model gives one error and no files" [
            for name, text, code, location in
                [ "not JSON", "not json", "invalid-json", "$"
                  "no format version", """{"distribution":{}}""", "unsupported-version", "$.formatVersion"
                  "an unknown format version", """{"formatVersion":"x"}""", "unsupported-version", "$.formatVersion"
                  "two format versions", """{"formatVersion":4,"formatVersion":4}""", "duplicate_format_version", "$.formatVersion"
                  "a model envelope", """{"envelopeVersion":"1"}""", "unsupported-profile", "$" ] ->
                test name {
                    let result = run (Encoding.UTF8.GetBytes(text: string))
                    Expect.isFalse result.Succeeded "the result fails"
                    Expect.isEmpty result.Files "no file"
                    Expect.equal (codes result) [ code ] "the code"
                    Expect.equal result.Diagnostics[0].Location location "the location"
                    Expect.isTrue result.Diagnostics[0].IsError "an error"
                }
        ]

        test "A model that the decoder refuses gives the rule of the decoder" {
            let result = run (Encoding.UTF8.GetBytes """{"formatVersion":4,"distribution":"not a distribution"}""")
            Expect.isFalse result.Succeeded "the result fails"
            Expect.isEmpty result.Files "no file"
            Expect.equal result.Diagnostics.Count 1 "one rule of the decoder"
            Expect.isTrue result.Diagnostics[0].IsError "an error"
            Expect.isNotEmpty result.Diagnostics[0].Message "the reason"
        }

        testList "The polyfill file is only for a framework that has no runtime types of C# 15" [
            for identifier, version, expected in
                [ ".NETCoreApp", "v10.0", true
                  ".NETCoreApp", "v8.0", true
                  ".NETStandard", "v2.0", true
                  ".NETFramework", "v4.8", true
                  ".NETCoreApp", "v11.0", false
                  ".NETCoreApp", "v12.0", false
                  "", "", true ] ->
                test $"{identifier} {version}" {
                    let request = CSharpPipelineRequest.Create(ReadOnlySpan(fixture "daily-rate-v4.json")).ForTargetFramework(identifier, version)
                    let result = CSharpPipeline.Generate(request, CancellationToken.None)
                    Expect.equal request.WritesPolyfill expected "the request"
                    Expect.equal (files result |> List.exists (fun (path, _) -> path = CSharpPipeline.PolyfillPath)) expected "the output"
                }
        ]

        test "A cancelled request throws and gives no result" {
            use source = new CancellationTokenSource()
            source.Cancel()

            Expect.throwsT<OperationCanceledException>
                (fun () -> CSharpPipeline.Generate(CSharpPipelineRequest.Create(ReadOnlySpan(fixture "daily-rate-v4.json")), source.Token) |> ignore)
                "cancellation"
        }
    ]
