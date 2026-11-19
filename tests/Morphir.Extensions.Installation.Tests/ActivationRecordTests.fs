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
    testLabel "PlatformSpecific" <| testList "ActivationRecords" [
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
    testLabel "PlatformSpecific" <| testList "ActivationInterprocess" [
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

[<Tests>]
let composedCapture =
    testLabel "PlatformSpecific" <| testList "ComposedCapture" [
        test "all selected records and leases share one exact export; expected drift refuses" {
            use sandbox = TestSandbox.Create()
            let owner = PackageTests.packageCleanup sandbox
            use cleanup = { new IDisposable with member _.Dispose() = owner.DisposeAsync().GetAwaiter().GetResult() }
            let plan, archive, _ = PackageTests.fixtureForHost sandbox
            let root = home sandbox
            let otherJson = nonNull (JsonNode.Parse(JsonText.toBytes plan.Descriptor.Json))
            otherJson["extensionId"] <- JsonValue.Create "org.example.second"
            otherJson["shortId"] <- JsonValue.Create "second"
            let otherArtifact = nonNull ((nonNull otherJson["artifacts"])[0])
            (nonNull ((nonNull otherArtifact["claims"])["extension"]))["id"] <- JsonValue.Create "org.example.second"
            let other = PackagePlan.Read(Encoding.UTF8.GetBytes(otherJson.ToJsonString()), ActivationPlatforms.current() |> StateOwnership.unwrap) |> get
            use first = install root plan archive (Path.Combine(root,"repo")) [||]
            use second = install root other archive (Path.Combine(root,"repo2")) [|"--second"|]
            let ids = Array.AsReadOnly [|first.Record.ExtensionId;second.Record.ExtensionId|]
            (first :> IDisposable).Dispose()
            (second :> IDisposable).Dispose()
            use captured = InstalledExtensions.CaptureManyAsync(root, ids, host, ct).GetAwaiter().GetResult() |> get
            Expect.equal captured.Captures.Count 2 "both roles captured"
            let catalog = captured.CatalogBytes
            let parsed = nonNull (JsonNode.Parse catalog)
            Expect.equal ((nonNull parsed["extensions"]).AsArray().Count) 2 "one selected projection"
            let expected = captured.Captures |> Seq.map(fun c -> c.Record.ExtensionId.Value,c.Record.LockBytes) |> readOnlyDict
            use again = InstalledExtensions.CaptureManyExpectedAsync(root,catalog,expected,host,ct).GetAwaiter().GetResult() |> get
            Expect.equal again.Captures.Count 2 "same exact export accepted"
            catalog[0] <- 0uy
            Expect.notEqual captured.CatalogBytes[0] 0uy "export is defensive"
            File.AppendAllText(Path.Combine(root,"locks","extensions",second.Record.ExtensionId.Value+".json")," ")
            InstalledExtensions.CaptureManyExpectedAsync(root,captured.CatalogBytes,expected,host,ct).GetAwaiter().GetResult()
            |> refused "extension.current_record_changed"
        }
        test "a missing selected id never returns a partial capture" {
            use sandbox = TestSandbox.Create()
            let owner = PackageTests.packageCleanup sandbox
            use cleanup = { new IDisposable with member _.Dispose() = owner.DisposeAsync().GetAwaiter().GetResult() }
            let plan, archive, _ = PackageTests.fixtureForHost sandbox
            let root = home sandbox
            let captured = install root plan archive (Path.Combine(root,"repo")) [||]
            let id = captured.Record.ExtensionId
            (captured :> IDisposable).Dispose()
            InstalledExtensions.CaptureManyAsync(root,Array.AsReadOnly [|id;Morphir.Extensions.ExtensionId.Create "org.example.missing"|],host,ct).GetAwaiter().GetResult()
            |> refused "extension.not_installed"
            use store = SafeDirectory.Open(Path.Combine(root,"store","extensions","packages","sha256"),false)
            use exclusive = store.NamedLease(plan.TreeDigest.Value+".lease",true,false)
            Expect.isTrue exclusive.CanRead "failure released any earlier selected package"
        }
    ]






let private sessionFixture () =
    let tuple = """{"operation":"morphir.frontend.compileModel","profile":"morphir-ir","codec":"morphir-json","envelopeVersion":"0.1.0-draft.1","irVersion":"4.0.0"}"""
    let text = """{"protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.example.frontend","name":"Frontend","version":"1.0.0","types":["frontend"]},"capabilities":{"streaming":false,"incremental":false,"cancellation":false,"progress":false,"frontend":{"languages":[{"id":"fsharp","fileExtensions":[".fs"]}],"irVersions":["4.0.0"],"compile":false,"incremental":false,"fragments":false,"multiDocument":true},"modelTransport":{"tuples":[TUPLE],"limits":{"maxEncodedBytes":1000,"maxDecodedBytes":900,"maxDepth":100,"maxNodes":200},"guarantees":[]}}}""".Replace("TUPLE",tuple)
    let value = Syntax.parseJson(Encoding.UTF8.GetBytes text) |> function Ok value -> value | Error _ -> failtest "invalid unit JSON"
    let claims = Claims.readEmbedded "claims" value
    let fields = SharedJson.members "claims" value
    let caps = SharedJson.required "claims" "capabilities" fields
    let capabilities = System.Text.Json.JsonSerializer.Deserialize(JsonText.toBytes caps, Morphir.Mep.MepJson.Default.ExtensionCapabilities) |> nonNull
    let session: Morphir.Mep.NegotiatedSession = { ProtocolVersion=Morphir.Mep.ProtocolVersion.Create "0.2.0-draft.1"; Host={Kind=Morphir.Mep.PeerKind.Cli;Name="test";Version="1.0.0"};Extension=claims.Extension;Capabilities=capabilities }
    claims,session

[<Tests>]
let sessionAgreement =
    testLabel "PlatformSpecific" <| testList "InstalledSessionAgreement" [
        test "actual matching session is accepted" {
            let claims,session = sessionFixture()
            Expect.isOk (Claims.agreeSession claims session) "same known members"
        }
        test "identity mismatch names the first agreement rule" {
            let claims,session = sessionFixture()
            match Claims.agreeSession claims {session with Extension={session.Extension with Version="2.0.0"}} with
            | Error failure -> Expect.stringContains failure.Message "rule 1" "identity rule"
            | Ok _ -> failtest "changed version accepted"
        }
        test "a negotiated protocol must occur in installed claims" {
            let claims,session = sessionFixture()
            Expect.isError (Claims.agreeSession claims {session with ProtocolVersion=Morphir.Mep.ProtocolVersion.Create "0.2.0"}) "protocol exact"
        }
        test "model transport positive limits may narrow" {
            let claims,session = sessionFixture()
            let transport = session.Capabilities.ModelTransport |> Option.get
            let narrowed = {transport with Limits={transport.Limits with MaxEncodedBytes=500;MaxDepth=50}}
            Expect.isOk (Claims.agreeSession claims {session with Capabilities={session.Capabilities with ModelTransport=Some narrowed}}) "negotiated subset is not whole-document equality"
        }
        test "model transport cannot expand static limits" {
            let claims,session = sessionFixture()
            let transport = session.Capabilities.ModelTransport |> Option.get
            let wider = {transport with Limits={transport.Limits with MaxDepth=101}}
            Expect.isError (Claims.agreeSession claims {session with Capabilities={session.Capabilities with ModelTransport=Some wider}}) "limits are bound"
        }
        test "an unclaimed tuple is rejected" {
            let claims,session = sessionFixture()
            let transport = session.Capabilities.ModelTransport |> Option.get
            let tuple = {transport.Tuples[0] with IrVersion=Morphir.Mep.SemanticVersion.Create "3.0.0"}
            Expect.isError (Claims.agreeSession claims {session with Capabilities={session.Capabilities with ModelTransport=Some {transport with Tuples=[|tuple|]}}}) "no tuple substitution"
        }
    ]




[<Tests>]
let repositoryRegistration =
    testLabel "PlatformSpecific" <| testList "LocalRepositoryRegistration" [
        test "dry run validates a registered name and directory without creating Home or locks" {
            use sandbox = TestSandbox.Create()
            let source = sandbox.PathOf "source"
            Directory.CreateDirectory source |> ignore
            let root = sandbox.MorphirHome
            InstalledState.RegisterLocalRepositoryAsync(root,"local-dev",source,true,ct).GetAwaiter().GetResult() |> get |> ignore
            Expect.isFalse (Directory.Exists root) "dry-run creates no Home"
        }
        test "registered repository names resolve; a directory string never resolves as a name" {
            use sandbox = TestSandbox.Create()
            let source = sandbox.PathOf "source"
            Directory.CreateDirectory source |> ignore
            let root = sandbox.MorphirHome
            InstalledState.RegisterLocalRepositoryAsync(root,"local-dev",source,false,ct).GetAwaiter().GetResult() |> get |> ignore
            let observed = InstalledState.ResolveLocalRepositoryAsync(root,"local-dev",ct).GetAwaiter().GetResult() |> get
            Expect.equal observed (Path.GetFullPath source) "named endpoint"
            InstalledState.ResolveLocalRepositoryAsync(root,source,ct).GetAwaiter().GetResult() |> refused "extension.invalid_repository"
        }
        test "an existing stable repository name cannot silently select a different directory" {
            use sandbox = TestSandbox.Create()
            let source = sandbox.PathOf "source"
            let other = sandbox.PathOf "other"
            Directory.CreateDirectory source |> ignore
            Directory.CreateDirectory other |> ignore
            let root = sandbox.MorphirHome
            InstalledState.RegisterLocalRepositoryAsync(root,"local-dev",source,false,ct).GetAwaiter().GetResult() |> get |> ignore
            InstalledState.RegisterLocalRepositoryAsync(root,"local-dev",other,false,ct).GetAwaiter().GetResult() |> refused "extension.repository_conflict"
            let observed = InstalledState.ResolveLocalRepositoryAsync(root,"local-dev",ct).GetAwaiter().GetResult() |> get
            Expect.equal observed source "stable binding preserved"
        }
    ]
