// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.CorpusJsonTests

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository

/// Reads the corpus copy of a sandbox.
let private readCopy (edit: JsonObject -> unit) =
    use sandbox = sandboxWith edit
    CorpusJson.load sandbox.Root

let private namesIt (code: string) (name: string) (issues: CorpusIssue list) =
    Expect.exists
        issues
        (fun issue ->
            issue.Code = code
            && issue.Message.Contains name
        )
        $"An issue {code} names {name}: %A{issues}"

[<Tests>]
let tests =
    // PlatformSpecific: these tests use the real file system, the checkout or worker processes, so the platform
    // scope of the Test target runs them on every operating system (docs/contributing/ci-os-matrix.md).
    testLabel "PlatformSpecific"
    <| testList "WASM host corpus JSON reader (task 1.5)" [
        test "The JSON reader loads the pinned kit and lists every case" {
            let corpus = corpus.Force()

            Expect.equal
                (corpusCaseCount ())
                PinnedCaseCount
                "The pinned corpus has 59 cases at 938f798"

            Expect.equal corpus.Cases.Length (corpusCaseCount ()) "Every case of the corpus is read"

            Expect.equal
                (corpus.Cases
                 |> List.map (fun case -> case.CaseId)
                 |> List.distinct
                 |> List.length)
                PinnedCaseCount
                "Case ids are distinct"

            let countFor profile =
                corpus.Cases
                |> List.filter (fun case -> case.AppliesTo = profile)
                |> List.length

            Expect.equal
                (countFor Profile.ExtismKernelV1)
                PinnedExtismCaseCount
                "49 cases for extism-kernel-v1"

            Expect.equal
                (countFor Profile.MorphirBytesV1)
                PinnedBytesCaseCount
                "10 cases for morphir-bytes-v1"

            Expect.equal (corpusModuleCount ()) PinnedModuleCount "The pinned corpus has 51 modules"

            Expect.equal
                (corpus.Cases
                 |> List.map (fun case -> case.Module.Name)
                 |> List.distinct
                 |> List.length)
                PinnedModuleCount
                "Every module is used by a case"

            Expect.equal corpus.Pin.Commit PinnedCommit "The pin's commit comes from PROVENANCE.md"

            Expect.equal
                corpus.Pin.Sha256
                (CorpusJson.sha256 (
                    File.ReadAllBytes(Path.Combine(root, CorpusJson.DefaultCorpusPath))
                ))
                "The pin records the corpus digest"

            Expect.equal corpus.Kit.Kit "morphir/mep-wasm-host-kit" "Kit id"
            Expect.equal corpus.Kit.DeadlineMarginSeconds 2m "Deadline margin"
        }

        test "A case keeps its frames, matchers and conditions" {
            let corpus = corpus.Force()

            let find caseId =
                corpus.Cases
                |> List.find (fun case -> case.CaseId = caseId)

            let unknown = find "classification/unknown-namespace"
            Expect.equal unknown.AppliesTo Profile.ExtismKernelV1 "appliesTo"

            Expect.equal
                unknown.Module.Sha256
                "bebbfeda31c2703c2022bf4ba27206fc0fcf22a540dda195476569c973e7addd"
                "module digest"

            match unknown.Runs with
            | [ {
                    Frames = [ frame ]
                    Expect = RunExpectation.Always expect
                } ] ->
                Expect.equal frame.Name "initialize" "frame name"

                Expect.equal
                    (Json.compact frame.Value)
                    """{"jsonrpc":"2.0","id":1,"method":"morphir.initialize","params":{"protocolVersion":"0.2.0-draft.1"}}"""
                    "The frame is sent compactly with members in the order written"

                Expect.equal expect.ExitCode (Some 3) "exit code"
                Expect.equal expect.StdoutBytes (Some 0) "stdout bytes"

                Expect.equal
                    expect.Events
                    [
                        EventMatcher.Refused(
                            Some "extension.abi_unsupported",
                            [ "env.open_socket" ]
                        )
                    ]
                    "events"

                Expect.equal expect.NoEvents [ WorkerEvent.Started; WorkerEvent.Fault ] "no events"
            | other -> failtest $"Unexpected runs %A{other}"

            let spin = find "bounds/spin-extism"
            Expect.equal spin.Bounds [ Bound.CallDeadline ] "bounds"

            let fault (expect: Expectation) =
                expect.Events
                |> List.tryPick (
                    function
                    | EventMatcher.Fault kind -> kind
                    | _ -> None
                )

            match spin.Runs with
            | [ {
                    Expect = RunExpectation.Alternatives [ budget; deadline; notEnforced ]
                } ] ->
                // Two alternatives with the same condition: a metering host may show either bound.
                Expect.equal
                    budget.When
                    (Condition.Bound(Bound.InstructionBudget, true))
                    "first alternative"

                Expect.equal
                    (fault budget.Expect)
                    (Some FaultKind.BudgetExhausted)
                    "the budget ends it"

                Expect.equal
                    budget.Expect.WithinSeconds
                    (Some
                        {
                            Until = Until.Fault
                            From = 0m
                            To = 30m
                        })
                    "before the deadline"

                Expect.equal
                    deadline.When
                    budget.When
                    "the second alternative has the same condition"

                Expect.equal
                    (fault deadline.Expect)
                    (Some FaultKind.CallDeadline)
                    "the deadline ends it"

                Expect.equal
                    deadline.Expect.WithinSeconds
                    (Some
                        {
                            Until = Until.Fault
                            From = 30m
                            To = 32m
                        })
                    "between the deadline and the margin"

                Expect.equal
                    notEnforced.When
                    (Condition.Bound(Bound.InstructionBudget, false))
                    "third alternative"

                Expect.equal
                    notEnforced.Expect.WithinSeconds
                    (Some
                        {
                            Until = Until.Fault
                            From = 30m
                            To = 32m
                        })
                    "timing"
            | other -> failtest $"Unexpected runs %A{other}"

            match (find "bounds/spin-bytes").Runs with
            | [ {
                    Expect = RunExpectation.Alternatives [ first; second; _ ]
                } ] -> Expect.equal first.When second.When "spin-bytes repeats the condition too"
            | other -> failtest $"Unexpected runs %A{other}"

            let missingAbi = find "worker/missing-abi"
            Expect.isNone missingAbi.DeclaredAbi "No declared ABI"
            Expect.equal missingAbi.Worker.Omit [ "--abi" ] "omit"
            Expect.isNone missingAbi.Worker.Instructions "no budget"
        }

        // Spec "A case's worker arguments reach the worker": Small instruction budget.
        test
            "bounds/small-budget gives the worker an instruction budget and accepts only budget-exhausted" {
            let small =
                (corpus.Force()).Cases
                |> List.find (fun case -> case.CaseId = "bounds/small-budget")

            Expect.equal small.Worker.Instructions (Some 1000000L) "worker.instructions"
            Expect.equal small.Bounds [ Bound.InstructionBudget ] "the bound it exercises"
            Expect.equal small.Module.Name "extism-spin" "the spinning module"

            match small.Runs with
            | [ {
                    Expect = RunExpectation.Always expect
                } ] ->
                Expect.equal
                    expect.Events
                    [
                        EventMatcher.Started(Some Profile.ExtismKernelV1)
                        EventMatcher.Fault(Some FaultKind.BudgetExhausted)
                    ]
                    "only budget-exhausted"
            | other -> failtest $"Unexpected runs %A{other}"
        }

        test "A worker budget the corpus cannot mean is refused" {
            readCopy (fun corpus ->
                (caseNode "bounds/small-budget" corpus).["worker"].["instructions"] <-
                    Nodes.JsonValue.Create 0
            )
            |> requireError "a zero budget"
            |> namesIt "mck.invalid_case" "/worker/instructions"

            readCopy (fun corpus ->
                (caseNode "bounds/small-budget" corpus).["worker"].["instructions"] <-
                    Nodes.JsonValue.Create 5000000000L
            )
            |> requireOk "the contract's default budget, above 32 bits"
            |> fun corpus ->
                Expect.equal
                    (corpus.Cases
                     |> List.find (fun case -> case.CaseId = "bounds/small-budget"))
                        .Worker.Instructions
                    (Some 5000000000L)
                    "a 64-bit budget"
        }

        test "The reader implements every convention of the pin, word for word" {
            let conventions =
                JsonNode
                    .Parse(File.ReadAllText(Path.Combine(root, CorpusJson.DefaultCorpusPath)))
                    .["conventions"].AsObject()

            Expect.equal
                (conventions
                 |> Seq.map (fun pair -> pair.Key, pair.Value.GetValue<string>())
                 |> List.ofSeq)
                CorpusJson.implementedConventions
                "The conventions the runner implements are the pinned ones, in order"
        }

        test "A convention whose text changed is refused, naming it" {
            readCopy (fun corpus ->
                corpus["conventions"].["guestOutput"] <-
                    Nodes.JsonValue.Create "guestOutput lists strings joined with a space."
            )
            |> requireError "changed convention"
            |> namesIt "mck.unknown_convention" "guestOutput"
        }

        test "An unknown matcher in a corpus copy is an error that names it" {
            readCopy (fun corpus ->
                (expectNode "bounds/trap" corpus).Add("stderrBytes", Nodes.JsonValue.Create(0))
            )
            |> requireError "unknown matcher"
            |> namesIt "mck.unknown_matcher" "stderrBytes"
        }

        test "An unknown member in a corpus copy is an error that names it" {
            readCopy (fun corpus ->
                (caseNode "bounds/trap" corpus).Add("timeoutMs", Nodes.JsonValue.Create(10))
            )
            |> requireError "unknown member"
            |> namesIt "mck.unknown_member" "timeoutMs"

            readCopy (fun corpus -> corpus.Add("profiles", JsonObject()))
            |> requireError "unknown top-level member"
            |> namesIt "mck.unknown_member" "profiles"

            readCopy (fun corpus ->
                (eventNode "bounds/trap" 1 corpus).Add("code", Nodes.JsonValue.Create("x"))
            )
            |> requireError "member outside the event's conventions"
            |> namesIt "mck.unknown_member" "code"
        }

        test "An unknown convention or value in a corpus copy is an error that names it" {
            readCopy (fun corpus ->
                corpus["conventions"]
                    .AsObject()
                    .Add("retries", Nodes.JsonValue.Create("Each run is tried twice."))
            )
            |> requireError "unknown convention"
            |> namesIt "mck.unknown_convention" "retries"

            readCopy (fun corpus ->
                corpus["conventions"].AsObject().Remove("timing")
                |> ignore
            )
            |> requireError "missing convention"
            |> namesIt "mck.unknown_convention" "timing"

            readCopy (fun corpus ->
                (eventNode "bounds/trap" 1 corpus).["kind"] <- Nodes.JsonValue.Create "segfault"
            )
            |> requireError "unknown fault kind"
            |> namesIt "mck.unknown_value" "segfault"

            readCopy (fun corpus ->
                (caseNode "bounds/trap" corpus).["appliesTo"] <- Nodes.JsonValue.Create "wasi-p2"
            )
            |> requireError "unknown profile"
            |> namesIt "mck.unknown_value" "wasi-p2"
        }

        test "A corpus of another kit version is refused" {
            readCopy (fun corpus -> corpus["version"] <- Nodes.JsonValue.Create "0.2.0")
            |> requireError "kit version"
            |> namesIt "mck.kit_version" "0.2.0"
        }
    ]
