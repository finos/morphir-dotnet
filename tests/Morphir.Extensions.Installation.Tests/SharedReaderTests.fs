// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.SharedReaderTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Extensions.Installation
open Morphir.Toolchain

let private parentRoot = Path.Combine(AppContext.BaseDirectory, "ParentContracts")
let private parentCommit = "0439c4c06d8ca6d4a46e41afb226ba0872f9bff8"

let private releaseSchema =
    "https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/release.schema.json"

let private bytes (text: string) = Encoding.UTF8.GetBytes text

let private cases () =
    use document =
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(parentRoot, "identity-cases.json")))

    document.RootElement.GetProperty("cases").EnumerateArray()
    |> Seq.map (fun item -> item.Clone())
    |> Seq.toArray

let private namedCase name =
    cases ()
    |> Array.find (fun item -> item.GetProperty("id").GetString() = name)

let private published () =
    (namedCase "published-descriptor")
        .GetProperty("input")
        .GetProperty("document")
        .GetRawText()

let private edit (change: JsonNode -> unit) =
    let node =
        JsonNode.Parse(published ())
        |> nonNull

    change node

    node.ToJsonString()
    |> bytes

let private child (name: string) (node: JsonNode) =
    node[name]
    |> nonNull

let private artifact node =
    (child "artifacts" node)[0]
    |> nonNull

let private accepted result =
    match result with
    | Ok _ -> ()
    | Error error -> failtestf "Expected acceptance: %s" (error.ToString())

let private refused code memberPath result =
    match result with
    | Ok _ -> failtest "Expected refusal"
    | Error(failure: InstallationFailure) ->
        Expect.equal failure.Code code "stable diagnostic"
        Expect.stringContains failure.Message memberPath "refused member or supported URL"

[<Tests>]
let tests =
    testList "SharedReaders" [
        test "all copied parent blobs have exact offline provenance" {
            use manifest =
                JsonDocument.Parse(File.ReadAllBytes(Path.Combine(parentRoot, "manifest.json")))

            let files =
                manifest.RootElement.GetProperty("files").EnumerateArray()
                |> Seq.toArray

            Expect.equal
                files.Length
                9
                "seven schemas and two case files, no fixture execution claimed"

            for file in files do
                Expect.equal
                    (file.GetProperty("repository").GetString())
                    "https://github.com/finos/morphir"
                    "authority"

                Expect.equal
                    (file.GetProperty("commit").GetString())
                    parentCommit
                    "immutable full commit"

                let relative =
                    file.GetProperty("file").GetString()
                    |> nonNull

                let contents = File.ReadAllBytes(Path.Combine(parentRoot, relative))

                Expect.equal
                    (Digest.ofBytes contents)
                    (file.GetProperty("sha256").GetString())
                    relative

                Expect.isNonEmpty
                    (file.GetProperty("path").GetString()
                     |> nonNull)
                    "source path"

            Expect.equal (cases ()).Length 137 "identity corpus inventory, not executed-case count"

            use packages =
                JsonDocument.Parse(
                    File.ReadAllBytes(Path.Combine(parentRoot, "package-cases.json"))
                )

            Expect.equal
                (packages.RootElement.GetProperty("cases").GetArrayLength())
                66
                "package inventory, not execution"
        }
        // Only these six parent cases exercise the release reader in this tranche.
        for name in
            [
                "published-descriptor"
                "nested-open-package"
                "nested-critical-package"
                "descriptor-claims"
                "earlier-dotted"
                "earlier-single"
            ] do
            testCase
                $"parent release case {name}"
                (fun () ->
                    let case = namedCase name
                    let input = case.GetProperty("input")
                    let expected = case.GetProperty("expected")

                    let result =
                        Descriptors.read (
                            input.GetProperty("document").GetRawText()
                            |> bytes
                        )

                    if expected.GetProperty("outcome").GetString() = "accept" then
                        accepted result
                    else
                        match result with
                        | Ok _ -> failtest $"Parent case {name} requires refusal"
                        | Error error ->
                            match expected.GetProperty("rule").GetString() with
                            | "schema" ->
                                Expect.equal
                                    error.Code
                                    "extension.unsupported_contract_version"
                                    "parent schema refusal"
                            | "critical" ->
                                Expect.equal
                                    error.Code
                                    "extension.unsupported_critical_member"
                                    "parent critical refusal"

                                Expect.stringContains
                                    error.Message
                                    (expected.GetProperty("member").GetString()
                                     |> nonNull)
                                    "parent member"
                            | "identity-equality" ->
                                Expect.stringContains
                                    error.Message
                                    "claims.extension.id"
                                    "binding path"
                            | rule ->
                                failtest
                                    $"Unsupported parent rule {rule}; do not count it as passed"
                )
        for name in [ "noninteger-json"; "duplicate-json-key" ] do
            testCase
                $"parent I-JSON case {name}"
                (fun () ->
                    let input = (namedCase name).GetProperty("input")

                    let file =
                        input.GetProperty("file").GetString()
                        |> nonNull

                    let inputBytes =
                        input.GetProperty("text").GetString()
                        |> nonNull
                        |> bytes

                    SharedJson.read
                        "release.json"
                        (published ()
                         |> bytes)
                    |> accepted

                    SharedJson.read file inputBytes
                    |> refused "extension.invalid_document" file
                )
        test "published release projects validated identity and exact version" {
            match
                Descriptors.read (
                    published ()
                    |> bytes
                )
            with
            | Error error -> failtest (error.ToString())
            | Ok release ->
                Expect.equal release.ExtensionId.Value "morphir-elm" "existing extension id type"
                Expect.equal release.Version.Text "0.6.0" "existing Toolchain SemVer"
        }
        for schema in
            [
                "https://morphir.finos.org/schemas/extensions/0.1.0-preview.2/release.schema.json"
                "https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/claims.schema.json"
                ""
            ] do
            testCase
                $"refuses exact schema mismatch {schema}"
                (fun () ->
                    edit (fun node -> node["$schema"] <- JsonValue.Create schema)
                    |> Descriptors.read
                    |> refused "extension.unsupported_contract_version" releaseSchema
                )
        test "an inherited old version is never converted" {
            edit (fun node ->
                node.AsObject().Remove "$schema"
                |> ignore

                node["schemaVersion"] <- JsonValue.Create "2.0.0-draft.2"
            )
            |> Descriptors.read
            |> refused "extension.unsupported_contract_version" releaseSchema
        }
        test "fractional unknown metadata is still not I-JSON in this family" {
            let text =
                (published ()).TrimEnd().TrimEnd('}')
                + ",\"future\":1.0}"

            Descriptors.read (bytes text)
            |> refused "extension.invalid_document" "future"
        }
        test "unknown optional metadata stays open recursively" {
            edit (fun node ->
                let extension =
                    node
                    |> artifact
                    |> child "claims"
                    |> child "extension"

                extension["future"] <- JsonValue.Create true
            )
            |> Descriptors.read
            |> accepted
        }
        test "an embedded claim set cannot carry its own schema URL" {
            edit (fun node ->
                let claims =
                    node
                    |> artifact
                    |> child "claims"

                claims["$schema"] <-
                    JsonValue.Create
                        "https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/claims.schema.json"
            )
            |> Descriptors.read
            |> refused "extension.unsupported_contract_version" "claims.$schema"
        }
        test "runtime mixing is refused" {
            edit (fun node ->
                let wasm = (artifact node).DeepClone()
                wasm["runtime"] <- JsonValue.Create "wasm"

                wasm.AsObject().Remove "platform"
                |> ignore

                (child "artifacts" node).AsArray().Add wasm
            )
            |> Descriptors.read
            |> refused "extension.invalid_descriptor" "runtime"
        }
        test "platformDifferences none compares artifact claim values" {
            edit (fun node ->
                let other = (artifact node).DeepClone()
                other["platform"] <- JsonValue.Create "aarch64-unknown-linux-gnu"

                let frontend =
                    other
                    |> child "claims"
                    |> child "capabilities"
                    |> child "frontend"

                frontend["compile"] <- JsonValue.Create false
                (child "artifacts" node).AsArray().Add other
            )
            |> Descriptors.read
            |> refused "extension.claims_mismatch" "capabilities.frontend.compile"
        }
    ]
