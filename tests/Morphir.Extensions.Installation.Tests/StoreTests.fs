// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.StoreTests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open Morphir.Extensions.Installation.Tests.IoTestSupport

let private bytes = Encoding.UTF8.GetBytes "a verified executable"

let private publish root source expected =
    SingleFileStore
        .PublishAsync(root, source, filename "backend.exe", expected, true, ct)
        .GetAwaiter()
        .GetResult()

// Proposed sealed-publication contract. No production declarations or implementation
// are added by this draft; all existing Store tests below remain unchanged.
let private sealedBytes
    (root: string)
    (content: byte array)
    (maximum: uint64)
    : Task<SealedArtifactStage * Stream> =
    task {
        let mutable observed: Stream option = None

        let writer =
            Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun output chunk token ->
                observed <- Some output
                output.WriteAsync(chunk, token)
            )

        let! started =
            PrivateStaging.BeginWithWriterAsync(
                root,
                uint64 content.Length,
                digest content,
                maximum,
                writer,
                ct
            )

        use! (writable: PrivateArtifactStage) = Task.FromResult(get started)
        do! writable.WriteAsync(ReadOnlyMemory<byte>(content), ct)
        let! result = writable.SealAsync ct

        let owned =
            observed
            |> Option.defaultWith (fun () ->
                failtest "Positive control never reached the owned writer."
            )

        return get result, owned
    }

let private sealedPublish
    (stage: SealedArtifactStage)
    (token: CancellationToken)
    : Task<InstallationResult<StoredArtifact>> =
    SingleFileStore.PublishAsync(stage, filename "backend.exe", true, token)

let private sealedHooks
    (stage: SealedArtifactStage)
    (before: Func<CancellationToken, Task>)
    (after: Action<string>)
    (cleanup: Action)
    (token: CancellationToken)
    : Task<InstallationResult<StoredArtifact>> =
    SingleFileStore.PublishSealedWithHooksAsync(
        stage,
        filename "backend.exe",
        true,
        before,
        after,
        cleanup,
        token
    )

let private sealedBefore (action: unit -> unit) =
    Func<CancellationToken, Task>(fun _ ->
        action ()
        Task.CompletedTask
    )

let private sealedAfter = Action<string>(ignore)
let private sealedCleanup = Action(ignore)

let private sealedTarget root =
    Path.Combine(root, "store", "extensions", "sha256", (digest bytes).Value, "backend.exe")

let private sealedOverwrite (stream: Stream) (content: byte array) =
    stream.Position <- 0L
    stream.SetLength(int64 content.Length)
    stream.Write(content, 0, content.Length)
    (stream :?> FileStream).Flush true

let private sealedReport (stage: SealedArtifactStage) =
    stage.CleanupReport
    |> Option.ofObj
    |> Option.defaultWith (fun () ->
        failtest "Awaited publication omitted its cleanup observation."
    )

let private sealedCancelled
    (token: CancellationToken)
    (operation: unit -> Task<InstallationResult<StoredArtifact>>)
    =
    task {
        let mutable observed = false

        try
            let! _ = operation ()
            ()
        with :? OperationCanceledException as error ->
            observed <- true

            Expect.equal
                error.CancellationToken
                token
                "primary caller cancellation survives cleanup"

        Expect.isTrue observed "async publication propagates cancellation before commit"
    }

let private sealedLifetime (owned: IDisposable) =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
            owned.Dispose()
            ValueTask.CompletedTask
    }

[<Tests>]
let tests =
    testList "Store" [
        test "new native files retain private owner permissions and reopen read-write" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let source = staged sandbox bytes

            let stored =
                publish root source (digest bytes)
                |> get

            let leasePath = Path.Combine(root, ".morphir-dotnet-io.lock")

            if not (OperatingSystem.IsWindows()) then
                Expect.equal
                    (File.GetUnixFileMode leasePath)
                    (UnixFileMode.UserRead
                     ||| UnixFileMode.UserWrite)
                    "native O_CREAT mode is private owner read-write"

                Expect.equal
                    (File.GetUnixFileMode stored.AbsolutePath)
                    (UnixFileMode.UserRead
                     ||| UnixFileMode.UserWrite
                     ||| UnixFileMode.UserExecute)
                    "publication preserves the intended source mode, including on a clone"

            // Dispose these observations before reacquiring the installer's cooperative lease.
            do
                use lease =
                    File.Open(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)

                Expect.isTrue lease.CanWrite "coordination file can be reopened by its owner"

            do
                use artifact =
                    File.Open(
                        stored.AbsolutePath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.Read
                    )

                Expect.equal
                    artifact.Length
                    (int64 bytes.Length)
                    "published object can be reopened read-write"

            let reused =
                publish root source (digest bytes)
                |> get

            Expect.equal
                (File.ReadAllBytes reused.AbsolutePath)
                bytes
                "reopening did not alter the winner"
        }
        test "publishes the bytes at the contract digest path without state metadata" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let stored =
                publish root (staged sandbox bytes) (digest bytes)
                |> get

            let relative = $"store/extensions/sha256/{(digest bytes).Value}/backend.exe"
            Expect.equal stored.RelativePath relative "parent single-file layout"
            Expect.equal (File.ReadAllBytes stored.AbsolutePath) bytes "copied bytes"
            Expect.equal stored.Length (int64 bytes.Length) "length copied"
            Expect.isFalse (Directory.Exists(Path.Combine(root, "catalog"))) "no pair metadata"
            Expect.equal (Directory.GetFiles(parent stored.AbsolutePath)).Length 1 "no stage files"

            if not (OperatingSystem.IsWindows()) then
                Expect.isTrue
                    ((File.GetUnixFileMode stored.AbsolutePath).HasFlag UnixFileMode.UserExecute)
                    "owner can execute"
        }
        test "rehashes an existing object and preserves a tampered winner" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let source = staged sandbox bytes

            let stored =
                publish root source (digest bytes)
                |> get

            let changed = Encoding.UTF8.GetBytes "changed object"
            File.WriteAllBytes(stored.AbsolutePath, changed)

            publish root source (digest bytes)
            |> refused "extension.store_object_mismatch"

            Expect.equal
                (File.ReadAllBytes stored.AbsolutePath)
                changed
                "never replaces the existing object"
        }
        test "reuse verifies matching bytes without rewriting the object" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let source = staged sandbox bytes

            let first =
                publish root source (digest bytes)
                |> get

            let timestamp = DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            File.SetLastWriteTimeUtc(first.AbsolutePath, timestamp)

            let second =
                publish root source (digest bytes)
                |> get

            Expect.equal second.AbsolutePath first.AbsolutePath "same object"
            Expect.equal (File.GetLastWriteTimeUtc second.AbsolutePath) timestamp "untouched"
        }
        test "a staged file changed after its recorded hash is never published" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let source = staged sandbox bytes
            File.WriteAllText(source, "rewritten after initial verification")

            publish root source (digest bytes)
            |> refused "extension.digest_mismatch"

            Expect.isEmpty (files (Path.Combine(root, "store"))) "no published or staged object"
        }
        for segment in
            [
                "store"
                "store/extensions"
                "store/extensions/sha256"
                $"store/extensions/sha256/{(digest bytes).Value}"
            ] do
            testCase $"refuses an escaping directory at {segment}"
            <| fun () ->
                use sandbox = TestSandbox.Create()
                let root = home sandbox
                let source = staged sandbox bytes

                let outside =
                    Directory.CreateDirectory(Path.Combine(sandbox.Root, "outside")).FullName

                let link = Path.Combine(root, segment.Replace('/', Path.DirectorySeparatorChar))

                Directory.CreateDirectory(parent link)
                |> ignore

                linkDirectory link outside

                publish root source (digest bytes)
                |> refused "extension.path_escape"

                Expect.isEmpty (Directory.GetFileSystemEntries outside) "nothing written outside"
        test "refuses a linked destination object even when its bytes match" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let outside = Path.Combine(sandbox.Root, "external.exe")
            File.WriteAllBytes(outside, bytes)

            let directory =
                Directory.CreateDirectory(
                    Path.Combine(root, "store", "extensions", "sha256", (digest bytes).Value)
                )

            linkFile (Path.Combine(directory.FullName, "backend.exe")) outside

            publish root (staged sandbox bytes) (digest bytes)
            |> refused "extension.path_escape"

            Expect.equal (File.ReadAllBytes outside) bytes "outside bytes unchanged"
        }
        test "refuses an escaping staged link before copying" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let outside = Path.Combine(sandbox.Root, "external.exe")
            File.WriteAllBytes(outside, bytes)
            let source = Path.Combine(root, "staged.exe")
            linkFile source outside

            publish root source (digest bytes)
            |> refused "extension.path_escape"

            Expect.isEmpty (files (Path.Combine(root, "store"))) "no object"
        }
        test "enforces the approved 256 MiB bound before reading an oversized file" {
            use sandbox = TestSandbox.Create()
            let source = staged sandbox [||]

            do
                use stream = File.OpenWrite source

                stream.SetLength(
                    256L
                    * 1024L
                    * 1024L
                    + 1L
                )

            publish (home sandbox) source (digest bytes)
            |> refused "extension.artifact_too_large"
        }
        test "a competing publication is verified, never clobbered" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let source = staged sandbox bytes
            let expected = digest bytes

            let target =
                Path.Combine(root, "store", "extensions", "sha256", expected.Value, "backend.exe")

            let winner = Encoding.UTF8.GetBytes "racing winner"

            let result =
                SingleFileStore
                    .PublishWithHookAsync(
                        root,
                        source,
                        filename "backend.exe",
                        expected,
                        true,
                        Action(fun () -> File.WriteAllBytes(target, winner)),
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()

            result
            |> refused "extension.store_object_mismatch"

            Expect.equal (File.ReadAllBytes target) winner "winner not replaced"
            Expect.equal (Directory.GetFiles(parent target)).Length 1 "losing stage removed"
        }
        test "cancellation does not publish an object" {
            use sandbox = TestSandbox.Create()
            use cancellation = new CancellationTokenSource()
            cancellation.Cancel()

            let result =
                SingleFileStore
                    .PublishAsync(
                        home sandbox,
                        staged sandbox bytes,
                        filename "backend.exe",
                        digest bytes,
                        true,
                        cancellation.Token
                    )
                    .GetAwaiter()
                    .GetResult()

            result
            |> refused "extension.cancelled"

            Expect.isEmpty (files (Path.Combine(sandbox.MorphirHome, "store"))) "no partial object"
        }
        test "publication keeps the verified file identity when its stage name is replaced" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let expected = digest bytes
            let directory = Path.Combine(root, "store", "extensions", "sha256", expected.Value)
            let foreign = Encoding.UTF8.GetBytes "foreign replacement"
            let mutable replaced: string option = None

            let hook =
                Action(fun () ->
                    let stage =
                        Directory.GetFiles(directory, ".stage-*")
                        |> Array.exactlyOne

                    try
                        File.Move(stage, Path.Combine(directory, "moved-original"))
                        File.WriteAllBytes(stage, foreign)
                        replaced <- Some stage
                    with :? IOException when OperatingSystem.IsWindows() ->
                        ()
                )

            let result =
                SingleFileStore
                    .PublishWithHookAsync(
                        root,
                        staged sandbox bytes,
                        filename "backend.exe",
                        expected,
                        true,
                        hook,
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()

            result.Match(
                Func<StoredArtifact, unit>(fun stored ->
                    Expect.equal
                        (File.ReadAllBytes stored.AbsolutePath)
                        bytes
                        "success must name the verified bytes"
                ),
                Func<InstallationFailure, unit>(fun _ ->
                    Expect.isFalse
                        (File.Exists(Path.Combine(directory, "backend.exe")))
                        "refusal publishes nothing"
                )
            )

            match replaced with
            | Some stage ->
                Expect.equal
                    (File.ReadAllBytes stage)
                    foreign
                    "cleanup must not remove the substituted foreign object"

                Expect.equal
                    (File.ReadAllBytes(Path.Combine(directory, "moved-original")))
                    bytes
                    "the other name stays untouched"
            | None -> ()
        }
        test "Unix refuses a digest directory renamed out before publication" {
            if OperatingSystem.IsWindows() then
                skiptest "Unix descriptor-relative rename interleaving"

            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let expected = digest bytes
            let directory = Path.Combine(root, "store", "extensions", "sha256", expected.Value)
            let outside = Path.Combine(sandbox.Root, "renamed-out")

            let result =
                SingleFileStore
                    .PublishWithHookAsync(
                        root,
                        staged sandbox bytes,
                        filename "backend.exe",
                        expected,
                        true,
                        Action(fun () -> Directory.Move(directory, outside)),
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()

            result
            |> refused "extension.path_escape"

            Expect.isFalse
                (File.Exists(Path.Combine(outside, "backend.exe")))
                "no commit through a moved directory descriptor"
        }
    ]
    |> testLabel "PlatformSpecific"

[<Tests>]
let sealedPublicationTests =
    testList "SealedPublication" [
        testTask "publishes sealed bytes with verified final identity and mode" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
            let (stored: StoredArtifact) = get result

            Expect.equal
                stored.AbsolutePath
                (sealedTarget root)
                "extension policy selects the existing store"

            Expect.equal stored.Length (int64 bytes.Length) "final verified length"
            Expect.equal stored.Digest (digest bytes) "final verified digest"

            Expect.equal
                (File.ReadAllBytes stored.AbsolutePath)
                bytes
                "actual published bytes, including a Darwin clone"

            if not (OperatingSystem.IsWindows()) then
                Expect.equal
                    (File.GetUnixFileMode stored.AbsolutePath)
                    (UnixFileMode.UserRead
                     ||| UnixFileMode.UserWrite
                     ||| UnixFileMode.UserExecute)
                    "actual published object has private launchable mode"

            Expect.isFalse
                owned.CanRead
                "publication releases transferred source ownership before returning"

            Expect.isFalse
                (sealedReport sealedStage).IsDeferred
                "uncontended private cleanup completes"

            Expect.equal
                (Directory.GetFiles(parent stored.AbsolutePath)).Length
                1
                "no second publication stage remains"

            Expect.isFalse
                (Directory.Exists(Path.Combine(root, "catalog")))
                "no shared metadata or activation"
        }
        testTask "small extension bytes accept a generic caller limit above 256 MiB" {
            use sandbox = TestSandbox.Create()

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes
                    (home sandbox)
                    bytes
                    (uint64 InstallationLimits.MaxArtifactBytes
                     + 1UL)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct

            Expect.equal
                (File.ReadAllBytes (get result).AbsolutePath)
                bytes
                "caller limit is not a host layout or extension size"
        }
        testTask "reuses a path-published winner without rewriting it" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let winner = get (publish root (staged sandbox bytes) (digest bytes))
            let stamp = DateTime(2010, 1, 2, 3, 4, 6, DateTimeKind.Utc)
            File.SetLastWriteTimeUtc(winner.AbsolutePath, stamp)

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
            let (reused: StoredArtifact) = get result
            Expect.equal reused.AbsolutePath winner.AbsolutePath "same common store entry"

            Expect.equal
                (File.GetLastWriteTimeUtc reused.AbsolutePath)
                stamp
                "matching winner is not rewritten"

            Expect.equal (File.ReadAllBytes reused.AbsolutePath) bytes "reuse verifies actual bytes"
        }
        testTask "refuses and preserves a corrupt existing winner" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let winner = get (publish root (staged sandbox bytes) (digest bytes))
            let corrupt = Encoding.UTF8.GetBytes "foreign corrupt winner"
            File.WriteAllBytes(winner.AbsolutePath, corrupt)

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
            refused "extension.store_object_mismatch" result
            Expect.equal (File.ReadAllBytes winner.AbsolutePath) corrupt "no repair or replacement"
        }
        testTask "Unix reuse refuses a winner without owner execute permission" {
            if OperatingSystem.IsWindows() then
                skiptest "Unix owner execute policy"

            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let winner = get (publish root (staged sandbox bytes) (digest bytes))

            File.SetUnixFileMode(
                winner.AbsolutePath,
                UnixFileMode.UserRead
                ||| UnixFileMode.UserWrite
            )

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
            refused "extension.not_launchable" result

            Expect.equal
                (File.GetUnixFileMode winner.AbsolutePath)
                (UnixFileMode.UserRead
                 ||| UnixFileMode.UserWrite)
                "reuse does not repair permissions"
        }
        for matching in [ true; false ] do
            testTask (
                if matching then
                    "rehashes a matching racing winner"
                else
                    "preserves a corrupt racing winner"
            ) {
                use sandbox = TestSandbox.Create()
                let root = home sandbox
                let target = sealedTarget root

                let winner =
                    if matching then
                        bytes
                    else
                        Encoding.UTF8.GetBytes "racing foreign object"

                let! ((sealedStage: SealedArtifactStage), _) =
                    sealedBytes root bytes (uint64 bytes.Length)

                use! stageLifetime = Task.FromResult(sealedStage)

                let before =
                    sealedBefore (fun () ->
                        File.WriteAllBytes(target, winner)

                        if not (OperatingSystem.IsWindows()) then
                            File.SetUnixFileMode(
                                target,
                                UnixFileMode.UserRead
                                ||| UnixFileMode.UserWrite
                                ||| UnixFileMode.UserExecute
                            )
                    )

                let! (result: InstallationResult<StoredArtifact>) =
                    sealedHooks sealedStage before sealedAfter sealedCleanup ct

                if matching then
                    Expect.equal (get result).AbsolutePath target "verified race winner is reused"
                else
                    refused "extension.store_object_mismatch" result

                Expect.equal
                    (File.ReadAllBytes target)
                    winner
                    "atomic no-clobber preserves the racing object"
            }
        for mutation in [ "changed"; "short"; "extra" ] do
            testTask ($"refuses {mutation} bytes in the sealed open object") {
                use sandbox = TestSandbox.Create()
                let root = home sandbox

                let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                    sealedBytes root bytes (uint64 bytes.Length)

                use! stageLifetime = Task.FromResult(sealedStage)

                let modified =
                    match mutation with
                    | "short" ->
                        bytes[0 .. bytes.Length
                                   - 2]
                    | "extra" -> Array.append bytes [| 0uy |]
                    | _ ->
                        Array.map
                            (fun value ->
                                value
                                ^^^ 1uy
                            )
                            bytes

                sealedOverwrite owned modified
                let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
                refused "extension.digest_mismatch" result

                Expect.isFalse
                    (File.Exists(sealedTarget root))
                    "seal metadata alone never authorizes changed bytes"
            }
        testTask "rehashes after the before-commit seam before publishing" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let mutable reached = false

            let before =
                sealedBefore (fun () ->
                    reached <- true

                    sealedOverwrite
                        owned
                        (Array.map
                            (fun value ->
                                value
                                ^^^ 1uy
                            )
                            bytes)
                )

            let! (result: InstallationResult<StoredArtifact>) =
                sealedHooks sealedStage before sealedAfter sealedCleanup ct

            Expect.isTrue reached "actual commit boundary was reached"
            refused "extension.digest_mismatch" result

            Expect.isFalse
                (File.Exists(sealedTarget root))
                "changed open bytes never become visible"
        }
        testTask "Unix refuses a substituted private source name and preserves foreign bytes" {
            if OperatingSystem.IsWindows() then
                skiptest "Windows sharing forbids this live-name substitution"

            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)

            let source =
                Directory.GetFiles(Path.Combine(root, "tmp"), "bytes", SearchOption.AllDirectories)
                |> Array.exactlyOne

            let moved =
                source
                + ".original"

            let foreign = Encoding.UTF8.GetBytes "foreign source name"
            File.Move(source, moved)
            File.WriteAllBytes(source, foreign)
            let! (result: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct
            refused "extension.path_escape" result
            Expect.isFalse (File.Exists(sealedTarget root)) "no pathname fallback"
            Expect.equal (File.ReadAllBytes source) foreign "foreign replacement remains"
            Expect.equal (File.ReadAllBytes moved) bytes "renamed original remains"
        }
        testTask "Unix refuses captured Home renamed out at the commit boundary" {
            if OperatingSystem.IsWindows() then
                skiptest "Windows denies this directory rename while handles are held"

            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let away = Path.Combine(sandbox.Root, "renamed-home")

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let before = sealedBefore (fun () -> Directory.Move(root, away))

            let! (result: InstallationResult<StoredArtifact>) =
                sealedHooks sealedStage before sealedAfter sealedCleanup ct

            refused "extension.path_escape" result

            Expect.isFalse
                (File.Exists(sealedTarget away))
                "no writes through a renamed-out directory descriptor"
        }
        testTask "pre-cancelled publication propagates its token without publishing" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(sealedLifetime cancellation)
            cancellation.Cancel()

            do!
                sealedCancelled
                    cancellation.Token
                    (fun () -> sealedPublish sealedStage cancellation.Token)

            Expect.isFalse (File.Exists(sealedTarget root)) "pre-cancellation has no commit"
        }
        testTask "cancellation before atomic commit releases ownership and publishes nothing" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(sealedLifetime cancellation)
            let before = sealedBefore cancellation.Cancel

            do!
                sealedCancelled
                    cancellation.Token
                    (fun () ->
                        sealedHooks sealedStage before sealedAfter sealedCleanup cancellation.Token
                    )

            Expect.isFalse (File.Exists(sealedTarget root)) "no false committed outcome"
            Expect.isFalse owned.CanRead "admitted ownership is closed before cancellation returns"

            sealedReport sealedStage
            |> ignore
        }
        testTask "cancellation after the actual commit cannot report rollback" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(sealedLifetime cancellation)

            let after =
                Action<string>(fun target ->
                    Expect.isTrue (File.Exists target) "hook follows actual native commit"
                    cancellation.Cancel()
                )

            let! (result: InstallationResult<StoredArtifact>) =
                sealedHooks sealedStage (sealedBefore ignore) after sealedCleanup cancellation.Token

            let (stored: StoredArtifact) = get result
            Expect.isTrue cancellation.IsCancellationRequested "late cancellation control executed"

            Expect.equal
                (File.ReadAllBytes stored.AbsolutePath)
                bytes
                "confirmed commit still receives final verification"

            Expect.isFalse owned.CanRead "no detached cleanup or live owner"

            sealedReport sealedStage
            |> ignore
        }
        testTask "verifies actual published bytes including the Darwin clone" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)

            let corrupt =
                Array.map
                    (fun value ->
                        value
                        ^^^ 1uy
                    )
                    bytes

            let mutable reached = false

            let after =
                Action<string>(fun target ->
                    reached <- true

                    if OperatingSystem.IsMacOS() then
                        // A real fclonefileat result has a new inode. Mutating only
                        // its source would not test final clone verification.
                        File.WriteAllBytes(target, corrupt)
                    else
                        sealedOverwrite owned corrupt
                )

            let! (result: InstallationResult<StoredArtifact>) =
                sealedHooks sealedStage (sealedBefore ignore) after sealedCleanup ct

            Expect.isTrue reached "actual native publication happened"

            result.Match(
                Func<StoredArtifact, unit>(fun _ ->
                    failtest "Unverified visible bytes cannot report success."
                ),
                Func<InstallationFailure, unit>(fun failure ->
                    Expect.equal
                        failure.Code
                        "extension.store_object_mismatch"
                        "required final verification failed"

                    Expect.isTrue
                        (failure.Message.Contains(sealedTarget root, StringComparison.Ordinal))
                        "failure identifies the already-visible object"
                )
            )

            Expect.equal
                (File.ReadAllBytes(sealedTarget root))
                corrupt
                "visible unverified object is retained, never silently repaired"
        }
        testTask "Unix verifies actual published mode after the native commit" {
            if OperatingSystem.IsWindows() then
                skiptest "Unix owner execute policy"

            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)

            let after =
                Action<string>(fun target ->
                    File.SetUnixFileMode(
                        target,
                        UnixFileMode.UserRead
                        ||| UnixFileMode.UserWrite
                    )
                )

            let! (result: InstallationResult<StoredArtifact>) =
                sealedHooks sealedStage (sealedBefore ignore) after sealedCleanup ct

            refused "extension.not_launchable" result

            Expect.isTrue
                (File.Exists(sealedTarget root))
                "a visible clone/link is not mislabeled as rollback"

            Expect.equal
                (File.GetUnixFileMode(sealedTarget root))
                (UnixFileMode.UserRead
                 ||| UnixFileMode.UserWrite)
                "final mode was observed, not assumed from source"
        }
        testTask "consumed sealed ownership cannot publish a second destination name" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), _) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let! (first: InstallationResult<StoredArtifact>) = sealedPublish sealedStage ct

            get first
            |> ignore

            let mutable refusedAgain = false

            try
                let! _ = SingleFileStore.PublishAsync(sealedStage, filename "second.exe", true, ct)
                ()
            with :? InvalidOperationException ->
                refusedAgain <- true

            Expect.isTrue refusedAgain "single transferred owner is consumed at most once"

            Expect.isFalse
                (File.Exists(Path.Combine(parent (sealedTarget root), "second.exe")))
                "no duplicate commit"
        }
        testTask "concurrent publication refuses and disposal joins the admitted commit" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)

            let entered =
                TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

            let resume =
                TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

            let before =
                Func<CancellationToken, Task>(fun token -> task {
                    entered.TrySetResult(())
                    |> ignore

                    do! resume.Task.WaitAsync(TimeSpan.FromSeconds 10., token)
                })

            let admitted = sealedHooks sealedStage before sealedAfter sealedCleanup ct
            // Even a failing assertion must release the pause and observe the
            // operation before the stage lifetime closes its native resources.
            let join =
                { new IAsyncDisposable with
                    member _.DisposeAsync() =
                        ValueTask(
                            task {
                                resume.TrySetResult(())
                                |> ignore

                                try
                                    let! _ = admitted
                                    ()
                                with _ ->
                                    ()
                            }
                        )
                }

            use! admittedLifetime = Task.FromResult(join)

            try
                do! entered.Task.WaitAsync(TimeSpan.FromSeconds 10.)
                let mutable refusedConcurrent = false

                try
                    let! _ = sealedPublish sealedStage ct
                    ()
                with :? InvalidOperationException ->
                    refusedConcurrent <- true

                Expect.isTrue refusedConcurrent "overlapping commits cannot share ownership"
                let disposal = (sealedStage :> IAsyncDisposable).DisposeAsync().AsTask()
                Expect.isFalse disposal.IsCompleted "disposal waits for the admitted commit"
                Expect.isTrue owned.CanRead "source stays open while commit is paused"

                resume.TrySetResult(())
                |> ignore

                let! (result: InstallationResult<StoredArtifact>) =
                    admitted.WaitAsync(TimeSpan.FromSeconds 10.)

                get result
                |> ignore

                do! disposal.WaitAsync(TimeSpan.FromSeconds 10.)
                Expect.isFalse owned.CanRead "both operations finish before ownership is closed"

                Expect.equal
                    (File.ReadAllBytes(sealedTarget root))
                    bytes
                    "one verified committed object"
            finally
                resume.TrySetResult(())
                |> ignore
        }
        testTask "busy postcommit cleanup reports a closed unowned private orphan" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let! ((sealedStage: SealedArtifactStage), (owned: Stream)) =
                sealedBytes root bytes (uint64 bytes.Length)

            use! stageLifetime = Task.FromResult(sealedStage)
            let rootDirectory = SafeDirectory.Open(root, true)
            use! rootLifetime = Task.FromResult(sealedLifetime rootDirectory)
            let mutable busy: IDisposable option = None

            let beforeCleanup =
                Action(fun () -> busy <- Some(FileSafety.mutationLease rootDirectory))

            try
                let! (result: InstallationResult<StoredArtifact>) =
                    sealedHooks sealedStage (sealedBefore ignore) sealedAfter beforeCleanup ct

                let (stored: StoredArtifact) = get result

                Expect.equal
                    (File.ReadAllBytes stored.AbsolutePath)
                    bytes
                    "cleanup cannot invent rollback"

                let report = sealedReport sealedStage

                Expect.isTrue
                    report.IsDeferred
                    "busy supported-writer lease leaves observable physical deferral"

                Expect.isSome
                    (Option.ofObj report.Failure)
                    "cleanup diagnostic is available through nullable metadata"

                Expect.isFalse owned.CanRead "deferred cleanup retains no live data owner"

                let owner =
                    Directory.GetFiles(
                        Path.Combine(root, "tmp"),
                        ".owner",
                        SearchOption.AllDirectories
                    )
                    |> Array.exactlyOne

                let released = File.Open(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                use! releasedLifetime = Task.FromResult(sealedLifetime released)
                Expect.isTrue released.CanRead "orphan owner lease was released before return"
            finally
                busy
                |> Option.iter (fun lease -> lease.Dispose())
        }
    ]
    |> testLabel "PlatformSpecific"
