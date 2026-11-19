// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.ReportContractTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository

let private directory = Path.Combine(root, "tests/Fixtures/MepWasm/ReportContract/parent-d5890d00")
let private controls = lazy (JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "wasm-host-report.controls.json"))))
let private pinnedCorpus = lazy (JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "wasm-host.cases.json"))))
let private profileContract = lazy (JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "wasm-execution-profiles.json"))))
let private suite = lazy (FeatureReader.read (corpus.Force()) (committed ()) |> requireOk "suite")
let private text (node: JsonNode) = node.GetValue<string>()
let private value (s: string) : JsonNode = System.Text.Json.Nodes.JsonValue.Create s
let private array (nodes: JsonNode seq) = JsonArray(nodes |> Seq.toArray)
let private strings (node: JsonNode) = node.AsArray() |> Seq.map text |> Seq.toList
let private copy (node: JsonNode) = node.DeepClone()
let private expectedIds = [
    "qualified"; "budget-not-enforced"; "bytes-host"; "bytes-host-without-bytes-guest"
    "deviation-does-not-pass"; "reasons-in-order"; "bound-undeclared"; "bound-inconsistent"
    "deadline-not-enforced"; "one-case-failed"; "one-case-error"; "one-case-refused"
    "no-guest-kit-run"; "guest-kit-run-of-another-executable"; "no-offering"
    "required-profile-not-declared"; "another-corpus"; "case-removed"
    "unenforced-bound-recorded-as-passed"; "totals-edited"; "citation-for-undeclared-profile"; "verdict-edited"
]

let private inventoryValid (items: JsonArray) =
    (items |> Seq.map (fun item -> text item["id"]) |> Seq.toList) = expectedIds

// These are synthetic policy inputs, never observations of a host. The fixed upstream
// expectations are the oracle. No checker implementation or recorded host output is copied.
let private reportFor (control: JsonNode) =
    let fixedData = controls.Force()
    let baseline = fixedData["base"]
    let change = control["change"]
    let report = JsonObject()
    report["$schema"] <- value "https://morphir.finos.org/schemas/mep-wasm-host-kit/0.1.0-draft.1/report.schema.json"
    for name in ["runner"; "host"] do
        report[name] <- copy baseline[name]
        match change[name] with
        | null -> ()
        | patch ->
            for member' in patch.AsObject() do
                if isNull member'.Value then report[name].AsObject().Remove(member'.Key) |> ignore
                else report[name].[member'.Key] <- copy member'.Value
    let declarations = baseline["bounds"].DeepClone().AsObject()
    match change["bounds"] with
    | null -> ()
    | patch -> for member' in patch.AsObject() do declarations[member'.Key] <- copy member'.Value
    let boundOrder = strings fixedData["rule"].["boundOrder"]
    let required = strings (profileContract.Force().["bounds"].["requiredForQualification"])
    report["bounds"] <- array (boundOrder |> Seq.map (fun name ->
        let item = JsonObject()
        item["bound"] <- value name
        item["declaration"] <- copy declarations[name]
        item["requiredForQualification"] <- System.Text.Json.Nodes.JsonValue.Create(List.contains name required)
        if text declarations[name] = "not-enforced" then item["reason"] <- value "no-instruction-metering"
        item :> JsonNode))
    report["guestKitCitations"] <- copy (if isNull change["guestKitCitations"] then baseline["guestKitCitations"] else change["guestKitCitations"])
    let profiles = strings report["host"].["profiles"]
    let cases = JsonArray()
    for original in pinnedCorpus.Force().["cases"].AsArray() do
        let id = text original["caseId"]
        let omitted = not (isNull change["removeCases"]) && List.contains id (strings change["removeCases"])
        if not omitted then
            let bounds = if isNull original["bounds"] then [] else strings original["bounds"]
            let states = bounds |> List.map (fun name -> text declarations[name])
            let natural =
                if not (List.contains (text original["appliesTo"]) profiles) then "not_applicable"
                elif List.exists (fun state -> state = "undeclared" || state = "inconsistent") states then "error"
                elif List.contains "not-enforced" states then "not_enforced"
                else "passed"
            let outcome = if not (isNull change["outcomes"]) && not (isNull change["outcomes"].[id]) then text change["outcomes"].[id] else natural
            let item = JsonObject()
            item["caseId"] <- value id
            item["appliesTo"] <- copy original["appliesTo"]
            item["bounds"] <- array (bounds |> Seq.map value)
            item["outcome"] <- value outcome
            item["runs"] <- JsonArray()
            if not (isNull change["deviations"]) && List.contains id (strings change["deviations"]) then
                item["deviation"] <- JsonObject()
            cases.Add item
    report["cases"] <- cases
    let totals = JsonObject()
    totals["cases"] <- System.Text.Json.Nodes.JsonValue.Create cases.Count
    for name, outcome in ["passed","passed"; "failed","failed"; "error","error"; "refused","refused"; "notApplicable","not_applicable"; "notEnforced","not_enforced"] do
        totals[name] <- System.Text.Json.Nodes.JsonValue.Create(cases |> Seq.filter (fun item -> text item["outcome"] = outcome) |> Seq.length)
    match change["storedTotals"] with
    | null -> ()
    | patch -> for member' in patch.AsObject() do totals[member'.Key] <- copy member'.Value
    report["totals"] <- totals
    let reasons = JsonArray()
    let reason (code: string) (key: string) (field: string) =
        let item = JsonObject()
        item["code"] <- value code
        item[key] <- value field
        reasons.Add item
    for name in boundOrder do
        match text declarations[name] with
        | "undeclared" -> reason "bound_undeclared" "bound" name
        | "inconsistent" -> reason "bound_inconsistent" "bound" name
        | "not-enforced" when List.contains name required -> reason "required_bound_not_enforced" "bound" name
        | _ -> ()
    for item in cases do
        match text item["outcome"] with
        | "failed" -> reason "case_failed" "caseId" (text item["caseId"])
        | "error" -> reason "case_error" "caseId" (text item["caseId"])
        | "refused" -> reason "case_refused" "caseId" (text item["caseId"])
        | _ -> ()
    for profile in strings fixedData["rule"].["profileOrder"] do
        if List.contains profile profiles then
            match report["guestKitCitations"].AsArray() |> Seq.tryFind (fun item -> text item["profile"] = profile) with
            | None -> reason "guest_kit_missing" "profile" profile
            | Some item when text item["hostSha256"] <> text report["host"].["sha256"] -> reason "guest_kit_host_mismatch" "profile" profile
            | _ -> ()
    let verdict = JsonObject()
    verdict["qualified"] <- System.Text.Json.Nodes.JsonValue.Create(reasons.Count = 0)
    verdict["reasons"] <- reasons
    report["verdict"] <- if isNull change["storedVerdict"] then (verdict :> JsonNode) else copy change["storedVerdict"]
    let suiteNode = JsonObject()
    let corpusNode = JsonObject()
    corpusNode["sha256"] <- if isNull change["corpusSha256"] then value (suite.Force().Pin.Sha256) else copy change["corpusSha256"]
    suiteNode["corpus"] <- corpusNode
    report["suite"] <- suiteNode
    Encoding.UTF8.GetBytes(report.ToJsonString())

// PlatformSpecific: these tests read files of the checkout, so the platform scope of the Test target runs them on
// every operating system (docs/contributing/ci-os-matrix.md).
[<Tests>]
let tests = testLabel "PlatformSpecific" <| testList "WASM host normative report controls" [
    test "all 22 pinned controls and every rule element remain present" {
        let data = controls.Force()
        Expect.isTrue (inventoryValid (data["controls"].AsArray())) "exact independent inventory"
        let covered = data["controls"].AsArray() |> Seq.map (fun item -> text item["covers"]) |> Seq.sort |> Seq.toList
        let needed = ["reasons"; "rejections"; "scenarios"] |> List.collect (fun name -> strings data["rule"].[name]) |> List.sort
        Expect.equal covered needed "each requirement covered once"
        let removed = data["controls"].DeepClone().AsArray()
        removed.RemoveAt 0
        Expect.isFalse (inventoryValid removed) "deleting a control is detected"
    }
    test "the pinned parent inputs retain their exact bytes and corpus identity" {
        let manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))
        Expect.equal (text manifest["commit"]) "d5890d00f4d7839a5edfbcca100ffaaf45281508" "parent authority"
        for item in manifest["files"].AsArray() do
            let bytes = File.ReadAllBytes(Path.Combine(directory, text item["file"]))
            Expect.equal (Convert.ToHexStringLower(SHA256.HashData bytes)) (text item["sha256"]) "unaltered source bytes"
        Expect.equal (File.ReadAllBytes(Path.Combine(directory, "wasm-host.cases.json"))) (File.ReadAllBytes(Path.Combine(root, CorpusJson.DefaultCorpusPath))) "same independently fixed corpus"
    }
    for id in expectedIds do
        testCase id (fun () ->
            let control = controls.Force().["controls"].AsArray() |> Seq.find (fun item -> text item["id"] = id)
            let expected = control["expect"]
            match HostReport.checkContract (suite.Force()) (reportFor control) with
            | Error refusals ->
                Expect.isNotNull expected["rejected"] "expected a verdict, not refusal"
                Expect.equal refusals (strings expected["rejected"]) "ordered refusals"
            | Ok verdict ->
                Expect.isNull expected["rejected"] "expected refusal without verdict"
                let actual = JsonNode.Parse(Json.compact verdict)
                Expect.isTrue (JsonNode.DeepEquals(actual, expected)) $"fixed control {id}")
]
