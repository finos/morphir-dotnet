// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The pinned contract files are byte-identical to the finos/morphir commit recorded in PROVENANCE.md.
module Morphir.Toolchain.Tests.ProvenanceTests

open System
open System.IO
open System.Security.Cryptography
open System.Text.RegularExpressions
open Expecto

let private provenance () =
    File.ReadAllText(Path.Combine(Fixtures.root, "PROVENANCE.md"))

let private rows (text: string) =
    Regex.Matches(text, @"(?m)^\| `([^`]+)` \| `([^`]+)` \| `([0-9a-f]{64})` \|\r?$")
    |> Seq.map (fun row -> row.Groups[1].Value, (row.Groups[2].Value, row.Groups[3].Value))
    |> Seq.toList

let private pinnedFiles () =
    Directory.GetFiles(Fixtures.root, "*", SearchOption.AllDirectories)
    |> Array.map (fun file -> Path.GetRelativePath(Fixtures.root, file).Replace('\\', '/'))
    |> Array.filter ((<>) "PROVENANCE.md")
    |> Set.ofArray

[<Tests>]
let tests =
    testList "Pinned toolchain contract" [
        testCase "PROVENANCE.md names the repository and a full commit" (fun () ->
            let text = provenance ()
            Expect.stringContains text "Repository: https://github.com/finos/morphir" "the source repository is recorded"
            Expect.isTrue (Regex.IsMatch(text, @"Commit: `[0-9a-f]{40}`")) "the full commit is recorded"
            Expect.stringContains text "pull request" "the pin says whether its commit is merged")

        testCase "every pinned file has exactly one provenance row" (fun () ->
            let listed = rows (provenance ()) |> List.map fst
            Expect.equal (List.length listed) (List.distinct listed |> List.length) "no file is listed twice"
            Expect.equal (Set.ofList listed) (pinnedFiles ()) "the rows and the files are the same set")

        testCase "every pinned file has its recorded SHA-256" (fun () ->
            for local, (_, expected) in rows (provenance ()) do
                let actual =
                    File.ReadAllBytes(Path.Combine(Fixtures.root, local))
                    |> SHA256.HashData
                    |> Convert.ToHexStringLower

                Expect.equal actual expected $"pinned bytes must not change: {local}")

        testCase "the pin covers the manifests, schemas and the readiness workflow case" (fun () ->
            let upstream = rows (provenance ()) |> List.map (snd >> fst) |> Set.ofList

            for path in
                [ "docs/spec/toolchain/fixtures/0.1.0-draft.1/cases.json"
                  "docs/spec/toolchain/fixtures/0.1.0-draft.1/signed/cases.json"
                  "docs/spec/toolchain/fixtures/0.1.0-draft.1/signed/repository.json"
                  "website/static/schemas/toolset-recipe-0.1.0-draft.1.schema.json"
                  "website/static/schemas/toolchain-request-0.1.0-draft.1.schema.json"
                  "website/static/schemas/toolchain-lock-0.1.0-draft.1.schema.json"
                  "website/static/schemas/toolchain-receipt-0.1.0-draft.1.schema.json"
                  "website/static/schemas/morphirup-result-0.1.0-draft.1.schema.json"
                  "spec/toolchain/workflows/fsharp-compile-generate/case.json" ] do
                Expect.contains upstream path $"{path} is pinned")

        testCase "CRLF and LF manifests list the same rows" (fun () ->
            let text = provenance().Replace("\r\n", "\n")
            Expect.equal (rows (text.Replace("\n", "\r\n"))) (rows text) "checkout newlines do not change the table")

        testCase "the readiness workflow files have the digests its case manifest states" (fun () ->
            use case = Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures.workflow, "case.json")))

            let listed =
                [ for source in case.RootElement.GetProperty("sources").EnumerateArray() do
                      nonNull (source.GetProperty("file").GetString()), nonNull (source.GetProperty("sha256").GetString())
                  for step in case.RootElement.GetProperty("steps").EnumerateArray() do
                      match step.GetProperty("expected").TryGetProperty("ir") with
                      | true, ir -> nonNull (ir.GetProperty("file").GetString()), nonNull (ir.GetProperty("sha256").GetString())
                      | false, _ -> () ]

            Expect.isNonEmpty listed "the case lists files"

            for file, expected in listed do
                let actual = Fixtures.bytesIn Fixtures.workflow file |> SHA256.HashData |> Convert.ToHexStringLower
                Expect.equal actual expected $"{file} has its stated SHA-256")
    ]
