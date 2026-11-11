// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.PrivateStagingTests

open System
open System.IO
open System.Reflection
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open Morphir.Mep
open Morphir.Extensions.Installation.Tests.IoTestSupport

// Test-first draft against the proposed API. Only declaration stubs exist so far.
// The internal writer replaces only the awaited byte write, inside the real stage's
// admission/count/hash/ownership machinery. It must receive the actual owned stream.
let private bytes = Encoding.UTF8.GetBytes "private streamed bytes"
let private deadline = TimeSpan.FromSeconds 10.

let private beginStage
    (root: string)
    (expectedLength: uint64)
    (expectedDigest: Sha256Digest)
    (maximum: uint64)
    : Task<PrivateArtifactStage> =
    task {
        let! result = PrivateStaging.BeginAsync(root, expectedLength, expectedDigest, maximum, ct)
        return get result
    }

let private beginWithWriter
    (root: string)
    (writer: Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>)
    : Task<PrivateArtifactStage> =
    task {
        let! result =
            PrivateStaging.BeginWithWriterAsync(
                root,
                uint64 bytes.Length,
                digest bytes,
                uint64 bytes.Length,
                writer,
                ct
            )

        return get result
    }

let private write (stage: PrivateArtifactStage) (content: byte array) =
    stage.WriteAsync(ReadOnlyMemory<byte>(content), ct).AsTask()

let private rejected (result: InstallationResult<'T>) =
    result.Match(
        Func<'T, unit>(fun _ ->
            failtest "The operation must refuse, without transferring ownership."
        ),
        Func<InstallationFailure, unit>(fun error ->
            Expect.isFalse (String.IsNullOrWhiteSpace error.Code) "refusal has a diagnostic"
        )
    )

let private throws<'T when 'T :> exn> (action: unit -> Task) = task {
    let mutable observed = false

    try
        do! action ()
    with :? 'T ->
        observed <- true

    Expect.isTrue observed $"Expected {typeof<'T>.Name}."
}

let private signal () =
    TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

let private finish (source: TaskCompletionSource<unit>) =
    source.TrySetResult(())
    |> ignore

// Expecto's task builder uses asynchronous disposal; adapt this test-owned resource.
let private asyncCleanup (owned: IDisposable) =
    { new IAsyncDisposable with
        member _.DisposeAsync() =
            owned.Dispose()
            ValueTask.CompletedTask
    }

[<Tests>]
let tests =
    testList "PrivateStaging" [
        testTask "valid chunks seal with exact length and digest" {
            use sandbox = TestSandbox.Create()

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do! stage.WriteAsync(ReadOnlyMemory<byte>(bytes, 0, 3), ct)

            do!
                stage.WriteAsync(
                    ReadOnlyMemory<byte>(
                        bytes,
                        3,
                        bytes.Length
                        - 3
                    ),
                    ct
                )

            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)
            Expect.equal sealedStage.Length (uint64 bytes.Length) "verified count"
            Expect.equal sealedStage.Digest (digest bytes) "verified digest"
            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "empty bytes have a valid seal" {
            use sandbox = TestSandbox.Create()
            use! (stage: PrivateArtifactStage) = beginStage (home sandbox) 0UL (digest [||]) 0UL
            do! write stage [||]
            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)
            Expect.equal sealedStage.Length 0UL "zero is an exact count"
            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "generic begin does not inherit the extension 256 MiB cap" {
            use sandbox = TestSandbox.Create()

            let length =
                uint64 InstallationLimits.MaxArtifactBytes
                + 1UL

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) length (digest bytes) length
            // Begin allocates no buffer or file length proportional to this declaration.
            Expect.isEmpty
                (files (Path.Combine(home sandbox, "store")))
                "no extension destination chosen"
        }
        testTask "recorded length above caller bound is refused before creating a file" {
            use sandbox = TestSandbox.Create()

            use! (control: PrivateArtifactStage) =
                beginStage (Path.Combine(sandbox.Root, "control-home")) 0UL (digest [||]) 0UL

            let root = home sandbox
            let before = files root
            let! result = PrivateStaging.BeginAsync(root, 2UL, digest bytes, 1UL, ct)
            rejected result
            Expect.sequenceEqual (files root) before "no coordination or staging side effect"
        }
        testTask "unrepresentable recorded length is refused before creating a file" {
            use sandbox = TestSandbox.Create()

            use! (control: PrivateArtifactStage) =
                beginStage (Path.Combine(sandbox.Root, "control-home")) 0UL (digest [||]) 0UL

            let root = home sandbox
            let before = files root

            let! result =
                PrivateStaging.BeginAsync(root, UInt64.MaxValue, digest bytes, UInt64.MaxValue, ct)

            rejected result
            Expect.sequenceEqual (files root) before "no allocation from attacker length"
        }
        testTask "cancelled begin propagates cancellation without filesystem effects" {
            use sandbox = TestSandbox.Create()

            use! (control: PrivateArtifactStage) =
                beginStage (Path.Combine(sandbox.Root, "control-home")) 0UL (digest [||]) 0UL

            let root = home sandbox
            let before = files root
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(asyncCleanup cancellation)
            cancellation.Cancel()

            do!
                throws<OperationCanceledException> (fun () -> task {
                    let! _ =
                        PrivateStaging.BeginAsync(
                            root,
                            uint64 bytes.Length,
                            digest bytes,
                            uint64 bytes.Length,
                            cancellation.Token
                        )

                    return ()
                })

            Expect.sequenceEqual (files root) before "no detached begin work"
        }
        testTask "short stream cannot transfer a sealed owner" {
            use sandbox = TestSandbox.Create()

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do!
                stage.WriteAsync(
                    ReadOnlyMemory<byte>(
                        bytes,
                        0,
                        bytes.Length
                        - 1
                    ),
                    ct
                )

            let! result = stage.SealAsync ct
            rejected result
        }
        testTask "wrong digest cannot transfer a sealed owner" {
            use sandbox = TestSandbox.Create()

            use! (stage: PrivateArtifactStage) =
                beginStage
                    (home sandbox)
                    (uint64 bytes.Length)
                    (digest [| 1uy |])
                    (uint64 bytes.Length)

            do! write stage bytes
            let! result = stage.SealAsync ct
            refused "extension.digest_mismatch" result
        }
        testTask "extra bytes are refused before the inner writer and fault the stage" {
            use sandbox = TestSandbox.Create()
            let mutable calls = 0

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    calls <- calls + 1
                    output.WriteAsync(content, token)
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            do! write stage bytes
            do! throws<IOException> (fun () -> write stage [| 0uy |])
            Expect.equal calls 1 "excess byte never reaches the owned file"
            let! result = stage.SealAsync ct
            rejected result
        }
        testTask "caller may reuse its buffer after awaited write" {
            use sandbox = TestSandbox.Create()
            let buffer = Array.copy bytes

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do! write stage buffer
            Array.Fill(buffer, 0uy)
            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)

            Expect.equal
                sealedStage.Digest
                (digest bytes)
                "seal verifies owned bytes, not borrowed memory"

            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "seal transfers the same open object and old disposal cannot close it" {
            use sandbox = TestSandbox.Create()

            let observed =
                TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously)

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    observed.TrySetResult output
                    |> ignore

                    output.WriteAsync(content, token)
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            do! write stage bytes
            let! (ownedStream: Stream) = observed.Task.WaitAsync deadline
            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)
            do! throws<InvalidOperationException> (fun () -> write stage [||])
            let! duplicate = stage.SealAsync ct
            rejected duplicate
            do! (stage :> IAsyncDisposable).DisposeAsync()
            Expect.isTrue ownedStream.CanRead "writable wrapper no longer owns disposal"
            Expect.equal ownedStream.Length (int64 bytes.Length) "original open object remains held"
            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
            Expect.isFalse ownedStream.CanRead "sealed owner releases the original stream"
        }
        for cancelled in [ false; true ] do
            testTask (
                if cancelled then
                    "partial cancelled write faults all later admission"
                else
                    "partial failed write faults all later admission"
            ) {
                use sandbox = TestSandbox.Create()
                let cancellation = new CancellationTokenSource()
                use! cancellationLifetime = Task.FromResult(asyncCleanup cancellation)
                let prefixWritten, resume = signal (), signal ()
                let mutable calls = 0

                let writer =
                    Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                         output
                                                                                         content
                                                                                         token ->
                        ValueTask(
                            task {
                                calls <- calls + 1
                                do! output.WriteAsync(content.Slice(0, 1), token)
                                do! output.FlushAsync token

                                Expect.equal
                                    output.Position
                                    1L
                                    "actual prefix reached the owned object"

                                finish prefixWritten
                                do! resume.Task.WaitAsync deadline

                                if cancelled then
                                    token.ThrowIfCancellationRequested()

                                raise (IOException "injected failure after an actual prefix")
                            }
                        )
                    )

                use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer

                let writing =
                    stage.WriteAsync(ReadOnlyMemory<byte>(bytes), cancellation.Token).AsTask()

                try
                    do! prefixWritten.Task.WaitAsync deadline

                    if cancelled then
                        cancellation.Cancel()
                finally
                    finish resume

                if cancelled then
                    do! throws<OperationCanceledException> (fun () -> writing)
                else
                    do! throws<IOException> (fun () -> writing)

                do! throws<InvalidOperationException> (fun () -> write stage bytes)
                let! result = stage.SealAsync ct
                rejected result
                Expect.equal calls 1 "neither retry nor seal can resume an uncertain prefix"
            }
        testTask "concurrent write is refused without corrupting the admitted write" {
            use sandbox = TestSandbox.Create()
            let entered, resume = signal (), signal ()

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    ValueTask(
                        task {
                            finish entered
                            do! resume.Task.WaitAsync deadline
                            do! output.WriteAsync(content, token)
                        }
                    )
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            let first = write stage bytes

            try
                do! entered.Task.WaitAsync deadline
                do! throws<InvalidOperationException> (fun () -> write stage bytes)
                Expect.isFalse first.IsCompleted "admitted writer is still paused"
            finally
                finish resume

            do! first.WaitAsync deadline
            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)

            Expect.equal
                sealedStage.Digest
                (digest bytes)
                "concurrent refusal does not poison successful write"

            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "dispose awaits the admitted write before closing ownership" {
            use sandbox = TestSandbox.Create()
            let entered, resume = signal (), signal ()

            let observed =
                TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously)

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    ValueTask(
                        task {
                            observed.TrySetResult output
                            |> ignore

                            finish entered
                            do! resume.Task.WaitAsync deadline
                            do! output.WriteAsync(content, token)
                        }
                    )
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            let writing = write stage bytes
            let mutable disposal = Task.CompletedTask

            try
                do! entered.Task.WaitAsync deadline
                disposal <- (stage :> IAsyncDisposable).DisposeAsync().AsTask()
                Expect.isFalse disposal.IsCompleted "dispose must join admitted byte I/O"
            finally
                finish resume

            do! writing.WaitAsync deadline
            do! disposal.WaitAsync deadline
            let! (ownedStream: Stream) = observed.Task.WaitAsync deadline
            Expect.isFalse ownedStream.CanRead "no owned handle remains after awaited disposal"
            do! throws<InvalidOperationException> (fun () -> write stage bytes)
            let! result = stage.SealAsync ct
            rejected result
        }
        testTask "a second process acquires mutation lease while a byte write is paused" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let entered, resume = signal (), signal ()

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    ValueTask(
                        task {
                            finish entered
                            do! resume.Task.WaitAsync deadline
                            do! output.WriteAsync(content, token)
                        }
                    )
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter root writer
            let writing = write stage bytes

            try
                do! entered.Task.WaitAsync deadline

                let start =
                    ChildProcess.StartInfo(
                        "dotnet",
                        [|
                            "exec"
                            Assembly.GetExecutingAssembly().Location
                            "--mutation-lease-only"
                            root
                        |]
                    )

                sandbox.Apply start

                let! (result: ChildProcessResult) =
                    ChildProcess.RunAsync(start, TimeSpan.FromSeconds 5., ct)

                Expect.equal result.ExitCode 0 result.CombinedOutput

                Expect.stringContains
                    result.StandardOutput
                    "mutation lease acquired"
                    "real interprocess acquisition"

                Expect.isFalse writing.IsCompleted "lease was acquired during transport-owned pause"
            finally
                finish resume

            do! writing.WaitAsync deadline
            let! result = stage.SealAsync ct
            use! (sealedStage: SealedArtifactStage) = Task.FromResult(get result)
            do! (sealedStage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "pre-cancelled admitted write faults sealing without calling the writer" {
            use sandbox = TestSandbox.Create()
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(asyncCleanup cancellation)
            let mutable calls = 0

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    calls <- calls + 1
                    output.WriteAsync(content, token)
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            cancellation.Cancel()

            do!
                throws<OperationCanceledException> (fun () ->
                    stage.WriteAsync(ReadOnlyMemory<byte>(bytes), cancellation.Token).AsTask()
                )

            Expect.equal calls 0 "cancelled admission writes no prefix"
            do! throws<InvalidOperationException> (fun () -> write stage bytes)
            let! result = stage.SealAsync ct
            rejected result
        }
        testTask "oversized first chunk never writes a permitted prefix" {
            use sandbox = TestSandbox.Create()
            let mutable calls = 0

            let writer =
                Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                     output
                                                                                     content
                                                                                     token ->
                    calls <- calls + 1
                    output.WriteAsync(content, token)
                )

            use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
            do! throws<IOException> (fun () -> write stage (Array.append bytes [| 0uy |]))
            Expect.equal calls 0 "reject whole excess chunk before the owned stream"
            let! result = stage.SealAsync ct
            rejected result
        }
        testTask "failed seal cannot return to writable ownership" {
            use sandbox = TestSandbox.Create()

            use! (stage: PrivateArtifactStage) =
                beginStage
                    (home sandbox)
                    (uint64 bytes.Length)
                    (digest [| 1uy |])
                    (uint64 bytes.Length)

            do! write stage bytes
            let! result = stage.SealAsync ct
            refused "extension.digest_mismatch" result
            do! throws<InvalidOperationException> (fun () -> write stage [||])
            let! duplicate = stage.SealAsync ct
            rejected duplicate
        }
        testTask "cancelled seal preserves cancellation and faults later sealing" {
            use sandbox = TestSandbox.Create()
            let cancellation = new CancellationTokenSource()
            use! cancellationLifetime = Task.FromResult(asyncCleanup cancellation)

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do! write stage bytes
            cancellation.Cancel()

            do!
                throws<OperationCanceledException> (fun () -> task {
                    let! _ = stage.SealAsync cancellation.Token
                    return ()
                })

            do! throws<InvalidOperationException> (fun () -> write stage [||])
            let! result = stage.SealAsync ct
            rejected result
        }
        for append in [ false; true ] do
            testTask (
                if append then
                    "seal detects extra bytes in its same open object"
                else
                    "seal rehashes changed bytes in its same open object"
            ) {
                use sandbox = TestSandbox.Create()

                let writer =
                    Func<Stream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>(fun
                                                                                         output
                                                                                         content
                                                                                         token ->
                        ValueTask(
                            task {
                                do! output.WriteAsync(content, token)

                                if not append then
                                    output.Position <- 0L

                                do! output.WriteAsync(ReadOnlyMemory<byte>([| 0uy |]), token)
                            }
                        )
                    )

                use! (stage: PrivateArtifactStage) = beginWithWriter (home sandbox) writer
                do! write stage bytes
                let! result = stage.SealAsync ct
                refused "extension.digest_mismatch" result
                do! throws<InvalidOperationException> (fun () -> write stage [||])
            }
        testTask "awaited disposal exposes completed or deferred physical cleanup" {
            use sandbox = TestSandbox.Create()

            use! (stage: PrivateArtifactStage) =
                beginStage (home sandbox) (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do! write stage bytes
            do! (stage :> IAsyncDisposable).DisposeAsync()

            match stage.CleanupReport with
            | null -> failtest "Awaited disposal must expose its physical cleanup outcome."
            | report -> Expect.isFalse report.IsDeferred "uncontended owned files are removed"

            do! (stage :> IAsyncDisposable).DisposeAsync()
        }
        testTask "busy cleanup releases ownership and exposes a closed private orphan" {
            use sandbox = TestSandbox.Create()
            let rootPath = home sandbox

            use! (stage: PrivateArtifactStage) =
                beginStage rootPath (uint64 bytes.Length) (digest bytes) (uint64 bytes.Length)

            do! write stage bytes
            let root = SafeDirectory.Open(rootPath, false)
            use! rootLifetime = Task.FromResult(asyncCleanup root)
            let mutation = FileSafety.mutationLease root
            use! mutationLifetime = Task.FromResult(asyncCleanup mutation)
            do! (stage :> IAsyncDisposable).DisposeAsync()

            match stage.CleanupReport with
            | null -> failtest "Awaited disposal must report deferred physical cleanup."
            | report ->
                Expect.isTrue report.IsDeferred "busy global lease prevents uncoordinated deletion"

            let directories =
                Directory.GetDirectories(Path.Combine(rootPath, "tmp"), "morphir-dotnet-stage-*")

            Expect.equal directories.Length 1 "one closed private orphan, no shared journal"
            let directory = SafeDirectory.Open(directories[0], false)
            use! directoryLifetime = Task.FromResult(asyncCleanup directory)
            let owner = directory.File(".owner", false, true)
            use! ownerLifetime = Task.FromResult(asyncCleanup owner)
            Expect.isTrue owner.CanRead "disposal released the original native owner lease"
        }
    ]
