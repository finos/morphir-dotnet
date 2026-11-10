// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The worker session factory of the generic guest kit's model tier (task 4.3 of add-wasm-extension-host): each
/// session the runner asks for is a `<host> extension worker` process, driven here through the scripted fake worker
/// over stdio.
module Morphir.Conformance.Tests.GuestKitWorkerTests

open System
open System.IO
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Mep
open Morphir.Conformance.WasmHost
open Morphir.Conformance.GuestKit
open Morphir.Conformance.Tests.FakeWorker
open Morphir.Conformance.Tests.GuestKitFakeGuest
open Morphir.TestSupport

/// A framed message on the fake's stdout.
let private frame (value: JsonValue) =
    action "frame" (JsonNode.Parse(Json.compact value))

/// The JSON-RPC response to the request with this id, as the scripted guest answers it.
let private response (id: int) (reply: Result<JsonValue, int * string>) =
    match reply with
    | Ok result -> obj [ "jsonrpc", str "2.0"; "id", num id; "result", result ]
    | Error(code, message) ->
        obj [
            "jsonrpc", str "2.0"
            "id", num id
            "error", obj [ "code", num code; "message", str message ]
        ]

/// The fake's script for one session: started, then for each request that the in-process run's guest received, in
/// order, read it and answer it as the scripted guest answered it. HostSession numbers the requests of a session from 0
/// on both channels, and sends morphir.initialized after initialize and morphir.exit at the end.
let private sessionScript (requests: (string * JsonValue) list) = [
    started []
    for index, (method, parameters) in List.indexed requests do
        read 1
        frame (response index (answer faithful method parameters))

        if method = "morphir.initialize" then
            read 1
    read 1
    event [ "event", text "exited"; "exitCode", number 0 ]
    exitWith 0
]

let private describeRequest () =
    KitSessionRequest(KitSessionPurpose.Describe, None)

let private create (fake: FakeHost) (moduleName: string) (limits: WorkerSessionLimits) =
    WorkerSessions
        .CreateAsync(
            fake.Command.Executable,
            List.toArray fake.Command.Arguments,
            fake.Sandbox.PathOf $"{moduleName}.wasm",
            GuestKitFakeGuest.host,
            limits,
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()

/// One describe request through a session of the factory, then the end of the factory's sessions. A worker that has
/// ended makes HostSession's write fail with an IOException, or settles the request with an error.
let private describeOnce (sessions: WorkerSessions) =
    task {
        let! session = sessions.OpenAsync(describeRequest (), CancellationToken.None)
        let run = session.RunAsync CancellationToken.None

        let! reply =
            task {
                try
                    let! reply = session.DescribeAsync CancellationToken.None
                    return Result.isOk reply
                with :? IOException ->
                    return false
            }

        let! records = sessions.EndAsync CancellationToken.None
        let! _ = Task.WhenAny(run :> Task, Task.Delay(TimeSpan.FromSeconds 10.0))
        return reply, List.ofSeq records
    }
    |> fun running -> running.GetAwaiter().GetResult()

let private only (records: WorkerSessionRecord list) =
    match records with
    | [ record ] -> record
    | other -> failtest $"Expected one session, got {other.Length}"

let private expectGone (record: WorkerSessionRecord) =
    Expect.isTrue (eventually (fun () -> not (isRunning record.ProcessId))) "the worker process is gone"

let private shortLimits () =
    WorkerSessionLimits.Create(TimeSpan.FromSeconds 3.0, TimeSpan.FromSeconds 1.0)

[<Tests>]
let tests =
    testSequenced
    <| testList "GuestKit worker sessions" [
        test "The model tier runs through two worker sessions over stdio, each started with the corpus's worker arguments" {
            let inProcess, opened = run faithful

            let requestsOf purpose =
                opened
                |> List.find (fun (request: KitSessionRequest, _) -> request.Purpose = purpose)
                |> fun (_, channel) -> channel.Requests

            let fake =
                FakeWorker.host [
                    "describe", sessionScript (requestsOf KitSessionPurpose.Describe)
                    "model", sessionScript (requestsOf KitSessionPurpose.ModelTier)
                ]

            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use describe = create fake "describe" WorkerSessionLimits.Default
            use model = create fake "model" WorkerSessionLimits.Default

            let opener =
                Func<KitSessionRequest, CancellationToken, Task<HostSession>>(fun request cancellation ->
                    if request.Purpose = KitSessionPurpose.Describe then
                        describe.OpenAsync(request, cancellation)
                    else
                        model.OpenAsync(request, cancellation)
                )

            let result =
                ModelTierRunner
                    .RunAsync(kit.Value, opener, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            let records =
                [ describe; model ]
                |> List.collect (fun sessions ->
                    sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                    |> List.ofSeq
                )

            let notPassed =
                result.Steps
                |> Seq.filter (fun step -> step.Outcome <> StepOutcome.Passed)
                |> Seq.map string
                |> List.ofSeq

            Expect.isEmpty notPassed "every step passes through the worker"

            Expect.equal
                (result.Steps |> Seq.map _.Id |> List.ofSeq)
                (inProcess.Steps |> Seq.map _.Id |> List.ofSeq)
                "the same steps as in-process"

            Expect.equal (List.ofSeq result.ModelIrVersions) [ "3.0.0"; "4.0.0" ] "both IR versions"
            Expect.equal describe.BuildIdentity Identity "the identity from --help --json"

            Expect.equal
                (records |> List.map _.Purpose)
                [ KitSessionPurpose.Describe; KitSessionPurpose.ModelTier ]
                "one session for each purpose"

            for record in records do
                Expect.isTrue record.IsClean $"a clean session: {record}"
                Expect.equal record.ExitCode (Nullable 0) "exit status 0"
                Expect.equal (List.ofSeq record.Events) [ "started"; "exited" ] "started and exited"
                Expect.isNull (record.FaultOrNull()) "no fault"
                expectGone record

            let workers =
                invocations fake
                |> List.filter (fun arguments -> not (List.contains "--help" arguments))

            Expect.equal
                workers
                [
                    for name in [ "describe"; "model" ] ->
                        [
                            "extension"
                            "worker"
                            "--abi"
                            "extism-kernel-v1"
                            "--module"
                            fake.Sandbox.PathOf $"{name}.wasm"
                            "--build-identity"
                            Identity
                            "--json"
                        ]
                ]
                "the corpus's worker arguments: --abi, --module, --build-identity and --json"
        }

        test "A worker that exits before it answers is reported with its status, and is gone" {
            let fake = FakeWorker.host [ "describe", [ started []; exitWith 1 ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())

            let answered, records = describeOnce sessions
            let record = only records

            Expect.isFalse answered "no describe result"
            Expect.equal record.End (WorkerEnd.Exited 1) "exited with 1"
            Expect.isFalse record.IsClean "not clean"
            Expect.contains record.Problems "the worker exited with status 1, not 0" "the status is a problem"
            Expect.contains record.Problems "the worker wrote no exited event" "the missing exited event is a problem"
            expectGone record
        }

        test "A worker that writes a fault is reported with the fault line, and is gone" {
            let fake =
                FakeWorker.host [ "describe", [ started []; read 1; fault "failure"; exitWith 1 ] ]

            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())

            let answered, records = describeOnce sessions
            let record = only records

            Expect.isFalse answered "no describe result"
            Expect.equal record.End (WorkerEnd.Exited 1) "exited with 1"
            Expect.equal (List.ofSeq record.Events) [ "started"; "fault" ] "started and fault"

            match record.FaultOrNull() with
            | null -> failtest "the fault line is kept"
            | line -> Expect.stringContains line "\"kind\":\"failure\"" "the fault line"

            Expect.exists
                record.Problems
                (fun problem -> problem.StartsWith("the worker wrote a fault event: ", StringComparison.Ordinal))
                "the fault is a problem"

            expectGone record
        }

        test "A worker that never answers is stopped at the session limit, which ends the request" {
            let fake = FakeWorker.host [ "describe", [ started []; read 1; hang ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())

            let answered, records = describeOnce sessions
            let record = only records

            Expect.isFalse answered "no describe result"
            Expect.equal record.End WorkerEnd.StoppedByRunner "stopped"
            Expect.equal record.ExitCode (Nullable()) "no exit status of its own"

            Expect.contains
                record.Problems
                "the worker was still running, so its process tree was stopped"
                "stopping is a problem"

            expectGone record
        }

        test "A worker that never exits has its process tree stopped at the end of the sessions" {
            let pidFile =
                Path.Combine(Path.GetTempPath(), $"morphir-fake-{Guid.NewGuid():N}.pid")

            try
                let fake = FakeWorker.host [ "describe", [ spawn pidFile; started []; hang ] ]
                use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

                use sessions =
                    create fake "describe" (WorkerSessionLimits.Create(TimeSpan.FromMinutes 5.0, TimeSpan.FromSeconds 1.0))

                sessions.OpenAsync(describeRequest (), CancellationToken.None).GetAwaiter().GetResult()
                |> ignore

                Expect.isTrue (eventually (fun () -> File.Exists pidFile && File.ReadAllText pidFile <> "")) "the child started"

                let record =
                    sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                    |> List.ofSeq
                    |> only

                Expect.equal record.End WorkerEnd.StoppedByRunner "stopped"
                Expect.equal (List.ofSeq record.Events) [ "started" ] "started only"

                let pids =
                    File.ReadAllText(pidFile).Split(' ')
                    |> Array.map int

                Expect.equal pids[0] record.ProcessId "the worker wrote its own id"
                expectGone record
                Expect.isTrue (eventually (fun () -> not (isRunning pids[1]))) "the worker's child is gone"
            finally
                if File.Exists pidFile then
                    File.Delete pidFile
        }

        test "Disposing the factory stops a worker that is still running" {
            let fake = FakeWorker.host [ "describe", [ started []; hang ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            let sessions = create fake "describe" WorkerSessionLimits.Default

            sessions.OpenAsync(describeRequest (), CancellationToken.None).GetAwaiter().GetResult()
            |> ignore

            let pid = sessions.ProcessIds |> Seq.exactlyOne
            (sessions :> IDisposable).Dispose()

            Expect.isTrue (eventually (fun () -> not (isRunning pid))) "the worker process is gone"
        }

        test "A host whose help gives no build identity is refused before any session" {
            let fake = FakeWorker.host []
            File.WriteAllText(fake.Script, """{"buildIdentity":"not-hex","modules":{}}""")
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            Expect.throwsT<InvalidOperationException>
                (fun () -> create fake "describe" WorkerSessionLimits.Default |> ignore)
                "refused"

            Expect.equal (invocations fake |> List.length) 1 "only --help ran"
        }

        test "Limits must be positive" {
            Expect.throwsT<ArgumentOutOfRangeException>
                (fun () -> WorkerSessionLimits.Create(TimeSpan.Zero, TimeSpan.FromSeconds 1.0) |> ignore)
                "session limit"

            Expect.throwsT<ArgumentOutOfRangeException>
                (fun () -> WorkerSessionLimits.Create(TimeSpan.FromSeconds 1.0, TimeSpan.FromSeconds -1.0) |> ignore)
                "exit wait"
        }
    ]
