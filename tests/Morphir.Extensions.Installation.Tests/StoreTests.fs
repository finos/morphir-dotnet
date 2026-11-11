// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.StoreTests

open System
open System.IO
open System.Text
open System.Threading
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

[<Tests>]
let tests =
    testList "Store" [
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
    ]
