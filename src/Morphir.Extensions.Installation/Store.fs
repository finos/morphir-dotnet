// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Threading
open Morphir.Mep
open Morphir.Toolchain

/// A verified single-file store publication, without catalog, lock or package metadata.
[<Sealed>]
type StoredArtifact
    internal (relative: string, absolute: string, digest: Sha256Digest, length: int64) =
    /// Path below Morphir Home in the contract's slash-separated spelling.
    member _.RelativePath = relative
    /// Native absolute path for this machine.
    member _.AbsolutePath = absolute
    /// SHA-256 verified over the final object bytes.
    member _.Digest = digest
    /// Number of bytes verified in the final object.
    member _.Length = length
    /// Stable text without reflection.
    override _.ToString() = relative

module internal Copying =
    // Reuse the managed SHA-256 used by Morphir.Toolchain, feeding exactly the chunks written to the sink.
    let hashCopy (input: Stream) (output: Stream) limit code (ct: CancellationToken) =
        let buffer = Array.zeroCreate<byte> (64 * 1024)
        let mutable length = 0L

        let parts = seq {
            let mutable reading = true

            while reading do
                ct.ThrowIfCancellationRequested()

                let count =
                    input.Read(
                        buffer,
                        0,
                        int (
                            min
                                (int64 buffer.Length)
                                (limit
                                 - length
                                 + 1L)
                        )
                    )

                if count = 0 then
                    reading <- false
                else
                    length <-
                        length
                        + int64 count

                    if length > limit then
                        Operations.fail code $"Input exceeds {limit} bytes."

                    output.Write(buffer, 0, count)
                    yield buffer[0 .. count - 1]
        }

        let hash = Digest.ofParts parts
        hash, length

// Both entry points use this one descriptor-bound publisher and final verifier.
// These records are in-process ownership, never shared metadata or a journal.
module private StorePublication =
    type Source = {
        Stream: FileStream
        Length: int64
        Validate: Action
        Cleanup: Action
    }

    let failure (visible: string option) (error: exn) =
        let code, message =
            match error with
            | InstallationException error -> error.Code, error.Message
            | :? OperationCanceledException when Option.isSome visible ->
                "extension.store_object_mismatch",
                "Required postcommit verification did not complete."
            | :? OperationCanceledException -> "extension.cancelled", "The operation was cancelled."
            | :? ArgumentException -> "extension.invalid_path", error.Message
            | _ -> "extension.io_error", error.Message

        let message =
            match visible with
            | Some path -> $"Native publication committed at {path}. {message}"
            | None -> message

        InstallationFailure(code, message)

    let run
        propagateCancellation
        (visible: string option ref)
        (work: unit -> System.Threading.Tasks.Task<StoredArtifact>)
        =
        task {
            try
                let! stored = work ()
                return InstallationResult(Ok stored)
            with
            | :? OperationCanceledException as error when
                propagateCancellation
                && Option.isNone visible.Value
                ->
                return raise error
            | error -> return InstallationResult(Error(failure visible.Value error))
        }

    let rec withDirectories
        (root: SafeDirectory)
        (parts: string list)
        (work: SafeDirectory -> System.Threading.Tasks.Task<'T>)
        =
        task {
            match parts with
            | [] -> return! work root
            | name :: rest ->
                use child = root.Child(name, true)
                return! withDirectories child rest work
        }

    let verifyBytes
        (stream: FileStream)
        (expected: Sha256Digest)
        (length: int64 option)
        (code: string)
        (ct: CancellationToken)
        =
        ct.ThrowIfCancellationRequested()

        if
            length
            |> Option.exists ((<>) stream.Length)
        then
            Operations.fail code "The open object differs from its recorded byte count."

        stream.Position <- 0L

        let actual, count =
            Copying.hashCopy
                stream
                Stream.Null
                InstallationLimits.MaxArtifactBytes
                "extension.artifact_too_large"
                ct

        if
            actual
            <> expected.Value
            || (length
                |> Option.exists ((<>) count))
            || stream.Length
               <> count
        then
            Operations.fail
                code
                $"Object has digest {actual} and {count} bytes, inconsistent with the recorded identity {expected.Value}."

        count

    let verifyFinal
        (directory: SafeDirectory)
        (name: string)
        (expected: Sha256Digest)
        (length: int64 option)
        (identity: StageIdentity option)
        (executable: bool)
        (ct: CancellationToken)
        =
        let absolute = Path.Combine(directory.Path, name)
        use current = directory.File(name, false, false)
        let openedIdentity = directory.FileIdentity current

        if
            identity
            |> Option.exists ((<>) openedIdentity)
        then
            Operations.fail
                "extension.store_object_mismatch"
                $"Published store identity changed: {absolute}"

        let count = verifyBytes current expected length "extension.store_object_mismatch" ct

        if
            executable
            && not (OperatingSystem.IsWindows())
            && not ((File.GetUnixFileMode current.SafeFileHandle).HasFlag UnixFileMode.UserExecute)
        then
            Operations.fail
                "extension.not_launchable"
                $"Store object has no owner execute bit: {absolute}"

        directory.ValidateLocation()

        if not (directory.HasFileIdentity(name, openedIdentity)) then
            Operations.fail
                "extension.store_object_mismatch"
                $"Verified store name changed identity: {absolute}"

        StoredArtifact(
            $"store/extensions/sha256/{expected.Value}/{name}",
            absolute,
            expected,
            count
        )

    let publish
        (root: SafeDirectory)
        (fileName: TargetPath)
        (expected: Sha256Digest)
        (executable: bool)
        (sourceForReuse: bool)
        (prepare: SafeDirectory -> Source)
        (beforeCommit: Func<CancellationToken, System.Threading.Tasks.Task>)
        (afterCommit: Action<string>)
        (visible: string option ref)
        (ct: CancellationToken)
        =
        withDirectories
            root
            [ "store"; "extensions"; "sha256"; expected.Value ]
            (fun directory -> task {
                let name = fileName.Text
                let absolute = Path.Combine(directory.Path, name)

                let exists =
                    try
                        use existing = directory.File(name, false, false)
                        true
                    with :? FileNotFoundException ->
                        false

                if
                    exists
                    && not sourceForReuse
                then
                    return verifyFinal directory name expected None None executable ct
                else
                    let source = prepare directory

                    try
                        source.Validate.Invoke()

                        if exists then
                            verifyBytes
                                source.Stream
                                expected
                                (Some source.Length)
                                "extension.digest_mismatch"
                                ct
                            |> ignore

                            source.Stream.Dispose()

                            return
                                verifyFinal
                                    directory
                                    name
                                    expected
                                    (Some source.Length)
                                    None
                                    executable
                                    ct
                        else
                            if
                                executable
                                && not (OperatingSystem.IsWindows())
                            then
                                File.SetUnixFileMode(
                                    source.Stream.SafeFileHandle,
                                    File.GetUnixFileMode(source.Stream.SafeFileHandle)
                                    ||| UnixFileMode.UserExecute
                                )

                            source.Stream.Flush true
                            ct.ThrowIfCancellationRequested()
                            do! beforeCommit.Invoke ct
                            root.ValidateLocation()
                            directory.ValidateLocation()
                            source.Validate.Invoke()

                            verifyBytes
                                source.Stream
                                expected
                                (Some source.Length)
                                "extension.digest_mismatch"
                                ct
                            |> ignore

                            source.Validate.Invoke()
                            let identity = directory.FileIdentity source.Stream
                            ct.ThrowIfCancellationRequested()

                            let committed =
                                Action(fun () ->
                                    visible.Value <- Some absolute
                                    afterCommit.Invoke absolute
                                )

                            let published =
                                directory.Publish(source.Stream.SafeFileHandle, name, committed)
                            // Windows source handles carry DELETE access. Close that
                            // handle before reopening the final name, with the captured
                            // identity still required for rename/link publication.
                            source.Stream.Dispose()

                            let finalIdentity =
                                if
                                    published
                                    && not (OperatingSystem.IsMacOS())
                                then
                                    Some identity
                                else
                                    None
                            // A successful native commit cannot be rolled back by a
                            // newly cancelled token. Verify the actual visible object,
                            // including a new Darwin clone inode, before any success.
                            let verificationToken =
                                if published then CancellationToken.None else ct

                            return
                                verifyFinal
                                    directory
                                    name
                                    expected
                                    (Some source.Length)
                                    finalIdentity
                                    executable
                                    verificationToken
                    finally
                        source.Cleanup.Invoke()
            })

/// Publishes verified raw single files using the parent store layout and no-clobber semantics.
[<AbstractClass; Sealed>]
type SingleFileStore private () =
    static member private ValidateName(fileName: TargetPath) =
        match DistributionIdentifiers.ParseArtifactFileName fileName.Text with
        | Error error -> raise (InstallationException error)
        | Ok _ -> ()

    static member private PublishCore
        (
            home: string,
            source: string,
            fileName: TargetPath,
            expected: Sha256Digest,
            executable: bool,
            beforePublish: Action,
            ct: CancellationToken
        ) =
        let visible = ref None

        StorePublication.run
            false
            visible
            (fun () -> task {
                ct.ThrowIfCancellationRequested()
                SingleFileStore.ValidateName fileName
                use root = SafeDirectory.Open(home, true)
                use mutation = FileSafety.mutationLease root
                use input = FileSafety.openRelative root source

                if input.Length > InstallationLimits.MaxArtifactBytes then
                    Operations.fail "extension.artifact_too_large" "Artifact exceeds 256 MiB."

                let prepare (directory: SafeDirectory) : StorePublication.Source =
                    let stage =
                        ".stage-"
                        + Guid.NewGuid().ToString("N")

                    let output = directory.File(stage, true, false)
                    let mutable capturedIdentity = None

                    let cleanup =
                        Action(fun () ->
                            output.Dispose()

                            capturedIdentity
                            |> Option.iter (FileSafety.tryDeleteIdentity directory stage)
                        )

                    try
                        let identity = directory.FileIdentity output
                        capturedIdentity <- Some identity

                        let actual, length =
                            Copying.hashCopy
                                input
                                output
                                InstallationLimits.MaxArtifactBytes
                                "extension.artifact_too_large"
                                ct

                        if
                            actual
                            <> expected.Value
                        then
                            Operations.fail
                                "extension.digest_mismatch"
                                $"Staged bytes have digest {actual}, expected {expected.Value}."

                        {
                            Stream = output
                            Length = length
                            Validate =
                                Action(fun () ->
                                    directory.ValidateLocation()

                                    if
                                        directory.FileIdentity output
                                        <> identity
                                    then
                                        Operations.fail
                                            "extension.path_escape"
                                            "Open copying stage changed identity."
                                )
                            Cleanup = cleanup
                        }
                    with _ ->
                        cleanup.Invoke()
                        reraise ()

                return!
                    StorePublication.publish
                        root
                        fileName
                        expected
                        executable
                        false
                        prepare
                        (Func<CancellationToken, System.Threading.Tasks.Task>(fun _ ->
                            beforePublish.Invoke()
                            System.Threading.Tasks.Task.CompletedTask
                        ))
                        (Action<string>(ignore))
                        visible
                        ct
            })

    static member private PublishSealedCore
        (
            stage: SealedArtifactStage,
            fileName: TargetPath,
            executable: bool,
            beforeCommit: Func<CancellationToken, System.Threading.Tasks.Task>,
            afterCommit: Action<string>,
            beforeCleanup: Action,
            ct: CancellationToken
        ) =
        ct.ThrowIfCancellationRequested()
        ArgumentNullException.ThrowIfNull stage

        stage.PublishAsync(
            Func<StageResources, System.Threading.Tasks.Task<InstallationResult<StoredArtifact>>>(fun
                                                                                                      resources ->
                let visible = ref None

                StorePublication.run
                    true
                    visible
                    (fun () -> task {
                        ct.ThrowIfCancellationRequested()
                        SingleFileStore.ValidateName fileName

                        if stage.Length > uint64 InstallationLimits.MaxArtifactBytes then
                            Operations.fail
                                "extension.artifact_too_large"
                                "Extension artifact exceeds 256 MiB."

                        resources.Validate()
                        use! mutation = FileSafety.mutationLeaseAsync resources.Root ct
                        resources.Validate()

                        let prepare (_: SafeDirectory) : StorePublication.Source = {
                            Stream = resources.Output
                            Length = int64 stage.Length
                            Validate = Action(resources.Validate)
                            Cleanup = Action(ignore)
                        }

                        return!
                            StorePublication.publish
                                resources.Root
                                fileName
                                stage.Digest
                                executable
                                true
                                prepare
                                beforeCommit
                                afterCommit
                                visible
                                ct
                    })
            ),
            beforeCleanup,
            ct
        )

    /// Publishes transferred sealed bytes once using the extension policy. Precommit cancellation propagates.
    static member PublishAsync
        (
            stage: SealedArtifactStage,
            fileName: TargetPath,
            executable: bool,
            ct: CancellationToken
        ) =
        SingleFileStore.PublishSealedCore(
            stage,
            fileName,
            executable,
            Func<CancellationToken, System.Threading.Tasks.Task>(fun _ ->
                System.Threading.Tasks.Task.CompletedTask
            ),
            Action<string>(ignore),
            Action(ignore),
            ct
        )

    static member internal PublishSealedWithHooksAsync
        (
            stage: SealedArtifactStage,
            fileName: TargetPath,
            executable: bool,
            beforeCommit: Func<CancellationToken, System.Threading.Tasks.Task>,
            afterCommit: Action<string>,
            beforeCleanup: Action,
            ct: CancellationToken
        ) =
        SingleFileStore.PublishSealedCore(
            stage,
            fileName,
            executable,
            beforeCommit,
            afterCommit,
            beforeCleanup,
            ct
        )

    /// Hashes while copying private staging. Reuse is rehashed; corrupt objects are never repaired.
    static member PublishAsync
        (
            home: string,
            source: string,
            fileName: TargetPath,
            expected: Sha256Digest,
            executable: bool,
            ct: CancellationToken
        ) =
        SingleFileStore.PublishCore(
            home,
            source,
            fileName,
            expected,
            executable,
            Action(ignore),
            ct
        )

    static member internal PublishWithHookAsync
        (
            home: string,
            source: string,
            fileName: TargetPath,
            expected: Sha256Digest,
            executable: bool,
            hook: Action,
            ct: CancellationToken
        ) =
        SingleFileStore.PublishCore(home, source, fileName, expected, executable, hook, ct)
