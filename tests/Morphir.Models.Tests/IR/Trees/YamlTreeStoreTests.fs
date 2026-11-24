/// Whole-tree YAML and multi-profile reading on <see cref="DocumentTreeStore"/>: reads v3.1 and v4 JSON and YAML
/// document trees (plain and contextual), enforces file/byte/depth/node/cancellation budgets, refuses mixed or
/// ambiguous manifests, and provides single-file YAML decoding via <see cref="TreeYaml"/>.
module Morphir.IR.Tests.Trees.YamlTreeStoreTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open Expecto
open Morphir.Files
open Morphir.IR
open Morphir.IR.Model
open Morphir.IR.Trees
open Morphir.TestSupport.Fixtures
open Morphir.IR.Tests.Trees.StoreTestKit

module Orders = Morphir.Models.TestSupport.DocumentTreeMother.BoundedOrders
module YamlDocs = Morphir.Models.TestSupport.DocumentTreeMother.YamlDocuments
module Driver = Morphir.Models.TestSupport.DocumentTreeStoreDriver

let private catalogTree version profile : VFileTree =
    Orders.catalogDocuments version profile
    |> Map.map (fun location fixture -> VFile.ofBytes location (ReadOnlyMemory(FixtureCatalog.ReadBytes fixture)))
    |> VFileTree.fromFileMap

let private readOk options files =
    match DocumentTreeStore.readAnyProfile options files with
    | Ok result -> result
    | Result.Error errors -> failtestf "Expected tree to read, got %A" errors

let private tryReadModelOk options files =
    let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
    let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<StoreError>>

    if DocumentTreeStore.tryReadModel (options, files, CancellationToken.None, &loaded, &errors) then
        loaded
    else
        failtestf "Expected tryReadModel to succeed, got %A" (List.ofSeq errors)

let private tryReadModelErrors options files =
    let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
    let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<StoreError>>

    if DocumentTreeStore.tryReadModel (options, files, CancellationToken.None, &loaded, &errors) then
        failtest "Expected tryReadModel to fail"
    else
        List.ofSeq errors

[<Tests>]
let tests =
    testList "Store" [
        testList "Multi-profile and YAML trees" [
            test "Reads v4 plain and contextual YAML trees equivalently to plain JSON" {
                let jsonTree, jsonReport =
                    readOk TreeReadOptions.Defaults (catalogTree "v4" "plain-json")

                Expect.isEmpty jsonReport.CompactedFiles "Plain JSON has no compacted files"

                for profile, expectCompacted in [ "plain-yaml", false; "contextual-yaml", true; "contextual-json", true ] do
                    let tree, report = readOk TreeReadOptions.Defaults (catalogTree "v4" profile)
                    Expect.equal tree jsonTree $"v4 {profile} matches v4 plain-json"

                    if expectCompacted then
                        Expect.isNonEmpty report.CompactedFiles $"v4 {profile} reports compacted files"
                    else
                        Expect.isEmpty report.CompactedFiles $"v4 {profile} has no compacted files"
            }

            test "Reads v3.1 plain and contextual YAML trees equivalently to plain JSON" {
                let expected =
                    match readOk TreeReadOptions.Defaults (catalogTree "v3.1" "plain-json") with
                    | ClassicTree classic, _ ->
                        match ClassicDocumentTree.join classic with
                        | Ok dist -> dist
                        | Result.Error errors -> failtestf "Expected v3.1 join to succeed: %A" errors
                    | _ -> failtest "Expected ClassicTree"

                for profile, expectCompacted in [ "plain-yaml", false; "contextual-yaml", true; "contextual-json", true ] do
                    let tree, report = readOk TreeReadOptions.Defaults (catalogTree "v3.1" profile)

                    match tree with
                    | ClassicTree classic ->
                        match ClassicDocumentTree.join classic with
                        | Ok actual ->
                            Expect.equal
                                (Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes
                                    Morphir.Json.Classic.ClassicWireProfile.v3
                                    false
                                    actual)
                                (Morphir.Json.Classic.ClassicJsonWriter.writeJsonAttributes
                                    Morphir.Json.Classic.ClassicWireProfile.v3
                                    false
                                    expected)
                                $"v3.1 {profile} joins identically to v3.1 plain-json"
                        | Result.Error errors -> failtestf "Expected join for v3.1 %s: %A" profile errors
                    | _ -> failtest $"Expected ClassicTree for v3.1 {profile}"

                    if expectCompacted then
                        Expect.isNonEmpty report.CompactedFiles $"v3.1 {profile} reports compacted files"
                    else
                        Expect.isEmpty report.CompactedFiles $"v3.1 {profile} has no compacted files"
            }

            test "TryReadModel returns VersionedModel with exact declared formatVersion and profile" {
                let v4Yaml = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v4" "plain-yaml")
                Expect.equal v4Yaml.Profile TreeProfile.Yaml "YAML profile"
                Expect.equal v4Yaml.Model.Family ModelFamily.V4 "v4 family"
                Expect.equal v4Yaml.Model.Kind DistributionKind.Library "Library kind"
                Expect.equal v4Yaml.Model.SourceVersion.Text "4" "Exact v4 declared formatVersion"

                let v3Yaml = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v3.1" "contextual-yaml")
                Expect.equal v3Yaml.Profile TreeProfile.Yaml "YAML profile"
                Expect.equal v3Yaml.Model.Family ModelFamily.Classic "Classic family"
                Expect.equal v3Yaml.Model.SourceVersion.Text "3.1.0" "Exact v3.1.0 declared formatVersion"

                let v4Json = tryReadModelOk TreeReadOptions.Defaults (catalogTree "v4" "plain-json")
                Expect.equal v4Json.Profile TreeProfile.Json "JSON profile"
                Expect.equal v4Json.Model.SourceVersion.Text "4" "Exact v4 JSON declared formatVersion"

                // Explicit string "4.0.0" in manifest is preserved verbatim.
                let files400 =
                    catalogTree "v4" "plain-yaml"
                    |> Driver.withText "manifest.yaml" (fun text -> text.Replace("formatVersion: 4", "formatVersion: \"4.0.0\""))

                let v400 = tryReadModelOk TreeReadOptions.Defaults files400
                Expect.equal v400.Model.SourceVersion.Text "4.0.0" "Preserves exact 4.0.0 string"
            }

            test "DocumentTreeStore.read keeps refusing YAML trees and preserves ignoring unrelated manifest.ion" {
                let errors = DocumentTreeStore.read ReadOptions.defaults (catalogTree "v4" "plain-yaml") |> refusal
                Expect.isNonEmpty errors "Legacy read refuses YAML"

                for err in errors do
                    Expect.equal err.Code "tree.unsupported_profile" "Legacy read refuses YAML files with tree.unsupported_profile"

                // Legacy DocumentTreeStore.read ignores non-JSON/YAML files including manifest.ion beside a JSON tree.
                let jsonWithIon =
                    catalogTree "v4" "plain-json"
                    |> Driver.withFile "manifest.ion" "morphir::{}"

                match DocumentTreeStore.read ReadOptions.defaults jsonWithIon with
                | Ok _ -> ()
                | Result.Error legacyErrors ->
                    failtestf "Expected legacy DocumentTreeStore.read to ignore manifest.ion, got %A" legacyErrors

                // Multi-profile TryReadModel / readAnyProfile refuses manifest.ion beside a JSON tree as ambiguous.
                let multiErrors = tryReadModelErrors TreeReadOptions.Defaults jsonWithIon
                Expect.equal (codesAndFiles multiErrors) [ "tree.ambiguous_profile", None ] "Multi-profile rejects json + ion"
            }

            test "Accepts .yml files in YAML trees and ignores package.yaml with a notice" {
                let renamed =
                    catalogTree "v4" "plain-yaml"
                    |> Driver.texts
                    |> Map.toList
                    |> List.map (fun (path, text) ->
                        if path.EndsWith(".yaml", StringComparison.Ordinal) then
                            path.Substring(0, path.Length - 5) + ".yml", text
                        else
                            path, text
                    )
                    |> Driver.ofTexts
                    |> Driver.withFile "pkg/acme/orders/package.yaml" "formatVersion: 4\nversion: 1.0.0\n"

                let _, report = readOk TreeReadOptions.Defaults renamed

                Expect.contains
                    report.Notices
                    (TreeNotice.IgnoredFile "pkg/acme/orders/package.yaml")
                    "package.yaml is ignored with a notice"
            }

            test "Refuses .yaml and .yml physical ambiguity for the same member before opening either file" {
                let mutable opened = false
                let yamlTree = catalogTree "v4" "plain-yaml"
                let yamlPath = "pkg/acme/orders/domain/order.value.yaml"
                let ymlPath = "pkg/acme/orders/domain/order.value.yml"

                let guardedFile path =
                    VFile.ofStream
                        path
                        (StreamSource(fun () ->
                            opened <- true
                            failwith $"Should not open {path} when ambiguous siblings exist."
                        ))

                let ambiguousTree =
                    VFileTree.toFileMap yamlTree
                    |> Map.add yamlPath (guardedFile yamlPath)
                    |> Map.add ymlPath (guardedFile ymlPath)
                    |> VFileTree.fromFileMap

                let errors = tryReadModelErrors TreeReadOptions.Defaults ambiguousTree
                Expect.isFalse opened "Neither ambiguous sibling file was opened"

                let err = List.exactlyOne errors
                Expect.equal err.Code "tree.ambiguous_profile" "Physical ambiguity code"
                Expect.stringContains err.Message yamlPath "Names .yaml sibling"
                Expect.stringContains err.Message ymlPath "Names .yml sibling"
            }

            test "Refuses ambiguous manifests across JSON, YAML, and Ion" {
                let baseYaml = catalogTree "v4" "plain-yaml"
                let baseJson = catalogTree "v4" "plain-json"

                for name, files in
                    [
                        "json + yaml", baseJson |> Driver.withFile "manifest.yaml" "formatVersion: 4\n"
                        "json + ion", baseJson |> Driver.withFile "manifest.ion" "morphir::{}"
                        "yaml + ion", baseYaml |> Driver.withFile "manifest.ion" "morphir::{}"
                        "yaml + yml", baseYaml |> Driver.withFile "manifest.yml" "formatVersion: 4\n"
                    ] do
                    let errors =
                        DocumentTreeStore.readAnyProfile TreeReadOptions.Defaults files |> refusal

                    Expect.equal
                        (codesAndFiles errors)
                        [ "tree.ambiguous_profile", None ]
                        $"Ambiguous manifests ({name}) fail with tree.ambiguous_profile"
            }

            test "Refuses profile selection mismatches and mixed profile files" {
                let yamlTree = catalogTree "v4" "plain-yaml"
                let jsonTree = catalogTree "v4" "plain-json"

                let yamlAsJson =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithProfiles TreeProfileSelection.JsonOnly) yamlTree
                    |> refusal

                Expect.isNonEmpty yamlAsJson "JsonOnly refuses YAML tree"
                Expect.all yamlAsJson (fun e -> e.Code = "tree.unsupported_profile") "All errors are tree.unsupported_profile"

                let jsonAsYaml =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithProfiles TreeProfileSelection.YamlOnly) jsonTree
                    |> refusal

                Expect.isNonEmpty jsonAsYaml "YamlOnly refuses JSON tree"
                Expect.all jsonAsYaml (fun e -> e.Code = "tree.unsupported_profile") "All errors are tree.unsupported_profile"

                let mixedInYaml =
                    yamlTree
                    |> Driver.withFile "pkg/acme/orders/domain/extra.type.json" "{}"

                let mixedErrors =
                    DocumentTreeStore.readAnyProfile TreeReadOptions.Defaults mixedInYaml |> refusal

                Expect.equal
                    (codesAndFiles mixedErrors)
                    [ "tree.unsupported_profile", Some "pkg/acme/orders/domain/extra.type.json" ]
                    "JSON file inside YAML tree is refused"
            }

            test "Enforces MaxFiles, MaxFileBytes, MaxDepth, MaxNodes, and CancellationToken on YAML trees" {
                let yamlTree = catalogTree "v4" "plain-yaml"

                let tooMany =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxFiles 2) yamlTree |> refusal

                Expect.equal (codesAndFiles tooMany) [ "tree.too_many_files", None ] "MaxFiles enforced"

                let tooLarge =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxFileBytes 10L) yamlTree |> refusal

                Expect.equal (codesAndFiles tooLarge) [ "tree.file_too_large", Some "manifest.yaml" ] "MaxFileBytes enforced"

                let tooDeep =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxDepth 2) yamlTree |> refusal

                Expect.isNonEmpty tooDeep "MaxDepth enforced"
                Expect.exists tooDeep (fun e -> e.Code = "depth_limit_exceeded") "Depth limit error code"

                let tooManyNodes =
                    DocumentTreeStore.readAnyProfile (TreeReadOptions.Defaults.WithMaxNodes 5) yamlTree |> refusal

                Expect.isNonEmpty tooManyNodes "MaxNodes enforced"
                Expect.exists tooManyNodes (fun e -> e.Code = "tree.document_node_limit") "Node limit error code"

                // Mid-decode cancellation: cancel the token when a module file stream opens.
                use cts = new CancellationTokenSource()
                let moduleLocation = "pkg/acme/orders/domain/module.yaml"
                let moduleBytes = Encoding.UTF8.GetBytes((Driver.texts yamlTree)[moduleLocation])

                let cancellingTree =
                    VFileTree.toFileMap yamlTree
                    |> Map.add
                        moduleLocation
                        (VFile.ofStream
                            moduleLocation
                            (StreamSource(fun () ->
                                cts.Cancel()
                                new MemoryStream(moduleBytes, writable = false) :> Stream
                            )))
                    |> VFileTree.fromFileMap

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        let mutable loaded = Unchecked.defaultof<LoadedTreeModel>
                        let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<StoreError>>
                        DocumentTreeStore.tryReadModel (TreeReadOptions.Defaults, cancellingTree, cts.Token, &loaded, &errors)
                        |> ignore
                    )
                    "Cancellation triggered during tree decoding throws OperationCanceledException"
            }

            test "Enforces expansion byte and node limits incrementally on compacted documents" {
                let compacted = YamlDocs.compactedExpandingManifestYaml 20 12
                let rawBytes = Encoding.UTF8.GetBytes compacted
                let rawLimit = int64 rawBytes.Length + 32L

                // Fits raw byte limit, but expanded text (~20 * 10 * 12 > 2400 bytes) exceeds rawLimit.
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<StoreError>>

                let byteRefused =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory rawBytes,
                        rawLimit,
                        64,
                        10_000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isFalse byteRefused "Expanded bytes exceeding byte limit are refused"
                Expect.equal (errors[0].Code) "tree.file_too_large" "Expansion byte limit diagnostic"

                // Classic contextual expansion where alias expands from 2 nodes ({"$name": "x"}) into nested classic word arrays,
                // exceeding a tight node budget during walk.
                let v3CompactedTree = catalogTree "v3.1" "contextual-yaml"
                let tightNodeErrors =
                    tryReadModelErrors (TreeReadOptions.Defaults.WithMaxNodes 15) v3CompactedTree

                Expect.isNonEmpty tightNodeErrors "Tight node budget refuses compacted v3 tree"
                Expect.exists tightNodeErrors (fun e -> e.Code = "tree.document_node_limit") "Expansion node limit diagnostic"
            }

            test "Refuses malformed YAML, anchors, and missing YAML node files with YAML path and split locations" {
                let yamlTree = catalogTree "v4" "plain-yaml"

                let malformed = yamlTree |> Driver.withFile "manifest.yaml" YamlDocs.malformedFlowYaml
                let malformedErrors = tryReadModelErrors TreeReadOptions.Defaults malformed
                Expect.equal (codesAndFiles malformedErrors) [ "invalid_yaml", Some "manifest.yaml" ] "Malformed YAML"
                Expect.isNotNull malformedErrors[0].LocationOrNull "YAML error carries pointer location"

                let anchored =
                    yamlTree
                    |> Driver.withText "manifest.yaml" (fun text -> text.Replace("4", "&v 4"))

                let anchoredErrors = tryReadModelErrors TreeReadOptions.Defaults anchored
                Expect.equal (codesAndFiles anchoredErrors) [ "unsupported_yaml_feature", Some "manifest.yaml" ] "Anchors refused"
                Expect.stringStarts (nonNull anchoredErrors[0].LocationOrNull) "/formatVersion" "Nested YAML pointer location"
                Expect.isFalse (anchoredErrors[0].MessageWithoutLocation.StartsWith("/formatVersion")) "Location stripped from message body"

                let missingFile = "pkg/acme/orders/domain/order.value.yaml"
                let withoutOrder = yamlTree |> Driver.withoutFile missingFile
                let missingErrors = tryReadModelErrors TreeReadOptions.Defaults withoutOrder
                Expect.equal (codesAndFiles missingErrors) [ "missing_member", Some missingFile ] "Missing member names .yaml path"
            }

            test "TryReadYamlDocument decodes plain and contextual YAML documents under limits and cancellation" {
                let plainBytes = ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.minimalV4PlainYaml)
                let mutable doc = Unchecked.defaultof<JsonElement>
                let mutable errors = Unchecked.defaultof<System.Collections.Generic.IReadOnlyList<StoreError>>

                let ok =
                    DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1000, CancellationToken.None, &doc, &errors)

                Expect.isTrue ok "Plain YAML document parses"
                Expect.equal (doc.GetProperty("formatVersion").GetInt32()) 4 "Parsed formatVersion"

                let limited =
                    DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1, CancellationToken.None, &doc, &errors)

                Expect.isFalse limited "Node limit enforced"
                Expect.equal (errors[0].Code) "tree.document_node_limit" "Node limit code"

                let ctxOk =
                    DocumentTreeStore.tryReadYamlDocument (
                        "manifest.yaml",
                        ReadOnlyMemory(Encoding.UTF8.GetBytes YamlDocs.contextualManifestYaml),
                        65536L,
                        64,
                        1000,
                        CancellationToken.None,
                        &doc,
                        &errors
                    )

                Expect.isTrue ctxOk "Contextual YAML document expands"
                Expect.equal (doc.GetProperty("package").GetString()) "acme/orders" "Expanded alias"

                use cts = new CancellationTokenSource()
                cts.Cancel()

                Expect.throwsT<OperationCanceledException>
                    (fun () ->
                        DocumentTreeStore.tryReadYamlDocument ("model.yaml", plainBytes, 65536L, 64, 1000, cts.Token, &doc, &errors)
                        |> ignore
                    )
                    "Cancelled token throws OperationCanceledException"
            }
        ]
    ]
