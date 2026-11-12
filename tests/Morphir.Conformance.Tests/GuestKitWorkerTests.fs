// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The worker session factory of the generic guest kit's model tier (task 4.3 of add-wasm-extension-host): each
/// session the runner asks for is a `<host> extension worker` process, driven here through the scripted fake worker
/// over stdio.
module Morphir.Conformance.Tests.GuestKitWorkerTests

open System
open System.Diagnostics
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

/// Fixed full-session requests and independent responses. No runner output is used to build this script.
let private sessionScript claim cases tuples firstVersion =
    let messages = GuestKitPlanFixture.expectedWire cases tuples firstVersion

    [
        yield started []
        for (id, method), message in List.zip cases messages do
            yield action "expectFrame" (JsonNode.Parse(Json.compact message))

            match memberOf "id" message with
            | None -> ()
            | Some requestId ->
                let parameters = at [ "params" ] message

                let reply =
                    match id with
                    | "describe"
                    | "describe-immutable" -> Ok claim
                    | "model-initialize" ->
                        Ok(
                            obj [
                                "protocolVersion", str "0.2.0-draft.1"
                                "extension", at [ "extension" ] claim
                                "capabilities",
                                at [ "capabilities" ] claim
                                |> replaceAt
                                    [ "modelTransport" ]
                                    (at [ "modelTransport" ] parameters)
                            ]
                        )
                    | "bad-digest" -> Error(-32602, "Wrong digest")
                    | "malformed-source" ->
                        Ok(
                            obj [
                                "success", JsonValue.Bool false
                                "diagnostics",
                                arr [
                                    obj [
                                        "severity", str "error"
                                        "message", str "Malformed fixed source"
                                    ]
                                ]
                            ]
                        )
                    | "unknown-method" -> Error(-32601, "Unknown method")
                    | "after-shutdown" -> Error(-32014, "Shut down")
                    | "model-shutdown" -> Ok(obj [])
                    | _ when method = "morphir.frontend.compileModel" ->
                        let version =
                            match at [ "options"; "irVersion" ] parameters with
                            | JsonValue.String value -> value
                            | _ -> failtest "Missing version"

                        Ok(
                            obj [
                                "success", JsonValue.Bool true
                                "diagnostics", arr []
                                "model", GuestKitPlanFixture.model version false
                            ]
                        )
                    | _ ->
                        Ok(
                            obj [
                                "success", JsonValue.Bool true
                                "diagnostics", arr []
                                "artifacts",
                                arr [ obj [ "path", str "main.py"; "content", pythonSource () ] ]
                            ]
                        )

                let packet =
                    match reply with
                    | Ok value -> obj [ "jsonrpc", str "2.0"; "id", requestId; "result", value ]
                    | Error(code, message) ->
                        obj [
                            "jsonrpc", str "2.0"
                            "id", requestId
                            "error", obj [ "code", num code; "message", str message ]
                        ]

                yield frame packet
        yield event [ "event", text "exited"; "exitCode", number 0 ]
        yield exitWith 0
    ]

let private fullSessionScript () =
    sessionScript
        GuestKitPlanFixture.claimBoth
        GuestKitPlanFixture.bothCases
        (GuestKitPlanFixture.threeTuples
         @ GuestKitPlanFixture.fourTuples)
        "3.0.0"

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

        let! reply = task {
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
    Expect.isTrue
        (eventually (fun () -> not (isRunning record.ProcessId)))
        "the worker process is gone"

let private shortLimits () =
    WorkerSessionLimits.Create(TimeSpan.FromSeconds 3.0, TimeSpan.FromSeconds 1.0)

/// The exception that an action throws, or None.
let private failureOf (action: unit -> unit) =
    try
        action ()
        None
    with error ->
        Some error

/// The exception that a describe request through the session ends with. The write must fail: no guest answers.
let private writeFailure (session: HostSession) : exn =
    let pending = session.DescribeAsync CancellationToken.None :> Task

    let finished =
        Task
            .WhenAny(pending, Task.Delay(TimeSpan.FromSeconds 10.0))
            .GetAwaiter()
            .GetResult()

    if not (Object.ReferenceEquals(finished, pending)) then
        failtest "the write neither failed nor got a reply"

    match pending.Exception with
    | null -> failtest "the write did not fail"
    | error ->
        match error.InnerException with
        | null -> error :> exn
        | inner -> inner

/// The only worker process the factory started.
let private onlyProcess (sessions: WorkerSessions) =
    sessions.ProcessIds
    |> Seq.exactlyOne

/// The command lines of the fake that were workers, not --help.
let private workerInvocations (fake: FakeHost) =
    invocations fake
    |> List.filter (fun arguments -> not (List.contains "--help" arguments))

[<Tests>]
let tests =
    testSequenced
    <| testList "GuestKit worker sessions" [
        for version, claim, cases, tuples in
            [
                "3.0.0",
                GuestKitPlanFixture.claimThree,
                GuestKitPlanFixture.threeCases,
                GuestKitPlanFixture.threeTuples
                "4.0.0",
                GuestKitPlanFixture.claimFour,
                GuestKitPlanFixture.fourCases,
                GuestKitPlanFixture.fourTuples
            ] do
            test $"The fixed IR {version} 16-case transcript uses one settled worker" {
                let fake = FakeWorker.host [ "generic", sessionScript claim cases tuples version ]
                use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

                use sessions =
                    create
                        fake
                        "generic"
                        (WorkerSessionLimits.Create(
                            TimeSpan.FromSeconds 20.0,
                            TimeSpan.FromSeconds 2.0
                        ))

                let result =
                    ModelTierRunner
                        .RunAsync(kit.Value, sessions.Opener, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult()

                let record =
                    sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                    |> List.ofSeq
                    |> only

                Expect.equal
                    (result.Steps
                     |> Seq.map (fun step -> step.Id, step.Method)
                     |> List.ofSeq)
                    cases
                    "the independent fixed case order"

                for step in result.Steps do
                    if
                        List.contains step.Id [
                            "model-initialized-silent"
                            "unknown-notification-silent"
                        ]
                    then
                        Expect.equal
                            step.Outcome.WireValue
                            "not-run"
                            "outer submission does not establish guest delivery"
                    else
                        Expect.equal step.Outcome StepOutcome.Passed step.Id

                Expect.equal sessions.ProcessIds.Count 1 "one actual worker process"
                Expect.equal (workerInvocations fake).Length 1 "one actual worker invocation"
                Expect.sequenceEqual result.IrVersions [ version ] "only the selected version"
                Expect.isTrue record.IsClean $"settled worker: {record}"
                Expect.equal record.ExitCode (Nullable 0) "actual exit code after reader EOF"
                expectGone record
            }

        test
            "The complete fixed plan uses one worker, with actual request checks and settled exit EOF" {
            let fake = FakeWorker.host [ "generic", fullSessionScript () ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "generic"
                    (WorkerSessionLimits.Create(TimeSpan.FromSeconds 20.0, TimeSpan.FromSeconds 2.0))

            let result =
                ModelTierRunner
                    .RunAsync(kit.Value, sessions.Opener, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal
                (result.Steps
                 |> Seq.map (fun step -> step.Id, step.Method)
                 |> List.ofSeq)
                GuestKitPlanFixture.bothCases
                "every required case remains ordered"

            for step in result.Steps do
                match step.Id with
                | "model-initialized-silent"
                | "unknown-notification-silent" ->
                    match step.Outcome with
                    | StepOutcome.NotRun reason ->
                        Expect.stringContains
                            reason
                            "delivery"
                            "outer worker proof is not inner guest proof"
                    | other -> failtest $"Notification lacks inner invocation proof: {other}"
                | _ -> Expect.equal step.Outcome StepOutcome.Passed step.Id

            Expect.equal (sessions.ProcessIds.Count) 1 "one owned process"
            Expect.equal (workerInvocations fake).Length 1 "one worker invocation, excluding help"
            Expect.equal record.Purpose KitSessionPurpose.ModelTier "one required session"
            Expect.isTrue record.IsClean $"settled worker: {record}"
            Expect.equal record.ExitCode (Nullable 0) "actual exit code"
            Expect.equal (List.ofSeq record.Events) [ "started"; "exited" ] "actual terminal events"
            Expect.sequenceEqual result.ModelIrVersions [ "3.0.0"; "4.0.0" ] "both versions"
            expectGone record
        }

        test "A complete transcript with process exit 1 cannot pass model-exit" {
            let complete = fullSessionScript ()

            let script =
                (complete
                 |> List.take (
                     complete.Length
                     - 2
                 ))
                @ [ event [ "event", text "exited"; "exitCode", number 1 ]; exitWith 1 ]

            let fake = FakeWorker.host [ "generic", script ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "generic"
                    (WorkerSessionLimits.Create(TimeSpan.FromSeconds 20.0, TimeSpan.FromSeconds 2.0))

            let result =
                ModelTierRunner
                    .RunAsync(kit.Value, sessions.Opener, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal
                (result.RequiredStep "after-shutdown").Outcome
                StepOutcome.Passed
                "the fixed transcript reached its last actual request"

            Expect.notEqual
                (result.RequiredStep "model-exit").Outcome
                StepOutcome.Passed
                "a delivered exit notification is not process success"

            Expect.equal record.ExitCode (Nullable 1) "actual settled process status"
            Expect.isFalse record.IsClean "the process failed even though stdout closed"
            expectGone record
        }

        test "Cancelling the shared runner settles its one owned worker before returning" {
            let script =
                (fullSessionScript ()
                 |> List.take 3)
                @ [ action "hang" null ]

            let fake = FakeWorker.host [ "generic", script ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "generic"
                    (WorkerSessionLimits.Create(TimeSpan.FromSeconds 20.0, TimeSpan.FromSeconds 2.0))

            use cancellation = new CancellationTokenSource()

            let opener =
                Func<KitSessionRequest, CancellationToken, Task<HostSession>>(fun request token -> task {
                    let! session = sessions.OpenAsync(request, token)
                    cancellation.CancelAfter(TimeSpan.FromSeconds 1.0)
                    return session
                })

            let error =
                failureOf (fun () ->
                    ModelTierRunner
                        .RunAsync(kit.Value, opener, cancellation.Token)
                        .GetAwaiter()
                        .GetResult()
                    |> ignore
                )

            match error with
            | Some(:? OperationCanceledException) -> ()
            | Some other ->
                failtest
                    $"Expected caller cancellation, got {other.GetType().Name}: {other.Message}"
            | None -> failtest "Caller cancellation was swallowed"

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal sessions.ProcessIds.Count 1 "no reopen on cancellation"
            Expect.equal record.End WorkerEnd.StoppedByRunner "the owned process was stopped"
            expectGone record
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

            Expect.contains
                record.Problems
                "the worker exited with status 1, not 0"
                "the status is a problem"

            Expect.contains
                record.Problems
                "the worker wrote no exited event"
                "the missing exited event is a problem"

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
                (fun problem ->
                    problem.StartsWith(
                        "the worker wrote a fault event: ",
                        StringComparison.Ordinal
                    )
                )
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
                    create
                        fake
                        "describe"
                        (WorkerSessionLimits.Create(
                            TimeSpan.FromMinutes 5.0,
                            TimeSpan.FromSeconds 1.0
                        ))

                sessions
                    .OpenAsync(describeRequest (), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()
                |> ignore

                Expect.isTrue
                    (eventually (fun () ->
                        File.Exists pidFile
                        && File.ReadAllText pidFile
                           <> ""
                    ))
                    "the child started"

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

                Expect.isTrue
                    (eventually (fun () -> not (isRunning pids[1])))
                    "the worker's child is gone"
            finally
                if File.Exists pidFile then
                    File.Delete pidFile
        }

        test "Disposing the factory stops a worker that is still running" {
            let fake = FakeWorker.host [ "describe", [ started []; hang ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            let sessions = create fake "describe" WorkerSessionLimits.Default

            sessions
                .OpenAsync(describeRequest (), CancellationToken.None)
                .GetAwaiter()
                .GetResult()
            |> ignore

            let pid =
                sessions.ProcessIds
                |> Seq.exactlyOne

            (sessions :> IDisposable).Dispose()

            Expect.isTrue (eventually (fun () -> not (isRunning pid))) "the worker process is gone"
        }

        test "A host whose help gives no build identity is refused before any session" {
            let fake = FakeWorker.host []
            File.WriteAllText(fake.Script, """{"buildIdentity":"not-hex","modules":{}}""")
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            Expect.throwsT<InvalidOperationException>
                (fun () ->
                    create fake "describe" WorkerSessionLimits.Default
                    |> ignore
                )
                "refused"

            Expect.equal
                (invocations fake
                 |> List.length)
                1
                "only --help ran"
        }

        test "Limits must be positive" {
            Expect.throwsT<ArgumentOutOfRangeException>
                (fun () ->
                    WorkerSessionLimits.Create(TimeSpan.Zero, TimeSpan.FromSeconds 1.0)
                    |> ignore
                )
                "session limit"

            Expect.throwsT<ArgumentOutOfRangeException>
                (fun () ->
                    WorkerSessionLimits.Create(TimeSpan.FromSeconds 1.0, TimeSpan.FromSeconds -1.0)
                    |> ignore
                )
                "exit wait"
        }

        test "Limits longer than Task.Delay takes are refused, and the message names the limit" {
            let longest = TimeSpan.FromMilliseconds 4294967294.0
            let tooLong = TimeSpan.FromMilliseconds 4294967295.0
            let accepted = WorkerSessionLimits.Create(longest, longest)

            Expect.equal
                accepted.SessionLimit
                longest
                "the longest span Task.Delay takes is accepted"

            for refused in
                [
                    fun () ->
                        WorkerSessionLimits.Create(tooLong, TimeSpan.FromSeconds 1.0)
                        |> ignore
                    fun () ->
                        WorkerSessionLimits.Create(TimeSpan.FromSeconds 1.0, tooLong)
                        |> ignore
                ] do
                match failureOf refused with
                | Some(:? ArgumentOutOfRangeException as error) ->
                    Expect.stringContains error.Message "4294967294" "the message names the limit"
                | other -> failtest $"Expected ArgumentOutOfRangeException, got {other}"
        }

        test "WorkerSessionLimits.Default is one instance" {
            Expect.isTrue
                (Object.ReferenceEquals(WorkerSessionLimits.Default, WorkerSessionLimits.Default))
                "the same instance on each read"
        }

        test "A stderr line's text is its time and its text" {
            let line = {
                AtMs = 12L
                Text = "{\"event\":\"started\"}"
            }

            Expect.equal (line.ToString()) "12 ms: {\"event\":\"started\"}" "stable text"
        }

        test
            "A write to a worker that has ended throws WorkerEndedException, which names the session and the worker" {
            let fake = FakeWorker.host [ "describe", [ started []; exitWith 0 ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())

            let session =
                sessions
                    .OpenAsync(describeRequest (), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            let pid = onlyProcess sessions
            Expect.isTrue (eventually (fun () -> not (isRunning pid))) "the worker exited"

            let checkEnded (error: exn) (cause: string) =
                match error with
                | :? WorkerEndedException as ended ->
                    Expect.isTrue
                        (ended :> exn :? IOException)
                        "an IOException, so existing catches hold"

                    Expect.equal ended.Purpose KitSessionPurpose.Describe $"{cause}: the purpose"
                    Expect.equal ended.ProcessId pid $"{cause}: the process id"

                    Expect.stringContains
                        ended.Message
                        "describe"
                        $"{cause}: the message names the session"

                    Expect.stringContains
                        ended.Message
                        (string pid)
                        $"{cause}: the message names the worker"

                    Expect.isNotNull ended.InnerException $"{cause}: the stream's exception is kept"
                | other ->
                    failtest
                        $"{cause}: expected WorkerEndedException, got {other.GetType().Name}: {other.Message}"

            checkEnded (writeFailure session) "after the worker exited"

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal record.End (WorkerEnd.Exited 0) "exited with 0"
            checkEnded (writeFailure session) "after EndAsync"
        }

        test "A worker ending after describe retains all planned cases and refuses later execution" {
            let script =
                (fullSessionScript ()
                 |> List.take 3)
                @ [ exitWith 0 ]

            let fake = FakeWorker.host [ "generic", script ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "generic" (shortLimits ())

            let result =
                ModelTierRunner
                    .RunAsync(kit.Value, sessions.Opener, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            Expect.equal
                (result.Steps
                 |> Seq.map (fun step -> step.Id, step.Method)
                 |> List.ofSeq)
                GuestKitPlanFixture.bothCases
                "all required entries retained after terminal failure"

            Expect.equal
                (step "describe" result).Outcome
                StepOutcome.Passed
                "actual original description"

            Expect.equal
                (step "model-initialize" result).Outcome.WireValue
                "failed"
                "cannot initialize after worker end"

            for later in
                result.Steps
                |> Seq.skip 2 do
                Expect.equal
                    later.Outcome.WireValue
                    "not-run"
                    $"{later.Id}: no observation after terminal failure"

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.isFalse record.IsClean "worker omitted its required terminal event"
            Expect.equal (sessions.ProcessIds.Count) 1 "no reopen workaround"
            expectGone record
        }
        test
            "OpenAsync after the factory is disposed throws ObjectDisposedException and starts no worker" {
            let fake = FakeWorker.host [ "describe", [ started []; exitWith 0 ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            let sessions = create fake "describe" (shortLimits ())
            (sessions :> IDisposable).Dispose()

            match
                failureOf (fun () ->
                    sessions
                        .OpenAsync(describeRequest (), CancellationToken.None)
                        .GetAwaiter()
                        .GetResult()
                    |> ignore
                )
            with
            | Some(:? ObjectDisposedException) -> ()
            | other -> failtest $"Expected ObjectDisposedException, got {other}"

            Expect.isEmpty sessions.ProcessIds "no worker is tracked"
            Expect.isEmpty (workerInvocations fake) "no worker started"
        }

        test "A null request is refused before any worker starts" {
            let fake = FakeWorker.host [ "describe", [ started []; exitWith 0 ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())

            match
                failureOf (fun () ->
                    sessions.OpenAsync(
                        Unchecked.defaultof<KitSessionRequest>,
                        CancellationToken.None
                    )
                    |> ignore
                )
            with
            | Some(:? ArgumentNullException) -> ()
            | other -> failtest $"Expected ArgumentNullException, got {other}"

            Expect.isEmpty (workerInvocations fake) "no worker started"
        }

        test "OpenAsync with a cancelled token returns a cancelled task and starts no worker" {
            let fake = FakeWorker.host [ "describe", [ started []; exitWith 0 ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            use sessions = create fake "describe" (shortLimits ())
            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()

            let opened =
                try
                    Ok(sessions.OpenAsync(describeRequest (), cancelled.Token))
                with error ->
                    Error error

            match opened with
            | Ok pending -> Expect.isTrue pending.IsCanceled "a cancelled task"
            | Error error ->
                failtest $"Expected a cancelled task, got a thrown {error.GetType().Name}"

            Expect.isEmpty (workerInvocations fake) "no worker started"
        }

        test "A cancelled EndAsync throws, and a later EndAsync still returns the records" {
            let fake = FakeWorker.host [ "describe", [ started []; hang ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "describe"
                    (WorkerSessionLimits.Create(TimeSpan.FromMinutes 5.0, TimeSpan.FromSeconds 5.0))

            sessions
                .OpenAsync(describeRequest (), CancellationToken.None)
                .GetAwaiter()
                .GetResult()
            |> ignore

            use cancelled = new CancellationTokenSource()
            cancelled.Cancel()

            match
                failureOf (fun () ->
                    sessions.EndAsync(cancelled.Token).GetAwaiter().GetResult()
                    |> ignore
                )
            with
            | Some(:? OperationCanceledException) -> ()
            | other -> failtest $"Expected OperationCanceledException, got {other}"

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal
                record.End
                WorkerEnd.StoppedByRunner
                "the cancelled call stopped the worker"

            expectGone record
        }

        test
            "Disposing the factory finishes each worker: its stdin is closed and EndAsync reports it" {
            let fake = FakeWorker.host [ "describe", [ started []; hang ] ]
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
            let sessions = create fake "describe" WorkerSessionLimits.Default

            let session =
                sessions
                    .OpenAsync(describeRequest (), CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            let pid = onlyProcess sessions
            (sessions :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

            Expect.isFalse (isRunning pid) "the worker is gone when DisposeAsync ends"

            match writeFailure session with
            | :? WorkerEndedException as ended ->
                match ended.InnerException with
                | :? ObjectDisposedException -> ()
                | other -> failtest $"Expected the worker's stdin to be closed, got {other}"
            | other ->
                failtest
                    $"Expected WorkerEndedException, got {other.GetType().Name}: {other.Message}"

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.equal record.End WorkerEnd.StoppedByRunner "stopped by disposal"
        }
    ]

[<Tests>]
let descendantOwnershipTests =
    testSequenced
    <| testList "GuestKit descendant settlement" [
        test "Excessive stderr fails capture and settles the owned worker" {
            let fake =
                FakeWorker.host [
                    "stderr-limit", [ started []; stderr (String('x', 65537)); hang ]
                ]

            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "stderr-limit"
                    (WorkerSessionLimits.Create(
                        TimeSpan.FromSeconds 5.0,
                        TimeSpan.FromMilliseconds 500.0
                    ))

            sessions
                .OpenAsync(describeRequest (), CancellationToken.None)
                .GetAwaiter()
                .GetResult()
            |> ignore

            let record =
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> List.ofSeq
                |> only

            Expect.exists
                record.Problems
                (fun problem -> problem.Contains("stderr capture limit", StringComparison.Ordinal))
                "capture bounds are explicit, not a complete stderr claim"

            Expect.isFalse record.IsClean "bounded capture cannot qualify"
            expectGone record
        }

        test "A descendant is settled even after the worker root exits" {
            let fake =
                FakeWorker.host [
                    "root-exits", [ spawn "{PIDFILE}"; started []; sleepMs 1500; exitWith 0 ]
                ]

            let pidFile = fake.Sandbox.PathOf "descendant.pid"

            let script =
                File
                    .ReadAllText(fake.Script)
                    .Replace("{PIDFILE}", pidFile.Replace("\\", "\\\\"))

            File.WriteAllText(fake.Script, script)
            use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()

            use sessions =
                create
                    fake
                    "root-exits"
                    (WorkerSessionLimits.Create(TimeSpan.FromSeconds 10.0, TimeSpan.FromSeconds 3.0))

            sessions
                .OpenAsync(describeRequest (), CancellationToken.None)
                .GetAwaiter()
                .GetResult()
            |> ignore

            Expect.isTrue
                (eventually (fun () ->
                    File.Exists pidFile
                    && File.ReadAllText(pidFile).Contains(' ')
                ))
                "owned descendant identity is available"

            let ids =
                File.ReadAllText(pidFile).Split(' ')
                |> Array.map int

            use descendant = Process.GetProcessById(ids[1])

            try
                sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult()
                |> ignore

                Expect.isTrue
                    descendant.HasExited
                    "root exit must not release a live owned descendant"
            finally
                if not descendant.HasExited then
                    descendant.Kill(true)
                    descendant.WaitForExit()
        }
    ]

[<Tests>]
let executableResolutionTests =
    testSequenced
    <| testList "GuestKit executable resolution" [
        for fromPath in [ false; true ] do
            testCase $"Probe and worker retain one absolute executable, PATH={fromPath}" (fun () ->
                let fake = FakeWorker.host [ "resolution", [ started []; hang ] ]
                use _sandbox = fake.Sandbox
                use _gate = ProcessGate.Shared.EnterAsync().GetAwaiter().GetResult()
                let absolute = Path.GetFullPath(fake.Command.Executable)
                let previous = Environment.GetEnvironmentVariable "PATH"
                try
                    let executable =
                        if fromPath then
                            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName absolute)
                            Path.GetFileNameWithoutExtension absolute
                        else Path.GetRelativePath(Directory.GetCurrentDirectory(), absolute)
                    let command = { fake with Command = { fake.Command with Executable = executable } }
                    use sessions = create command "resolution" (WorkerSessionLimits.Create(TimeSpan.FromSeconds 5.0, TimeSpan.FromMilliseconds 100.0))
                    Expect.equal sessions.Executable absolute "the help probe's executable is retained as an absolute path"
                    sessions.OpenAsync(describeRequest (), CancellationToken.None).GetAwaiter().GetResult() |> ignore
                    let record = sessions.EndAsync(CancellationToken.None).GetAwaiter().GetResult() |> List.ofSeq |> only
                    expectGone record
                finally Environment.SetEnvironmentVariable("PATH", previous)
            )
    ]
