// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.JudgeTests

open System.IO
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.Repository
open Morphir.Conformance.Tests.FakeWorker

let private startedWith (bounds: string) = {
    Arguments = []
    FramesWritten = 1
    LastFrameWrittenMs = Some 0L
    Stdout = [||]
    Stderr = [
        {
            AtMs = 1L
            Text =
                $"{{\"profile\":\"morphir/command-event\",\"event\":\"started\",\"abi\":\"extism-kernel-v1\",\"moduleSha256\":\"x\",\"buildIdentity\":\"{Identity}\",\"bounds\":{bounds}}}"
        }
    ]
    End = WorkerEnd.Exited 0
    EndMs = 2L
}

let private bounds (budget: string) =
    $"{{\"memoryCeiling\":{{\"enforced\":true}},\"instructionBudget\":{budget},\"callDeadline\":{{\"enforced\":true}}}}"

let private metering = startedWith (bounds "{\"enforced\":true}")

let private notMetering =
    startedWith (bounds "{\"enforced\":false,\"reason\":\"no-instruction-metering\"}")

let private enforcedBudget = "{\"enforced\":true}"

let private unmeteredBudget =
    "{\"enforced\":false,\"reason\":\"no-instruction-metering\"}"

/// A run of a case of one frame: a well-formed started event, then a fault of this kind this many
/// milliseconds after the frame was written, and exit status 1.
let private faultRun (caseId: string) (budget: string) (kind: string) (atMs: int64) =
    let case = (suiteCase caseId).Case

    {
        Arguments = []
        FramesWritten = 1
        LastFrameWrittenMs = Some 0L
        Stdout = [||]
        Stderr = [
            {
                AtMs = 1L
                Text =
                    $"{{\"profile\":\"morphir/command-event\",\"event\":\"started\",\"abi\":\"extism-kernel-v1\",\"moduleSha256\":\"{case.Module.Sha256}\",\"buildIdentity\":\"{Identity}\",\"bounds\":{bounds budget}}}"
            }
            {
                AtMs = atMs
                Text =
                    $"{{\"profile\":\"morphir/command-event\",\"event\":\"fault\",\"fault\":{{\"kind\":\"{kind}\"}}}}"
            }
        ]
        End = WorkerEnd.Exited 1
        EndMs = atMs + 5L
    }

let private spinRun = faultRun "bounds/spin-extism"

let private judgeSpin (observation: RunObservation) =
    let case = (suiteCase "bounds/spin-extism").Case

    Judge.outcome
        [ Profile.ExtismKernelV1 ]
        (Judge.declaration [ observation ])
        case
        (Some case.Module.Sha256)
        [ observation ]

let private satisfied (alternative: int) (enforced: bool) : SatisfiedAlternative = {
    Run = 1
    Alternative = alternative
    Condition = Condition.Bound(Bound.InstructionBudget, enforced)
}

/// The alternatives whose matchers failed, each once, in order.
let private failedAlternatives (outcome: CaseOutcome) =
    match outcome with
    | CaseOutcome.Failed failures ->
        failures
        |> List.map (fun failure -> failure.Alternative)
        |> List.distinct
    | other -> failtest $"Expected a failed case, got {other}"

[<Tests>]
let tests =
    testList "WASM host judge: selection, bounds and outcomes (task 1.5)" [
        test "The declaration of each bound comes from the started events" {
            let declaration = Judge.declaration [ notMetering ]

            Expect.equal
                (Map.find Bound.InstructionBudget declaration)
                (BoundDeclaration.Declared(BoundState.NotEnforced "no-instruction-metering"))
                "not enforced"

            Expect.equal
                (Map.find Bound.CallDeadline declaration)
                (BoundDeclaration.Declared BoundState.Enforced)
                "enforced"

            Expect.equal
                (Map.find Bound.CallDeadline (Judge.declaration []))
                BoundDeclaration.Undeclared
                "no started event"
        }

        test
            "Started events that disagree make the declaration inconsistent, and its cases cannot be judged" {
            let declaration = Judge.declaration [ metering; notMetering ]

            match Map.find Bound.InstructionBudget declaration with
            | BoundDeclaration.Inconsistent states -> Expect.equal states.Length 2 "both states"
            | other -> failtest $"{other}"

            let case = (suiteCase "bounds/spin-extism").Case

            let outcome =
                Judge.outcome [ Profile.ExtismKernelV1 ] declaration case (Some case.Module.Sha256) [
                    metering
                ]

            match outcome with
            | CaseOutcome.Error _ -> ()
            | other -> failtest $"Expected an error, got {other}"
        }

        test "A run's alternative follows the bound the started events declare" {
            let case = (suiteCase "bounds/spin-extism").Case

            let observed =
                { notMetering with
                    Stderr = notMetering.Stderr
                }

            // The not-enforced alternative expects call-deadline between 30 and 32 seconds; this run has no
            // fault, so the case fails on the matchers of that alternative.
            match
                Judge.outcome
                    [ Profile.ExtismKernelV1 ]
                    (Judge.declaration [ observed ])
                    case
                    (Some case.Module.Sha256)
                    [ observed ]
            with
            | CaseOutcome.Failed failures ->
                Expect.contains
                    (failures
                     |> List.map (fun failure -> failure.Matcher))
                    "withinSeconds"
                    "the deadline alternative"
            | other -> failtest $"{other}"
        }

        // Spec "Each alternative is its own scenario" and "A runaway guest is stopped in time": Metering host.
        test
            "A metering host passes the runaway case on the first alternative: budget-exhausted before 30 seconds" {
            Expect.equal
                (judgeSpin (spinRun enforcedBudget "budget-exhausted" 1000L))
                (CaseOutcome.Passed [ satisfied 1 true ])
                "passed on alternative 1, which records the bound that ended the call"
        }

        test
            "A metering host passes the runaway case on the second alternative: call-deadline at 31 seconds" {
            Expect.equal
                (judgeSpin (spinRun enforcedBudget "call-deadline" 31000L))
                (CaseOutcome.Passed [ satisfied 2 true ])
                "passed on alternative 2, with the same condition as alternative 1"
        }

        // Spec "Each alternative is its own scenario": No applicable alternative satisfied.
        test
            "A metering host whose call ends in a trap satisfies neither applicable alternative and fails" {
            let outcome = judgeSpin (spinRun enforcedBudget "trap" 1000L)

            Expect.equal
                (failedAlternatives outcome)
                [ Some 1; Some 2 ]
                "the failures of both applicable alternatives are recorded, and none of the third"

            match outcome with
            | CaseOutcome.Failed failures ->
                for alternative in [ 1; 2 ] do
                    Expect.contains
                        (failures
                         |> List.filter (fun failure -> failure.Alternative = Some alternative)
                         |> List.map (fun failure -> failure.Matcher))
                        "events"
                        $"alternative {alternative} fails on its fault event"
            | other -> failtest $"{other}"
        }

        test "A call-deadline after the margin fails both alternatives of a metering host" {
            let outcome = judgeSpin (spinRun enforcedBudget "call-deadline" 33000L)

            Expect.equal (failedAlternatives outcome) [ Some 1; Some 2 ] "neither alternative"
        }

        // Spec "Each alternative is its own scenario": Alternative for another condition.
        test "An alternative for another condition neither passes nor fails the case" {
            Expect.equal
                (judgeSpin (spinRun unmeteredBudget "call-deadline" 31000L))
                (CaseOutcome.Passed [ satisfied 3 false ])
                "a host without metering passes on the third alternative"

            Expect.equal
                (failedAlternatives (judgeSpin (spinRun unmeteredBudget "budget-exhausted" 1000L)))
                [ Some 3 ]
                "budget-exhausted is an alternative only for a metering host, so it does not pass this one"
        }

        test "A case without alternatives passes with nothing to record" {
            let case = (suiteCase "bounds/trap").Case

            let judge kind =
                let observation = faultRun "bounds/trap" enforcedBudget kind 10L

                Judge.outcome
                    [ Profile.ExtismKernelV1 ]
                    (Judge.declaration [ observation ])
                    case
                    (Some case.Module.Sha256)
                    [ observation ]

            Expect.equal (judge "trap") (CaseOutcome.Passed []) "passed, no alternative to name"

            Expect.equal
                (failedAlternatives (judge "failure"))
                [ None ]
                "a failure of a run without alternatives names none"
        }

        test
            "A case for a bound the host does not enforce is not_enforced, and only a required bound blocks the pass" {
            let case = (suiteCase "bounds/spin-extism").Case

            let budgetCase =
                { case with
                    Bounds = [ Bound.InstructionBudget ]
                }

            let outcome =
                Judge.outcome
                    [ Profile.ExtismKernelV1 ]
                    (Judge.declaration [ notMetering ])
                    budgetCase
                    (Some case.Module.Sha256)
                    [ notMetering ]

            Expect.equal
                outcome
                (CaseOutcome.NotEnforced [ Bound.InstructionBudget ])
                "not_enforced"

            Expect.isTrue (Judge.acceptable outcome) "the budget is optional for qualification"

            Expect.isFalse
                (Judge.acceptable (CaseOutcome.NotEnforced [ Bound.CallDeadline ]))
                "the deadline is required"

            Expect.isFalse
                (Judge.acceptable (CaseOutcome.NotEnforced [ Bound.MemoryCeiling ]))
                "the ceiling is required"
        }

        test "A case whose module file is missing is an error, not a pass" {
            let case = (suiteCase "extism/echo").Case

            match Judge.outcome [ Profile.ExtismKernelV1 ] Map.empty case None [] with
            | CaseOutcome.Error _ -> ()
            | other -> failtest $"{other}"
        }

        test "A worker that did not start is an error, not a failure of the host" {
            let case = (suiteCase "classification/unknown-namespace").Case

            let observed =
                { metering with
                    End = WorkerEnd.NotStarted "missing"
                    Stderr = []
                }

            match
                Judge.outcome [ Profile.ExtismKernelV1 ] Map.empty case (Some case.Module.Sha256) [
                    observed
                ]
            with
            | CaseOutcome.Error _ -> ()
            | other -> failtest $"{other}"
        }

        test "The contract values the runner uses are the pinned contract's" {
            let contract =
                JsonNode
                    .Parse(
                        File.ReadAllText(
                            Path.Combine(
                                root,
                                "tests",
                                "Fixtures",
                                "MepWasm",
                                "wasm-execution-profiles.json"
                            )
                        )
                    )
                    .AsObject()

            let worker = contract["worker"].AsObject()

            let argument name =
                worker["arguments"].AsArray()
                |> Seq.find (fun item -> item["name"].GetValue<string>() = name)

            let callMs = (argument "--call-ms")["default"]

            Expect.equal
                (int64 (Contract.callDeadline Profile.ExtismKernelV1).TotalMilliseconds)
                (callMs["extism-kernel-v1"].GetValue<int64>())
                "the Extism deadline"

            Expect.equal
                (int64 (Contract.callDeadline Profile.MorphirBytesV1).TotalMilliseconds)
                (callMs["morphir-bytes-v1"].GetValue<int64>())
                "the byte-profile deadline"

            let started = worker["startedBounds"].AsObject()

            Expect.equal
                (Contract.bounds
                 |> List.map (fun bound -> bound.WireValue))
                (started["members"].AsArray()
                 |> Seq.map (fun item -> item.GetValue<string>())
                 |> List.ofSeq)
                "the bound members"

            Expect.equal
                Contract.notEnforcedReasons
                (started["notEnforcedReasons"].AsArray()
                 |> Seq.map (fun item -> item.GetValue<string>())
                 |> List.ofSeq)
                "the reasons"

            Expect.equal
                (Contract.requiredForQualification
                 |> List.map (fun bound -> bound.WireValue))
                (contract["bounds"].["requiredForQualification"].AsArray()
                 |> Seq.map (fun item -> item.GetValue<string>())
                 |> List.ofSeq)
                "the bounds qualification requires"

            Expect.equal
                (worker["events"].AsObject()
                 |> Seq.map (fun pair -> pair.Key)
                 |> List.ofSeq)
                [ "started"; "output"; "fault"; "refused"; "exited" ]
                "the events the runner reads"

            Expect.equal
                (RunLimits.ofKit (corpus.Force()).Kit).Margin
                (System.TimeSpan.FromSeconds 2.0)
                "the corpus margin"
        }

        test "The runner's limit for a run is one call deadline per frame and the margin" {
            let limits = RunLimits.ofKit (corpus.Force()).Kit
            let spin = (suiteCase "bounds/spin-extism").Case
            let bytesSpin = (suiteCase "bounds/spin-bytes").Case

            Expect.equal
                (RunLimits.runLimit limits spin spin.Runs.Head)
                (System.TimeSpan.FromSeconds 32.0)
                "30 + 2"

            Expect.equal
                (RunLimits.runLimit limits bytesSpin bytesSpin.Runs.Head)
                (System.TimeSpan.FromSeconds 12.0)
                "10 + 2"
        }
    ]
