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
let private parentCommit = "65bbbc961d9eb962ad941a859c9d09fba058f5a0"

let private releaseSchema =
    "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json"

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
                11
                "seven schemas, two case files and two package fixtures, no fixture execution claimed"

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

            Expect.equal (cases ()).Length 158 "identity corpus inventory, not executed-case count"

            use packages =
                JsonDocument.Parse(
                    File.ReadAllBytes(Path.Combine(parentRoot, "package-cases.json"))
                )

            Expect.equal
                (packages.RootElement.GetProperty("cases").GetArrayLength())
                72
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
                "https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/release.schema.json"
                "https://morphir.finos.org/schemas/extensions/0.1.0-preview.2/release.schema.json"
                "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/claims.schema.json"
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
                        "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/claims.schema.json"
            )
            |> Descriptors.read
            |> refused "extension.unsupported_contract_version" "claims.$schema"
        }
        // packages.md: "A descriptor may offer more than one runtime" (finos/morphir#1176).
        test "a descriptor that mixes process and wasm artifacts is accepted" {
            edit (fun node ->
                let wasm = (artifact node).DeepClone()
                wasm["runtime"] <- JsonValue.Create "wasm"

                wasm.AsObject().Remove "platform"
                |> ignore

                (child "artifacts" node).AsArray().Add wasm
            )
            |> Descriptors.read
            |> accepted
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
    |> testLabel "PlatformSpecific"

let private claimsSchema =
    "https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/claims.schema.json"

let private standaloneNode () =
    let release =
        JsonNode.Parse(published ())
        |> nonNull

    let claims =
        (release
         |> artifact
         |> child "claims")
            .DeepClone()

    claims["$schema"] <- JsonValue.Create claimsSchema
    claims

let private editClaims change =
    let node = standaloneNode ()
    change node

    node.ToJsonString()
    |> bytes

let private readClaims input =
    match Claims.readStandalone input with
    | Ok claims -> claims
    | Error error -> failtest (error.ToString())

[<Tests>]
let claimTests =
    testList "Claims" [
        for name in [ "nested-members-claims"; "current-protocol-claims" ] do
            testCase
                $"parent standalone case {name}"
                (fun () ->
                    let input = (namedCase name).GetProperty("input").GetProperty("document")

                    input.GetRawText()
                    |> bytes
                    |> Claims.readStandalone
                    |> accepted
                )
        test "current standalone claims project existing MEP identity and protocols" {
            let parsed =
                (standaloneNode ()).ToJsonString()
                |> bytes
                |> readClaims

            Expect.equal parsed.Extension.Id "morphir-elm" "existing MEP identity record"

            Expect.equal
                parsed.ProtocolVersions.Head.Value
                "0.2.0-draft.1"
                "existing protocol value"
        }
        test "a missing standalone schema is not converted from claimsVersion" {
            editClaims (fun node ->
                node.AsObject().Remove "$schema"
                |> ignore

                node["claimsVersion"] <- JsonValue.Create "0.1.0-draft.2"
            )
            |> Claims.readStandalone
            |> refused "extension.unsupported_contract_version" claimsSchema
        }
        for oldName in [ "claimsVersion"; "statementVersion" ] do
            testCase
                $"a current schema does not authorize legacy {oldName}"
                (fun () ->
                    editClaims (fun node -> node[oldName] <- JsonValue.Create "0.1.0-draft.2")
                    |> Claims.readStandalone
                    |> refused "extension.unsupported_contract_version" oldName
                )
        test "a different prerelease schema is refused exactly" {
            editClaims (fun node ->
                node["$schema"] <- JsonValue.Create(claimsSchema.Replace("preview.3", "preview.2"))
            )
            |> Claims.readStandalone
            |> refused "extension.unsupported_contract_version" claimsSchema
        }
        test "comparison ignores member order and optional unknown members recursively" {
            let first =
                (standaloneNode ()).ToJsonString()
                |> bytes
                |> readClaims

            let other =
                editClaims (fun node ->
                    node["future"] <- JsonValue.Create 7

                    let language =
                        (node
                         |> child "capabilities"
                         |> child "frontend"
                         |> child "languages")[0]
                        |> nonNull

                    language["future"] <- JsonValue.Create "ignored"
                    let identity = child "extension" node
                    let name = identity["name"]

                    identity.AsObject().Remove "name"
                    |> ignore

                    identity["name"] <- name
                )
                |> readClaims

            Expect.isTrue
                (Claims.equivalent first other)
                "projection compares understood values only"
        }
        test "comparison preserves array order" {
            let left =
                editClaims (fun node ->
                    node["protocolVersions"] <- JsonNode.Parse("[\"0.2.0-draft.1\",\"0.2.0\"]")
                )
                |> readClaims

            let right =
                editClaims (fun node ->
                    node["protocolVersions"] <- JsonNode.Parse("[\"0.2.0\",\"0.2.0-draft.1\"]")
                )
                |> readClaims

            Expect.isFalse (Claims.equivalent left right) "array order is significant"
        }
        test "absent critical equals an empty critical array" {
            let left =
                (standaloneNode ()).ToJsonString()
                |> bytes
                |> readClaims

            let right =
                editClaims (fun node -> node["critical"] <- JsonNode.Parse "[]")
                |> readClaims

            Expect.isTrue (Claims.equivalent left right) "explicit contract default"
        }
        test "unknown critical nested metadata is refused" {
            editClaims (fun node ->
                let language =
                    (node
                     |> child "capabilities"
                     |> child "frontend"
                     |> child "languages")[0]
                    |> nonNull

                language["future"] <- JsonValue.Create true
                node["critical"] <- JsonNode.Parse "[\"capabilities.frontend.languages.future\"]"
            )
            |> Claims.readStandalone
            |> refused
                "extension.unsupported_critical_member"
                "capabilities.frontend.languages.future"
        }
        for field in [ "incremental"; "fragments"; "multiDocument" ] do
            testCase
                $"frontend optional boolean {field} rejects null"
                (fun () ->
                    editClaims (fun node ->
                        (node
                         |> child "capabilities"
                         |> child "frontend")[field] <- null
                    )
                    |> Claims.readStandalone
                    |> refused "extension.invalid_document" $"frontend.{field}"
                )
        for suffixes in [ "[]"; "[\"elm\"]"; "[\" .elm\"]"; "[\".elm\",\".elm\"]" ] do
            testCase
                $"language suffix constraints {suffixes}"
                (fun () ->
                    editClaims (fun node ->
                        let language =
                            (node
                             |> child "capabilities"
                             |> child "frontend"
                             |> child "languages")[0]
                            |> nonNull

                        language["fileExtensions"] <- JsonNode.Parse suffixes
                    )
                    |> Claims.readStandalone
                    |> refused "extension.invalid_document" "fileExtensions"
                )
        test "undeclared but known capability metadata is still type checked" {
            editClaims (fun node ->
                (child "capabilities" node)["workspace"] <-
                    JsonNode.Parse "{\"protocolVersions\":[\"0.1.0-draft.1\"],\"discover\":null}"
            )
            |> Claims.readStandalone
            |> refused "extension.invalid_document" "workspace.discover"
        }
        test "model transport limits remain positive integers" {
            let input =
                (namedCase "nested-members-claims").GetProperty("input").GetProperty("document")

            let node =
                JsonNode.Parse(input.GetRawText())
                |> nonNull

            (node
             |> child "capabilities"
             |> child "modelTransport"
             |> child "limits")["maxDepth"] <- JsonValue.Create 0

            node.ToJsonString()
            |> bytes
            |> Claims.readStandalone
            |> refused "extension.invalid_document" "maxDepth"
        }
        test "requires host is checked using the established comparator values" {
            let input =
                (namedCase "current-protocol-claims")
                    .GetProperty("input")
                    .GetProperty("document")
                    .GetRawText()
                |> bytes

            let parsed = readClaims input

            let host value =
                match SemVer.tryParse value with
                | Ok version -> version
                | Error _ -> failtest "Invalid test literal"

            Expect.isTrue
                (Claims.supportsHost (host "0.4.0-alpha.7") parsed)
                "exact prerelease comparator admitted"

            Expect.isTrue (Claims.supportsHost (host "0.4.1") parsed) "later final version"
            Expect.isFalse (Claims.supportsHost (host "0.4.0-alpha.6") parsed) "earlier prerelease"
        }
        for requirement in [ "[]"; "[\"^0.4.0\"]"; "[\">=0.4.0 <1.0.0\"]"; "[\">=0.4\"]"; "null" ] do
            testCase
                $"requires host single-comparator grammar {requirement}"
                (fun () ->
                    editClaims (fun node ->
                        let requirements = JsonObject()
                        requirements["host"] <- JsonNode.Parse requirement
                        node["requires"] <- requirements
                        node["critical"] <- JsonNode.Parse "[\"requires.host\"]"
                    )
                    |> Claims.readStandalone
                    |> refused "extension.invalid_document" "requires.host"
                )
        test "requires host must be named critical" {
            editClaims (fun node -> node["requires"] <- JsonNode.Parse "{\"host\":[\">=0.4.0\"]}")
            |> Claims.readStandalone
            |> refused "extension.invalid_document" "critical"
        }
    ]
    |> testLabel "PlatformSpecific"
