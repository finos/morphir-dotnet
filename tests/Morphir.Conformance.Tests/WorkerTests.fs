// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Conformance.Tests.WorkerTests

open System
open System.Diagnostics
open System.IO
open Expecto
open Morphir.Conformance.WasmHost
open Morphir.Conformance.Tests.FakeWorker

let private extismOnly = [ Profile.ExtismKernelV1 ]

/// The refusal that classification/unknown-namespace expects.
let private refusal = [
    refused
        "extension.abi_unsupported"
        "the import env.open_socket is outside the extism-kernel-v1 surface"
    exitWith 3
]

/// The first run of a case passed on this alternative of an instruction-budget condition.
let private satisfiedBudget (alternative: int) (enforced: bool) : SatisfiedAlternative = {
    Run = 1
    Alternative = alternative
    Condition = Condition.Bound(Bound.InstructionBudget, enforced)
}

/// The first run of a case passed on this alternative of a byte-profile condition.
let private satisfiedProfile (alternative: int) (runs: bool) : SatisfiedAlternative = {
    Run = 1
    Alternative = alternative
    Condition = Condition.HostRuns(Profile.MorphirBytesV1, runs)
}

let private runOne (modules: (string * Text.Json.Nodes.JsonNode list) list) (caseId: string) =
    let fake = host modules
    use _ = fake.Sandbox
    let report = runSuite (options fake extismOnly) (suiteOf [ caseId ])
    report, invocations fake

[<Tests>]
let tests =
    // PlatformSpecific: these tests use the real file system, the checkout or worker processes, so the platform
    // scope of the Test target runs them on every operating system (docs/contributing/ci-os-matrix.md).
    testLabel "PlatformSpecific"
    <| testList "WASM host worker adapter and runner (task 1.5)" [
        test "The worker arguments follow the corpus's worker convention" {
            let case = (suiteCase "limits/request-over-limit").Case

            Expect.equal
                (Worker.arguments Identity "/m.wasm" case)
                [
                    "extension"
                    "worker"
                    "--abi"
                    "extism-kernel-v1"
                    "--module"
                    "/m.wasm"
                    "--build-identity"
                    Identity
                    "--request-bytes"
                    "64"
                    "--json"
                ]
                "abi, module, build identity, the case's limit, json"
        }

        test "A case that omits --abi leaves out the argument and its value" {
            let case = (suiteCase "worker/missing-abi").Case
            let arguments = Worker.arguments Identity "/m.wasm" case
            Expect.isFalse (List.contains "--abi" arguments) "no --abi"
            Expect.isFalse (List.contains "extism-kernel-v1" arguments) "no ABI value"
        }

        test "Another build identity changes only the last hexadecimal digit" {
            let case = (suiteCase "worker/other-build-identity").Case
            let arguments = Worker.arguments Identity "/m.wasm" case

            let given =
                arguments[(List.findIndex ((=) "--build-identity") arguments)
                          + 1]

            Expect.notEqual given Identity "another identity"

            Expect.equal
                (given.Substring(0, 63))
                (Identity.Substring(0, 63))
                "the same first 63 digits"
        }

        test "A frame is a Content-Length header and the compact UTF-8 body" {
            let frame = (suiteCase "extism/echo").Case.Runs.Head.Frames.Head
            let body = Json.compact frame.Value
            let bytes = Worker.encodeFrame frame

            Expect.equal
                (Text.Encoding.UTF8.GetString bytes)
                $"Content-Length: {Text.Encoding.UTF8.GetByteCount body}\r\n\r\n{body}"
                "header and body"
        }

        // Spec "Hosts are driven through a stdio worker"; task 1.5 first failing test.
        test "A fake worker's refused event passes the refusal case" {
            let report, calls =
                runOne [ "env-open-socket", refusal ] "classification/unknown-namespace"

            Expect.equal (onlyOutcome report) (CaseOutcome.Passed []) "passed"
            Expect.equal report.Host.BuildIdentity Identity "the identity from --help --json"

            Expect.equal
                (calls
                 |> List.map (List.truncate 3))
                [ [ "extension"; "worker"; "--help" ]; [ "extension"; "worker"; "--abi" ] ]
                "help once, then one worker"
        }

        test "A wrong refusal code fails the events matcher" {
            let report, _ =
                runOne
                    [
                        "env-open-socket",
                        [ refused "extension.abi_mismatch" "env.open_socket"; exitWith 3 ]
                    ]
                    "classification/unknown-namespace"

            Expect.equal (failedMatchers (onlyOutcome report)) [ "events" ] "events"
        }

        test "A refusal message without the fragment fails the events matcher" {
            let report, _ =
                runOne
                    [
                        "env-open-socket",
                        [
                            refused "extension.abi_unsupported" "an import is outside the surface"
                            exitWith 3
                        ]
                    ]
                    "classification/unknown-namespace"

            Expect.equal (failedMatchers (onlyOutcome report)) [ "events" ] "events"
        }

        test "Extra stdout bytes fail the stdoutBytes matcher" {
            let report, _ =
                runOne
                    [
                        "env-open-socket",
                        rawStdout "x"
                        :: refusal
                    ]
                    "classification/unknown-namespace"

            Expect.equal (failedMatchers (onlyOutcome report)) [ "stdoutBytes" ] "stdoutBytes"
        }

        test "Another exit status fails the exitCode matcher" {
            let report, _ =
                runOne
                    [
                        "env-open-socket",
                        [ refused "extension.abi_unsupported" "env.open_socket"; exitWith 1 ]
                    ]
                    "classification/unknown-namespace"

            Expect.equal (failedMatchers (onlyOutcome report)) [ "exitCode" ] "exitCode"
        }

        test "An echoing worker passes a case whose response is the sent frame" {
            let report, _ =
                runOne [ "extism-echo", [ started []; echo 1; read 1; exitWith 0 ] ] "extism/echo"

            Expect.equal (onlyOutcome report) (CaseOutcome.Passed []) "passed"
        }

        // Spec "Module digests are checked before a case runs": Binary does not match its text.
        test
            "A module whose bytes differ from the recorded digest is refused with both digests, and no worker starts" {
            let fake = host [ "env-open-socket", refusal ]
            use _ = fake.Sandbox
            let tampered = fake.Sandbox.PathOf "env-open-socket.wasm"
            let original = moduleFile (suiteCase "classification/unknown-namespace").Case.Module
            File.WriteAllBytes(tampered, Array.append (File.ReadAllBytes original) [| 0uy |])

            let report =
                runSuite
                    { options fake extismOnly with
                        ModuleFile = fun _ -> tampered
                    }
                    (suiteOf [ "classification/unknown-namespace" ])

            let expected = (suiteCase "classification/unknown-namespace").Case.Module.Sha256

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.Refused(expected, HostRunner.fileSha256 tampered))
                "refused with the recorded and the actual digest"

            Expect.isTrue
                (invocations fake
                 |> List.forall (List.contains "--help"))
                "no worker was started for the case"

            Expect.isEmpty report.Cases.Head.Runs "no run"
        }

        // Spec "Hosts are driven through a stdio worker": Worker left running.
        test
            "A worker still alive at the end of a case has its process tree stopped, and the case records it" {
            let pidFile =
                Path.Combine(Path.GetTempPath(), $"morphir-fake-{Guid.NewGuid():N}.pid")

            try
                let report, _ =
                    runOne
                        [ "env-open-socket", [ spawn pidFile; hang ] ]
                        "classification/unknown-namespace"

                let run = report.Cases.Head.Runs.Head
                Expect.equal run.End WorkerEnd.StoppedByRunner "stopped by the runner"

                Expect.contains
                    (failedMatchers (onlyOutcome report))
                    "exitCode"
                    "the exit status is not the worker's"

                let pids =
                    File.ReadAllText(pidFile).Split(' ')
                    |> Array.map int

                Expect.isTrue (eventually (fun () -> not (isRunning pids[0]))) "the worker is gone"

                Expect.isTrue
                    (eventually (fun () -> not (isRunning pids[1])))
                    "the worker's child is gone"
            finally
                if File.Exists pidFile then
                    File.Delete pidFile
        }

        // Spec "A runaway guest is stopped in time": a call still running after the margin fails the case
        // and the runner stops the worker.
        test "A runaway worker is stopped within the call deadline plus the margin" {
            let watch = Stopwatch.StartNew()

            let report, _ =
                runOne [ "extism-spin", [ started []; read 1; hang ] ] "bounds/spin-extism"

            watch.Stop()
            let run = report.Cases.Head.Runs.Head
            Expect.equal run.End WorkerEnd.StoppedByRunner "stopped by the runner"

            let limit =
                shortLimits.ExtismCallDeadline
                + shortLimits.Margin

            match run.LastFrameWrittenMs with
            | Some written ->
                Expect.isGreaterThanOrEqual
                    (run.EndMs
                     - written)
                    (int64 limit.TotalMilliseconds)
                    "not before the limit"

                Expect.isLessThan
                    (run.EndMs
                     - written)
                    (int64 (
                        limit.TotalMilliseconds
                        + 2000.0
                    ))
                    "soon after the limit"
            | None -> failtest "the frame was written"

            Expect.contains (failedMatchers (onlyOutcome report)) "events" "no fault event"
            Expect.isLessThan watch.Elapsed.TotalSeconds 30.0 "the short limit, not the default"
        }

        // Spec "A runaway guest is stopped in time": Metering host.
        test "A metering host that ends the spinning call with budget-exhausted passes" {
            let report, _ =
                runOne
                    [ "extism-spin", [ started []; read 1; fault "budget-exhausted"; exitWith 1 ] ]
                    "bounds/spin-extism"

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.Passed [ satisfiedBudget 1 true ])
                "passed on the budget alternative"
        }

        // Spec "A case's worker arguments reach the worker": Small instruction budget.
        test "The worker arguments of bounds/small-budget give --instructions 1000000" {
            let case = (suiteCase "bounds/small-budget").Case

            Expect.equal
                (Worker.arguments Identity "/m.wasm" case)
                [
                    "extension"
                    "worker"
                    "--abi"
                    "extism-kernel-v1"
                    "--module"
                    "/m.wasm"
                    "--build-identity"
                    Identity
                    "--instructions"
                    "1000000"
                    "--json"
                ]
                "abi, module, build identity, the case's budget, json"
        }

        // Spec "A case's worker arguments reach the worker": Case without a worker object.
        test "A case without a worker object starts the worker without any limit or budget" {
            let case = (suiteCase "bounds/spin-extism").Case
            let arguments = Worker.arguments Identity "/m.wasm" case

            for name in [ "--request-bytes"; "--response-bytes"; "--instructions" ] do
                Expect.isFalse (List.contains name arguments) $"no {name}"
        }

        test
            "--instructions reaches the fake worker, and budget-exhausted passes bounds/small-budget" {
            let report, calls =
                runOne
                    [ "extism-spin", [ started []; read 1; fault "budget-exhausted"; exitWith 1 ] ]
                    "bounds/small-budget"

            let worker =
                calls
                |> List.find (fun call -> not (List.contains "--help" call))

            let at = List.findIndex ((=) "--instructions") worker
            Expect.equal worker[at + 1] "1000000" "the budget's value follows the argument"
            Expect.equal (onlyOutcome report) (CaseOutcome.Passed []) "passed"
        }

        // Spec "A runaway guest is stopped in time": Small budget on a metering host.
        test
            "A metering host that reaches the call deadline under the small budget fails bounds/small-budget" {
            let report, _ =
                runOne
                    [ "extism-spin", [ started []; read 1; fault "call-deadline"; exitWith 1 ] ]
                    "bounds/small-budget"

            Expect.contains
                (failedMatchers (onlyOutcome report))
                "events"
                "only budget-exhausted passes"
        }

        test "A host without metering records bounds/small-budget as not_enforced" {
            let report, _ =
                runOne
                    [
                        "extism-spin",
                        [
                            started [ "instructionBudget" ]
                            read 1
                            fault "call-deadline"
                            exitWith 1
                        ]
                    ]
                    "bounds/small-budget"

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.NotEnforced [ Bound.InstructionBudget ])
                "not_enforced, not passed or failed"

            Expect.equal (HostRunner.exitCode report) 0 "the budget is optional for qualification"
        }

        // Spec "A runaway guest is stopped in time": Host without metering. Uses the contract's 30-second deadline.
        test
            "A host without metering that ends the call with call-deadline between 30 and 32 seconds passes" {
            let fake =
                host [
                    "extism-spin",
                    [
                        started [ "instructionBudget" ]
                        read 1
                        sleepMs 30300
                        fault "call-deadline"
                        exitWith 1
                    ]
                ]

            use _ = fake.Sandbox
            let whole = suite.Force()

            let report =
                runSuite
                    { options fake extismOnly with
                        Limits = RunLimits.ofKit whole.Kit
                    }
                    (suiteOf [ "bounds/spin-extism" ])

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.Passed [ satisfiedBudget 3 false ])
                "passed on the alternative for a host without metering"

            Expect.equal
                (Map.find Bound.InstructionBudget report.Bounds)
                (BoundDeclaration.Declared(BoundState.NotEnforced "no-instruction-metering"))
                "the budget is declared not enforced and stays visible"

            Expect.equal (HostRunner.exitCode report) 0 "the host can still pass"
        }

        // Spec "Cases follow the profiles a host declares": Extism-only host.
        test
            "An Extism-only host: byte-profile cases are not applicable, and the byte module is refused by name" {
            let fake =
                host [
                    "bytes-echo",
                    [
                        refused
                            "extension.abi_unsupported"
                            "this host does not run morphir-bytes-v1"
                        exitWith 3
                    ]
                ]

            use _ = fake.Sandbox

            let report =
                runSuite
                    (options fake extismOnly)
                    (suiteOf [ "bytes/echo"; "classification/bytes-profile-unsupported" ])

            let outcome caseId =
                (report.Cases
                 |> List.find (fun case -> case.Case.Case.CaseId = caseId))
                    .Outcome

            Expect.equal
                (outcome "bytes/echo")
                (CaseOutcome.NotApplicable "The host does not declare the profile morphir-bytes-v1")
                "not applicable, with the reason"

            Expect.equal
                (outcome "classification/bytes-profile-unsupported")
                (CaseOutcome.Passed [ satisfiedProfile 1 false ])
                "the refusal passes"

            Expect.equal
                (invocations fake
                 |> List.filter (fun call -> not (List.contains "--help" call))
                 |> List.length)
                1
                "the not-applicable case started no worker"
        }

        test "A host that declares the byte profile runs the byte-profile alternative" {
            let fake = host [ "bytes-echo", [ started []; echo 1; read 1; exitWith 0 ] ]
            use _ = fake.Sandbox

            let report =
                runSuite
                    (options fake [ Profile.ExtismKernelV1; Profile.MorphirBytesV1 ])
                    (suiteOf [ "classification/bytes-profile-unsupported" ])

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.Passed [ satisfiedProfile 2 true ])
                "served"
        }

        // Spec "Unenforced bounds stay visible": Deadline not enforced.
        test
            "A host that declares the call deadline not enforced: the case is not_enforced and the run does not pass" {
            let fake =
                host [
                    "extism-spin",
                    [ started [ "callDeadline" ]; read 1; fault "budget-exhausted"; exitWith 1 ]
                ]

            use _ = fake.Sandbox
            let report = runSuite (options fake extismOnly) (suiteOf [ "bounds/spin-extism" ])

            Expect.equal
                (onlyOutcome report)
                (CaseOutcome.NotEnforced [ Bound.CallDeadline ])
                "not_enforced, not passed"

            Expect.equal (HostRunner.exitCode report) 1 "qualification requires the call deadline"

            Expect.equal
                (Map.find Bound.CallDeadline report.Bounds)
                (BoundDeclaration.Declared(BoundState.NotEnforced "no-call-interruption"))
                "the declaration is in the report"
        }

        test "A host that cannot give its build identity stops the run before any case" {
            let fake = host []
            use _ = fake.Sandbox

            let broken =
                { fake.Command with
                    Arguments = [ "exec"; assembly; "--script"; fake.Sandbox.PathOf "missing.json" ]
                }

            let result =
                (HostRunner.run
                    { options fake extismOnly with
                        Host = broken
                    }
                    (suiteOf [ "classification/unknown-namespace" ])
                    Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            Expect.isError result "no identity, no run"
        }
    ]
