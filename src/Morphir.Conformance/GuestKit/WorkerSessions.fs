// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.GuestKit

open System
open System.Collections.Generic
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Morphir.Mep
open Morphir.Conformance
open Morphir.Conformance.WasmHost

/// How long the sessions of a WorkerSessions factory may take.
[<Sealed>]
type WorkerSessionLimits private (sessionLimit: TimeSpan, exitWait: TimeSpan) =
    /// The longest span Task.Delay takes: 4294967294 ms, about 49.7 days.
    static let longest = TimeSpan.FromMilliseconds 4294967294.0

    static let defaults =
        WorkerSessionLimits(TimeSpan.FromMinutes 10.0, TimeSpan.FromSeconds 10.0)

    static let check (name: string) (what: string) (span: TimeSpan) =
        if span <= TimeSpan.Zero || span > longest then
            raise (
                ArgumentOutOfRangeException(
                    name,
                    span,
                    $"The {what} must be positive and at most 4294967294 ms, the longest span Task.Delay takes."
                )
            )

    /// The defaults: a worker may run 10 minutes, which is more than one call deadline of extism-kernel-v1 for each
    /// request of a model-tier session, and EndAsync waits 10 seconds for it to exit, as the model-tier runner waits
    /// for a session to end after morphir.exit. Every read returns the same instance.
    static member Default = defaults

    /// Limits with these spans. Each must be positive and at most 4294967294 ms, the longest span Task.Delay takes;
    /// otherwise this throws ArgumentOutOfRangeException.
    static member Create(sessionLimit: TimeSpan, exitWait: TimeSpan) =
        check (nameof sessionLimit) "session limit" sessionLimit
        check (nameof exitWait) "exit wait" exitWait
        WorkerSessionLimits(sessionLimit, exitWait)

    /// How long a worker may run from its start. A worker still running then has its process tree stopped, which
    /// ends its session: every request still waiting gets an error.
    member _.SessionLimit = sessionLimit

    /// How long EndAsync waits for a worker to exit by itself before it stops the worker's process tree.
    member _.ExitWait = exitWait

    /// Stable text for diagnostics.
    override _.ToString() = $"session limit {sessionLimit}, exit wait {exitWait}"

/// What a WorkerSessions factory saw of one worker process: its arguments, how it ended and every line it wrote on
/// stderr. Only the factory builds one.
[<Sealed>]
type WorkerSessionRecord
    internal
    (
        purpose: KitSessionPurpose,
        processId: int,
        arguments: IReadOnlyList<string>,
        ending: WorkerEnd,
        endMs: int64,
        stderr: IReadOnlyList<StderrLine>,
        events: IReadOnlyList<string>,
        fault: string option,
        problems: IReadOnlyList<string>
    ) =
    /// What the session was for.
    member _.Purpose = purpose

    /// The id of the worker process.
    member _.ProcessId = processId

    /// The arguments after the host's own, starting with extension worker.
    member _.Arguments = arguments

    /// How the worker ended. C# callers read End.WireValue or ExitCode.
    member _.End = ending

    /// The worker's exit status, or null when it did not exit by itself.
    member _.ExitCode: Nullable<int> =
        match ending with
        | WorkerEnd.Exited code -> Nullable code
        | WorkerEnd.StoppedByRunner
        | WorkerEnd.NotStarted _ -> Nullable()

    /// When the worker ended, in milliseconds from its start.
    member _.EndMs = endMs

    /// Every line the worker wrote on stderr, in order, with the time it was read.
    member _.Stderr = stderr

    /// The event member of each morphir/command-event line on stderr, in order: started, then exited or fault.
    member _.Events = events

    /// The first fault event line, for F# callers.
    member internal _.Fault = fault

    /// For C# callers: the first fault event line, or null when the worker wrote none.
    member _.FaultOrNull() : string | null = Option.toObj fault

    /// Why the session is not clean, one sentence each: the worker did not exit by itself with status 0, did not
    /// write started or exited, wrote a fault, or wrote a stderr line that is not a morphir/command-event document.
    /// Empty for a clean session.
    member _.Problems = problems

    /// Whether the session is clean: Problems is empty.
    member _.IsClean = problems.Count = 0

    /// Stable text for diagnostics.
    override _.ToString() =
        let summary =
            if problems.Count = 0 then
                "clean"
            else
                String.Join("; ", problems)

        $"""{purpose} worker {processId}: {ending}; events {String.Join(", ", events)}; {summary}"""

/// A write to the stdin of a worker that has ended: the worker exited or was stopped, or EndAsync or the disposal of
/// the factory closed its stdin. A HostSession from WorkerSessions.OpenAsync throws it from a request or from
/// morphir.exit; the write is not retried and no reply is awaited. It derives from IOException, so a catch of
/// IOException still holds. InnerException is the stream's own exception. Only the factory builds one.
[<Sealed>]
type WorkerEndedException internal (purpose: KitSessionPurpose, processId: int, inner: exn) =
    inherit
        IOException(
            $"The worker {processId} of the {purpose.WireValue} session has ended, so nothing more can be written to it: {inner.Message}",
            inner
        )

    /// What the session was for.
    member _.Purpose = purpose

    /// The id of the worker process.
    member _.ProcessId = processId

/// The stdin of a worker as its HostSession writes it. A write or a flush that fails because the worker has ended, with
/// an IOException or an ObjectDisposedException from the pipe, throws WorkerEndedException instead. No write is
/// swallowed: the session would then wait for a reply that never comes.
[<Sealed>]
type internal WorkerInput(inner: Stream, purpose: KitSessionPurpose, processId: int) =
    inherit Stream()

    static let isEnded (error: exn) =
        error :? IOException || error :? ObjectDisposedException

    let ended (error: exn) =
        WorkerEndedException(purpose, processId, error)

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = raise (NotSupportedException())

    override _.Position
        with get () = raise (NotSupportedException())
        and set (_: int64) = raise (NotSupportedException())

    override _.Seek(_: int64, _: SeekOrigin) : int64 = raise (NotSupportedException())
    override _.SetLength(_: int64) = raise (NotSupportedException())
    override _.Read(_: byte array, _: int, _: int) : int = raise (NotSupportedException())

    override _.Write(buffer: byte array, offset: int, count: int) =
        try
            inner.Write(buffer, offset, count)
        with error when isEnded error ->
            raise (ended error)

    override _.Write(buffer: ReadOnlySpan<byte>) =
        try
            inner.Write buffer
        with error when isEnded error ->
            raise (ended error)

    override _.WriteByte(value: byte) =
        try
            inner.WriteByte value
        with error when isEnded error ->
            raise (ended error)

    override _.WriteAsync(buffer: byte array, offset: int, count: int, cancellation: CancellationToken) : Task =
        task {
            try
                do! inner.WriteAsync(buffer, offset, count, cancellation)
            with error when isEnded error ->
                raise (ended error)
        }

    override _.WriteAsync(buffer: ReadOnlyMemory<byte>, cancellation: CancellationToken) : ValueTask =
        ValueTask(
            task {
                try
                    do! inner.WriteAsync(buffer, cancellation)
                with error when isEnded error ->
                    raise (ended error)
            }
            :> Task
        )

    override _.Flush() =
        try
            inner.Flush()
        with error when isEnded error ->
            raise (ended error)

    override _.FlushAsync(cancellation: CancellationToken) : Task =
        task {
            try
                do! inner.FlushAsync cancellation
            with error when isEnded error ->
                raise (ended error)
        }

/// The checks on what a worker wrote.
module internal WorkerSessionCheck =
    /// The event member of a morphir/command-event line, or None for any other line.
    let eventOf (line: string) =
        try
            use document = JsonDocument.Parse line
            let root = document.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                None
            else
                match root.TryGetProperty "profile", root.TryGetProperty "event" with
                | (true, profile), (true, name) when
                    profile.ValueKind = JsonValueKind.String
                    && profile.GetString() = Contract.CommandEventProfile
                    && name.ValueKind = JsonValueKind.String
                    ->
                    Some(nonNull (name.GetString()))
                | _ -> None
        with :? JsonException ->
            None

    /// Why a session is not clean.
    let problems (ending: WorkerEnd) (events: string list) (fault: string option) (otherLines: int) = [
        match ending with
        | WorkerEnd.Exited 0 -> ()
        | WorkerEnd.Exited code -> yield $"the worker exited with status {code}, not 0"
        | WorkerEnd.StoppedByRunner -> yield "the worker was still running, so its process tree was stopped"
        | WorkerEnd.NotStarted reason -> yield $"the worker did not start: {reason}"

        if not (List.contains "started" events) then
            yield "the worker wrote no started event"

        if not (List.contains "exited" events) then
            yield "the worker wrote no exited event"

        match fault with
        | Some line -> yield $"the worker wrote a fault event: {line}"
        | None -> ()

        if otherLines > 0 then
            yield $"the worker wrote {otherLines} stderr lines that are not morphir/command-event documents"
    ]

/// One running worker process and what it wrote on stderr.
[<Sealed>]
type internal LiveWorker(purpose: KitSessionPurpose, proc: Process, arguments: IReadOnlyList<string>, limits: WorkerSessionLimits) =
    static let killWait = TimeSpan.FromSeconds 10.0

    /// How long the factory keeps reading stderr after the worker ended. A descendant that holds the pipe open is not
    /// waited for longer.
    static let drainWait = TimeSpan.FromSeconds 2.0

    let id = proc.Id
    let clock = Stopwatch.StartNew()
    let gate = obj ()
    let lines = ResizeArray<StderrLine>()
    let mutable stopped = false
    let mutable disposed = false
    let mutable exitMs = 0L
    let mutable finished: Task<WorkerSessionRecord> option = None
    let watchdogCancel = new CancellationTokenSource()

    let exited =
        proc
            .WaitForExitAsync(CancellationToken.None)
            .ContinueWith(fun (_: Task) -> exitMs <- clock.ElapsedMilliseconds)

    let readStderr = task {
        try
            let mutable reading = true

            while reading do
                let! line = proc.StandardError.ReadLineAsync()

                match line with
                | null -> reading <- false
                | line ->
                    lock lines (fun () ->
                        lines.Add
                            {
                                AtMs = clock.ElapsedMilliseconds
                                Text = line
                            }
                    )
        with
        | :? IOException
        | :? ObjectDisposedException
        | :? InvalidOperationException -> ()
    }

    /// Stops the worker's process tree when it has not exited. It does not wait for the tree to end: the caller
    /// awaits the exit.
    let stop () =
        lock gate (fun () ->
            if not disposed && not exited.IsCompleted then
                stopped <- true

                CliAdapter.stopProcessTree proc TimeSpan.Zero
                |> ignore
        )

    let watchdog = task {
        let! _ = Task.WhenAny(exited, Task.Delay(limits.SessionLimit, watchdogCancel.Token))

        if
            not exited.IsCompleted
            && not watchdogCancel.IsCancellationRequested
        then
            stop ()
    }

    /// Waits for the worker to exit or for exitWait to end, stops the process tree when it is still running, releases
    /// the process and the watchdog, and records what it saw. It always returns the record.
    let finish (exitWait: unit -> Task) = task {
        let! _ = Task.WhenAny(exited, exitWait ())
        stop ()
        watchdogCancel.Cancel()
        let! _ = Task.WhenAny(readStderr, Task.Delay drainWait)
        let! _ = Task.WhenAny(exited, Task.Delay killWait)
        let! _ = watchdog

        try
            proc.StandardInput.Close()
        with
        | :? IOException
        | :? ObjectDisposedException
        | :? InvalidOperationException -> ()

        let ending =
            if stopped then WorkerEnd.StoppedByRunner
            elif proc.HasExited then WorkerEnd.Exited proc.ExitCode
            else WorkerEnd.StoppedByRunner

        let endMs =
            if stopped || exitMs = 0L then
                clock.ElapsedMilliseconds
            else
                exitMs

        let stderr = lock lines (fun () -> List.ofSeq lines)
        let parsed = stderr |> List.map (fun line -> line, WorkerSessionCheck.eventOf line.Text)
        let events = parsed |> List.choose snd

        let fault =
            parsed
            |> List.tryPick (fun (line, name) -> if name = Some "fault" then Some line.Text else None)

        let otherLines =
            parsed
            |> List.filter (fun (_, name) -> name.IsNone)
            |> List.length

        let problems = WorkerSessionCheck.problems ending events fault otherLines

        lock gate (fun () ->
            disposed <- true
            proc.Dispose()
        )

        watchdogCancel.Dispose()

        return
            WorkerSessionRecord(
                purpose,
                id,
                arguments,
                ending,
                endMs,
                List.toArray stderr,
                List.toArray events,
                fault,
                List.toArray problems
            )
    }

    /// The first call's finish, or a new one with this exit wait.
    let start (exitWait: unit -> Task) =
        lock gate (fun () ->
            match finished with
            | Some running -> running
            | None ->
                let running = finish exitWait
                finished <- Some running
                running
        )

    /// The id of the worker process.
    member _.Id = id

    /// Waits up to Limits.ExitWait for the worker to exit, or until the token is cancelled, stops its process tree
    /// when it has not exited, and records what it saw. The task always ends with the record; a second call returns
    /// the first call's record.
    member _.FinishAsync(cancellation: CancellationToken) =
        start (fun () -> Task.Delay(limits.ExitWait, cancellation))

    /// Stops the worker's process tree at once when it is still running, then finishes as FinishAsync does, without
    /// the exit wait. A finish already running ends as soon as the worker has.
    member _.StopAndFinishAsync() =
        stop ()
        start (fun () -> Task.CompletedTask)

/// A session factory for ModelTierRunner.RunAsync that runs each session the runner asks for in a new worker
/// process: `<host> [host arguments] extension worker --abi extism-kernel-v1 --module <module> --build-identity
/// <identity> --json`, the arguments of the wasm-host corpus's worker convention, with the build identity that the
/// host's `extension worker --help --json` reports. The session is a HostSession over the worker's stdout and stdin;
/// the factory reads the worker's stderr events. EndAsync waits for each worker to exit, stops the process tree of one
/// that does not, and reports each worker's end and events. Disposing the factory stops every worker still running and
/// releases each worker's process.
///
/// A worker that has ended cannot be written to: a later request or morphir.exit of its HostSession throws
/// WorkerEndedException, an IOException. ModelTierRunner records the step as failed and does not run the steps after
/// it. EndAsync still reports the worker.
[<Sealed>]
type WorkerSessions
    private (host: HostCommand, modulePath: string, buildIdentity: string, peer: PeerInfo, limits: WorkerSessionLimits) =
    let gate = obj ()
    let workers = ResizeArray<LiveWorker>()
    let mutable disposed = false

    let workerArguments: string array = [|
        "extension"
        "worker"
        "--abi"
        Profile.ExtismKernelV1.WireValue
        "--module"
        modulePath
        "--build-identity"
        buildIdentity
        "--json"
    |]

    /// Marks the factory disposed, then stops every worker still running and waits until each is finished.
    let disposeAsync () : Task =
        let current =
            lock gate (fun () ->
                disposed <- true
                List.ofSeq workers
            )

        current
        |> List.map (fun worker -> worker.StopAndFinishAsync() :> Task)
        |> Task.WhenAll

    /// Reads the build identity of the host from `<host> [host arguments] extension worker --help --json` and returns a
    /// factory for the module at modulePath. It throws InvalidOperationException when the help gives no build
    /// identity. host is the peer that each session's morphir.initialize introduces.
    static member CreateAsync
        (
            executable: string,
            hostArguments: IReadOnlyList<string>,
            modulePath: string,
            host: PeerInfo,
            limits: WorkerSessionLimits,
            cancellation: CancellationToken
        ) : Task<WorkerSessions> =
        task {
            let command = {
                Executable = executable
                Arguments = List.ofSeq hostArguments
            }

            match! Worker.buildIdentity command (TimeSpan.FromSeconds 60.0) cancellation with
            | Ok identity -> return new WorkerSessions(command, modulePath, identity, host, limits)
            | Error reason ->
                return
                    raise (InvalidOperationException $"The build identity of {executable} could not be read: {reason}")
        }

    /// CreateAsync with WorkerSessionLimits.Default.
    static member CreateAsync
        (
            executable: string,
            hostArguments: IReadOnlyList<string>,
            modulePath: string,
            host: PeerInfo,
            cancellation: CancellationToken
        ) : Task<WorkerSessions> =
        WorkerSessions.CreateAsync(executable, hostArguments, modulePath, host, WorkerSessionLimits.Default, cancellation)

    /// The host executable.
    member _.Executable = host.Executable

    /// The module each worker serves.
    member _.ModulePath = modulePath

    /// The build identity from the host's `extension worker --help --json`.
    member _.BuildIdentity = buildIdentity

    /// The limits of the sessions.
    member _.Limits = limits

    /// The arguments of each worker after the host's own.
    member _.WorkerArguments: IReadOnlyList<string> = workerArguments

    /// The ids of the worker processes started so far, in order.
    member _.ProcessIds: IReadOnlyList<int> =
        lock gate (fun () -> workers |> Seq.map _.Id |> Seq.toArray) :> IReadOnlyList<_>

    /// Starts a worker for one session and returns a HostSession over its stdio that offers the request's model
    /// transport. Pass this method as ModelTierRunner.RunAsync's openSession.
    ///
    /// It throws ArgumentNullException for a null request and ObjectDisposedException when the factory is disposed,
    /// before any worker starts. For a cancelled token it returns a cancelled task and starts no worker. When the
    /// worker does not start, the task fails with InvalidOperationException; when the factory is disposed while the
    /// worker starts, the worker is stopped and the task fails with ObjectDisposedException.
    ///
    /// Once the worker has ended, a write of the session, a request or morphir.exit, throws WorkerEndedException, an
    /// IOException that names the session's purpose and the worker's process id. This holds also after EndAsync or
    /// the disposal of the factory, which close the worker's stdin.
    member this.OpenAsync(request: KitSessionRequest, cancellation: CancellationToken) : Task<HostSession> =
        ArgumentNullException.ThrowIfNull request
        ObjectDisposedException.ThrowIf(lock gate (fun () -> disposed), this)

        if cancellation.IsCancellationRequested then
            Task.FromCanceled<HostSession> cancellation
        else
            let start = ProcessStartInfo(host.Executable)

            for argument in host.Arguments do
                start.ArgumentList.Add argument

            for argument in workerArguments do
                start.ArgumentList.Add argument

            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardInput <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            start.StandardErrorEncoding <- UTF8Encoding(false)
            let proc = new Process(StartInfo = start)

            let started =
                try
                    if proc.Start() then
                        Ok()
                    else
                        Error "The process did not start"
                with
                | :? Win32Exception as error -> Error error.Message
                | :? FileNotFoundException as error -> Error error.Message
                | :? DirectoryNotFoundException as error -> Error error.Message
                | :? UnauthorizedAccessException as error -> Error error.Message

            match started with
            | Error reason ->
                proc.Dispose()
                Task.FromException<HostSession>(InvalidOperationException $"The worker {host.Executable} did not start: {reason}")
            | Ok() ->
                let output = proc.StandardOutput.BaseStream
                let input = new WorkerInput(proc.StandardInput.BaseStream, request.Purpose, proc.Id)
                let worker = LiveWorker(request.Purpose, proc, workerArguments, limits)

                let added =
                    lock gate (fun () ->
                        if disposed then
                            false
                        else
                            workers.Add worker
                            true
                    )

                if added then
                    Task.FromResult(new HostSession(output, input, peer, request.OfferOrNull()))
                else
                    task {
                        let! _ = worker.StopAndFinishAsync()

                        return
                            raise (
                                ObjectDisposedException(
                                    nameof WorkerSessions,
                                    "The factory was disposed while the worker started, so the worker was stopped."
                                )
                            )
                    }

    /// OpenAsync as the delegate that ModelTierRunner.RunAsync takes, for F# callers.
    member this.Opener =
        Func<KitSessionRequest, CancellationToken, Task<HostSession>>(fun request cancellation ->
            this.OpenAsync(request, cancellation))

    /// Ends every session opened so far: waits up to Limits.ExitWait for each worker to exit, stops the process tree of
    /// one that has not, and returns what was seen of each worker, in the order they started. Call it after the run. A
    /// second call returns the same records for the same workers. Cancellation ends the exit wait at once: the workers
    /// still running are stopped and recorded, and the call then throws OperationCanceledException; a later call
    /// returns the records.
    member _.EndAsync(cancellation: CancellationToken) : Task<IReadOnlyList<WorkerSessionRecord>> =
        task {
            let current = lock gate (fun () -> List.ofSeq workers)

            let! records =
                current
                |> List.map (fun worker -> worker.FinishAsync cancellation)
                |> Task.WhenAll

            cancellation.ThrowIfCancellationRequested()
            return records :> IReadOnlyList<_>
        }

    /// Stable text for diagnostics.
    override _.ToString() =
        $"worker sessions of {host.Executable} for {modulePath}, build identity {buildIdentity}"

    interface IDisposable with
        /// Stops every worker still running, at once and without its exit wait, and blocks until each worker has ended
        /// and its process is released. That normally takes milliseconds; a process tree that does not end makes it
        /// wait up to 12 seconds, for all workers together. Later calls of OpenAsync throw ObjectDisposedException.
        member _.Dispose() =
            Task.Run(Func<Task>(disposeAsync)).GetAwaiter().GetResult()

    interface IAsyncDisposable with
        /// Stops every worker still running, at once and without its exit wait, and completes when each worker has
        /// ended and its process is released: normally within milliseconds, at most about 12 seconds when a process
        /// tree does not end. Later calls of OpenAsync throw ObjectDisposedException.
        member _.DisposeAsync() = ValueTask(disposeAsync ())
