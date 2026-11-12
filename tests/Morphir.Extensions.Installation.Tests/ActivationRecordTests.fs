// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.ActivationRecordTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Reflection
open Expecto
open Morphir.Extensions.Installation
open Morphir.Toolchain
open Morphir.TestSupport
open IoTestSupport

let private host =
    SemVer.tryParse "0.4.0-alpha.4"
    |> function
        | Ok value -> value
        | Error _ -> failtest "literal"

let private install root plan archive repository args =
    InstalledExtensions
        .InstallLocalUncheckedAsync(
            root,
            plan,
            archive,
            repository,
            Array.AsReadOnly args,
            host,
            ct
        )
        .GetAwaiter()
        .GetResult()
    |> get

[<Tests>]
let tests =
    testList "ActivationRecords" [
        test "foreign platform cannot create an activation lifetime or registration" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let foreign =
                    InstallationPlatform.Create(
                        if OperatingSystem.IsLinux() then "win-x64" else "linux-x64"
                    )

                let plan, archive, _ = PackageTests.fixtureForPlatform foreign sandbox
                let root = home sandbox

                InstalledExtensions
                    .InstallLocalUncheckedAsync(
                        root,
                        plan,
                        archive,
                        Path.Combine(root, "repo"),
                        Array.AsReadOnly [||],
                        host,
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()
                |> refused "extension.no_matching_artifact"

                Expect.isFalse
                    (Directory.Exists(Path.Combine(root, "store")))
                    "refused before package ownership"

                PackageInstaller.InstallAsync(root, plan, archive, ct).GetAwaiter().GetResult()
                |> get
                |> ignore

                PackageInstaller.AcquireAsync(root, plan, ct).GetAwaiter().GetResult()
                |> refused "extension.no_matching_artifact"

                use directory =
                    SafeDirectory.Open(
                        Path.Combine(root, "store", "extensions", "packages", "sha256"),
                        false
                    )

                use exclusive =
                    directory.NamedLease(
                        plan.TreeDigest.Value
                        + ".lease",
                        true,
                        false
                    )

                Expect.isTrue exclusive.CanRead "foreign lifetime was not leaked"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "registration holds a shared package lease through the durable journal phases" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, _ = PackageTests.fixtureForHost sandbox
                let root = home sandbox
                let mutable observed = 0

                let observer =
                    Action<int>(fun phase ->
                        if phase = 0 then
                            observed <-
                                observed
                                + 1

                            use directory =
                                SafeDirectory.Open(
                                    Path.Combine(root, "store", "extensions", "packages", "sha256"),
                                    false
                                )

                            Expect.throwsT<FileLeaseBusyException>
                                (fun () ->
                                    use exclusive =
                                        directory.NamedLease(
                                            plan.TreeDigest.Value
                                            + ".lease",
                                            true,
                                            false
                                        ) in

                                    ()
                                )
                                "collector excluded before first metadata replacement"
                    )

                let captured =
                    InstalledExtensions
                        .InstallLocalWithObserverAsync(
                            root,
                            plan,
                            archive,
                            Path.Combine(root, "repo"),
                            Array.AsReadOnly [||],
                            host,
                            observer,
                            ct
                        )
                        .GetAwaiter()
                        .GetResult()
                    |> get

                (captured :> IDisposable).Dispose()
                Expect.equal observed 1 "actual journal boundary"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "local install creates published provenance and exact validated activation snapshots" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, payload = PackageTests.fixtureForHost sandbox
                let root = home sandbox
                let repository = Path.Combine(root, "developer-repository")
                let captured = install root plan archive repository [| "--rpc"; "two words" |]

                try
                    Expect.sequenceEqual
                        captured.Record.Arguments
                        [ "--rpc"; "two words" ]
                        "exact vector"

                    Expect.sequenceEqual
                        (File.ReadAllBytes captured.Activation.EntryPointPath)
                        payload
                        "real tree bytes"

                    Expect.equal captured.Record.ArchiveDigest plan.ArchiveDigest "archive binding"
                    Expect.equal captured.Record.TreeDigest plan.TreeDigest "tree binding"

                    Expect.isTrue
                        (File.Exists(
                            Path.Combine(
                                repository,
                                "extensions",
                                captured.Record.ExtensionId.Value
                                + ".jsonl"
                            )
                        ))
                        "published history layout"

                    let catalog =
                        StateDocuments.readCatalog (captured.Record.CatalogSnapshotBytes)
                        |> StateTransactionTests.getResult

                    let locked =
                        StateDocuments.readLock (captured.Record.LockBytes)
                        |> StateTransactionTests.getResult

                    StateDocuments.bind catalog.Entries.Head locked
                    |> StateTransactionTests.getResult
                    |> ignore

                    Expect.equal
                        (SharedJson.required
                            "catalog"
                            "claimCheck"
                            (SharedJson.members "catalog" catalog.Entries.Head.Json))
                        (Value.String "unchecked")
                        "no fabricated probe"
                finally
                    (captured :> IDisposable).Dispose()
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test
            "unpublished A refuses new acquisition after B while the already owned A lease remains valid" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, _ = PackageTests.fixtureForHost sandbox
                let root = home sandbox
                let a = install root plan archive (Path.Combine(root, "repo")) [| "--A" |]
                let record = a.Record
                let frozen = record.LockBytes

                try
                    let b = install root plan archive (Path.Combine(root, "repo-b")) [| "--B" |]
                    (b :> IDisposable).Dispose()
                    let bytes = record.LockBytes
                    bytes[0] <- 0uy
                    Expect.sequenceEqual record.LockBytes frozen "defensive copy"

                    let acquired =
                        InstalledExtensions
                            .AcquireCurrentAsync(root, record, host, ct)
                            .GetAwaiter()
                            .GetResult()

                    acquired.Match(
                        Func<PackageActivation, unit>(fun lifetime ->
                            (lifetime :> IDisposable).Dispose()

                            failtest
                                "Unpublished snapshot must not authorize retained acquisition."
                        ),
                        Func<InstallationFailure, unit>(fun error ->
                            Expect.equal
                                error.Code
                                "extension.current_record_changed"
                                error.Message
                        )
                    )

                    Expect.sequenceEqual record.Arguments [ "--A" ] "immutable old record"

                    Expect.isTrue
                        (File.Exists a.Activation.EntryPointPath)
                        "already held lifetime remains owned"
                finally
                    (a :> IDisposable).Dispose()
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "capture preserves unknown optional selected catalog values" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, _ = PackageTests.fixtureForHost sandbox
                let root = home sandbox
                let installed = install root plan archive (Path.Combine(root, "repo")) [||]
                let id = installed.Record.ExtensionId
                (installed :> IDisposable).Dispose()
                let path = Path.Combine(root, "catalog", "extensions.json")
                let json = nonNull (JsonNode.Parse(File.ReadAllText path))
                let entry = nonNull ((nonNull json["extensions"])[0])
                entry["future"] <- JsonNode.Parse "{\"opaque\":\"retained\"}"
                File.WriteAllText(path, json.ToJsonString())

                let captured =
                    InstalledExtensions.CaptureAsync(root, id, host, ct).GetAwaiter().GetResult()
                    |> get

                try
                    Expect.isTrue
                        (Encoding.UTF8.GetString(captured.Record.CatalogSnapshotBytes).Contains
                            "retained")
                        "unknown preserved"
                finally
                    (captured :> IDisposable).Dispose()
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "identical explicit local install reuses its existing one-release repository" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, _ = PackageTests.fixtureForHost sandbox
                let root = home sandbox

                for _ in 1..2 do
                    let captured = install root plan archive (Path.Combine(root, "repo")) [||]
                    (captured :> IDisposable).Dispose()
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test
            "expected-current capture refuses exact lock byte drift and never turns supplied JSON into authority" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archive, _ = PackageTests.fixtureForHost sandbox
                let root = home sandbox
                let first = install root plan archive (Path.Combine(root, "repo")) [||]
                let record = first.Record
                (first :> IDisposable).Dispose()

                let current =
                    InstalledExtensions
                        .CaptureExpectedAsync(
                            root,
                            record.CatalogSnapshotBytes,
                            record.LockBytes,
                            host,
                            ct
                        )
                        .GetAwaiter()
                        .GetResult()
                    |> get

                (current :> IDisposable).Dispose()

                File.AppendAllText(
                    Path.Combine(
                        root,
                        "locks",
                        "extensions",
                        record.ExtensionId.Value
                        + ".json"
                    ),
                    " "
                )

                InstalledExtensions
                    .CaptureExpectedAsync(
                        root,
                        record.CatalogSnapshotBytes,
                        record.LockBytes,
                        host,
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()
                |> refused "extension.current_record_changed"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
    ]

[<Tests>]
let interprocess =
    testList "ActivationInterprocess" [
        testTask
            "another process shares a live package lease and exclusive ownership waits for disposal" {
            use sandbox = TestSandbox.Create()
            use cleanup = PackageTests.packageCleanup sandbox
            let plan, archive, _ = PackageTests.fixtureForHost sandbox
            let root = home sandbox
            let captured = install root plan archive (Path.Combine(root, "repo")) [||]

            let child mode =
                let start =
                    ChildProcess.StartInfo(
                        "dotnet",
                        [|
                            "exec"
                            Assembly.GetExecutingAssembly().Location
                            "--package-lease-only"
                            root
                            plan.TreeDigest.Value
                            mode
                        |]
                    )

                sandbox.Apply start
                ChildProcess.RunAsync(start, TimeSpan.FromSeconds 10., ct)

            try
                let! (shared: ChildProcessResult) = child "shared"
                Expect.equal shared.ExitCode 0 shared.CombinedOutput
                let! (exclusive: ChildProcessResult) = child "exclusive"
                Expect.equal exclusive.ExitCode 2 exclusive.CombinedOutput
                (captured :> IDisposable).Dispose()
                let! (released: ChildProcessResult) = child "exclusive"
                Expect.equal released.ExitCode 0 released.CombinedOutput
            finally
                (captured :> IDisposable).Dispose()
        }
    ]
