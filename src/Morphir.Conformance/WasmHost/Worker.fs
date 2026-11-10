// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.WasmHost

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open Morphir.Conformance

/// The host a run drives: an executable and the arguments it takes before `extension worker`. The
/// runner knows nothing else about a host, so the same code path drives every host.
type HostCommand = {
    /// The path of the executable.
    Executable: string
    /// The arguments before `extension worker`, for example `exec fake-worker.dll` for `dotnet`.
    Arguments: string list
}

/// The values of the pinned profile contract (`wasm-execution-profiles.json`, contract
/// `morphir/mep-wasm-execution-profiles` 0.1.0-draft.1) that the runner needs. A test compares each
/// with the pinned file.
module Contract =
    /// The profile of a stderr event line.
    [<Literal>]
    let CommandEventProfile = "morphir/command-event"

    /// The default of `--call-ms` for a profile: the profile's call deadline.
    let callDeadline (profile: Profile) =
        match profile with
        | Profile.ExtismKernelV1 -> TimeSpan.FromMilliseconds 30000.0
        | Profile.MorphirBytesV1 -> TimeSpan.FromMilliseconds 10000.0

    /// The bounds that a started event declares, in the order of startedBounds.members.
    let bounds = [ Bound.MemoryCeiling; Bound.InstructionBudget; Bound.CallDeadline ]

    /// The reasons a started event may give for a bound it does not enforce.
    let notEnforcedReasons = [
        "no-memory-ceiling"
        "no-instruction-metering"
        "no-call-interruption"
    ]

    /// The bounds that qualification requires: bounds.requiredForQualification.
    let requiredForQualification = [ Bound.MemoryCeiling; Bound.CallDeadline ]

/// How long the runner waits for a host. The defaults are the contract's call deadlines and the
/// corpus's margin; a test passes shorter ones explicitly.
type RunLimits = {
    /// The call deadline of an `extism-kernel-v1` run.
    ExtismCallDeadline: TimeSpan
    /// The call deadline of a `morphir-bytes-v1` run.
    BytesCallDeadline: TimeSpan
    /// The corpus's fixed margin after a call deadline.
    Margin: TimeSpan
    /// How long `extension worker --help --json` may take.
    HelpTimeout: TimeSpan
}

/// The limits of a run.
module RunLimits =
    /// The contract's call deadlines and the margin of the kit.
    let ofKit (kit: KitIdentity) = {
        ExtismCallDeadline = Contract.callDeadline Profile.ExtismKernelV1
        BytesCallDeadline = Contract.callDeadline Profile.MorphirBytesV1
        Margin = TimeSpan.FromSeconds(float kit.DeadlineMarginSeconds)
        HelpTimeout = TimeSpan.FromSeconds 60.0
    }

    /// The call deadline of a profile under these limits.
    let callDeadline (limits: RunLimits) (profile: Profile) =
        match profile with
        | Profile.ExtismKernelV1 -> limits.ExtismCallDeadline
        | Profile.MorphirBytesV1 -> limits.BytesCallDeadline

    /// How long the runner waits, from the end of the write of a run's last frame, before it stops a
    /// worker that is still alive: one call deadline of the declared profile for each frame, and the
    /// margin. For a runaway case of one frame this is the end of its timing range.
    let runLimit (limits: RunLimits) (case: WasmHostCase) (run: Run) =
        let profile =
            case.DeclaredAbi
            |> Option.defaultValue case.AppliesTo

        let frames = max 1 run.Frames.Length

        TimeSpan.FromTicks(
            (callDeadline limits profile).Ticks
            * int64 frames
        )
        + limits.Margin

/// One line that the worker wrote on stderr, with the time it was read, in milliseconds from the start of
/// the worker.
type StderrLine =
    { AtMs: int64; Text: string }

    /// Stable text for diagnostics: the time and the line, such as `12 ms: <line>`.
    override this.ToString() = $"{this.AtMs} ms: {this.Text}"

/// How a worker ended.
[<RequireQualifiedAccess>]
type WorkerEnd =
    /// The worker exited by itself with this status.
    | Exited of exitCode: int
    /// The worker was still alive at the end of the run, so the runner stopped its process tree.
    | StoppedByRunner
    /// The worker did not start.
    | NotStarted of reason: string

    /// The name of the end, as the report writes it.
    member this.WireValue =
        match this with
        | Exited _ -> "exited"
        | StoppedByRunner -> "stopped-by-runner"
        | NotStarted _ -> "not-started"

    override this.ToString() =
        match this with
        | Exited code -> $"exited with status {code}"
        | StoppedByRunner -> "stopped by the runner"
        | NotStarted reason -> $"not started: {reason}"

/// What the runner saw of one worker process.
type RunObservation = {
    /// The arguments after the host's own, starting with `extension worker`.
    Arguments: string list
    /// The number of frames written completely before the worker stopped reading.
    FramesWritten: int
    /// When the write of the last frame ended, in milliseconds from the start, if every frame was written.
    LastFrameWrittenMs: int64 option
    /// Every byte of stdout.
    Stdout: byte array
    /// The stderr lines, in order.
    Stderr: StderrLine list
    /// How the worker ended.
    End: WorkerEnd
    /// When it ended, in milliseconds from the start.
    EndMs: int64
}

/// The worker adapter: it starts `<host> extension worker` with the arguments of a case, writes the frames
/// and collects stdout, the stderr lines, the exit status and the times, and always ends the worker.
module Worker =
    let private hexIdentity = Regex("^[0-9a-fA-F]{64}$")

    /// The arguments that `--help --json` takes.
    let helpArguments = [ "extension"; "worker"; "--help"; "--json" ]

    /// Reads the build identity from the stdout of `extension worker --help --json`: the one
    /// `morphir/command-result` document and its `result.buildIdentity` of 64 hexadecimal digits.
    let parseHelp (stdout: string) : Result<string, string> =
        try
            use document = JsonDocument.Parse(stdout.Trim())
            let root = document.RootElement

            let identity =
                match root.ValueKind with
                | JsonValueKind.Object ->
                    match root.TryGetProperty "result" with
                    | true, result when result.ValueKind = JsonValueKind.Object ->
                        match result.TryGetProperty "buildIdentity" with
                        | true, value when value.ValueKind = JsonValueKind.String ->
                            Some(nonNull (value.GetString()))
                        | _ -> None
                    | _ -> None
                | _ -> None

            match identity with
            | Some text when hexIdentity.IsMatch text -> Ok text
            | Some text -> Error $"result.buildIdentity \"{text}\" is not 64 hexadecimal digits"
            | None -> Error "The help document has no string result.buildIdentity"
        with :? JsonException as error ->
            Error $"The help output is not one JSON document: {error.Message}"

    /// Reads the host's build identity, once for the host: `<host> extension worker --help --json`.
    let buildIdentity
        (host: HostCommand)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        : Task<Result<string, string>> =
        task {
            try
                let! result =
                    CliAdapter.runProcess
                        timeout
                        {
                            FileName = host.Executable
                            Arguments =
                                host.Arguments
                                @ helpArguments
                            WorkingDirectory = Environment.CurrentDirectory
                        }
                        cancellationToken

                if
                    result.ExitCode
                    <> 0
                then
                    return
                        Error
                            $"extension worker --help --json exited with status {result.ExitCode}: {result.StandardError.Trim()}"
                else
                    return parseHelp result.StandardOutput
            with
            | :? TimeoutException as error -> return Error error.Message
            | :? Win32Exception as error -> return Error $"The host did not start: {error.Message}"
        }

    /// The host's build identity with its last hexadecimal digit changed: the identity of another build.
    let otherIdentity (identity: string) =
        let last =
            identity[identity.Length
                     - 1]

        let changed = if last = '0' then '1' else '0'

        identity.Substring(
            0,
            identity.Length
            - 1
        )
        + string changed

    /// The worker arguments of a case, as the corpus's worker convention gives them:
    /// `extension worker --abi <declaredAbi> --module <wasm> --build-identity <identity>`, then
    /// `--request-bytes`, `--response-bytes` and `--instructions` when the case gives them, then `--json`.
    /// An argument the case omits is left out with its value, and the other build identity changes the
    /// last digit. A case that gives none of the limits leaves the contract's defaults to the worker.
    let arguments (identity: string) (modulePath: string) (case: WasmHostCase) =
        let identity =
            match case.Worker.BuildIdentity with
            | BuildIdentity.Host -> identity
            | BuildIdentity.Other -> otherIdentity identity

        let number (value: int64) =
            value.ToString(Globalization.CultureInfo.InvariantCulture)

        let pairs = [
            match case.DeclaredAbi with
            | Some abi -> yield "--abi", Some abi.WireValue
            | None -> ()
            yield "--module", Some modulePath
            yield "--build-identity", Some identity
            match case.Worker.RequestBytes with
            | Some bytes -> yield "--request-bytes", Some(number (int64 bytes))
            | None -> ()
            match case.Worker.ResponseBytes with
            | Some bytes -> yield "--response-bytes", Some(number (int64 bytes))
            | None -> ()
            match case.Worker.Instructions with
            | Some budget -> yield "--instructions", Some(number budget)
            | None -> ()
            yield "--json", None
        ]

        [ "extension"; "worker" ]
        @ (pairs
           |> List.filter (fun (name, _) -> not (List.contains name case.Worker.Omit))
           |> List.collect (fun (name, value) ->
               name
               :: Option.toList value
           ))

    /// A frame as the worker convention writes it: a Content-Length header and the UTF-8 of the frame's
    /// value written compactly with its members in the order written.
    let encodeFrame (frame: Frame) =
        let body = Encoding.UTF8.GetBytes(Json.compact frame.Value)

        Array.append (Encoding.ASCII.GetBytes $"Content-Length: {body.Length}\r\n\r\n") body

    let private killWait = TimeSpan.FromSeconds 10.0

    /// How long the runner keeps reading stdout and stderr after the worker exited. A descendant that holds
    /// the pipes open is not waited for longer.
    let private drainWait = TimeSpan.FromSeconds 2.0

    let private isTerminatingEvent (line: string) =
        try
            use document = JsonDocument.Parse line
            let root = document.RootElement

            root.ValueKind = JsonValueKind.Object
            && (
                match root.TryGetProperty "event" with
                | true, value when value.ValueKind = JsonValueKind.String ->
                    value.GetString() = "fault"
                    || value.GetString() = "refused"
                | _ -> false
            )
        with :? JsonException ->
            false

    /// Runs one worker: starts it with the host's arguments and these, writes the frames in order and keeps
    /// stdin open until the worker exits. A worker still alive limit after the end of the last frame's write
    /// (or limit after its start, while frames are still being written) has its whole process tree stopped,
    /// and the observation records it. After a fault or refused event the worker gets grace more to exit.
    let run
        (host: HostCommand)
        (arguments: string list)
        (frames: Frame list)
        (limit: TimeSpan)
        (grace: TimeSpan)
        (cancellationToken: CancellationToken)
        : Task<RunObservation> =
        task {
            let start = ProcessStartInfo(host.Executable)

            for argument in
                host.Arguments
                @ arguments do
                start.ArgumentList.Add argument

            start.UseShellExecute <- false
            start.CreateNoWindow <- true
            start.RedirectStandardInput <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            start.StandardErrorEncoding <- UTF8Encoding(false)
            use proc = new Process(StartInfo = start)
            let clock = Stopwatch.StartNew()

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
                return {
                    Arguments = arguments
                    FramesWritten = 0
                    LastFrameWrittenMs = None
                    Stdout = [||]
                    Stderr = []
                    End = WorkerEnd.NotStarted reason
                    EndMs = clock.ElapsedMilliseconds
                }
            | Ok() ->
                let stdout = new MemoryStream()
                let stderr = ResizeArray<StderrLine>()
                let terminating = ref false

                let readStdout = task {
                    let buffer = Array.zeroCreate<byte> 16384
                    let stream = proc.StandardOutput.BaseStream
                    let mutable reading = true

                    while reading do
                        let! count = stream.ReadAsync(buffer, 0, buffer.Length)

                        if count = 0 then
                            reading <- false
                        else
                            lock stdout (fun () -> stdout.Write(buffer, 0, count))
                }

                let readStderr = task {
                    let mutable reading = true

                    while reading do
                        let! line = proc.StandardError.ReadLineAsync()

                        match line with
                        | null -> reading <- false
                        | line ->
                            if isTerminatingEvent line then
                                terminating.Value <- true

                            lock
                                stderr
                                (fun () ->
                                    stderr.Add
                                        {
                                            AtMs = clock.ElapsedMilliseconds
                                            Text = line
                                        }
                                )
                }

                let exitMs = ref 0L

                let exited =
                    proc
                        .WaitForExitAsync(CancellationToken.None)
                        .ContinueWith(fun (_: Task) -> exitMs.Value <- clock.ElapsedMilliseconds)

                let written = ref 0
                let writtenAt = ref 0L

                let write = task {
                    try
                        for frame in frames do
                            let bytes = encodeFrame frame
                            do! proc.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length)
                            do! proc.StandardInput.BaseStream.FlushAsync()

                            written.Value <-
                                written.Value
                                + 1

                            writtenAt.Value <- clock.ElapsedMilliseconds
                    with
                    | :? IOException
                    | :? ObjectDisposedException -> ()
                }

                let delay (span: TimeSpan) = Task.Delay(span, cancellationToken)

                // Phase 1: the frames, bounded by the limit from the start.
                let! _ = Task.WhenAny(write, exited, delay limit)

                // A worker that exits as soon as it read the last frame can end before the write task records
                // that write. The write cannot block on an exited worker, so wait for it a little.
                if
                    exited.IsCompleted
                    && not write.IsCompleted
                then
                    let! _ = Task.WhenAny(write, Task.Delay drainWait)
                    ()

                let lastWritten =
                    if
                        write.IsCompleted
                        && written.Value = frames.Length
                    then
                        Some writtenAt.Value
                    else
                        None

                // Phase 2: the exit, bounded by the limit from the end of the last write, with one grace after
                // a fault or a refusal.
                let waitForExit () = task {
                    if not exited.IsCompleted then
                        if write.IsCompleted then
                            let! _ = Task.WhenAny(exited, delay limit)
                            ()

                        if
                            not exited.IsCompleted
                            && terminating.Value
                        then
                            let! _ = Task.WhenAny(exited, delay grace)
                            ()
                }

                try
                    do! waitForExit ()
                with :? OperationCanceledException ->
                    ()

                let stopped = not exited.IsCompleted

                if stopped then
                    CliAdapter.stopProcessTree proc killWait
                    |> ignore

                // The pipes close when the worker and every descendant that holds them have ended.
                let! _ = Task.WhenAny(Task.WhenAll(readStdout, readStderr), Task.Delay drainWait)
                let! _ = Task.WhenAny(exited, Task.Delay killWait)

                try
                    proc.StandardInput.Close()
                with
                | :? IOException
                | :? ObjectDisposedException -> ()

                cancellationToken.ThrowIfCancellationRequested()

                let ending =
                    if stopped then WorkerEnd.StoppedByRunner
                    elif proc.HasExited then WorkerEnd.Exited proc.ExitCode
                    else WorkerEnd.StoppedByRunner

                let bytes = lock stdout (fun () -> stdout.ToArray())
                let lines = lock stderr (fun () -> List.ofSeq stderr)

                return {
                    Arguments = arguments
                    FramesWritten = written.Value
                    LastFrameWrittenMs = lastWritten
                    Stdout = bytes
                    Stderr = lines
                    End = ending
                    EndMs =
                        if
                            stopped
                            || exitMs.Value = 0L
                        then
                            clock.ElapsedMilliseconds
                        else
                            exitMs.Value
                }
        }
