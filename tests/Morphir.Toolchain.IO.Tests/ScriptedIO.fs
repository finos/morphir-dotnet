// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.IO.Tests.ScriptedIO

open System
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Toolchain.IO

type ReadStep =
    | Bytes of byte array
    | Broken
    | Blocked of TaskCompletionSource<unit>

/// No Length, Seek, synchronous Read or eager copy is available to the downloader.
type ScriptedStream(steps: ReadStep list) =
    inherit Stream()
    let mutable pending = steps
    let requests = ResizeArray<int>()
    let mutable disposed = false
    member _.Requests = List.ofSeq requests
    member _.Disposed = disposed
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())
    override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Flush() = raise (NotSupportedException())
    override _.Read(_, _, _) = raise (NotSupportedException "Only asynchronous reads are allowed.")
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    override _.ReadAsync(buffer: Memory<byte>, ct: CancellationToken) =
        ValueTask<int>(task {
            ct.ThrowIfCancellationRequested()
            ObjectDisposedException.ThrowIf(disposed, "ScriptedStream")
            requests.Add buffer.Length
            match pending with
            | [] -> return 0
            | Broken :: _ -> return raise (IOException "Interrupted response")
            | Blocked started :: _ ->
                started.TrySetResult() |> ignore
                do! Task.Delay(Timeout.InfiniteTimeSpan, ct)
                return 0
            | Bytes bytes :: tail ->
                let count = min buffer.Length bytes.Length
                bytes.AsMemory(0, count).CopyTo buffer
                pending <- if count = bytes.Length then tail else Bytes bytes[count..] :: tail
                return count
        })
    override _.Dispose(disposing) =
        disposed <- true
        base.Dispose disposing

type ScriptedTransport(body: ScriptedStream, declared: int64 option) =
    let mutable opens = 0
    let mutable responsesDisposed = 0
    let mutable beforeOpen: CancellationToken -> Task = fun _ -> Task.CompletedTask
    let mutable failDisposal = false
    let mutable source: Uri option = None
    member _.Opens = opens
    member _.ResponsesDisposed = responsesDisposed
    member _.BeforeOpen with set value = beforeOpen <- value
    member _.FailDisposal with set value = failDisposal <- value
    member _.Source = source
    interface IAcquisitionTransport with
        member _.OpenAsync(uri, ct) = task {
            opens <- opens + 1
            source <- Some uri
            do! beforeOpen ct
            return {
                new IAcquisitionResponse with
                    member _.Content = body :> Stream
                    member _.DeclaredLength = declared
                interface IAsyncDisposable with
                    member _.DisposeAsync() =
                        responsesDisposed <- responsesDisposed + 1
                        body.Dispose()
                        if failDisposal then
                            ValueTask(Task.FromException(IOException "Response disposal failed"))
                        else
                            ValueTask.CompletedTask
            }
        }

/// This is a test store, not a filesystem implementation or an installation state format.
type MemoryStore() =
    let objects = Dictionary<string, byte array>(StringComparer.Ordinal)
    let mutable starts = 0
    let mutable publishAttempts = 0
    let mutable disposals = 0
    let mutable stagedBytes = 0L
    let mutable activeStages = 0
    let mutable target: TargetPath option = None
    let mutable recorded: AuthenticatedTarget option = None
    let mutable beforeBegin: CancellationToken -> Task = fun _ -> Task.CompletedTask
    let mutable beforeWrite: CancellationToken -> Task = fun _ -> Task.CompletedTask
    let mutable beforePublish: CancellationToken -> Task = fun _ -> Task.CompletedTask
    let mutable afterPublish: unit -> unit = ignore
    let mutable disposeFailure: exn option = None
    member _.Objects = objects
    member _.Starts = starts
    member _.PublishAttempts = publishAttempts
    member _.Disposals = disposals
    member _.StagedBytes = stagedBytes
    member _.ActiveStages = activeStages
    member _.Target = target
    member _.Recorded = recorded
    member _.BeforeBegin with set value = beforeBegin <- value
    member _.BeforeWrite with set value = beforeWrite <- value
    member _.BeforePublish with set value = beforePublish <- value
    member _.AfterPublish with set value = afterPublish <- value
    member _.DisposeFailure with set value = disposeFailure <- value
    interface IAcquisitionStore with
        member _.BeginAsync(path, expected, ct) = task {
            ct.ThrowIfCancellationRequested()
            do! beforeBegin ct
            starts <- starts + 1
            activeStages <- activeStages + 1
            target <- Some path
            recorded <- Some expected
            let content = new MemoryStream()
            return {
                new IPrivateObject with
                    member _.WriteAsync(bytes, token) = ValueTask(task {
                        do! beforeWrite token
                        token.ThrowIfCancellationRequested()
                        do! content.WriteAsync(bytes, token)
                        stagedBytes <- stagedBytes + int64 bytes.Length
                    })
                    member _.PublishAsync(token) = task {
                        do! beforePublish token
                        token.ThrowIfCancellationRequested()
                        publishAttempts <- publishAttempts + 1
                        let candidate = content.ToArray()
                        let key = expected.Sha256.Text
                        match objects.TryGetValue key with
                        | true, winner when winner <> candidate ->
                            return raise (IOException "Existing immutable object differs")
                        | true, _ -> ()
                        | false, _ -> objects.Add(key, candidate)
                        afterPublish ()
                    }
                interface IAsyncDisposable with
                    member _.DisposeAsync() =
                        disposals <- disposals + 1
                        activeStages <- activeStages - 1
                        content.Dispose()
                        match disposeFailure with
                        | Some error -> ValueTask(Task.FromException error)
                        | None -> ValueTask.CompletedTask
            }
        }

/// Deterministic expiry while an asynchronous read or write is pending, with no wall-clock sleeps.
type ManualClock() =
    inherit TimeProvider()
    let mutable now = DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero)
    let timers = ResizeArray<unit -> unit>()
    let mutable activeTimers = 0
    member _.ActiveTimers = activeTimers
    override _.GetUtcNow() = now
    override _.CreateTimer(callback, state, dueTime, interval) =
        let mutable disposed = false
        let dueAfter delay = if delay = Timeout.InfiniteTimeSpan then None else Some(now + delay)
        let mutable due = dueAfter dueTime
        let mutable period = interval
        activeTimers <- activeTimers + 1
        let dispose () =
            if not disposed then
                disposed <- true
                activeTimers <- activeTimers - 1
        timers.Add(fun () ->
            if not disposed && (due |> Option.exists (fun scheduled -> scheduled <= now)) then
                due <- if period > TimeSpan.Zero then Some(now + period) else None
                callback.Invoke state)
        { new ITimer with
            member _.Change(delay, repeat) =
                if disposed then
                    false
                else
                    due <- dueAfter delay
                    period <- repeat
                    true
          interface IDisposable with
            member _.Dispose() = dispose ()
          interface IAsyncDisposable with
            member _.DisposeAsync() = dispose (); ValueTask.CompletedTask }
    member _.Advance(elapsed: TimeSpan) =
        now <- now + elapsed
        for fire in timers.ToArray() do fire ()
    member this.Expire() = this.Advance(TimeSpan.FromMinutes 2.0)

let started () = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

/// Surface an early acquisition failure directly, instead of misreporting it as a missing phase signal.
let awaitPhase (signal: Task<unit>) (work: Task<'T>) (stop: unit -> unit) = task {
    try
        let! first = Task.WhenAny(signal :> Task, work :> Task).WaitAsync(TimeSpan.FromSeconds 5.0)
        if Object.ReferenceEquals(first, work) then
            let! _ = work
            invalidOp "Acquisition completed before reaching the requested test phase"
        do! signal
    with error ->
        stop ()
        return raise error
}
