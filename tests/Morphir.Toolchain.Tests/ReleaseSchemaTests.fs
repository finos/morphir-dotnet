// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// <summary>
/// The release source of morphirup against the published local-toolchains 0.1.0-preview.5 schemas pinned in
/// tests/Fixtures/LocalToolchains: a bundle with finos/morphir release URLs and a generation that records the
/// finos/morphir release are valid, and the earlier finos/morphir-dotnet spelling is refused by the schema and by
/// the reader alike.
/// </summary>
module Morphir.Toolchain.Tests.ReleaseSchemaTests

open System
open System.IO
open System.Text.Json
open Expecto
open Json.Schema
open Morphir.Toolchain

let private pins = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "LocalToolchains"))
let private family = Path.Combine(pins, "0.1.0-preview.5")

// The schemas whose $defs the bundle and generation schemas reference, pinned beside them, each after the schemas
// that it references. Nothing is fetched.
let private references =
    [ "extensions/0.1.0-preview.1/claims"; "extensions/0.1.0-preview.1/release"; "toolchains/0.1.0-preview.1/receipt" ]
    |> List.map (fun name -> Path.Combine(pins, "references", $"{name}.schema.json"))

let private schema name =
    let file = Path.Combine(family, $"{name}.schema.json")
    file, lazy (
        let options = BuildOptions(SchemaRegistry = SchemaRegistry(Fetch = fun uri _ -> failwith $"The schema {uri} is not pinned."))
        for reference in references do JsonSchema.FromText(SchemaTests.endAnchored (File.ReadAllText reference), options) |> ignore
        JsonSchema.FromText(SchemaTests.endAnchored (File.ReadAllText file), options))

let private bundleFile, private bundleSchema = schema "bundle"
let private generationFile, private generationSchema = schema "generation"

/// The first failure of <paramref name="bytes"/> against <paramref name="schema"/>, or None when they are valid.
let private failure (schema: Lazy<JsonSchema>) (bytes: byte array) =
    use document = JsonDocument.Parse bytes
    let results = schema.Value.Evaluate(document.RootElement, EvaluationOptions(OutputFormat = OutputFormat.List))
    if results.IsValid then None
    else
        (match results.Details with null -> [] | details -> List.ofSeq details)
        |> List.tryPick (fun detail ->
            match detail.Errors with
            | NonNull errors when not detail.IsValid && errors.Count > 0 -> Some $"{detail.InstanceLocation}: {Seq.head errors.Values}"
            | _ -> None)
        |> Option.orElse (Some "invalid")

let private parsed = function Ok value -> value | Error error -> failtestf "Bad unit input: %A" error

let private bundleFrom (repository: string) =
    let platform = "win-x64"
    let asset role =
        let name =
            match role with
            | "host-candidate" -> $"morphir-host-candidate-{platform}.json"
            | "selection" -> $"morphir-extensions-{platform}.json"
            | _ -> $"{role}-{platform}.zip"
        Value.Object [
            "name", Value.String name
            "url", Value.String $"{repository}/releases/download/v0.5.0-alpha.1/{name}"
            "bytes", Value.Number "3"
            "sha256", Value.String(String('a', 64))
            "role", Value.String role ]
    Value.Object [
        "$schema", Value.String(ReleaseBundleDocument.schema "bundle")
        "version", Value.String "0.5.0-alpha.1"
        "sourceCommit", Value.String(String('b', 40))
        "platform", Value.String platform
        "assets", Value.Array(List.map asset ReleaseBundleDocument.roles) ]
    |> JsonText.toBytes

let private generationFrom (repository: string) =
    let candidate = LocalCandidateTests.preview5Candidate "release-checksum" |> LocalComposedDocument.readReleaseCandidate |> parsed
    let proof = Value.Object [
        "repository", Value.String repository; "tag", Value.String "v0.5.0-alpha.1"
        "bundle", Value.Object [ "path", Value.String "acquisition/bundle.json"; "sha256", Value.String(String('c', 64)) ]
        "checksums", Value.Object [ "path", Value.String "acquisition/SHA256SUMS"; "sha256", Value.String(String('d', 64)) ] ]
    candidate,
    ComposedDocuments.writeManifest ReleaseBundleDocument.family candidate [||]
        [ candidate.FrontendId, [||]; candidate.BackendId, [||] ] [] (LocalCandidateTests.releaseWorkflow candidate) (Some proof)

// Reads the pinned schema files of tests/Fixtures/LocalToolchains, so it is PlatformSpecific (docs/contributing/ci-os-matrix.md).
[<Tests>]
let tests =
    testList "ReleaseSchema" [
        test "the pinned bundle and generation schemas have the bytes of finos/morphir" {
            Expect.equal (Sha256.ofBytes(File.ReadAllBytes bundleFile)).Text "406835b6035f523565a6188a763d09d71a77625d1a06a8cfa9e2570cead1663a" "PROVENANCE.md"
            Expect.equal (Sha256.ofBytes(File.ReadAllBytes generationFile)).Text "8ea6e4d6e49cf22413e2b9a9994f2c6aba9004b4ea3f5b59f7216b3f2a56828c" "PROVENANCE.md"
            let digests = references |> List.map (fun file -> (Sha256.ofBytes(File.ReadAllBytes file)).Text)
            Expect.equal digests [
                "9cc87748441e727c766a9a080f3160dbdb1b576e25f14455e199a0f47e07d6ff"
                "e9be40a101ba745689bd7bf769f07ec19f1d19e48703538f0840ff5d4afcdd5a"
                "935a0ccc903dfd6bdff097f3a37262779b19187a1be47a3d254e50eca8909e6a" ] "PROVENANCE.md"
        }
        test "a bundle with finos/morphir release URLs is valid and is read" {
            let raw = bundleFrom ReleaseBundleDocument.repository
            Expect.equal (failure bundleSchema raw) None "the bundle schema accepts finos/morphir"
            Expect.isOk (ReleaseBundleDocument.read raw) "the reader accepts finos/morphir"
        }
        test "a bundle with finos/morphir-dotnet release URLs is refused by the schema and by the reader" {
            let raw = bundleFrom "https://github.com/finos/morphir-dotnet"
            Expect.isSome (failure bundleSchema raw) "the bundle schema refuses the earlier source"
            Expect.isError (ReleaseBundleDocument.read raw) "the reader refuses the earlier source"
        }
        test "a generation that records the finos/morphir release is valid and is read" {
            let candidate, raw = generationFrom "https://github.com/finos/morphir"
            Expect.equal (failure generationSchema raw) None "the generation schema accepts finos/morphir"
            Expect.isOk (ComposedDocuments.readManifest ReleaseBundleDocument.family candidate raw) "the reader accepts finos/morphir"
        }
        test "a generation that records a finos/morphir-dotnet release is refused by the schema and by the reader" {
            let candidate, raw = generationFrom "https://github.com/finos/morphir-dotnet"
            Expect.isSome (failure generationSchema raw) "the generation schema refuses the earlier source"
            Expect.isError (ComposedDocuments.readManifest ReleaseBundleDocument.family candidate raw) "the reader refuses the earlier source"
        }
    ]
    |> testLabel "PlatformSpecific"
