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
    /// SHA-256 verified over the copied bytes.
    member _.Digest = digest
    /// Number of bytes copied.
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

/// Publishes verified raw single files using the parent store layout and no-clobber semantics.
[<AbstractClass; Sealed>]
type SingleFileStore private () =
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
        Operations.run
            ct
            (fun () ->
                match DistributionIdentifiers.ParseArtifactFileName fileName.Text with
                | Error error -> raise (InstallationException error)
                | Ok _ -> ()

                use root = SafeDirectory.Open(home, true)
                use mutation = FileSafety.mutationLease root
                use input = FileSafety.openRelative root source

                if input.Length > InstallationLimits.MaxArtifactBytes then
                    Operations.fail "extension.artifact_too_large" "Artifact exceeds 256 MiB."

                FileSafety.withDirectories
                    root
                    [ "store"; "extensions"; "sha256"; expected.Value ]
                    true
                    (fun directory ->
                        let name = fileName.Text
                        let relative = $"store/extensions/sha256/{expected.Value}/{name}"
                        let absolute = Path.Combine(directory.Path, name)

                        let reuse () =
                            use existing = directory.File(name, false, false)

                            let actual, length =
                                Copying.hashCopy
                                    existing
                                    Stream.Null
                                    InstallationLimits.MaxArtifactBytes
                                    "extension.artifact_too_large"
                                    ct

                            if
                                actual
                                <> expected.Value
                            then
                                Operations.fail
                                    "extension.store_object_mismatch"
                                    $"Store object has digest {actual}, expected {expected.Value}: {absolute}"

                            if
                                executable
                                && not (OperatingSystem.IsWindows())
                                && not (
                                    (File.GetUnixFileMode existing.SafeFileHandle).HasFlag
                                        UnixFileMode.UserExecute
                                )
                            then
                                Operations.fail
                                    "extension.not_launchable"
                                    $"Store object has no owner execute bit: {absolute}"

                            StoredArtifact(relative, absolute, expected, length)

                        let exists =
                            try
                                use existing = directory.File(name, false, false) in true
                            with :? FileNotFoundException ->
                                false

                        if exists then
                            reuse ()
                        else
                            let stage =
                                ".stage-"
                                + Guid.NewGuid().ToString("N")

                            let mutable stageIdentity = None

                            try
                                // Keep the verified file open through the kernel publication operation.
                                use output = directory.File(stage, true, false)
                                let identity = directory.FileIdentity output
                                stageIdentity <- Some identity

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

                                if
                                    executable
                                    && not (OperatingSystem.IsWindows())
                                then
                                    File.SetUnixFileMode(
                                        output.SafeFileHandle,
                                        File.GetUnixFileMode(output.SafeFileHandle)
                                        ||| UnixFileMode.UserExecute
                                    )

                                output.Flush true
                                ct.ThrowIfCancellationRequested()
                                beforePublish.Invoke()
                                directory.ValidateLocation()
                                output.Position <- 0L

                                let verified, _ =
                                    Copying.hashCopy
                                        output
                                        Stream.Null
                                        InstallationLimits.MaxArtifactBytes
                                        "extension.artifact_too_large"
                                        ct

                                if
                                    verified
                                    <> expected.Value
                                then
                                    Operations.fail
                                        "extension.digest_mismatch"
                                        "The open staging object changed after copying."

                                if directory.Publish(output.SafeFileHandle, name) then
                                    if OperatingSystem.IsMacOS() then
                                        // fclonefileat creates a new inode from the verified descriptor. Verify its snapshot too.
                                        output.Dispose()
                                        reuse ()
                                    else
                                        if not (directory.HasFileIdentity(name, identity)) then
                                            Operations.fail
                                                "extension.store_object_mismatch"
                                                $"Published store identity changed: {absolute}"

                                        StoredArtifact(relative, absolute, expected, length)
                                else
                                    output.Dispose()
                                    reuse ()
                            finally
                                match stageIdentity with
                                | Some identity ->
                                    FileSafety.tryDeleteIdentity directory stage identity
                                | None -> ()
                    )
            )

    /// Hashes while copying from private staging inside Morphir Home. Reuse is rehashed; mismatched objects are never repaired.
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
