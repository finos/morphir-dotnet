// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.NamedLockTests

open System
open System.IO
open System.Reflection
open System.Threading
open System.Runtime.InteropServices
open System.Threading.Tasks
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open IoTestSupport

[<DllImport("libc", EntryPoint = "dup", SetLastError = true)>]
extern int private dup(int descriptor)

[<DllImport("libc", EntryPoint = "close", SetLastError = true)>]
extern int private close(int descriptor)

// A child process between fork and exec holds a copy of each descriptor, as dup does: both share the open file
// description that owns the flock, so close() alone leaves the lock held until the child execs.
let private releasesWhileDescriptionIsShared
    (root: SafeDirectory)
    name
    (acquire: unit -> FileStream)
    (dispose: FileStream -> unit)
    =
    let lease = acquire ()
    let copy = dup (lease.SafeFileHandle.DangerousGetHandle().ToInt32())

    if copy < 0 then
        failtest $"dup failed: {Marshal.GetLastPInvokeError()}"

    try
        dispose lease
        use next = root.NamedLease(name, true, false)
        Expect.isTrue next.CanRead $"{name}: disposal released the lock of the shared description"
    finally
        close copy |> ignore

[<Tests>]
let tests =
    testLabel "PlatformSpecific" <| testList "NamedLocks" [
        test "lease disposal releases the lock while a starting child process shares its descriptor" {
            if OperatingSystem.IsWindows() then
                skiptest "Windows creates lease handles uninheritable; no child process shares them"

            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)

            let dispose (lease: FileStream) = lease.Dispose()

            let disposeAsync (lease: FileStream) =
                lease.DisposeAsync().AsTask().GetAwaiter().GetResult()

            let named name () = root.NamedLease(name, true, false)

            let namedAsync name () =
                root
                    .NamedLeaseAsync(name, true, false, ct)
                    .WaitAsync(TimeSpan.FromSeconds 3.)
                    .GetAwaiter()
                    .GetResult()

            releasesWhileDescriptionIsShared root "sync.lock" (named "sync.lock") dispose
            releasesWhileDescriptionIsShared root "async.lock" (namedAsync "async.lock") dispose
            releasesWhileDescriptionIsShared root "sync-await.lock" (named "sync-await.lock") disposeAsync
            releasesWhileDescriptionIsShared root "async-await.lock" (namedAsync "async-await.lock") disposeAsync

            releasesWhileDescriptionIsShared
                root
                "private.lease"
                (fun () -> root.File("private.lease", false, true))
                dispose
        }
        test "native async cancellation settles before returning and releases its open owner" {
            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)
            use held = root.NamedLease("cancel.lock", true, false)
            use cancellation = new CancellationTokenSource()
            let completions = Collections.Concurrent.ConcurrentQueue<int>()
            let elapsed = Diagnostics.Stopwatch.StartNew()

            let pending =
                root.NamedLeaseObservedAsync(
                    "cancel.lock",
                    true,
                    true,
                    cancellation.Token,
                    Action<int>(fun status ->
                        completions.Enqueue status

                        if status = 997 then
                            cancellation.Cancel()
                    )
                )

            try
                Expect.isLessThan
                    elapsed.Elapsed
                    (TimeSpan.FromMilliseconds 500.)
                    "returns without a blocking kernel wait on caller"

                if not (OperatingSystem.IsWindows()) then
                    cancellation.CancelAfter 100

                try
                    use unexpected =
                        pending.WaitAsync(TimeSpan.FromSeconds 3.).GetAwaiter().GetResult()

                    failtest "cancelled acquisition must not return a lease"
                with :? OperationCanceledException as error ->
                    Expect.equal error.CancellationToken cancellation.Token "original token"

                if OperatingSystem.IsWindows() then
                    Expect.equal
                        (completions.ToArray())
                        [| 997; 995 |]
                        "actual pending and cancelled OVERLAPPED completion before task settlement"

                held.Dispose()
                use next = root.NamedLease("cancel.lock", true, false)
                Expect.isTrue next.CanRead "no late native waiter or leaked range lock"
            finally
                cancellation.Cancel()
                held.Dispose()

                try
                    use settled =
                        pending.WaitAsync(TimeSpan.FromSeconds 3.).GetAwaiter().GetResult()

                    ()
                with :? OperationCanceledException ->
                    ()
        }
        test "precancelled native admission creates no lock file" {
            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)
            use cancellation = new CancellationTokenSource()
            cancellation.Cancel()

            try
                use unexpected =
                    root
                        .NamedLeaseAsync("absent.lock", true, true, cancellation.Token)
                        .WaitAsync(TimeSpan.FromSeconds 3.)
                        .GetAwaiter()
                        .GetResult()

                failtest "precancelled admission cannot return ownership"
            with :? OperationCanceledException as error ->
                Expect.equal error.CancellationToken cancellation.Token "original token"

            Expect.isFalse
                (File.Exists(Path.Combine(home sandbox, "absent.lock")))
                "no admission side effect"
        }
        test "successful ownership is transferred once and survives later token cancellation" {
            use sandbox = TestSandbox.Create()
            use root = SafeDirectory.Open(home sandbox, false)
            use cancellation = new CancellationTokenSource()

            use acquired =
                root
                    .NamedLeaseAsync("owned.lock", true, true, cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds 3.)
                    .GetAwaiter()
                    .GetResult()

            cancellation.Cancel()

            Expect.throwsT<FileLeaseBusyException>
                (fun () ->
                    use unexpected = root.NamedLease("owned.lock", true, false)
                    ()
                )
                "caller still owns the returned lease"

            acquired.Dispose()

            use next =
                root
                    .NamedLeaseAsync("owned.lock", true, false, ct)
                    .WaitAsync(TimeSpan.FromSeconds 3.)
                    .GetAwaiter()
                    .GetResult()

            Expect.isTrue next.CanRead "disposal releases the transferred owner"
        }
        test "process architecture admits x64 Windows emulation without changing OS-platform policy" {
            let actual =
                ActivationPlatforms.forProcess true false false Architecture.X64 "win-x64"
                |> StateOwnership.unwrap

            Expect.equal
                actual.RuntimeIdentifier
                "win-x64"
                "x64 process on either Windows OS architecture"

            let arm =
                ActivationPlatforms.forProcess true false false Architecture.Arm64 "win-arm64"
                |> StateOwnership.unwrap

            Expect.equal arm.RuntimeIdentifier "win-arm64" "native ARM64 process"

            let linux =
                ActivationPlatforms.forProcess false false true Architecture.X64 "linux-musl-x64"
                |> StateOwnership.unwrap

            Expect.equal linux.RuntimeIdentifier "linux-musl-x64" "musl remains distinct"
        }
        for name in [ "extensions.state.lock"; "extensions.repositories.lock" ] do
            test (
                $"contended {name} API returns asynchronously and cancels before its holder releases"
            ) {
                use sandbox = TestSandbox.Create()
                let rootPath = home sandbox
                use root = SafeDirectory.Open(rootPath, false)
                use locks = root.Child("locks", true)
                use held = locks.NamedLease(name, true, false)

                let start =
                    ChildProcess.StartInfo(
                        "dotnet",
                        [|
                            "exec"
                            Assembly.GetExecutingAssembly().Location
                            "--cancel-state-call"
                            rootPath
                        |]
                    )

                sandbox.Apply start

                let result =
                    ChildProcess
                        .RunAsync(start, TimeSpan.FromSeconds 3., ct)
                        .GetAwaiter()
                        .GetResult()

                Expect.equal result.ExitCode 0 result.CombinedOutput

                Expect.stringContains
                    result.StandardOutput
                    "returned asynchronously"
                    "caller was not blocked"

                Expect.stringContains
                    result.StandardOutput
                    "cancelled and settled"
                    "held lock was never released to force progress"
            }
    ]
