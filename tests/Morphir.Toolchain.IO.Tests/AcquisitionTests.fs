// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.IO.Tests.AcquisitionTests

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.Toolchain.IO.Tests.ScriptedIO

let private bytes = Encoding.UTF8.GetBytes "streamed manager object"
let private target = TargetPath.tryParse "artifacts/manager.bin" |> Result.defaultWith (fun e -> failwithf "%A" e)
let private expected: AuthenticatedTarget =
    { Length = uint64 bytes.Length; Sha256 = Sha256.ofBytes bytes; Custom = None }
let private link: TufMetaFile =
    { Version = 1UL; Length = expected.Length; Sha256 = expected.Sha256 }
let private request (clock: TimeProvider) limit =
    { Source = Uri "https://caller-configured.invalid/object"
      MaxBytes = limit
      Deadline = clock.GetUtcNow().AddMinutes 1
      Mode = AcquisitionMode.Online }

let private noPublication (store: MemoryStore) =
    Expect.equal store.PublishAttempts 0 "Refused bytes never reach publication"
    Expect.equal store.Objects.Count 0 "No partial object becomes visible"
    Expect.equal store.ActiveStages 0 "No abandoned private stage"

let private cancelled (work: Task<'T>) = task {
    try
        let! _ = work
        failtest "Caller cancellation must propagate as OperationCanceledException"
    with :? OperationCanceledException -> ()
}

[<Tests>]
let tests = testList "Bounded acquisition" [
    testCaseTask "short asynchronous chunks publish exactly the expected bytes" (fun () -> task {
        use input = new ScriptedStream([ for b in bytes -> Bytes [| b |] ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let acquisition = Acquisition(transport, clock)
        let! result = acquisition.AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
        Expect.equal result (Ok()) "Missing Content-Length is allowed when streamed integrity succeeds"
        Expect.equal store.Objects[expected.Sha256.Text] bytes "Published bytes are exact"
        Expect.equal store.PublishAttempts 1 "Only one publication"
        Expect.equal store.Disposals 1 "Private staging is released"
        Expect.equal transport.ResponsesDisposed 1 "Response ownership is released"
        Expect.isTrue input.Disposed "Content stream is disposed"
        Expect.equal transport.Source (Some ((request clock 128UL).Source)) "Only the caller's source is opened"
        Expect.equal store.Target (Some target) "The existing domain path reaches the store unchanged"
        Expect.equal store.Recorded (Some expected) "No replacement target record is invented"
        Expect.equal store.ActiveStages 0 "No private stage survives success"
        Expect.equal clock.ActiveTimers 0 "Operation deadline timer released"
    })

    testCaseTask "metadata is bounded without inventing a trusted parent" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, Some(int64 bytes.Length))
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).ReadMetadataAsync(request clock expected.Length, None, CancellationToken.None)
        Expect.equal result (Ok bytes) "Root or timestamp bytes remain untrusted input for a future verifier"
        Expect.isTrue input.Disposed "Metadata response is disposed"
    })

    for declared in [ None; Some(int64 bytes.Length) ] do
        testCaseTask $"linked metadata verifies the same exact bytes with length header {declared}" (fun () -> task {
            use input = new ScriptedStream([ Bytes bytes[..2]; Bytes bytes[3..] ])
            let transport = ScriptedTransport(input, declared)
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).ReadMetadataAsync(request clock 128UL, Some link, CancellationToken.None)
            Expect.equal result (Ok bytes) "Length and SHA256 match the existing metadata link"
        })

    for declared in [ Some -1L; Some(int64 bytes.Length - 1L); Some 129L ] do
        testCaseTask $"declared length {declared} is refused before reading or staging" (fun () -> task {
            use input = new ScriptedStream([ Bytes bytes ])
            let transport = ScriptedTransport(input, declared)
            let store = MemoryStore()
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
            Expect.isError result "The declared length is invalid, differs from the record, or exceeds the limit"
            Expect.isEmpty input.Requests "No body read"
            Expect.equal store.Starts 0 "No private object allocated"
            noPublication store
            Expect.isTrue input.Disposed "Refused response disposed"
        })

    testCaseTask "recorded length above the caller bound refuses before network or staging" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 1UL, target, expected, store, CancellationToken.None)
        Expect.equal result (Error AcquisitionFailure.TooLarge) "Check the record before allocating or opening"
        Expect.equal transport.Opens 0 "No network"
        Expect.equal store.Starts 0 "No private staging"
    })

    for name, chunks, failure in [
        "short", [ Bytes bytes[..bytes.Length - 2] ], AcquisitionFailure.LengthMismatch
        "extra", [ Bytes bytes; Bytes [| 0uy |] ], AcquisitionFailure.LengthMismatch
        "same length wrong bytes", [ Bytes(Array.create bytes.Length 0uy) ], AcquisitionFailure.DigestMismatch
    ] do
        testCaseTask $"{name} body cannot publish despite an exact declared length" (fun () -> task {
            use input = new ScriptedStream(chunks)
            let transport = ScriptedTransport(input, Some(int64 bytes.Length))
            let store = MemoryStore()
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
            Expect.equal result (Error failure) "Content-Length never replaces streamed integrity checks"
            noPublication store
            Expect.isTrue (store.StagedBytes <= int64 expected.Length) "No byte past the recorded length is copied"
            Expect.equal store.Disposals 1 "Private stage discarded"
            Expect.isTrue input.Disposed "Response disposed"
        })

    testCaseTask "unknown metadata length is stopped at the bound before copying the extra byte" (fun () -> task {
        use input = new ScriptedStream([ Bytes(Array.create 100 42uy) ])
        let transport = ScriptedTransport(input, None)
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).ReadMetadataAsync(request clock 3UL, None, CancellationToken.None)
        Expect.equal result (Error AcquisitionFailure.TooLarge) "Partial metadata is never returned"
        Expect.isTrue (input.Requests |> List.forall (fun size -> size <= 4)) "Read at most the remaining bound plus one detection byte"
        Expect.isTrue input.Disposed "Response disposed"
    })

    for name, chunks, metadataLink in [
        "short linked metadata", [ Bytes bytes[..2] ], Some link
        "long linked metadata", [ Bytes bytes; Bytes [| 0uy |] ], Some link
        "wrong linked digest", [ Bytes(Array.create bytes.Length 0uy) ], Some link
        "lying header without a parent link", [ Bytes bytes[..2] ], None
    ] do
        testCaseTask $"{name} returns no partial metadata" (fun () -> task {
            use input = new ScriptedStream(chunks)
            let transport = ScriptedTransport(input, Some(int64 bytes.Length))
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).ReadMetadataAsync(request clock 128UL, metadataLink, CancellationToken.None)
            Expect.isError result "No metadata bytes escape on an integrity refusal"
            Expect.isTrue input.Disposed "Refused metadata response disposed"
        })

    testCaseTask "huge recorded lengths never determine an allocation or overflow the read bound" (fun () -> task {
        use input = new ScriptedStream([])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let huge = { expected with Length = UInt64.MaxValue }
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock UInt64.MaxValue, target, huge, store, CancellationToken.None)
        Expect.equal result (Error AcquisitionFailure.LengthMismatch) "EOF is short even at the uint64 bound"
        Expect.isNonEmpty input.Requests "The bounded reader actually attempted the stream"
        Expect.isTrue (input.Requests |> List.forall (fun size -> size > 0 && size <= 65536)) "Working memory is bounded independently of recorded size"
        noPublication store
    })

    testCaseTask "metadata limits too large for a byte array are refused before transport" (fun () -> task {
        use input = new ScriptedStream([])
        let transport = ScriptedTransport(input, None)
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).ReadMetadataAsync(request clock UInt64.MaxValue, None, CancellationToken.None)
        Expect.equal result (Error AcquisitionFailure.InvalidRequest) "A metadata read cannot promise an impossible array"
        Expect.equal transport.Opens 0 "No allocation or transport from an invalid request"
    })

    testCaseTask "zero byte object still checks EOF and its digest" (fun () -> task {
        use input = new ScriptedStream([])
        let transport = ScriptedTransport(input, Some 0L)
        let store = MemoryStore()
        let clock = ManualClock()
        let empty = { expected with Length = 0UL; Sha256 = Sha256.ofBytes [||] }
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 0UL, target, empty, store, CancellationToken.None)
        Expect.equal result (Ok()) "An empty object has exact length and the empty SHA256"
        Expect.equal store.Objects[empty.Sha256.Text] [||] "Empty object published"
    })

    testCaseTask "interrupted response discards private staging" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes[..2]; Broken ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
        Expect.isError result "Transport interruption is a refusal"
        Expect.equal store.StagedBytes 3L "The interruption happened after private staging began"
        noPublication store
        Expect.equal store.Disposals 1 "Interrupted stage discarded"
        Expect.isTrue input.Disposed "Interrupted response disposed"
    })

    for phase in [ "response open"; "body read"; "private begin"; "private write"; "publication" ] do
        testCaseTask $"caller cancellation during {phase} propagates and releases owned resources" (fun () -> task {
            let waiting = started ()
            use input = new ScriptedStream(if phase = "body read" then [ Bytes bytes[..2]; Blocked waiting ] else [ Bytes bytes ])
            let transport = ScriptedTransport(input, None)
            let store = MemoryStore()
            let block (ct: CancellationToken) : Task = task {
                waiting.TrySetResult() |> ignore
                do! Task.Delay(Timeout.InfiniteTimeSpan, ct)
            }
            match phase with
            | "response open" -> transport.BeforeOpen <- block
            | "private begin" -> store.BeforeBegin <- block
            | "private write" -> store.BeforeWrite <- block
            | "publication" -> store.BeforePublish <- block
            | _ -> ()
            let clock = ManualClock()
            use ct = new CancellationTokenSource()
            let work = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, ct.Token)
            do! awaitPhase waiting.Task work ct.Cancel
            ct.Cancel()
            do! cancelled (work.WaitAsync(TimeSpan.FromSeconds 5.0))
            noPublication store
            Expect.equal clock.ActiveTimers 0 "Cancellation releases its deadline timer"
            if phase <> "response open" then
                Expect.isTrue input.Disposed "Cancellation disposes the response"
                Expect.equal store.Disposals store.Starts "Cancellation disposes every private stage"
        })

    for phase in [ "response open"; "body read"; "private begin"; "private write"; "publication" ] do
        testCaseTask $"deadline during {phase} covers the entire operation" (fun () -> task {
            let waiting = started ()
            use input = new ScriptedStream(if phase = "body read" then [ Blocked waiting ] else [ Bytes bytes ])
            let transport = ScriptedTransport(input, None)
            let store = MemoryStore()
            let block (ct: CancellationToken) : Task = task {
                waiting.TrySetResult() |> ignore
                do! Task.Delay(Timeout.InfiniteTimeSpan, ct)
            }
            match phase with
            | "response open" -> transport.BeforeOpen <- block
            | "private begin" -> store.BeforeBegin <- block
            | "private write" -> store.BeforeWrite <- block
            | "publication" -> store.BeforePublish <- block
            | _ -> ()
            let clock = ManualClock()
            let work = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
            do! awaitPhase waiting.Task work clock.Expire
            clock.Expire()
            let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            Expect.equal result (Error AcquisitionFailure.DeadlineExceeded) "Deadline isn't a generic repository failure"
            noPublication store
            Expect.equal clock.ActiveTimers 0 "Deadline timer released"
            if phase <> "response open" then
                Expect.isTrue input.Disposed "Deadline disposes the response"
                Expect.equal store.Disposals store.Starts "Deadline discards private staging"
        })

    testCaseTask "offline metadata and target requests make no transport or store calls" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let acquisition = Acquisition(transport, clock)
        let offline = { request clock 128UL with Mode = AcquisitionMode.Offline }
        let! metadata = acquisition.ReadMetadataAsync(offline, Some link, CancellationToken.None)
        let! artifact = acquisition.AcquireTargetAsync(offline, target, expected, store, CancellationToken.None)
        Expect.equal metadata (Error AcquisitionFailure.Offline) "Offline does not fetch metadata"
        Expect.equal artifact (Error AcquisitionFailure.Offline) "Offline does not fetch a target"
        Expect.equal transport.Opens 0 "No transport calls"
        Expect.equal store.Starts 0 "No stage"
        noPublication store
    })

    testCaseTask "expired requests make no transport or store calls" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        let expired = { request clock 128UL with Deadline = clock.GetUtcNow() }
        let! result = Acquisition(transport, clock).AcquireTargetAsync(expired, target, expected, store, CancellationToken.None)
        Expect.equal result (Error AcquisitionFailure.DeadlineExceeded) "Deadline checked before opening"
        Expect.equal transport.Opens 0 "No transport calls"
        Expect.equal store.Starts 0 "No stage"
    })

    testCaseTask "already cancelled caller makes no transport or store calls" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        use ct = new CancellationTokenSource()
        ct.Cancel()
        let work = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, ct.Token)
        do! cancelled work
        Expect.equal transport.Opens 0 "No network for a cancelled caller"
        Expect.equal store.Starts 0 "No staging for a cancelled caller"
    })

    for expiry in [ false; true ] do
        testCaseTask $"metadata pending read releases its response on cancellation, deadline={expiry}" (fun () -> task {
            let waiting = started ()
            use input = new ScriptedStream([ Bytes bytes[..2]; Blocked waiting ])
            let transport = ScriptedTransport(input, None)
            let clock = ManualClock()
            use ct = new CancellationTokenSource()
            let work = Acquisition(transport, clock).ReadMetadataAsync(request clock 128UL, Some link, ct.Token)
            do! awaitPhase waiting.Task work (fun () -> ct.Cancel(); clock.Expire())
            if expiry then
                clock.Expire()
                let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
                Expect.equal result (Error AcquisitionFailure.DeadlineExceeded) "Partial metadata is not returned at expiry"
            else
                ct.Cancel()
                do! cancelled (work.WaitAsync(TimeSpan.FromSeconds 5.0))
            Expect.isTrue input.Disposed "Partial metadata response is disposed"
            Expect.equal transport.ResponsesDisposed 1 "Response released once"
        })

    testCaseTask "no-clobber publication preserves an existing conflicting object" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let winner = [| 23uy |]
        store.Objects.Add(expected.Sha256.Text, winner)
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
        Expect.isError result "A store conflict is not successful acquisition"
        Expect.equal store.Objects[expected.Sha256.Text] winner "The winner is never overwritten"
        Expect.equal store.Disposals 1 "Losing private stage discarded"
        Expect.isTrue input.Disposed "Response disposed after store refusal"
    })

    for phase in [ "private begin"; "private write"; "publication"; "response disposal" ] do
        testCaseTask $"I/O failure during {phase} produces no successful acquisition or publication" (fun () -> task {
            use input = new ScriptedStream([ Bytes bytes ])
            let transport = ScriptedTransport(input, None)
            let store = MemoryStore()
            let fail (_: CancellationToken) = Task.FromException(System.IO.IOException "Scripted I/O failure")
            match phase with
            | "private begin" -> store.BeforeBegin <- fail
            | "private write" -> store.BeforeWrite <- fail
            | "publication" -> store.BeforePublish <- fail
            | "response disposal" -> transport.FailDisposal <- true
            | _ -> failtest "Unknown test phase"
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
            Expect.isError result "The failure cannot become a successful result"
            noPublication store
            Expect.equal store.Disposals store.Starts "Every successfully opened stage is discarded"
            Expect.equal transport.ResponsesDisposed 1 "Response disposal attempted exactly once"
            Expect.equal clock.ActiveTimers 0 "Failure releases its deadline timer"
        })

    testCaseTask "an identical immutable winner can be reused without replacement" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let winner = Array.copy bytes
        store.Objects.Add(expected.Sha256.Text, winner)
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, CancellationToken.None)
        Expect.equal result (Ok()) "An identical existing object satisfies the publication contract"
        Expect.isTrue (Object.ReferenceEquals(store.Objects[expected.Sha256.Text], winner)) "No replacement"
        Expect.equal store.Disposals 1 "Private candidate released"
    })

    testCaseTask "publication observes an already disposed response and only the complete verified bytes" (fun () -> task {
        use input = new ScriptedStream([ Bytes bytes[..2]; Bytes bytes[3..] ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        store.BeforePublish <- fun _ ->
            Expect.isTrue input.Disposed "Response closes before any visible publication"
            Expect.equal transport.ResponsesDisposed 1 "Close completes exactly once"
            Expect.equal store.StagedBytes (int64 bytes.Length) "Complete target has been staged"
            Expect.equal store.Objects.Count 0 "Bytes were private up to this point"
            Task.CompletedTask
        let clock = ManualClock()
        let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock expected.Length, target, expected, store, CancellationToken.None)
        Expect.equal result (Ok()) "Verified publication succeeds after response disposal"
    })

    testCaseTask "a not-yet-due deadline leaves the read pending until caller cancellation" (fun () -> task {
        let waiting = started ()
        use input = new ScriptedStream([ Blocked waiting ])
        let transport = ScriptedTransport(input, None)
        let store = MemoryStore()
        let clock = ManualClock()
        use ct = new CancellationTokenSource()
        let work = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, ct.Token)
        do! awaitPhase waiting.Task work ct.Cancel
        clock.Advance(TimeSpan.FromSeconds 30.0)
        Expect.isFalse work.IsCompleted "The one-minute deadline cannot fire at thirty seconds"
        ct.Cancel()
        do! cancelled (work.WaitAsync(TimeSpan.FromSeconds 5.0))
        noPublication store
    })

    testCaseTask "response cleanup failure cannot hide caller cancellation" (fun () -> task {
        let waiting = started ()
        use input = new ScriptedStream([ Bytes bytes[..2]; Blocked waiting ])
        let transport = ScriptedTransport(input, None)
        transport.FailDisposal <- true
        let store = MemoryStore()
        let clock = ManualClock()
        use ct = new CancellationTokenSource()
        let work = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, ct.Token)
        do! awaitPhase waiting.Task work ct.Cancel
        ct.Cancel()
        do! cancelled (work.WaitAsync(TimeSpan.FromSeconds 5.0))
        Expect.equal transport.ResponsesDisposed 1 "Response release is attempted despite cancellation"
        noPublication store
    })

    for failCleanup in [ false; true ] do
        testCaseTask $"cancellation after a successful publication commit preserves success, cleanupFailure={failCleanup}" (fun () -> task {
            use input = new ScriptedStream([ Bytes bytes ])
            let transport = ScriptedTransport(input, None)
            let store = MemoryStore()
            use ct = new CancellationTokenSource()
            store.AfterPublish <- ct.Cancel
            if failCleanup then store.DisposeFailure <- Some(System.IO.IOException "Cleanup after commit")
            let clock = ManualClock()
            let! result = Acquisition(transport, clock).AcquireTargetAsync(request clock 128UL, target, expected, store, ct.Token)
            Expect.isTrue ct.IsCancellationRequested "The race was exercised after publication committed"
            Expect.equal result (Ok()) "A successful atomic commit cannot become an uncommitted refusal"
            Expect.equal store.Objects[expected.Sha256.Text] bytes "Committed object remains exact"
            Expect.equal store.ActiveStages 0 "Stage disposal was still attempted"
        })
]
