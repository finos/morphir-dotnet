// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.RunCopyTests

open System
open System.IO
open System.Threading
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open Morphir.Extensions.Installation.Tests.IoTestSupport

[<Tests>]
let tests =
    testList "RunCopies" [
        test "native owner permissions support repeated ownership and disposal" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            for _ in 1..2 do
                let owned =
                    RunCopies.CreateAsync(root, ct).GetAwaiter().GetResult()
                    |> get

                use lifetime = owned :> IDisposable
                let path = owned.Path

                if not (OperatingSystem.IsWindows()) then
                    let privateMode =
                        UnixFileMode.UserRead
                        ||| UnixFileMode.UserWrite

                    Expect.equal
                        (File.GetUnixFileMode(Path.Combine(root, ".morphir-dotnet-io.lock")))
                        privateMode
                        "coordination mode permits its next native read-write open"

                    Expect.equal
                        (File.GetUnixFileMode(Path.Combine(path, ".owner")))
                        privateMode
                        "owner mode permits the native reopen required for disposal"

                lifetime.Dispose()

                Expect.isFalse
                    (Directory.Exists path)
                    "disposal reopens and removes exactly its owned directory"
        }
        test "a live owner keeps its directory until disposal" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let owned =
                RunCopies.CreateAsync(root, ct).GetAwaiter().GetResult()
                |> get

            let path = owned.Path
            File.WriteAllText(Path.Combine(path, "guest.exe"), "test guest")

            RunCopies.SweepAsync(root, ct).GetAwaiter().GetResult()
            |> get
            |> ignore

            Expect.isTrue (Directory.Exists path) "active ownership is held"
            (owned :> IDisposable).Dispose()
            Expect.isFalse (Directory.Exists path) "disposed owner cleans its own directory"
        }
        test "sweeps only stale .NET copies and leaves other hosts' copies" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let temporary =
                Directory.CreateDirectory(Path.Combine(root, "tmp", "extensions")).FullName

            let stale =
                Directory
                    .CreateDirectory(Path.Combine(temporary, "morphir-dotnet-run-stale"))
                    .FullName

            let other =
                Directory
                    .CreateDirectory(Path.Combine(temporary, "morphir-extension-other"))
                    .FullName

            File.WriteAllText(Path.Combine(stale, "guest.exe"), "stale")

            let report =
                RunCopies.SweepAsync(root, ct).GetAwaiter().GetResult()
                |> get

            Expect.sequenceEqual report.Removed [ stale ] "owned prefix only"
            Expect.isFalse (Directory.Exists stale) "stale removed"
            Expect.isTrue (Directory.Exists other) "foreign prefix untouched"
        }
        test "sweep never follows a run-directory reparse point" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let temporary =
                Directory.CreateDirectory(Path.Combine(root, "tmp", "extensions")).FullName

            let outside =
                Directory.CreateDirectory(Path.Combine(sandbox.Root, "outside")).FullName

            let protectedFile = Path.Combine(outside, "keep.exe")
            File.WriteAllText(protectedFile, "keep")
            linkDirectory (Path.Combine(temporary, "morphir-dotnet-run-link")) outside

            RunCopies.SweepAsync(root, ct).GetAwaiter().GetResult()
            |> get
            |> ignore

            Expect.equal
                (File.ReadAllText protectedFile)
                "keep"
                "no recursive deletion through a link"
        }
        test "another process cannot sweep a creator before its owner lease exists" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox

            let hook =
                Action<string>(fun directory ->
                    let start =
                        ChildProcess.StartInfo(
                            "dotnet",
                            [|
                                "exec"
                                System.Reflection.Assembly.GetExecutingAssembly().Location
                                "--sweep-only"
                                root
                            |]
                        )

                    sandbox.Apply start

                    let swept =
                        ChildProcess
                            .RunAsync(start, TimeSpan.FromSeconds 15., ct)
                            .GetAwaiter()
                            .GetResult()

                    Expect.equal swept.ExitCode 0 swept.CombinedOutput

                    Expect.isTrue
                        (Directory.Exists directory)
                        "interprocess creator/sweeper serialization"

                    Expect.equal
                        (Directory.GetFiles(directory)).Length
                        0
                        "hook is before owner creation"
                )

            use owned =
                RunCopies.CreateWithHookAsync(root, hook, ct).GetAwaiter().GetResult()
                |> get

            Expect.isTrue
                (File.Exists(Path.Combine(owned.Path, ".owner")))
                "creator returns only after owning its directory"
        }
        test "Unix disposal never removes a replacement directory at the owned spelling" {
            if OperatingSystem.IsWindows() then
                skiptest "Unix permits directory rename while the owner file stays open"

            use sandbox = TestSandbox.Create()

            let owned =
                RunCopies.CreateAsync(home sandbox, ct).GetAwaiter().GetResult()
                |> get

            let moved = Path.Combine(sandbox.Root, "moved-run")
            Directory.Move(owned.Path, moved)

            Directory.CreateDirectory(owned.Path)
            |> ignore

            let foreign = Path.Combine(owned.Path, "foreign")
            File.WriteAllText(foreign, "keep")
            (owned :> IDisposable).Dispose()

            Expect.equal
                (File.ReadAllText foreign)
                "keep"
                "the old pathname is not proof of ownership"

            Expect.isTrue
                (File.Exists(Path.Combine(moved, ".owner")))
                "no cleanup through a directory moved outside home"
        }
        test "Unix disposal leaves a substituted owner file untouched" {
            if OperatingSystem.IsWindows() then
                skiptest "Windows sharing prevents owner-file replacement while open"

            use sandbox = TestSandbox.Create()

            let owned =
                RunCopies.CreateAsync(home sandbox, ct).GetAwaiter().GetResult()
                |> get

            let owner = Path.Combine(owned.Path, ".owner")
            File.Move(owner, Path.Combine(owned.Path, ".original-owner"))
            File.WriteAllText(owner, "foreign owner")
            (owned :> IDisposable).Dispose()

            Expect.equal
                (File.ReadAllText owner)
                "foreign owner"
                "disposal must prove the owner identity again"
        }
    ]
