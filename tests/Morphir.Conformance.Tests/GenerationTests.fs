// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.GenerationTests

open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository

let private committedIds () =
    CaseIds.parse Suite.IdMapFile (committedMap ())
    |> requireOk "The committed id map"

let private generated () =
    Generation.generate (corpus.Force()) (Some(committedIds ()))
    |> requireOk "generate"

let private suitePath (sandbox: Morphir.TestSupport.TestSandbox) (name: string) =
    sandbox.PathOf(Array.append (Generation.SuiteDirectory.Split '/') [| name |])

/// Generates the suite of a sandbox again and reads the id map it writes.
let private regenerateIn (sandbox: Morphir.TestSupport.TestSandbox) =
    Generation.writeRepository sandbox.Root
    |> requireOk "write"
    |> ignore

    CaseIds.parse Suite.IdMapFile (File.ReadAllBytes(suitePath sandbox Suite.IdMapFile))
    |> requireOk "map"

let private activeNumbers (map: CaseIdMap) =
    map.Entries
    |> List.filter (fun entry -> not entry.Retired)
    |> List.map (fun entry -> entry.CaseId, entry.Number)
    |> Map.ofList

let private highest (map: CaseIdMap) =
    map.Entries
    |> List.map (fun entry -> entry.Number)
    |> List.max

/// A copy of a case with another id, appended to a corpus copy.
let private addCase (fromCaseId: string) (newCaseId: string) (corpusNode: JsonObject) =
    let copy = (caseNode fromCaseId corpusNode).DeepClone().AsObject()
    copy["caseId"] <- Nodes.JsonValue.Create newCaseId
    corpusNode["cases"].AsArray().Add copy

let private removeCase (caseId: string) (corpusNode: JsonObject) =
    corpusNode["cases"].AsArray().Remove(caseNode caseId corpusNode)
    |> ignore

/// The tag line above each scenario of a case.
let private scenarioTags (text: string) (caseId: string) =
    let lines = text.Split '\n'

    [
        for index in
            1 .. lines.Length
                 - 1 do
            if
                lines[index].Trim().StartsWith "Scenario:"
                && lines[index]
                    .EndsWith(
                        " "
                        + caseId
                    )
            then
                yield lines[index - 1].Trim()
    ]

[<Tests>]
let tests =
    testList "WASM host feature generation (task 1.5)" [
        test "Generating twice gives identical bytes, with LF line endings" {
            let first = generated ()
            let second = generated ()
            Expect.equal first second "Two generations are equal"
            Expect.isNonEmpty first "Files are generated"

            for file in first do
                Expect.isFalse (file.Text.Contains '\r') $"{file.Path} has LF line endings only"
                Expect.isTrue (file.Text.EndsWith '\n') $"{file.Path} ends with a line break"
        }

        test "The script's copy of the library types it loads is the same as Types.fs" {
            let types =
                File.ReadAllText(Path.Combine(root, "src", "Morphir.Conformance", "Types.fs"))

            let shim =
                File.ReadAllText(Path.Combine(root, "scripts", "wasm-host", "Artifacts.fs"))

            let start = shim.IndexOf "type Artifact"
            Expect.isGreaterThan start 0 "The shim declares Artifact"

            Expect.stringContains
                types
                (shim.Substring(start).TrimEnd())
                "Types.fs declares the same types"
        }

        test "The committed features and id map are what the pinned corpus generates" {
            Expect.equal
                (Generation.checkRepository root)
                []
                "The stale check passes on the checkout"
        }

        test "Each generated feature says it is generated, from which pin, and must not be edited" {
            let corpus = corpus.Force()

            for file in
                generated ()
                |> List.filter (fun file -> file.Path.EndsWith ".feature") do
                let header =
                    file.Text.Split('\n')
                    |> Array.takeWhile (fun line -> line.StartsWith "#")
                    |> String.concat "\n"

                Expect.stringContains header "Generated" $"{file.Path} says it is generated"
                Expect.stringContains header "Do not edit" $"{file.Path} says it must not be edited"
                Expect.stringContains header corpus.Pin.Commit $"{file.Path} names the commit"

                Expect.stringContains
                    header
                    corpus.Pin.Sha256
                    $"{file.Path} names the corpus digest"

                Expect.stringContains
                    header
                    Suite.RegenerateCommand
                    $"{file.Path} names the command"
        }

        test "A conditional case becomes one tagged scenario for each alternative" {
            Expect.equal
                (scenarioTags
                    (featureText "classification.feature")
                    "classification/bytes-profile-unsupported")
                [
                    "@requires:profile:extism-kernel-v1 @requires:does-not-run:morphir-bytes-v1"
                    "@requires:profile:extism-kernel-v1 @requires:runs:morphir-bytes-v1"
                ]
                "The host-profile alternatives"

            // Spec "Each alternative is its own scenario": Two alternatives with the same condition.
            Expect.equal
                (scenarioTags (featureText "bounds.feature") "bounds/spin-extism")
                [
                    "@requires:profile:extism-kernel-v1 @requires:enforced:instructionBudget @bound:callDeadline"
                    "@requires:profile:extism-kernel-v1 @requires:enforced:instructionBudget @bound:callDeadline"
                    "@requires:profile:extism-kernel-v1 @requires:not-enforced:instructionBudget @bound:callDeadline"
                ]
                "The bound alternatives, two of them with the same tags"

            Expect.equal
                (scenarioTags
                    (featureText "classification.feature")
                    "classification/known-name-other-signature")
                [
                    "@requires:profile:extism-kernel-v1 @requires:runs:morphir-bytes-v1"
                    "@requires:profile:extism-kernel-v1 @requires:does-not-run:morphir-bytes-v1"
                ]
                "The case applies to every host, with an alternative for each"

            Expect.equal
                (scenarioTags (featureText "bytes.feature") "bytes/echo")
                [ "@requires:profile:morphir-bytes-v1" ]
                "A byte-profile case requires the profile"
        }

        // Spec "A case's worker arguments reach the worker": the scenario states the budget in its own step.
        test "A case's instruction budget is a step of its scenario" {
            let text = featureText "bounds.feature"
            let start = text.IndexOf " bounds/small-budget\n"
            Expect.isGreaterThan start 0 "the scenario"
            let finish = text.IndexOf("\n  @", start)

            let scenario =
                if finish < 0 then
                    text.Substring start
                else
                    text.Substring(
                        start,
                        finish
                        - start
                    )

            Expect.stringContains
                scenario
                "    And the worker limits instructions to 1000000 units\n"
                "the budget step"

            Expect.isFalse
                ((featureText "bounds.feature").Substring(0, start).Contains
                    "the worker limits instructions")
                "no other case of the group sets a budget"
        }

        // Spec "Generated cases keep stable ids": Case added upstream, at the move of the pin to 48d10706f.
        test "The cases added at 48d10706f took the next unused numbers and no number moved" {
            let map = committedIds ()

            Expect.equal map.Pin.Commit PinnedCommit "The map was generated from the pin"
            Expect.equal (highest map) PinnedCaseCount "58 numbers, none retired"

            Expect.isTrue
                (map.Entries
                 |> List.forall (fun entry -> not entry.Retired))
                "no case was removed"

            let numbers = activeNumbers map

            for caseId, number in
                [
                    "classification/unknown-namespace", 1
                    "extism/notifications-accepted", 29
                    "extism/notification-answered", 30
                    "bounds/spin-bytes", 33
                    "bounds/grow-to-ceiling-then-trap", 35
                    "bounds/bytes-grow-refused", 37
                    "sandbox/logging", 56
                    "extism/notification-delivery-unlisted", 57
                    "bounds/small-budget", 58
                ] do
                Expect.equal
                    (Map.tryFind caseId numbers)
                    (Some number)
                    $"{caseId} is wasm-host-{number:D4}"
        }

        // Spec scenario: Stale features.
        test "A changed corpus with features not generated again fails the check, naming each case" {
            use sandbox =
                sandboxWith (fun corpus ->
                    (expectNode "bounds/trap" corpus).["exitCode"] <- Nodes.JsonValue.Create 2

                    (caseNode "sandbox/logging" corpus).["gaps"].[0] <-
                        Nodes.JsonValue.Create "Changed upstream."
                )

            let issues = Generation.checkRepository sandbox.Root

            let messages =
                issues
                |> List.map (fun issue ->
                    issue.Path
                    + " "
                    + issue.Message
                )

            Expect.exists
                issues
                (fun issue ->
                    issue.Path.EndsWith "bounds.feature"
                    && issue.Message.Contains "bounds/trap"
                )
                $"bounds/trap is named: %A{messages}"

            Expect.exists
                issues
                (fun issue ->
                    issue.Path.EndsWith "sandbox.feature"
                    && issue.Message.Contains "sandbox/logging"
                )
                $"sandbox/logging is named: %A{messages}"

            Expect.all
                issues
                (fun issue -> not (issue.Message.Contains "bounds/spin-extism"))
                $"No unchanged case is named: %A{messages}"

            Expect.exists
                issues
                (fun issue -> issue.Path.EndsWith Suite.IdMapFile)
                "The id map records another corpus digest"
        }

        // Spec scenario: Case added upstream.
        test "A case added to the corpus takes the next unused number and no existing number moves" {
            let before = committedIds ()
            use sandbox = sandboxWith (addCase "bounds/trap" "bounds/trap-again")
            let after = regenerateIn sandbox

            let next =
                highest before
                + 1

            Expect.equal
                (activeNumbers after
                 |> Map.tryFind "bounds/trap-again")
                (Some next)
                "The new case takes the next unused number"

            for KeyValue(caseId, number) in activeNumbers before do
                Expect.equal
                    (activeNumbers after
                     |> Map.tryFind caseId)
                    (Some number)
                    $"{caseId} keeps its number"

            let bounds = File.ReadAllText(suitePath sandbox "bounds.feature")

            Expect.stringContains
                bounds
                $"Scenario: {Suite.id next} bounds/trap-again"
                "The new scenario has its id"

            Expect.equal
                (Generation.checkRepository sandbox.Root)
                []
                "The written suite passes the check"
        }

        // Spec scenario: Case removed upstream.
        test "A removed case's scenario goes, and its number stays reserved and is not reused" {
            let before = committedIds ()

            let trap =
                activeNumbers before
                |> Map.find "bounds/trap"

            use sandbox = sandboxWith (removeCase "bounds/trap")
            let removed = regenerateIn sandbox

            Expect.contains
                removed.Entries
                {
                    Number = trap
                    CaseId = "bounds/trap"
                    Retired = true
                }
                "The number is retired in the map"

            let bounds = File.ReadAllText(suitePath sandbox "bounds.feature")
            Expect.isFalse (bounds.Contains "bounds/trap\n") "The scenario is removed"
            Expect.isFalse (bounds.Contains(Suite.id trap)) "No scenario has the id"

            // A later case does not reuse the reserved number.
            let corpusPath = sandbox.PathOf(CorpusJson.DefaultCorpusPath.Split '/')
            let node = JsonNode.Parse(File.ReadAllText corpusPath).AsObject()
            addCase "bounds/spin-extism" "bounds/new" node
            File.WriteAllText(corpusPath, node.ToJsonString(), UTF8Encoding false)
            let added = regenerateIn sandbox

            Expect.equal
                (activeNumbers added
                 |> Map.tryFind "bounds/new")
                (Some(
                    highest before
                    + 1
                ))
                "The new case takes the number after every reserved one"

            Expect.contains
                added.Entries
                {
                    Number = trap
                    CaseId = "bounds/trap"
                    Retired = true
                }
                "The number stays retired"
        }
    ]
