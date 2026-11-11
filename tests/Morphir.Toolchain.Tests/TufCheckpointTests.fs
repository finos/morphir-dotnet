// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Tests for one pure acceptance core and an awaited, injected checkpoint driver.
/// No network, persistence format, production root or production authority is configured here.
module Morphir.Toolchain.Tests.TufCheckpointTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.Toolchain.Tests.TufFixture

type private Fixture =
    { Initial: TufTrustedSet
      Source: MetadataSource
      Buffers: Map<TufRole, byte array>
      Expected: Map<TufRole, byte array>
      Canonical: Map<TufRole, byte array>
      RootKeyIds: string list }

let private anchor root =
    match TrustAnchor.load TrustAnchorPurpose.TestOnly (canonical root) with
    | Ok(trusted, _) -> trusted
    | Error failure -> failtestf "TEST-ONLY fixture anchor refused: %A" failure

let private fixture () =
    // Whitespace outside signed changes exact envelope bytes without changing its signature input.
    // The existing fixture relinker hashes the bytes actually served, including these replacements.
    let metadata =
        metadataSet "metadata"
        |> replaceRoleText "targets" "\"signatures\":" "\"signatures\" :\n"
        |> replaceRoleText "snapshot" "\"signatures\":" "\"signatures\" :\n"
        |> replaceRoleText "timestamp" "\"signatures\":" "\"signatures\" :\n"
        |> relink

    let nextKeys = [ newTestKey "checkpoint-root-a"; newTestKey "checkpoint-root-b" ]
    let keys =
        match at [ "signed"; "keys" ] metadata.Root with
        | Value.Object current -> Value.Object(current @ [ for key in nextKeys -> key.KeyId, keyValue key ])
        | _ -> failtest "Fixture root keys must be an object"

    let nextRoot =
        memberOf "signed" metadata.Root
        |> setAt [ "version" ] (number 2UL)
        |> setAt [ "keys" ] keys
        |> setAt [ "roles"; "root" ]
            (Value.Object [ "keyids", Value.Array [ for key in nextKeys -> text key.KeyId ]; "threshold", number 2UL ])
        |> fun signed -> envelope signed (keysOf "root" @ nextKeys)

    let buffers =
        Map.ofList [
            TufRole.Root, JsonText.toBytes nextRoot
            TufRole.Timestamp, served "timestamp" metadata
            TufRole.Snapshot, served "snapshot" metadata
            TufRole.Targets, served "targets" metadata
        ]
    let source = {
        Root = fun version -> if version = 2UL then Some buffers[TufRole.Root] else None
        Timestamp = fun () -> Some buffers[TufRole.Timestamp]
        Snapshot = fun version -> if version = versionOf metadata.Snapshot then Some buffers[TufRole.Snapshot] else None
        Targets = fun version -> if version = versionOf metadata.Targets then Some buffers[TufRole.Targets] else None
    }
    { Initial = anchor metadata.Root
      Source = source
      Buffers = buffers
      Expected = buffers |> Map.map (fun _ bytes -> Array.copy bytes)
      Canonical =
        Map.ofList [
            TufRole.Root, canonical nextRoot
            TufRole.Timestamp, canonical metadata.Timestamp
            TufRole.Snapshot, canonical metadata.Snapshot
            TufRole.Targets, canonical metadata.Targets
        ]
      RootKeyIds = nextKeys |> List.map _.KeyId }

let private requestKey (request: TufMetadataRequest) = request.Role, request.Version

let private fetch (fixture: Fixture) (reads: ResizeArray<TufRole * uint64 option>) request (ct: CancellationToken) =
    ct.ThrowIfCancellationRequested()
    reads.Add(requestKey request)
    let bytes =
        match request.Role, request.Version with
        | TufRole.Root, Some version -> fixture.Source.Root version
        | TufRole.Timestamp, None -> fixture.Source.Timestamp()
        | TufRole.Snapshot, Some version -> fixture.Source.Snapshot version
        | TufRole.Targets, Some version -> fixture.Source.Targets version
        | _ -> failtestf "Invalid role/version request: %A" (requestKey request)
    Task.FromResult(Ok bytes)

let private allReads = [
    TufRole.Root, Some 2UL
    TufRole.Root, Some 3UL
    TufRole.Timestamp, None
    TufRole.Snapshot, Some 1UL
    TufRole.Targets, Some 1UL
]

let private readsThrough role =
    let count =
        match role with
        | TufRole.Root -> 1
        | TufRole.Timestamp -> 3
        | TufRole.Snapshot -> 4
        | TufRole.Targets -> 5
    List.take count allReads

let private roles = [ TufRole.Root; TufRole.Timestamp; TufRole.Snapshot; TufRole.Targets ]

let private acknowledgedBefore role (trusted: TufTrustedSet) =
    Expect.equal trusted.Root.Version (if role = TufRole.Root then 1UL else 2UL) "Only acknowledged root is durable"
    Expect.equal (trusted.Timestamp |> Option.map _.Version) (if role = TufRole.Snapshot || role = TufRole.Targets then Some 1UL else None) "Acknowledged timestamp"
    Expect.equal (trusted.Snapshot |> Option.map _.Version) (if role = TufRole.Targets then Some 1UL else None) "Acknowledged snapshot"
    Expect.isNone trusted.Targets "No acknowledged targets exist before the last checkpoint"

let private checkPending fixture role (progress: TufCheckpointProgress) =
    acknowledgedBefore role progress.Acknowledged
    Expect.equal progress.Accepted.Root.Version 2UL "Accepted root security change remains visible as accepted, not durably acknowledged"
    Expect.equal progress.Accepted.Root.Roles[TufRole.Root].KeyIds fixture.RootKeyIds "Old root keys were replaced in accepted state"
    Expect.equal progress.Accepted.Purpose TrustAnchorPurpose.TestOnly "No purpose upgrade"
    Expect.equal (progress.Accepted.Timestamp |> Option.map _.Version) (if role = TufRole.Root then None else Some 1UL) "Accepted timestamp is reported truthfully even before acknowledgement"
    Expect.equal (progress.Accepted.Snapshot |> Option.map _.Version) (if role = TufRole.Snapshot || role = TufRole.Targets then Some 1UL else None) "Accepted snapshot progress"
    Expect.equal (progress.Accepted.Targets |> Option.map _.Version) (if role = TufRole.Targets then Some 1UL else None) "Pending targets remain unacknowledged, not completed authority"
    match progress.Pending with
    | Some checkpoint ->
        Expect.equal checkpoint.Role role "The unacknowledged checkpoint is retained"
        Expect.equal checkpoint.Version (if role = TufRole.Root then 2UL else 1UL) "The accepted checkpoint's version"
        Expect.equal checkpoint.Accepted progress.Accepted "Pending evidence and reported acceptance agree"
        Expect.equal checkpoint.EnvelopeBytes fixture.Expected[role] "Its accepted evidence is exact"
    | None -> failtest "A refused or cancelled acknowledgement must identify its pending checkpoint"

let private completed (result: TufCheckpointResult) =
    match result with
    | TufCheckpointResult.Completed(RefreshResult.Refreshed accepted, progress) ->
        Expect.equal progress.Acknowledged accepted "Completion uses only acknowledged state"
        Expect.equal progress.Accepted accepted "All accepted state has been acknowledged"
        Expect.isNone progress.Pending "No outstanding acknowledgement at completion"
        accepted
    | other -> failtestf "Expected a fully acknowledged refresh, got %A" other

let private waitForCheckpoint (signal: Task<TufAcceptedCheckpoint>) (work: Task<'T>) (stop: unit -> unit) = task {
    try
        let! first = Task.WhenAny(signal :> Task, work :> Task).WaitAsync(TimeSpan.FromSeconds 5.0)
        if obj.ReferenceEquals(first, work) then
            let! _ = work
            failtest "Refresh ended before the requested checkpoint was reached"
        return! signal
    with error ->
        stop ()
        return raise error
}

let private cancelOnDispose (ct: CancellationTokenSource) =
    { new IDisposable with member _.Dispose() = ct.Cancel() }

[<Tests>]
let exactEnvelopeTests = testList "TUF checkpoint exact accepted envelopes" [
    testCase "the pull core exposes the accepted root before asking for the next input" (fun () ->
        let fixture = fixture ()
        match TufAcceptance.start referenceTime fixture.Initial with
        | TufAcceptanceStep.Request(request, supply) ->
            // The outstanding pure request must own the trust state it will verify against.
            for KeyValue(_, key) in fixture.Initial.Root.Keys do Array.Fill(key, 0uy)
            Expect.equal (requestKey request) (TufRole.Root, Some 2UL) "Start at root N+1"
            Expect.equal request.MaxBytes TufMetadata.rootLimit "The existing profile root bound is reused"
            Expect.isNone request.Link "A root rotation is not given an invented parent digest link"
            match supply (Ok(Some fixture.Buffers[TufRole.Root])) with
            | TufAcceptanceStep.Accepted(checkpoint, acknowledge) ->
                Expect.equal checkpoint.Role TufRole.Root "Accepted root checkpoint"
                Expect.equal checkpoint.Version 2UL "Accepted envelope version"
                Expect.equal checkpoint.EnvelopeBytes fixture.Expected[TufRole.Root] "Exact indented root envelope"
                Expect.equal checkpoint.Accepted.Root.Roles[TufRole.Root].KeyIds fixture.RootKeyIds "New keys are accepted"
                match acknowledge () with
                | TufAcceptanceStep.Request(next, _) -> Expect.equal (requestKey next) (TufRole.Root, Some 3UL) "Only acknowledgement permits the next root request"
                | other -> failtestf "Expected the next request after acknowledgement, got %A" other
            | other -> failtestf "Expected an accepted checkpoint, got %A" other
        | other -> failtestf "Expected a metadata request, got %A" other)

    testCaseTask "every acknowledged envelope is exact and isolated from source and consumer buffer mutations" (fun () -> task {
        let fixture = fixture ()
        let reads = ResizeArray()
        let checkpoints = ResizeArray<TufAcceptedCheckpoint>()
        let persist (checkpoint: TufAcceptedCheckpoint) (_: CancellationToken) =
            let exact = fixture.Expected[checkpoint.Role]
            Expect.equal checkpoint.EnvelopeBytes exact "The signed envelope is retained byte for byte"
            Expect.notEqual checkpoint.EnvelopeBytes fixture.Canonical[checkpoint.Role] "Checkpoint evidence is not re-encoded canonical JSON"
            Array.Fill(fixture.Buffers[checkpoint.Role], 0uy)
            let borrowed = checkpoint.EnvelopeBytes
            Array.Fill(borrowed, 255uy)
            let borrowedState = checkpoint.Accepted
            for KeyValue(_, key) in borrowedState.Root.Keys do Array.Fill(key, 0uy)
            Expect.equal checkpoint.EnvelopeBytes exact "Neither source mutation nor mutating a returned copy changes evidence"
            Expect.notEqual checkpoint.Accepted.Root.Keys borrowedState.Root.Keys "Borrowed parsed keys cannot mutate checkpoint state or future verification"
            checkpoints.Add checkpoint
            Task.FromResult(Ok())
        let! result = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist CancellationToken.None
        let accepted = completed result
        Expect.equal (checkpoints |> Seq.map _.Role |> Seq.toList) roles "One checkpoint for every accepted role/root"
        for checkpoint in checkpoints do
            Expect.equal checkpoint.EnvelopeBytes fixture.Expected[checkpoint.Role] "Retained checkpoint remains immutable after all continuations"
        Expect.equal (List.ofSeq reads) allReads "Existing fetch order is unchanged"
        Expect.equal accepted.Purpose TrustAnchorPurpose.TestOnly "No production authority is created"
    })

    testCaseTask "immediate acknowledgements preserve the synchronous refresh result" (fun () -> task {
        let fixture = fixture ()
        let expected = TufClient.refresh referenceTime fixture.Initial fixture.Source
        let reads = ResizeArray()
        let! actual = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) (fun _ _ -> Task.FromResult(Ok())) CancellationToken.None
        match actual with
        | TufCheckpointResult.Completed(result, progress) ->
            Expect.equal result expected "The compatibility driver and awaited driver use the same acceptance behavior"
            Expect.equal progress.Acknowledged expected.Trusted "Only acknowledged state is returned as durable"
        | other -> failtestf "Expected completion, got %A" other
    })
]

[<Tests>]
let pausedAcknowledgementTests = testList "TUF checkpoint paused acknowledgement" [
    for role in roles do
        testCaseTask $"{role} acknowledgement blocks every subsequent read and refresh completion" (fun () -> task {
            let fixture = fixture ()
            let reads = ResizeArray()
            let arrived = TaskCompletionSource<TufAcceptedCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously)
            let acknowledgement = TaskCompletionSource<Result<unit, CheckpointRefusal>>(TaskCreationOptions.RunContinuationsAsynchronously)
            use ct = new CancellationTokenSource()
            use cleanup = cancelOnDispose ct
            let persist (checkpoint: TufAcceptedCheckpoint) (token: CancellationToken) =
                if checkpoint.Role = role then
                    arrived.TrySetResult checkpoint |> ignore
                    acknowledgement.Task.WaitAsync token
                else Task.FromResult(Ok())
            let work = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist ct.Token
            let! checkpoint = waitForCheckpoint arrived.Task work ct.Cancel
            Expect.equal checkpoint.EnvelopeBytes fixture.Expected[role] "The paused write holds exact accepted evidence"
            Expect.equal (List.ofSeq reads) (readsThrough role) "No next request before durable acknowledgement"
            Expect.isFalse work.IsCompleted "No completed refresh or targets handoff before acknowledgement"
            acknowledgement.SetResult(Ok())
            let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            let accepted = completed result
            Expect.equal accepted.Root.Version 2UL "The acknowledged root is retained"
            Expect.isSome accepted.Targets "Targets complete only after all acknowledgements"
            Expect.equal (List.ofSeq reads) allReads "The driver resumes exactly at the next read"
        })
]

[<Tests>]
let failedAcknowledgementTests = testList "TUF checkpoint refusal and cancellation" [
    for role in roles do
        testCaseTask $"refusing {role} persistence preserves acknowledged state and stops without completion" (fun () -> task {
            let fixture = fixture ()
            let reads = ResizeArray()
            let persist (checkpoint: TufAcceptedCheckpoint) (_: CancellationToken) =
                Task.FromResult(if checkpoint.Role = role then Error CheckpointRefusal.StorageUnavailable else Ok())
            let! result = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist CancellationToken.None
            match result with
            | TufCheckpointResult.PersistenceRefused(CheckpointRefusal.StorageUnavailable, progress) -> checkPending fixture role progress
            | other -> failtestf "Persistence refusal must not become a publisher failure or completed refresh: %A" other
            Expect.equal (List.ofSeq reads) (readsThrough role) "No further metadata reads after failed persistence"
        })

        testCaseTask $"cancelling {role} persistence retains progress and is not a publisher failure" (fun () -> task {
            let fixture = fixture ()
            let reads = ResizeArray()
            let arrived = TaskCompletionSource<TufAcceptedCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously)
            let acknowledgement = TaskCompletionSource<Result<unit, CheckpointRefusal>>(TaskCreationOptions.RunContinuationsAsynchronously)
            use ct = new CancellationTokenSource()
            use cleanup = cancelOnDispose ct
            let persist (checkpoint: TufAcceptedCheckpoint) (token: CancellationToken) =
                if checkpoint.Role = role then
                    arrived.TrySetResult checkpoint |> ignore
                    acknowledgement.Task.WaitAsync token
                else Task.FromResult(Ok())
            let work = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist ct.Token
            let! _ = waitForCheckpoint arrived.Task work ct.Cancel
            ct.Cancel()
            let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            match result with
            | TufCheckpointResult.Cancelled(token, progress) ->
                Expect.equal token ct.Token "The caller's cancellation is retained separately from publisher failures"
                checkPending fixture role progress
            | other -> failtestf "Expected explicit cancelled control flow, no publisher failure or completed refresh: %A" other
            Expect.equal (List.ofSeq reads) (readsThrough role) "No read or target completion after cancellation"
        })

        testCaseTask $"{role} acknowledgement success remains durable when cancellation arrives during an awaited write" (fun () -> task {
            let fixture = fixture ()
            let reads = ResizeArray()
            let arrived = TaskCompletionSource<TufAcceptedCheckpoint>(TaskCreationOptions.RunContinuationsAsynchronously)
            let acknowledgement = TaskCompletionSource<Result<unit, CheckpointRefusal>>(TaskCreationOptions.RunContinuationsAsynchronously)
            use ct = new CancellationTokenSource()
            use cleanup = cancelOnDispose ct
            let persist (checkpoint: TufAcceptedCheckpoint) (_: CancellationToken) =
                if checkpoint.Role = role then
                    arrived.TrySetResult checkpoint |> ignore
                    // Deliberately delay cooperative completion to model an operation already in progress.
                    acknowledgement.Task
                else Task.FromResult(Ok())
            let work = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist ct.Token
            let! checkpoint = waitForCheckpoint arrived.Task work ct.Cancel
            ct.Cancel()
            Expect.isFalse work.IsCompleted "The owned write is awaited, never detached on cancellation"
            acknowledgement.SetResult(Ok())
            let! result = work.WaitAsync(TimeSpan.FromSeconds 5.0)
            match result with
            | TufCheckpointResult.Cancelled(token, progress) ->
                Expect.equal token ct.Token "Caller cancellation remains primary"
                Expect.equal progress.Accepted checkpoint.Accepted "Accepted security changes are retained"
                Expect.equal progress.Acknowledged checkpoint.Accepted "An Ok acknowledgement cannot be undone by cancellation"
                Expect.isNone progress.Pending "The successful write is acknowledged even though refresh was cancelled"
            | other -> failtestf "Expected cancellation after acknowledged persistence, got %A" other
            Expect.equal (List.ofSeq reads) (readsThrough role) "No next request after cancellation"
        })

    testCaseTask "a thrown checkpoint storage failure retains the pending root separately from publisher failures" (fun () -> task {
        let fixture = fixture ()
        let reads = ResizeArray()
        let persist (_: TufAcceptedCheckpoint) (_: CancellationToken) : Task<Result<unit, CheckpointRefusal>> =
            raise (IO.IOException "scripted checkpoint storage failure")
        let! result = TufCheckpointRefresh.runAsync referenceTime fixture.Initial (fetch fixture reads) persist CancellationToken.None
        match result with
        | TufCheckpointResult.PersistenceRefused(CheckpointRefusal.StorageUnavailable, progress) -> checkPending fixture TufRole.Root progress
        | other -> failtestf "Expected a checkpoint refusal, got %A" other
        Expect.equal (List.ofSeq reads) (readsThrough TufRole.Root) "Storage refusal stops before the next root read"
    })

    testCaseTask "a thrown later fetch failure preserves the acknowledged root without a pending checkpoint" (fun () -> task {
        let fixture = fixture ()
        let reads = ResizeArray()
        let read (request: TufMetadataRequest) token =
            let response = fetch fixture reads request token
            if request.Role = TufRole.Timestamp then raise (IO.IOException "scripted transport failure")
            else response
        let! result = TufCheckpointRefresh.runAsync referenceTime fixture.Initial read (fun _ _ -> Task.FromResult(Ok())) CancellationToken.None
        match result with
        | TufCheckpointResult.VerificationRefused(TufFailure.Unavailable TufRole.Timestamp, progress) ->
            Expect.equal progress.Accepted.Root.Version 2UL "Accepted root survives the later transfer failure"
            Expect.equal progress.Acknowledged progress.Accepted "The new root was durably acknowledged"
            Expect.equal progress.Acknowledged.Root.Roles[TufRole.Root].KeyIds fixture.RootKeyIds "Learned key revocations survive"
            Expect.isNone progress.Pending "Failed bytes were never accepted or checkpointed"
            Expect.isNone progress.Accepted.Targets "No target authority on refusal"
        | other -> failtestf "Expected a transport refusal with acknowledged progress, got %A" other
        Expect.equal (List.ofSeq reads) (readsThrough TufRole.Timestamp) "No later role is read"
    })

    testCaseTask "fetch cancellation preserves acknowledged progress and never becomes repository unavailable" (fun () -> task {
        let fixture = fixture ()
        let reads = ResizeArray()
        use ct = new CancellationTokenSource()
        let read (request: TufMetadataRequest) token =
            let response = fetch fixture reads request token
            if request.Role = TufRole.Timestamp then
                ct.Cancel()
                raise (OperationCanceledException ct.Token)
            else response
        let! result = TufCheckpointRefresh.runAsync referenceTime fixture.Initial read (fun _ _ -> Task.FromResult(Ok())) ct.Token
        match result with
        | TufCheckpointResult.Cancelled(token, progress) ->
            Expect.equal token ct.Token "Caller cancellation is preserved"
            Expect.equal progress.Acknowledged.Root.Version 2UL "Earlier acknowledged root is retained"
            Expect.equal progress.Accepted progress.Acknowledged "Cancelled fetch accepted no new metadata"
            Expect.isNone progress.Pending "No fabricated checkpoint for a cancelled fetch"
        | other -> failtestf "Expected cancellation, got %A" other
        Expect.equal (List.ofSeq reads) (readsThrough TufRole.Timestamp) "Cancellation stops the read sequence"
    })
]
