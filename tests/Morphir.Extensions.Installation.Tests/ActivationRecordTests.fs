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
        test "model access positive limits including int64 bounds may narrow and cannot expand" {
            let tuple = """{"protocolVersion":"0.2.0-draft.2","accessVersion":"0.1.0-draft.1","operation":"morphir.backend.generateModel","irVersion":"4.0.0","profile":"morphir-ir","codec":"morphir-json","layout":"document-tree","contextualEncoding":"plain"}"""
            let limits = """{"maxFrameBytes":32768,"maxEncodedBytesPerExchange":8192,"maxDecodedBytesPerExchange":6144,"maxDocumentsPerBatch":8,"maxDepth":64,"maxNodes":256,"maxDiagnosticsPerExchange":16,"maxOutstandingRequests":8,"maxGuestStateBytes":1048576,"maxHostCacheBytes":1048576,"maxArtifactChunkBytes":4096,"maxArtifactBytes":4294967296,"maxTotalArtifactBytes":4294967296,"maxSessionDiskBytes":8589934592,"maxSessionWorkUnits":4096,"maxRounds":16,"maxCallMilliseconds":30000,"maxSessionMilliseconds":60000}"""
            let text = """{"protocolVersions":["0.2.0-draft.2","0.2.0-draft.1"],"extension":{"id":"org.example.backend","name":"Backend","version":"1.0.0","types":["backend"]},"capabilities":{"streaming":false,"incremental":false,"cancellation":false,"progress":false,"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":{"tuples":[TUPLE],"limits":LIMITS}}}""".Replace("TUPLE",tuple).Replace("LIMITS",limits)
            let value = Syntax.parseJson(Encoding.UTF8.GetBytes text) |> function Ok value -> value | Error _ -> failtest "invalid unit JSON"
            let claims = Claims.readEmbedded "claims" value
            let fields = SharedJson.members "claims" value
            let caps = SharedJson.required "claims" "capabilities" fields
            let capabilities = System.Text.Json.JsonSerializer.Deserialize(JsonText.toBytes caps, Morphir.Mep.MepJson.Default.ExtensionCapabilities) |> nonNull
            let session: Morphir.Mep.NegotiatedSession = { ProtocolVersion=Morphir.Mep.ProtocolVersion.Create "0.2.0-draft.2"; Host={Kind=Morphir.Mep.PeerKind.Cli;Name="test";Version="1.0.0"};Extension=claims.Extension;Capabilities=capabilities }
            let access = session.Capabilities.ModelAccess |> Option.get
            Expect.isOk (Claims.agreeSession claims session) "exact matching modelAccess session"
            let narrowed = { access with Limits = { access.Limits with MaxFrameBytes = 16384; MaxArtifactBytes = 1048576L } }
            Expect.isOk (Claims.agreeSession claims { session with Capabilities = { session.Capabilities with ModelAccess = Some narrowed } }) "narrowed modelAccess limits"
            let expanded = { access with Limits = { access.Limits with MaxArtifactBytes = 4294967297L } }
            Expect.isError (Claims.agreeSession claims { session with Capabilities = { session.Capabilities with ModelAccess = Some expanded } }) "expanded int64 modelAccess limit refused"
            let wrongEncoding = { access.Tuples[0] with ContextualEncoding = Morphir.Mep.ModelAccessEncoding.ContextualDraft1 }
            Expect.isError (Claims.agreeSession claims { session with Capabilities = { session.Capabilities with ModelAccess = Some { access with Tuples = [| wrongEncoding |] } } }) "unclaimed modelAccess tuple refused"
        }
        test "malformed modelAccess static claims are refused before activation" {
            let validTuple = """{"protocolVersion":"0.2.0-draft.2","accessVersion":"0.1.0-draft.1","operation":"morphir.backend.generateModel","irVersion":"4.0.0","profile":"morphir-ir","codec":"morphir-json","layout":"document-tree","contextualEncoding":"plain"}"""
            let validLimits = """{"maxFrameBytes":65536,"maxEncodedBytesPerExchange":65536,"maxDecodedBytesPerExchange":32768,"maxDocumentsPerBatch":8,"maxDepth":64,"maxNodes":10000,"maxDiagnosticsPerExchange":16,"maxOutstandingRequests":4,"maxGuestStateBytes":16777216,"maxHostCacheBytes":16777216,"maxArtifactChunkBytes":65536,"maxArtifactBytes":4294967296,"maxTotalArtifactBytes":4294967296,"maxSessionDiskBytes":4294967296,"maxSessionWorkUnits":100000,"maxRounds":100,"maxCallMilliseconds":10000,"maxSessionMilliseconds":60000}"""
            let makeClaims tuples limits =
                """{"protocolVersions":["0.2.0-draft.2"],"extension":{"id":"org.example.orders","name":"Orders","version":"1.0.0","types":["backend"]},"capabilities":{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":{"tuples":"""
                + tuples
                + ""","limits":"""
                + limits
                + "}}}"
            let wrap item = $"[{item}]"
            let cases = [
                "empty tuples", makeClaims "[]" validLimits
                "duplicate tuples", makeClaims $"[{validTuple},{validTuple}]" validLimits
                "unsupported irVersion", makeClaims (wrap (validTuple.Replace("4.0.0", "2.0.0"))) validLimits
                "unsupported profile", makeClaims (wrap (validTuple.Replace("morphir-ir", "other"))) validLimits
                "unsupported codec", makeClaims (wrap (validTuple.Replace("morphir-json", "ion-binary"))) validLimits
                "unsupported layout", makeClaims (wrap (validTuple.Replace("document-tree", "single-document"))) validLimits
                "unsupported encoding", makeClaims (wrap (validTuple.Replace("plain", "bogus"))) validLimits
                "32-bit bound above Int32.MaxValue", makeClaims (wrap validTuple) (validLimits.Replace("65536", "3000000000"))
            ]
            for label, json in cases do
                let value = Syntax.parseJson (Text.Encoding.UTF8.GetBytes json) |> function Ok v -> v | Error e -> failtest $"Parse failed for {label}: {e}"
                let refused =
                    try
                        Claims.readEmbedded "claims" value |> ignore
                        false
                    with _ -> true
                Expect.isTrue refused $"Malformed modelAccess claims must be refused: {label}"
        }
        test "installing preview.3 stays on preview.3 and installing preview.4 with modelAccess promotes catalog, lock, journal, and index-record" {
            use sandbox = TestSandbox.Create()
            let root = home sandbox
            let repoRoot = sandbox.PathOf("local-repo")
            Directory.CreateDirectory(repoRoot) |> ignore

            let wasmBytes = [| 0x00uy; 0x61uy; 0x73uy; 0x6Duy; 0x01uy; 0x00uy; 0x00uy; 0x00uy |]
            let wasmSha = Digest.ofBytes wasmBytes
            let wasmPath = sandbox.PathOf("ext.wasm")
            File.WriteAllBytes(wasmPath, wasmBytes)

            let p3Root = Path.Combine(AppContext.BaseDirectory, "ParentContracts")
            let p4Root = Path.Combine(AppContext.BaseDirectory, "ParentContractsPreview4")

            let p3Release =
                Encoding.UTF8.GetBytes(
                    """{"$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json","extensionId":"org.acme.legacy","shortId":"acme-legacy","version":"1.0.0","platformDifferences":"none","artifacts":[{"runtime":"wasm","filename":"legacy.wasm","sha256":"SHA","claims":{"protocolVersions":["0.2.0-draft.1"],"extension":{"id":"org.acme.legacy","name":"Legacy","version":"1.0.0","types":["backend"]},"capabilities":{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":true}}}}]}"""
                        .Replace("SHA", wasmSha)
                )

            let p3Plan =
                PackagePlan.Read(p3Release, InstallationPlatform.Create "linux-x64")
                |> get

            let mutable p3JournalBytes: byte array = [||]

            use p3Captured =
                InstalledExtensions
                    .InstallLocalWithObserverAsync(
                        root,
                        p3Plan,
                        wasmPath,
                        repoRoot,
                        Array.AsReadOnly [||],
                        host,
                        Action<int>(fun phase ->
                            if phase = 0 then
                                p3JournalBytes <- File.ReadAllBytes(Path.Combine(root, "locks", "extensions", "org.acme.legacy.json.transaction"))
                        ),
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()
                |> get

            let p3LockBytes = File.ReadAllBytes(Path.Combine(root, "locks", "extensions", "org.acme.legacy.json"))
            let p3CatalogBytes = File.ReadAllBytes(Path.Combine(root, "catalog", "extensions.json"))
            let p3IndexBytes = File.ReadAllBytes(Path.Combine(repoRoot, "extensions", "org.acme.legacy.jsonl"))

            for kind, bytes in [
                "lock", p3LockBytes
                "catalog", p3CatalogBytes
                "journal", p3JournalBytes
                "index-record", p3IndexBytes
            ] do
                let text = Encoding.UTF8.GetString bytes
                Expect.stringContains text $"0.1.0-preview.3/{kind}.schema.json" $"preview.3 {kind} uses preview.3 schema"
                let ok, err = SharedReaderTests.validateAgainstPinnedSchema p3Root kind bytes
                Expect.isTrue ok $"preview.3 {kind} validates against pinned preview.3 schema: {err}"

            // Now install a preview.4 extension carrying capabilities.modelAccess into the same home.
            let modelAccessJson =
                """{"tuples":[{"protocolVersion":"0.2.0-draft.2","accessVersion":"0.1.0-draft.1","operation":"morphir.backend.generateModel","irVersion":"4.0.0","profile":"morphir-ir","codec":"morphir-json","layout":"document-tree","contextualEncoding":"plain"}],"limits":{"maxFrameBytes":65536,"maxEncodedBytesPerExchange":65536,"maxDecodedBytesPerExchange":32768,"maxDocumentsPerBatch":8,"maxDepth":64,"maxNodes":10000,"maxDiagnosticsPerExchange":16,"maxOutstandingRequests":4,"maxGuestStateBytes":16777216,"maxHostCacheBytes":16777216,"maxArtifactChunkBytes":65536,"maxArtifactBytes":4294967296,"maxTotalArtifactBytes":4294967296,"maxSessionDiskBytes":4294967296,"maxSessionWorkUnits":100000,"maxRounds":100,"maxCallMilliseconds":10000,"maxSessionMilliseconds":60000}}"""

            let p4Release =
                Encoding.UTF8.GetBytes(
                    """{"$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.4/release.schema.json","extensionId":"org.acme.bounded","shortId":"acme-bounded","version":"1.0.0","platformDifferences":"none","artifacts":[{"runtime":"wasm","filename":"bounded.wasm","sha256":"SHA","claims":{"protocolVersions":["0.2.0-draft.2"],"extension":{"id":"org.acme.bounded","name":"Bounded","version":"1.0.0","types":["backend"]},"capabilities":{"backend":{"targets":["text"],"irVersions":["4.0.0"],"generate":false},"modelAccess":MA}}}]}"""
                        .Replace("SHA", wasmSha)
                        .Replace("MA", modelAccessJson)
                )

            let p4Plan =
                PackagePlan.Read(p4Release, InstallationPlatform.Create "linux-x64")
                |> get

            let mutable p4JournalBytes: byte array = [||]

            use p4Captured =
                InstalledExtensions
                    .InstallLocalWithObserverAsync(
                        root,
                        p4Plan,
                        wasmPath,
                        repoRoot,
                        Array.AsReadOnly [||],
                        host,
                        Action<int>(fun phase ->
                            if phase = 0 then
                                p4JournalBytes <- File.ReadAllBytes(Path.Combine(root, "locks", "extensions", "org.acme.bounded.json.transaction"))
                        ),
                        ct
                    )
                    .GetAwaiter()
                    .GetResult()
                |> get

            let p4LockBytes = File.ReadAllBytes(Path.Combine(root, "locks", "extensions", "org.acme.bounded.json"))
            let p4CatalogBytes = File.ReadAllBytes(Path.Combine(root, "catalog", "extensions.json"))
            let p4IndexBytes = File.ReadAllBytes(Path.Combine(repoRoot, "extensions", "org.acme.bounded.jsonl"))

            for kind, bytes in [
                "lock", p4LockBytes
                "catalog", p4CatalogBytes
                "journal", p4JournalBytes
                "index-record", p4IndexBytes
            ] do
                let text = Encoding.UTF8.GetString bytes
                Expect.stringContains text $"0.1.0-preview.4/{kind}.schema.json" $"preview.4 {kind} uses preview.4 schema"
                let ok, err = SharedReaderTests.validateAgainstPinnedSchema p4Root kind bytes
                Expect.isTrue ok $"preview.4 {kind} validates against pinned preview.4 schema: {err}"

            // Verify compatibility check passes on the mixed preview.3 lock + preview.4 lock + promoted preview.4 catalog home
            let compatible =
                InstalledState.CheckCompatibilityAsync(root, ct).GetAwaiter().GetResult()
                |> get

            Expect.isTrue compatible "mixed home with preview.3 and preview.4 locks passes compatibility check"
            ignore p3Captured
            ignore p4Captured
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
