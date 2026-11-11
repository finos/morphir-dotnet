// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.RunCopyTests

open System
open System.IO
open Expecto
open Morphir.Extensions.Installation
open Morphir.TestSupport
open Morphir.Extensions.Installation.Tests.IoTestSupport

[<Tests>]
let tests =
    testList "RunCopies" [
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
    ]
