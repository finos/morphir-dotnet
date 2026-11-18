// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.BoundedGenerationReceiptTests

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Expecto
open Morphir.Conformance.BoundedGeneration
open Morphir.TestSupport.Fixtures

let private text (name: string) (element: JsonElement) =
    element.GetProperty(name).GetString()
    |> nonNull

let private pointerParts (pointer: string) =
    if not (pointer.StartsWith("/", StringComparison.Ordinal)) then
        failtestf "Expected an object member pointer, got %s" pointer

    pointer.Substring(1).Split('/')
    |> Array.map (fun token ->
        if Regex.IsMatch(token, "~(?![01])") then
            failtestf "Invalid JSON Pointer escape in %s" pointer

        token.Replace("~1", "/").Replace("~0", "~")
    )
    |> Array.toList

// The frozen controls patch object members only. Fail on an operation outside that vocabulary.
let private applyPatch (root: JsonObject) (operation: JsonElement) =
    let pointer = text "path" operation

    let value () =
        JsonNode.Parse(operation.GetProperty("value").GetRawText())

    let rec apply (parent: JsonObject) (parts: string list) =
        match parts with
        | [ name ] ->
            match text "op" operation with
            | "add" -> parent[name] <- value ()
            | "replace" ->
                if not (parent.ContainsKey name) then
                    failtestf "Cannot replace missing member %s" pointer

                parent[name] <- value ()
            | "remove" ->
                if not (parent.Remove name) then
                    failtestf "Cannot remove missing member %s" pointer
            | other -> failtestf "Unsupported receipt control patch operation %s" other
        | name :: rest ->
            match parent[name] with
            | :? JsonObject as child -> apply child rest
            | _ -> failtestf "Expected an object parent for %s" pointer
        | [] -> failtestf "Expected an object member pointer, got %s" pointer

    apply root (pointerParts pointer)

let private patchReceipt (sample: string) (control: JsonElement) =
    let root =
        (JsonNode.Parse sample
         |> nonNull)
            .AsObject()

    for operation in control.GetProperty("patch").EnumerateArray() do
        applyPatch root operation

    Encoding.UTF8.GetBytes(root.ToJsonString())

let private caseExpectation (controls: JsonElement) : CaseExpectation =
    let expected = controls.GetProperty("caseExpectation")

    {
        ContractSha256 = text "contractSha256" expected
        SuiteSha256 = text "suiteSha256" expected
        CaseId = text "caseId" expected
        ExpectedSha256 = text "expectedSha256" expected
    }

let private refusalOf (expected: JsonElement) =
    match expected.TryGetProperty "refusal" with
    | true, code ->
        Some(
            code.GetString()
            |> nonNull
        )
    | false, _ -> None

let private expectOutcome (expected: JsonElement) label state reasons =
    let expectedState =
        match text "state" expected with
        | "passed" -> OutcomeState.Passed
        | "failed" -> OutcomeState.Failed
        | "not-run" -> OutcomeState.NotRun
        | other -> failtestf "Unknown frozen outcome state %s" other

    let expectedReasons =
        expected.GetProperty("reasons").EnumerateArray()
        |> Seq.map (fun reason ->
            reason.GetString()
            |> nonNull
        )
        |> Seq.toList

    Expect.equal
        state
        expectedState
        (label
         + " state")

    Expect.equal
        reasons
        expectedReasons
        (label
         + " reasons in order")

[<Tests>]
let tests =
    use document =
        FixtureCatalog.ReadText FixtureCatalog.BoundedGenerationReceipts.Controls
        |> JsonDocument.Parse

    let controls = document.RootElement.Clone()

    let cases =
        controls.GetProperty("cases").EnumerateArray()
        |> Seq.toList

    let expectation = caseExpectation controls

    let schema =
        FixtureCatalog.ReadBytes FixtureCatalog.BoundedGenerationReceipts.Schema

    let sample = FixtureCatalog.ReadText FixtureCatalog.BoundedGenerationReceipts.Sample

    let replayPatch patch =
        use control =
            JsonDocument.Parse(
                "{\"patch\":"
                + patch
                + "}"
            )

        Receipt.check schema expectation (patchReceipt sample control.RootElement)

    testList "Bounded-generation receipt controls" [
        test "native and reader receipts refuse a guest linear-memory observation" {
            for execution, comparison in
                [ "native-generation", "artifact-set"; "in-process-reader", "reader-facts" ] do
                let patch =
                    """[
                        {"op":"replace","path":"/execution","value":"EXECUTION"},
                        {"op":"remove","path":"/guest"},
                        {"op":"replace","path":"/comparison/kind","value":"COMPARISON"}
                    ]"""
                        .Replace("EXECUTION", execution)
                        .Replace("COMPARISON", comparison)

                Expect.equal (replayPatch patch) (Error "contradictory_evidence") execution
        }

        test "a tree peak cannot be lower than a descendant peak from the same samples" {
            let patch =
                """[
                    {"op":"replace","path":"/measurements/peakHostResidentBytes/source/method","value":"independent host samples"},
                    {"op":"replace","path":"/measurements/peakDescendantResidentBytes","value":{
                        "state":"measured",
                        "value":2097152,
                        "source":{
                            "method":"example host and descendants sampled every millisecond",
                            "evidenceSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
                        }
                    }}
                ]"""

            Expect.equal
                (replayPatch patch)
                (Error "contradictory_evidence")
                "Descendants belong to the tree"
        }

        test "an altered schema with the same ID is refused" {
            let alteredSchema = Array.append schema [| 32uy |]
            let receipt = Encoding.UTF8.GetBytes sample

            Expect.equal
                (Receipt.check alteredSchema expectation receipt)
                (Error "invalid_shape")
                "Replay checks the pinned schema bytes"
        }
        test "the frozen inventory covers schema checks and semantic replay" {
            let ids =
                cases
                |> List.map (text "id")

            let schemaFailures =
                cases
                |> List.filter (fun control ->
                    not (control.GetProperty("schemaValid").GetBoolean())
                )
                |> List.map (text "id")

            let invalidShape, semanticRefusals, outcomes =
                cases
                |> List.fold
                    (fun (invalidShape, semanticRefusals, outcomes) control ->
                        match refusalOf (control.GetProperty("expected")) with
                        | Some "invalid_shape" ->
                            text "id" control
                            :: invalidShape,
                            semanticRefusals,
                            outcomes
                        | Some _ ->
                            invalidShape,
                            semanticRefusals
                            + 1,
                            outcomes
                        | None ->
                            invalidShape,
                            semanticRefusals,
                            outcomes
                            + 1
                    )
                    ([], 0, 0)

            Expect.equal
                (text "baseReceipt" controls)
                "fixtures/receipt-qualified-wasm.json"
                "sample reference"

            Expect.equal ids.Length 19 "control count"
            Expect.equal (List.distinct ids).Length 19 "unique control IDs"
            Expect.equal schemaFailures.Length 4 "schema-invalid controls"

            Expect.equal
                (List.rev invalidShape)
                schemaFailures
                "schema-invalid controls expect invalid_shape"

            Expect.equal semanticRefusals 4 "schema-valid semantic refusals"
            Expect.equal outcomes 11 "schema-valid replayed outcome pairs"
        }

        for control in cases do
            testCase
                (text "id" control)
                (fun () ->
                    let expected = control.GetProperty("expected")
                    let receipt = patchReceipt sample control
                    let result = Receipt.check schema expectation receipt

                    let schemaValid =
                        match result with
                        | Error "invalid_shape" -> false
                        | _ -> true

                    Expect.equal
                        schemaValid
                        (control.GetProperty("schemaValid").GetBoolean())
                        "schema admission"

                    match refusalOf expected, result with
                    | Some code, Error actual -> Expect.equal actual code "receipt refusal"
                    | Some code, Ok _ -> failtestf "Expected receipt refusal %s" code
                    | None, Error actual -> failtestf "Expected replayed outcomes, got %s" actual
                    | None, Ok replayed ->
                        expectOutcome
                            (expected.GetProperty "correctness")
                            "Correctness"
                            replayed.Correctness.State
                            replayed.Correctness.Reasons

                        expectOutcome
                            (expected.GetProperty "resources")
                            "Resources"
                            replayed.Resources.State
                            replayed.Resources.Reasons
                )
    ]
