// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Morphir.Mep
open Morphir.Toolchain

[<RequireQualifiedAccess>]
type internal StageCleanupOutcome =
    | Removed of InstallationFailure option
    | Deferred of InstallationFailure

/// Physical cleanup observed after awaited disposal, separate from logical abandonment.
[<Sealed>]
type StageCleanupReport internal (outcome: StageCleanupOutcome) =
    /// True when private bytes may remain for a later coordinated cleanup.
    member _.IsDeferred =
        match outcome with
        | StageCleanupOutcome.Deferred _ -> true
        | StageCleanupOutcome.Removed _ -> false

    /// Optional cleanup diagnostic, for C# callers as a nullable reference.
    member _.Failure =
        (match outcome with
         | StageCleanupOutcome.Deferred error
         | StageCleanupOutcome.Removed(Some error) -> Some error
         | StageCleanupOutcome.Removed None -> None)
        |> Option.toObj

    /// Stable description without reflection.
    override _.ToString() =
        match outcome with
        | StageCleanupOutcome.Removed _ ->
            "Private stage ownership released; physical cleanup completed."
        | StageCleanupOutcome.Deferred _ ->
            "Private stage ownership released; physical cleanup deferred."

type internal StageIdentity = (struct (uint64 * uint64 * uint64))

type internal StageLocation = {
    Home: string
    HomeIdentity: StageIdentity
    ParentIdentity: StageIdentity
    Name: string
    DirectoryIdentity: StageIdentity
    OwnerIdentity: StageIdentity option
    DataIdentity: StageIdentity option
}

module private StageFiles =
    let own<'T when 'T :> IDisposable> (owned: ResizeArray<IDisposable>) (resource: 'T) : 'T =
        owned.Add(resource :> IDisposable)
        resource

    let failure (error: exn) =
        let refusal =
            match error with
            | InstallationException error -> error
            | :? ArgumentException -> InstallationFailure("extension.invalid_path", error.Message)
            | :? OperationCanceledException ->
                InstallationFailure("extension.cancelled", "Private byte operation was cancelled.")
            | _ -> InstallationFailure("extension.io_error", error.Message)

        match error.Data["Morphir.PrivateStaging.CleanupReport"] with
        | :? StageCleanupReport as report when report.IsDeferred ->
            InstallationFailure(
                refusal.Code,
                refusal.Message
                + " Private stage ownership was released; physical cleanup was deferred."
            )
        | _ -> refusal

    // No recursive deletion, pathname source substitution, persisted journal or shared metadata.
    // Closed private orphans are observable; a later identity-aware sweep is a separate slice.
    let cleanup location closeFailure =
        try
            use root = SafeDirectory.Open(location.Home, false)

            if
                root.Identity
                <> location.HomeIdentity
            then
                Operations.fail "extension.path_escape" "Private stage Home changed identity."

            match FileSafety.tryMutationLease root with
            | None ->
                StageCleanupOutcome.Deferred(
                    InstallationFailure(
                        "extension.io_error",
                        "Private stage cleanup deferred while the cooperative I/O lease is busy."
                    )
                )
            | Some lease ->
                use lease = lease
                use parent = root.Child("tmp", false)

                if
                    parent.Identity
                    <> location.ParentIdentity
                then
                    Operations.fail
                        "extension.path_escape"
                        "Private staging parent changed identity."

                do
                    use directory = parent.Child(location.Name, false)

                    if
                        directory.Identity
                        <> location.DirectoryIdentity
                    then
                        Operations.fail
                            "extension.path_escape"
                            "Private staging directory changed identity."

                    let expected = [
                        ".owner", location.OwnerIdentity
                        "bytes", location.DataIdentity
                    ]

                    for name in directory.Entries() do
                        match
                            expected
                            |> List.tryFind (fun (candidate, _) -> candidate = name)
                        with
                        | Some(_, Some identity) when directory.HasFileIdentity(name, identity) ->
                            ()
                        | _ ->
                            Operations.fail
                                "extension.path_escape"
                                "Private cleanup retained an unowned or substituted entry."

                    for name, identity in expected do
                        match identity with
                        | Some identity -> directory.DeleteIfIdentity(name, identity)
                        | None -> ()

                parent.DeleteDirectory location.Name
                StageCleanupOutcome.Removed closeFailure
        with error ->
            StageCleanupOutcome.Deferred(failure error)

type internal StageResources
    (
        root: SafeDirectory,
        parent: SafeDirectory,
        directory: SafeDirectory,
        owner: FileStream,
        output: FileStream,
        location: StageLocation
    ) =
    let dataHandle = output.SafeFileHandle
    member _.Output = output
    member _.Root = root
    /// The full path of the staged bytes, for a reader that opens them by name inside this owned directory.
    member _.DataPath = Path.Combine(directory.Path, "bytes")

    member _.Validate() =
        root.ValidateLocation()
        parent.ValidateLocation()
        directory.ValidateLocation()

        match location.DataIdentity with
        | Some identity when
            directory.FileIdentity output = identity
            && directory.HasFileIdentity("bytes", identity)
            ->
            ()
        | _ -> Operations.fail "extension.path_escape" "Private staging object changed identity."

    member _.CloseAsync(initialFailure: InstallationFailure option) : Task<StageCleanupReport> = task {
        let mutable closeFailure = initialFailure
        // Await even a slow native close/flush. No timeout detaches work from its owner.
        try
            do! output.DisposeAsync()
        with error ->
            closeFailure <- Some(StageFiles.failure error)

        try
            dataHandle.Dispose()
        with error ->
            closeFailure <- Some(StageFiles.failure error)

        for owned in
            [
                owner :> IDisposable
                directory :> IDisposable
                parent :> IDisposable
                root :> IDisposable
            ] do
            try
                owned.Dispose()
            with error ->
                closeFailure <- Some(StageFiles.failure error)

        return StageCleanupReport(StageFiles.cleanup location closeFailure)
    }

    member this.CloseAsync() = this.CloseAsync(None)

    static member CreateAsync(home: string, ct: CancellationToken) : Task<StageResources> = task {
        ct.ThrowIfCancellationRequested()
        let owned = ResizeArray<IDisposable>()
        let mutable location = None

        try
            let root =
                SafeDirectory.Open(home, true)
                |> StageFiles.own owned

            use! mutation = FileSafety.mutationLeaseAsync root ct
            ct.ThrowIfCancellationRequested()

            let parent =
                root.Child("tmp", true)
                |> StageFiles.own owned

            let name =
                "morphir-dotnet-stage-"
                + Guid.NewGuid().ToString("N")

            if
                parent.Entries()
                |> Array.contains name
            then
                Operations.fail "extension.io_error" "Private staging name already exists."

            let directory =
                parent.Child(name, true)
                |> StageFiles.own owned

            location <-
                Some
                    {
                        Home = root.Path
                        HomeIdentity = root.Identity
                        ParentIdentity = parent.Identity
                        Name = name
                        DirectoryIdentity = directory.Identity
                        OwnerIdentity = None
                        DataIdentity = None
                    }

            if not (OperatingSystem.IsWindows()) then
                File.SetUnixFileMode(
                    directory.Handle,
                    UnixFileMode.UserRead
                    ||| UnixFileMode.UserWrite
                    ||| UnixFileMode.UserExecute
                )

            let owner =
                directory.File(".owner", false, true)
                |> StageFiles.own owned

            location <-
                location
                |> Option.map (fun value ->
                    { value with
                        OwnerIdentity = Some(directory.FileIdentity owner)
                    }
                )

            let output =
                directory.File("bytes", true, false, true)
                |> StageFiles.own owned

            let initial =
                location
                |> Option.get

            let captured =
                { initial with
                    DataIdentity = Some(directory.FileIdentity output)
                }

            location <- Some captured
            ct.ThrowIfCancellationRequested()
            let resources = StageResources(root, parent, directory, owner, output, captured)
            resources.Validate()
            owned.Clear()
            return resources
        with error ->
            for resource in
                owned
                |> Seq.rev do
                try
                    resource.Dispose()
                with _ ->
                    ()

            match location with
            | Some location ->
                let report = StageCleanupReport(StageFiles.cleanup location None)
                // Failed Begin cannot return a stage. Preserve cancellation and expose cleanup on its exception.
                error.Data["Morphir.PrivateStaging.CleanupReport"] <- report
            | None -> ()

            return raise error
    }

[<RequireQualifiedAccess>]
type private StagePhase =
    | Writable
    | Writing of TaskCompletionSource<unit>
    | Sealing of TaskCompletionSource<unit>
    | Sealed
    | Publishing of TaskCompletionSource<unit>
    | Faulted of InstallationFailure * Task
    | Closing of TaskCompletionSource<unit>
    | Disposed of StageCleanupReport

type internal StageCore
    (
        resources: StageResources,
        expectedLength: uint64,
        expectedDigest: Sha256Digest,
        maximumBytes: uint64,
        writer: Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>
    ) =
    let gate = obj ()
    let mutable phase = StagePhase.Writable
    let mutable written = 0UL
    let streamedDigest = Digest.Incremental()
    let buffer = Array.zeroCreate<byte> (64 * 1024)

    let completion () =
        TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

    let finish (signal: TaskCompletionSource<unit>) =
        signal.TrySetResult(())
        |> ignore

    let fault error =
        lock
            gate
            (fun () ->
                match phase with
                | StagePhase.Writing signal
                | StagePhase.Sealing signal ->
                    phase <- StagePhase.Faulted(StageFiles.failure error, signal.Task :> Task)
                | _ -> ()
            )

    member _.Length = expectedLength
    member _.Digest = expectedDigest

    member _.CleanupReport =
        lock
            gate
            (fun () ->
                match phase with
                | StagePhase.Disposed report -> Some report
                | _ -> None
            )
        |> Option.toObj

    member _.WriteAsync(content: ReadOnlyMemory<byte>, ct: CancellationToken) : ValueTask =
        let operation =
            lock
                gate
                (fun () ->
                    match phase with
                    | StagePhase.Writable ->
                        let signal = completion ()
                        phase <- StagePhase.Writing signal
                        signal
                    | _ -> invalidOp "Private stage is not writable or another operation owns it."
                )

        ValueTask(
            task {
                try
                    try
                        ct.ThrowIfCancellationRequested()

                        if
                            uint64 content.Length > expectedLength
                                                    - written
                            || uint64 content.Length > maximumBytes
                                                       - written
                        then
                            raise (
                                IOException
                                    "Private write exceeds the recorded length or caller byte bound."
                            )

                        resources.Validate()
                        let mutable offset = 0

                        while offset < content.Length do
                            ct.ThrowIfCancellationRequested()

                            let count =
                                min
                                    buffer.Length
                                    (content.Length
                                     - offset)

                            content.Slice(offset, count).CopyTo(Memory<byte>(buffer, 0, count))

                            do!
                                writer.Invoke(
                                    resources.Output,
                                    ReadOnlyMemory<byte>(buffer, 0, count),
                                    ct
                                )

                            ct.ThrowIfCancellationRequested()
                            streamedDigest.Append(buffer, 0, count)

                            written <-
                                written
                                + uint64 count

                            offset <-
                                offset
                                + count

                        ct.ThrowIfCancellationRequested()

                        lock
                            gate
                            (fun () ->
                                match phase with
                                | StagePhase.Writing _ -> phase <- StagePhase.Writable
                                | _ -> ()
                            )
                    with error ->
                        fault error
                        return raise error
                finally
                    finish operation
            }
        )

    member _.SealAsync(ct: CancellationToken) : Task<Result<unit, InstallationFailure>> = task {
        let admission =
            lock
                gate
                (fun () ->
                    match phase with
                    | StagePhase.Writable ->
                        let signal = completion ()
                        phase <- StagePhase.Sealing signal
                        Ok signal
                    | StagePhase.Faulted(error, _) -> Error error
                    | _ ->
                        Error(
                            InstallationFailure(
                                "extension.io_error",
                                "Private stage cannot transfer sealed ownership in its current state."
                            )
                        )
                )

        match admission with
        | Error error -> return Error error
        | Ok operation ->
            try
                try
                    ct.ThrowIfCancellationRequested()

                    if
                        written
                        <> expectedLength
                    then
                        Operations.fail
                            "extension.digest_mismatch"
                            $"Private stage has {written} bytes; expected {expectedLength}."

                    resources.Validate()
                    do! resources.Output.FlushAsync ct
                    ct.ThrowIfCancellationRequested()
                    // Native durability cannot be forcibly cancelled safely in process; await actual completion.
                    resources.Output.Flush true
                    ct.ThrowIfCancellationRequested()
                    resources.Validate()
                    resources.Output.Position <- 0L
                    let verifiedDigest = Digest.Incremental()
                    let mutable verifiedLength = 0UL
                    let mutable reading = true

                    while reading do
                        ct.ThrowIfCancellationRequested()

                        let requested =
                            int (
                                min
                                    (uint64 buffer.Length)
                                    (expectedLength
                                     - verifiedLength
                                     + 1UL)
                            )

                        let! count =
                            resources.Output.ReadAsync(Memory<byte>(buffer, 0, requested), ct)

                        if count = 0 then
                            reading <- false
                        else
                            verifiedLength <-
                                verifiedLength
                                + uint64 count

                            if
                                verifiedLength > expectedLength
                                || verifiedLength > maximumBytes
                            then
                                Operations.fail
                                    "extension.digest_mismatch"
                                    "The open private object exceeds its expected byte count."

                            verifiedDigest.Append(buffer, 0, count)

                    ct.ThrowIfCancellationRequested()
                    let actual = verifiedDigest.Finish()
                    let streamed = streamedDigest.Finish()

                    if
                        verifiedLength
                        <> expectedLength
                        || actual
                           <> expectedDigest.Value
                        || streamed
                           <> expectedDigest.Value
                    then
                        Operations.fail
                            "extension.digest_mismatch"
                            "The private streamed bytes or same open object differ from the recorded length/digest."

                    resources.Validate()

                    if
                        uint64 resources.Output.Length
                        <> expectedLength
                    then
                        Operations.fail
                            "extension.digest_mismatch"
                            "Private object length changed while sealing."

                    ct.ThrowIfCancellationRequested()

                    lock
                        gate
                        (fun () ->
                            match phase with
                            | StagePhase.Sealing _ -> phase <- StagePhase.Sealed
                            | _ ->
                                Operations.fail
                                    "extension.io_error"
                                    "Private stage disposal began before sealed ownership transferred."
                        )

                    return Ok()
                with
                | :? OperationCanceledException as error ->
                    fault error
                    return raise error
                | error ->
                    fault error
                    return Error(StageFiles.failure error)
            finally
                finish operation
    }

    member _.PublishAsync<'T>
        (
            operation: Func<StageResources, Task<'T>>,
            beforeCleanup: Action,
            ct: CancellationToken
        ) : Task<'T> =
        ct.ThrowIfCancellationRequested()

        let signal =
            lock
                gate
                (fun () ->
                    match phase with
                    | StagePhase.Sealed ->
                        let signal = completion ()
                        phase <- StagePhase.Publishing signal
                        signal
                    | _ ->
                        invalidOp
                            "Sealed stage is consumed, disposed or another operation owns it."
                )

        task {
            try
                let! outcome = task {
                    try
                        let! value = operation.Invoke resources
                        return Ok value
                    with error ->
                        return Error error
                }
                // The publisher has left its mutation-lease scope before this
                // hook and close. Cleanup never waits while holding that lease.
                let mutable cleanupFailure = None

                try
                    beforeCleanup.Invoke()
                with error ->
                    cleanupFailure <- Some(StageFiles.failure error)

                let! report = resources.CloseAsync(cleanupFailure)
                lock gate (fun () -> phase <- StagePhase.Disposed report)
                finish signal

                match outcome with
                | Ok value -> return value
                | Error error -> return raise error
            with error ->
                // Unexpected close failure is observable to a disposal joining
                // this owner; no task or native work is detached.
                if not signal.Task.IsCompleted then
                    signal.TrySetException error
                    |> ignore

                return raise error
        }

    member _.DisposeAsync(sealedOwner: bool) : ValueTask =
        let admission =
            lock
                gate
                (fun () ->
                    match phase with
                    | StagePhase.Sealed when not sealedOwner -> Choice1Of2 Task.CompletedTask
                    | StagePhase.Publishing signal ->
                        Choice1Of2(
                            if sealedOwner then
                                (signal.Task :> Task)
                            else
                                Task.CompletedTask
                        )
                    | StagePhase.Disposed _ -> Choice1Of2 Task.CompletedTask
                    | StagePhase.Closing signal -> Choice1Of2(signal.Task :> Task)
                    | current ->
                        let waiting =
                            match current with
                            | StagePhase.Writing signal
                            | StagePhase.Sealing signal -> signal.Task :> Task
                            | StagePhase.Faulted(_, operation) -> operation
                            | _ -> Task.CompletedTask

                        let signal = completion ()
                        phase <- StagePhase.Closing signal
                        Choice2Of2(waiting, signal)
                )

        match admission with
        | Choice1Of2 existing -> ValueTask(existing)
        | Choice2Of2(waiting, signal) ->
            let closing = task {
                try
                    do! waiting
                    let! report = resources.CloseAsync()
                    lock gate (fun () -> phase <- StagePhase.Disposed report)
                    finish signal
                with error ->
                    signal.TrySetException error
                    |> ignore
            }
            // Both the actual close and its completion are awaited, including every native call.
            ValueTask(
                task {
                    do! closing
                    do! signal.Task
                }
            )

/// Exclusive sealed ownership of verified bytes. No destination or host layout is selected here.
[<Sealed>]
type SealedArtifactStage internal (core: StageCore) =
    /// Verified exact byte count.
    member _.Length = core.Length
    /// Verified SHA-256, separate from publisher authentication.
    member _.Digest = core.Digest
    /// Physical cleanup result after awaited disposal; null while ownership remains live.
    member _.CleanupReport = core.CleanupReport

    member internal _.PublishAsync<'T>
        (
            operation: Func<StageResources, Task<'T>>,
            beforeCleanup: Action,
            ct: CancellationToken
        ) =
        core.PublishAsync(operation, beforeCleanup, ct)

    /// Stable description without reflection.
    override _.ToString() = "Sealed private artifact stage."

    interface IAsyncDisposable with
        member _.DisposeAsync() = core.DisposeAsync true

/// Writable private bytes, disabled after a failed operation or ownership transfer.
[<Sealed>]
type PrivateArtifactStage internal (core: StageCore) =
    /// Consumes borrowed memory before completion. Concurrent writes refuse; interrupted writes fault the stage.
    member _.WriteAsync(bytes: ReadOnlyMemory<byte>, ct: CancellationToken) =
        core.WriteAsync(bytes, ct)

    /// Flushes and rehashes the same open object, transferring ownership exactly once. Cancellation propagates.
    member _.SealAsync(ct: CancellationToken) : Task<InstallationResult<SealedArtifactStage>> = task {
        let! result = core.SealAsync ct

        return
            InstallationResult(
                result
                |> Result.map (fun () -> new SealedArtifactStage(core))
            )
    }

    /// Physical cleanup result once the current owner completes disposal; otherwise null.
    member _.CleanupReport = core.CleanupReport
    /// Stable description without reflection.
    override _.ToString() = "Private artifact byte stage."

    interface IAsyncDisposable with
        member _.DisposeAsync() = core.DisposeAsync false

/// Generic installer-owned byte staging, independent of extension limits and publication layout.
[<AbstractClass; Sealed>]
type PrivateStaging private () =
    static member private BeginCore
        (home: string | null,
                        expectedLength: uint64,
                        expectedDigest: Sha256Digest,
                        maximumBytes: uint64,
                        writer: Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>,
                        ct: CancellationToken)
        : Task<InstallationResult<PrivateArtifactStage>> =
        task {
            ct.ThrowIfCancellationRequested()

            try
                if
                    expectedLength > maximumBytes
                    || expectedLength > uint64 Int64.MaxValue
                then
                    Operations.fail
                        "extension.artifact_too_large"
                        "Recorded bytes exceed the caller bound or the representable file length."

                let home = Operations.requiredPath home

                if String.IsNullOrWhiteSpace home then
                    Operations.fail "extension.invalid_path" "Morphir Home is empty."

                if obj.ReferenceEquals(expectedDigest, null) then
                    Operations.fail "extension.invalid_digest" "The recorded digest is missing."

                ArgumentNullException.ThrowIfNull writer
                let! resources = StageResources.CreateAsync(home, ct)

                return
                    InstallationResult(
                        Ok(
                            new PrivateArtifactStage(
                                StageCore(
                                    resources,
                                    expectedLength,
                                    expectedDigest,
                                    maximumBytes,
                                    writer
                                )
                            )
                        )
                    )
            with
            | :? OperationCanceledException as error -> return raise error
            | error -> return InstallationResult(Error(StageFiles.failure error))
        }

    /// Begins private bytes with caller-supplied limits. Integrity expectations confer no publisher authority.
    static member BeginAsync
        (home: string | null,
                        expectedLength: uint64,
                        expectedDigest: Sha256Digest,
                        maximumBytes: uint64,
                        ct: CancellationToken)
        =
        PrivateStaging.BeginCore(
            home,
            expectedLength,
            expectedDigest,
            maximumBytes,
            Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun output bytes token ->
                output.WriteAsync(bytes, token)
            ),
            ct
        )

    static member internal BeginWithWriterAsync
        (home: string | null,
                        expectedLength: uint64,
                        expectedDigest: Sha256Digest,
                        maximumBytes: uint64,
                        writer: Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>,
                        ct: CancellationToken)
        =
        PrivateStaging.BeginCore(home, expectedLength, expectedDigest, maximumBytes, writer, ct)
