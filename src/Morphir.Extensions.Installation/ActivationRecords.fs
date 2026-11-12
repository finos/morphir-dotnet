// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Text
open System.Text.Json
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Mep

/// Installer-validated immutable selected state. The constructor is unavailable to public consumers.
[<Sealed>]
type InstalledActivationRecord
    internal
    (
        entry: StateEntry,
        locked: StateLock,
        catalogBytes: byte array,
        manifest: byte array,
        homeIdentity: StageIdentity,
        plan: PackagePlan
    ) =
    let lockBytes = Array.copy locked.Bytes
    let catalogBytes = Array.copy catalogBytes
    let manifest = Array.copy manifest
    /// Whole extension id, parsed by the existing domain.
    member _.ExtensionId = Morphir.Extensions.ExtensionId.Create entry.Id
    /// Exact selected extension version.
    member _.Version = entry.Version
    /// Exact locked argument vector; no shell tokenization or additional arguments.
    member _.Arguments: IReadOnlyList<string> = Array.AsReadOnly(Array.ofList entry.Args)
    /// Delivered archive identity, distinct from tree identity.
    member _.ArchiveDigest = entry.Digest
    /// Canonical manifest and tree identity.
    member _.TreeDigest = plan.TreeDigest
    /// Existing-format single-selection catalog snapshot, preserving optional values.
    member _.CatalogSnapshotBytes = Array.copy catalogBytes
    /// Exact existing-format selected lock bytes, without reserialization.
    member _.LockBytes = Array.copy lockBytes
    /// Canonical file names, sizes, digests and executable modes validated at capture.
    member _.ManifestBytes = Array.copy manifest
    /// Embedded claims as supplied; this grants no authentication or readiness.
    member _.ClaimsBytes = JsonText.toBytes entry.Claims.Json
    /// Current published provenance is explicitly local developer provenance.
    member _.IsLocalDeveloper = true
    /// Validates the actual negotiated session before an installed operation. This does not prove invocation.
    member _.CheckSession(session: NegotiatedSession) =
        InstallationResult(Claims.agreeSession entry.Claims session)
    member internal _.HomeIdentity = homeIdentity
    member internal _.Plan = plan
    member internal _.Entry = entry

    override _.ToString() =
        $"{entry.Id}@{entry.Version.Text}, tree {plan.TreeDigest.Value}."

/// A snapshot and its already-acquired package lease, captured atomically under the state lock.
[<Sealed>]
type CapturedExtension internal (record: InstalledActivationRecord, activation: PackageActivation) =
    /// Immutable metadata that remains valid independently of global id replacement.
    member _.Record = record
    /// Retain until every consumer child and output pump has settled.
    member _.Activation = activation
    override _.ToString() = record.ToString()

    interface IDisposable with
        member _.Dispose() = (activation :> IDisposable).Dispose()

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            (activation :> IAsyncDisposable).DisposeAsync()

[<Sealed>]
type internal CapturedExtensionSet internal (captures: CapturedExtension array, catalog: byte array) =
    member _.Captures: IReadOnlyList<CapturedExtension> = Array.AsReadOnly captures
    member _.CatalogBytes = Array.copy catalog
    interface IDisposable with
        member _.Dispose() = for capture in Array.rev captures do (capture :> IDisposable).Dispose()

module private ActivationState =
    let value name (value: Value) =
        SharedJson.required "value" name (SharedJson.members "value" value)

    let stringValue text = Value.String text

    let withFields fields value =
        let original = SharedJson.members "document" value

        Value.Object(
            original
            |> List.map (fun (name, value) ->
                name,
                (List.tryFind (fun (key, _) -> key = name) fields
                 |> Option.map snd
                 |> Option.defaultValue value)
            )
        )

    let ensureHost (host: SemVer) (claims: ReleaseClaims) =
        if not (Claims.supportsHost host claims) then
            Operations.fail
                "extension.host_incompatible"
                "Installed claims require a different host version."

        MepSupport
            .Evaluate(Array.AsReadOnly(Array.ofList claims.ProtocolVersions))
            .Match(
                Func<ProtocolVersion, unit>(ignore),
                Func<string, unit>(fun reason ->
                    Operations.fail "extension.mep_not_implemented" reason
                ),
                Func<string, unit>(fun reason ->
                    Operations.fail "extension.mep_not_supported" reason
                )
            )

    let acquire (home: string) expectedHome (plan: PackagePlan) fullHash (ct: CancellationToken) =
        ActivationPlatforms.ensure plan.Artifact.Platform
        |> StateOwnership.unwrap

        let resources = ResizeArray<IDisposable>()

        try
            let root = SafeDirectory.Open(home, false)
            resources.Add root

            if
                root.Identity
                <> expectedHome
            then
                Operations.fail
                    "extension.path_escape"
                    "Morphir Home was replaced since metadata capture."

            let store =
                PackageTreeIO.directories
                    resources
                    root
                    [ "store"; "extensions"; "packages"; "sha256" ]
                    false

            resources.Add(
                store.NamedLease(
                    plan.TreeDigest.Value
                    + ".lease",
                    false,
                    false
                )
            )

            let directory = store.Child(plan.TreeDigest.Value, false)
            resources.Add directory
            let installed = PackageTreeIO.verify directory plan fullHash ct
            new PackageActivation(installed, resources.ToArray())
        with _ ->
            PackageTreeIO.close resources
            reraise ()

    let fromActivation
        (root: SafeDirectory)
        (catalog: StateCatalog)
        (entry: StateEntry)
        locked
        plan
        (activation: PackageActivation)
        =
        try
            let selected =
                withFields [ "extensions", Value.Array[entry.Json] ] catalog.Json
                |> JsonText.toBytes

            let record =
                InstalledActivationRecord(
                    entry,
                    locked,
                    selected,
                    activation.Package.Manifest.ToBytes(),
                    root.Identity,
                    plan
                )

            new CapturedExtension(record, activation)
        with _ ->
            (activation :> IDisposable).Dispose()
            reraise ()

    let capture (root: SafeDirectory) (catalog: StateCatalog) (entry: StateEntry) locked host ct =
        StateDocuments.bind entry locked
        |> StateOwnership.unwrap

        ensureHost host entry.Claims

        if not (Comparator.allMet entry.HostRequirements host) then
            Operations.fail
                "extension.host_incompatible"
                "Selected catalog requires a different host."

        let plan = PackagePlan.FromState(entry, locked)
        let activation = acquire root.Path root.Identity plan false ct
        fromActivation root catalog entry locked plan activation

    let captureExpected (root: SafeDirectory) expectedCatalogBytes expectedLockBytes host ct =
        let expectedCatalog =
            StateDocuments.readCatalog expectedCatalogBytes
            |> StateOwnership.unwrap

        let expectedLock =
            StateDocuments.readLock expectedLockBytes
            |> StateOwnership.unwrap

        let expectedEntry =
            match expectedCatalog.Entries with
            | [ entry ] -> entry
            | _ ->
                Operations.fail
                    "extension.invalid_document"
                    "Expected exactly one selected catalog export."

        StateDocuments.bind expectedEntry expectedLock
        |> StateOwnership.unwrap

        let current, locks = StateOwnership.readCurrent root

        let changed () =
            Operations.fail
                "extension.current_record_changed"
                "Current installed export differs from the expected unpublished record."

        let current =
            current
            |> Option.defaultWith changed

        let entry =
            current.Entries
            |> List.tryFind (fun entry -> entry.Id = expectedEntry.Id)
            |> Option.defaultWith changed

        let locked =
            locks
            |> List.tryFind (fun locked -> locked.Entry.Id = expectedEntry.Id)
            |> Option.defaultWith changed

        let exported =
            withFields [ "extensions", Value.Array[entry.Json] ] current.Json
            |> JsonText.toBytes

        if
            not (exported.AsSpan().SequenceEqual(expectedCatalogBytes.AsSpan()))
            || not (locked.Bytes.AsSpan().SequenceEqual(expectedLockBytes.AsSpan()))
        then
            changed ()

        capture root current entry locked host ct

    let captureMany (root: SafeDirectory) (ids: string list) expected host ct =
        if ids.IsEmpty || ids.Length > 8192 || (Set.ofList ids).Count <> ids.Length then
            Operations.fail "extension.invalid_selection" "Selection must contain unique installed ids."
        let current, locks = StateOwnership.readCurrent root
        let catalog = current |> Option.defaultWith(fun () -> Operations.fail "extension.not_installed" "No installed catalog.")
        let selected =
            ids |> List.sort |> List.map(fun id ->
                let entry = catalog.Entries |> List.tryFind(fun entry -> entry.Id = id)
                            |> Option.defaultWith(fun () -> Operations.fail "extension.not_installed" "Selected id is absent.")
                let locked = locks |> List.tryFind(fun locked -> locked.Entry.Id = id)
                             |> Option.defaultWith(fun () -> Operations.fail "extension.lock_mismatch" "Selected lock is absent.")
                entry, locked)
        let exported = withFields ["extensions", Value.Array(selected |> List.map(fun (entry,_) -> entry.Json))] catalog.Json |> JsonText.toBytes
        match expected with
        | None -> ()
        | Some(expectedCatalog: byte array, expectedLocks: IReadOnlyDictionary<string,byte array>) ->
            if not (exported.AsSpan().SequenceEqual(expectedCatalog.AsSpan())) || expectedLocks.Count <> selected.Length then
                Operations.fail "extension.current_record_changed" "Current selected catalog differs from the preparation."
            for entry, locked in selected do
                match expectedLocks.TryGetValue entry.Id with
                | true, bytes when locked.Bytes.AsSpan().SequenceEqual(bytes.AsSpan()) -> ()
                | _ -> Operations.fail "extension.current_record_changed" "Current selected lock differs from the preparation."
        let captures = ResizeArray<CapturedExtension>()
        try
            for entry, locked in selected do captures.Add(capture root catalog entry locked host ct)
            new CapturedExtensionSet(captures.ToArray(),exported)
        with _ ->
            for captured in Seq.rev captures do (captured :> IDisposable).Dispose()
            reraise()

    let compact value =
        use parsed = JsonDocument.Parse(JsonText.toBytes value)
        use output = new MemoryStream()

        do
            use writer = new Utf8JsonWriter(output)
            parsed.RootElement.WriteTo writer
            writer.Flush()

        Array.append (output.ToArray()) [| 10uy |]

    let registerDocuments (plan: PackagePlan) args provenance source =
        let descriptor = plan.Descriptor
        let claims = plan.Artifact.Claims
        let extension = value "extension" claims.Json
        let capabilities = value "capabilities" claims.Json

        let summaries = [
            for kind in [ "frontend"; "backend" ] do
                match SharedJson.optional kind (SharedJson.members "capabilities" capabilities) with
                | Some value -> yield kind, value
                | None -> ()
        ]

        let platform =
            InstallationPlatform.FromDescriptorTriple(plan.Artifact.Platform.Value)
            |> StateOwnership.unwrap
            |> fun p ->
                p.GetIndexPlatform()
                |> StateOwnership.unwrap

        let common =
            [
                "extensionId", stringValue descriptor.ExtensionId.Value
                "name", value "name" extension
                "version", stringValue descriptor.Version.Text
                "runtime", stringValue "process"
                "platform",
                Value.Object["os", stringValue platform.Os
                             "arch", stringValue platform.Arch]
                "args", Value.Array(List.map stringValue args)
                "digest", stringValue plan.ArchiveDigest.Value
                "capabilities", value "types" extension
                "mepVersions", value "protocolVersions" claims.Json
                "executable", Value.Boolean true
                "package", plan.Artifact.Package.Value
            ]
            @ summaries

        let catalog =
            Value.Object(
                common
                @ [
                    "storePath",
                    stringValue
                        $"store/extensions/packages/sha256/{plan.TreeDigest.Value}/tree/{plan.EntryPoint.Text}"
                    "index", provenance
                    "claims", claims.Json
                    "claimCheck", stringValue "unchecked"
                ]
            )

        let locked =
            Value.Object(
                [
                    "$schema", stringValue (StateDocuments.schemaUrl "lock")
                    "selection",
                    Value.Object["kind", stringValue "exact"
                                 "value", stringValue descriptor.Version.Text]
                    "index",
                    Value.Object(
                        SharedJson.members "index" provenance
                        @ [ "claims", claims.Json ]
                    )
                    "source", source
                ]
                @ common
            )

        catalog, JsonText.toBytes locked

    let publishLocalAsync
        (repositoryPath: string)
        (plan: PackagePlan)
        (archivePath: string)
        args
        (ct: CancellationToken)
        =
        task {
            use repository = SafeDirectory.Open(repositoryPath, true)
            use! lease = repository.NamedLeaseAsync(".publish.lock", true, true, ct)
            let identity = repository.CanonicalPath
            let descriptor = plan.Descriptor
            let claims = plan.Artifact.Claims
            let extension = value "extension" claims.Json

            let source =
                Value.Object["kind", stringValue "local-file"

                             "path",
                             stringValue (
                                 "artifacts/"
                                 + plan.Artifact.Filename.Text
                             )]

            let platform =
                InstallationPlatform.FromDescriptorTriple(plan.Artifact.Platform.Value)
                |> StateOwnership.unwrap
                |> fun p ->
                    p.GetIndexPlatform()
                    |> StateOwnership.unwrap

            let artifact =
                Value.Object["runtime", stringValue "process"

                             "platform",
                             Value.Object["os", stringValue platform.Os
                                          "arch", stringValue platform.Arch]

                             "filename", stringValue plan.Artifact.Filename.Text
                             "sha256", stringValue plan.ArchiveDigest.Value
                             "claims", claims.Json
                             "source", source
                             "args", Value.Array(List.map stringValue args)
                             "executable", Value.Boolean true
                             "claimCheck", stringValue "unchecked"
                             "package", plan.Artifact.Package.Value]

            let cap =
                value "capabilities" claims.Json
                |> SharedJson.members "capabilities"

            let record =
                Value.Object(
                    [
                        "$schema", stringValue (StateDocuments.schemaUrl "index-record")
                        "id", stringValue descriptor.ExtensionId.Value
                        "name", value "name" extension
                        "version", stringValue descriptor.Version.Text
                        "channels", Value.Array []
                        "capabilities", value "types" extension
                        "mepVersions", value "protocolVersions" claims.Json
                        "artifacts", Value.Array[artifact]
                        "critical", Value.Array[stringValue "artifacts.package"]
                    ]
                    @ [
                        for kind in [ "frontend"; "backend" ] do
                            match SharedJson.optional kind cap with
                            | Some value -> yield kind, value
                            | None -> ()
                    ]
                )

            let historyBytes = compact record

            let provenance =
                Value.Object["kind", stringValue "local-directory"
                             "identity", stringValue identity
                             "revision", stringValue (Digest.ofBytes historyBytes)]

            StateDocuments.readIndex historyBytes provenance
            |> StateOwnership.unwrap
            |> ignore

            use histories = repository.Child("extensions", true)

            let historyName =
                descriptor.ExtensionId.Value
                + ".jsonl"

            if
                histories.Entries()
                |> Array.contains historyName
            then
                use input = histories.File(historyName, false, false)

                if
                    input.Length
                    <> int64 historyBytes.Length
                then
                    Operations.fail
                        "extension.repository_conflict"
                        "Generated one-release history has different bytes; retained unchanged."

                let actual, _ =
                    Copying.hashCopy
                        input
                        Stream.Null
                        (int64 historyBytes.Length)
                        "extension.history_too_large"
                        ct

                if
                    actual
                    <> Digest.ofBytes historyBytes
                then
                    Operations.fail
                        "extension.repository_conflict"
                        "Generated one-release repository history differs; retained unchanged."

            use artifacts = repository.Child("artifacts", true)
            let filename = plan.Artifact.Filename.Text

            if
                artifacts.Entries()
                |> Array.contains filename
            then
                use input = artifacts.File(filename, false, false)

                let actual, _ =
                    Copying.hashCopy
                        input
                        Stream.Null
                        InstallationLimits.MaxArtifactBytes
                        "package.archive_too_large"
                        ct

                if
                    actual
                    <> plan.ArchiveDigest.Value
                then
                    Operations.fail
                        "extension.repository_conflict"
                        "Repository artifact differs; retained unchanged."
            else
                use input = FileSafety.openInput archivePath

                let name =
                    ".stage-"
                    + Guid.NewGuid().ToString("N")

                use output = artifacts.File(name, true, false)
                let identity = artifacts.FileIdentity output

                try
                    let actual, _ =
                        Copying.hashCopy
                            input
                            output
                            InstallationLimits.MaxArtifactBytes
                            "package.archive_too_large"
                            ct

                    if
                        actual
                        <> plan.ArchiveDigest.Value
                    then
                        Operations.fail
                            "package.archive_digest_mismatch"
                            "Repository copy differs from the archive digest."

                    output.Flush true

                    if not (artifacts.Publish(output.SafeFileHandle, filename)) then
                        Operations.fail
                            "extension.repository_conflict"
                            "A repository artifact won publication."
                finally
                    output.Dispose()
                    artifacts.DeleteIfIdentity(name, identity)

            if
                not (
                    histories.Entries()
                    |> Array.contains historyName
                )
            then
                histories.ReplaceStateFile(historyName, historyBytes)

            return provenance, source
        }

    let register
        (root: SafeDirectory)
        (selected: Value)
        (locked: StateLock)
        hostVersion
        afterPhase
        ct
        =
        let current, _ = StateOwnership.readCurrent root

        let entries =
            (current
             |> Option.map (fun catalog -> catalog.Entries)
             |> Option.defaultValue [])
            |> List.filter (fun entry ->
                entry.Id
                <> locked.Entry.Id
            )
            |> List.map (fun entry -> entry.Json)

        let entries =
            selected
            :: entries
            |> List.sortWith (fun a b ->
                StringComparer.Ordinal.Compare(
                    value "extensionId" a
                    |> SharedJson.text "id",
                    value "extensionId" b
                    |> SharedJson.text "id"
                )
            )

        let catalogJson =
            match current with
            | Some current ->
                let existing =
                    SharedJson.optional "critical" (SharedJson.members "catalog" current.Json)
                    |> Option.map (SharedJson.strings "critical")
                    |> Option.defaultValue []

                let critical =
                    if List.contains "extensions.package" existing then
                        existing
                    else
                        existing
                        @ [ "extensions.package" ]

                withFields
                    [
                        "extensions", Value.Array entries
                        "critical", Value.Array(List.map Value.String critical)
                    ]
                    current.Json
            | None ->
                Value.Object["$schema", Value.String(StateDocuments.schemaUrl "catalog")

                             "extensions", Value.Array entries

                             "critical", Value.Array[Value.String "extensions.package"]]
        // withFields preserves existing optional root metadata; add the package marker if absent.
        let fields = SharedJson.members "catalog" catalogJson

        let catalogJson =
            if
                SharedJson.optional "critical" fields
                |> Option.isNone
            then
                Value.Object(
                    fields
                    @ [ "critical", Value.Array[Value.String "extensions.package"] ]
                )
            else
                catalogJson

        let catalog =
            StateDocuments.readCatalog (JsonText.toBytes catalogJson)
            |> StateOwnership.unwrap

        let entry =
            catalog.Entries
            |> List.find (fun entry -> entry.Id = locked.Entry.Id)

        let retainedPlan = PackagePlan.FromState(entry, locked)

        let activation = acquire root.Path root.Identity retainedPlan true ct

        try
            StateTransactions.commit
                root
                locked.Entry.Id
                (Some locked.Bytes)
                (Some catalog.Bytes)
                afterPhase
                ct

            fromActivation root catalog entry locked retainedPlan activation
        with _ ->
            (activation :> IDisposable).Dispose()
            reraise ()

/// Installation and activation through the existing extension catalog/lock contracts.
[<AbstractClass; Sealed>]
type InstalledExtensions private () =
    static member internal CaptureManyAsync(home: string, ids: IReadOnlyList<Morphir.Extensions.ExtensionId>, host: SemVer, ct: CancellationToken) : Task<InstallationResult<CapturedExtensionSet>> =
        let ids = ids |> Seq.map _.Value |> List.ofSeq
        Operations.runAsync ct (fun () -> StateOwnership.withLockAsync home ct (fun root -> ActivationState.captureMany root ids None host ct))

    static member internal CaptureManyExpectedAsync(home: string, catalog: byte array, locks: IReadOnlyDictionary<string, byte array>, host: SemVer, ct: CancellationToken) : Task<InstallationResult<CapturedExtensionSet>> =
        let catalog = Array.copy catalog
        let locks = locks |> Seq.map(fun (KeyValue(id,bytes)) -> id,Array.copy bytes) |> readOnlyDict
        Operations.runAsync ct (fun () -> StateOwnership.withLockAsync home ct (fun root ->
            let parsed = StateDocuments.readCatalog catalog |> StateOwnership.unwrap
            ActivationState.captureMany root (parsed.Entries |> List.map _.Id) (Some(catalog,locks)) host ct))
    /// Installs an explicitly named local developer package without probing it, then captures its registered lease.
    static member internal InstallLocalWithObserverAsync
        (
            home: string,
            plan: PackagePlan,
            archivePath: string,
            repositoryRoot: string,
            args: IReadOnlyList<string>,
            hostVersion: SemVer,
            afterPhase: Action<int>,
            ct: CancellationToken
        ) : Task<InstallationResult<CapturedExtension>> =
        task {
            let! preflight =
                Operations.runAsync
                    ct
                    (fun () -> task {
                        ActivationPlatforms.ensure plan.Artifact.Platform
                        |> StateOwnership.unwrap

                        DistributionIdentifiers.ParseExtensionId plan.Descriptor.ExtensionId.Value
                        |> StateOwnership.unwrap
                        |> ignore

                        ActivationState.ensureHost hostVersion plan.Artifact.Claims

                        let requirements =
                            SharedJson.protect (fun () ->
                                StateDocuments.requirements
                                    "release"
                                    (SharedJson.members "release" plan.Descriptor.Json)
                            )
                            |> StateOwnership.unwrap

                        if not (Comparator.allMet requirements hostVersion) then
                            Operations.fail
                                "extension.host_incompatible"
                                "Release requires a different host."

                        do!
                            StateOwnership.withLockAsync
                                home
                                ct
                                (fun root ->
                                    StateOwnership.readCurrent root
                                    |> ignore
                                )
                    })

            match
                preflight.Match(
                    Func<unit, InstallationFailure option>(fun _ -> None),
                    Func<InstallationFailure, InstallationFailure option>(Some)
                )
            with
            | Some failure -> return InstallationResult(Error failure)
            | None ->
                let! tree = PackageInstaller.InstallAsync(home, plan, archivePath, ct)

                match
                    tree.Match(
                        Func<InstalledPackage, InstallationFailure option>(fun _ -> None),
                        Func<InstallationFailure, InstallationFailure option>(Some)
                    )
                with
                | Some failure -> return InstallationResult(Error failure)
                | None ->
                    return!
                        Operations.runAsync
                            ct
                            (fun () -> task {
                                let args = Seq.toList args

                                let! provenance, source =
                                    ActivationState.publishLocalAsync
                                        repositoryRoot
                                        plan
                                        archivePath
                                        args
                                        ct

                                let selected, lockBytes =
                                    ActivationState.registerDocuments plan args provenance source

                                let locked =
                                    StateDocuments.readLock lockBytes
                                    |> StateOwnership.unwrap

                                return!
                                    StateOwnership.withLockAsync
                                        home
                                        ct
                                        (fun root ->
                                            ActivationState.register
                                                root
                                                selected
                                                locked
                                                hostVersion
                                                afterPhase
                                                ct
                                        )
                            })
        }

    /// Installs a named local developer source with the explicit unchecked/no-probe policy.
    static member InstallLocalUncheckedAsync
        (
            home: string,
            plan: PackagePlan,
            archivePath: string,
            repositoryRoot: string,
            args: IReadOnlyList<string>,
            hostVersion: SemVer,
            ct: CancellationToken
        ) =
        InstalledExtensions.InstallLocalWithObserverAsync(
            home,
            plan,
            archivePath,
            repositoryRoot,
            args,
            hostVersion,
            Action<int>(ignore),
            ct
        )

    /// Captures selected metadata and its actual shared package lifetime atomically under the state lock.
    static member CaptureAsync
        (
            home: string,
            extensionId: Morphir.Extensions.ExtensionId,
            hostVersion: SemVer,
            ct: CancellationToken
        ) =
        Operations.runAsync
            ct
            (fun () ->
                StateOwnership.withLockAsync
                    home
                    ct
                    (fun root ->
                        let catalog, locks = StateOwnership.readCurrent root

                        let catalog =
                            catalog
                            |> Option.defaultWith (fun () ->
                                Operations.fail "extension.not_installed" "No installed catalog."
                            )

                        let entry =
                            catalog.Entries
                            |> List.tryFind (fun entry -> entry.Id = extensionId.Value)
                            |> Option.defaultWith (fun () ->
                                Operations.fail
                                    "extension.not_installed"
                                    "Selected id is absent."
                            )

                        let locked =
                            locks
                            |> List.tryFind (fun locked -> locked.Entry.Id = extensionId.Value)
                            |> Option.defaultWith (fun () ->
                                Operations.fail
                                    "extension.lock_mismatch"
                                    "Selected lock is absent."
                            )

                        ActivationState.capture root catalog entry locked hostVersion ct
                    )
            )

    /// Reacquires an unpublished capture only when the exact current export is unchanged.
    /// Retained-generation authority requires a separately supported published binding.
    static member AcquireCurrentAsync
        (
            home: string,
            record: InstalledActivationRecord,
            hostVersion: SemVer,
            ct: CancellationToken
        ) =
        Operations.runAsync
            ct
            (fun () ->
                StateOwnership.withLockAsync
                    home
                    ct
                    (fun root ->
                        if
                            root.Identity
                            <> record.HomeIdentity
                        then
                            Operations.fail
                                "extension.path_escape"
                                "Retained record belongs to a different Home identity."

                        let captured =
                            ActivationState.captureExpected
                                root
                                record.CatalogSnapshotBytes
                                record.LockBytes
                                hostVersion
                                ct

                        captured.Activation
                    )
            )

    /// Validates exact expected-current export bytes and atomically captures their actual package lease.
    /// Supplied bytes confer no retained authority: a changed current export refuses.
    static member CaptureExpectedAsync
        (
            home: string,
            catalogSnapshotBytes: byte array,
            lockBytes: byte array,
            hostVersion: SemVer,
            ct: CancellationToken
        ) =
        let catalogSnapshotBytes = Array.copy catalogSnapshotBytes
        let lockBytes = Array.copy lockBytes

        Operations.runAsync
            ct
            (fun () ->
                StateOwnership.withLockAsync
                    home
                    ct
                    (fun root ->
                        ActivationState.captureExpected
                            root
                            catalogSnapshotBytes
                            lockBytes
                            hostVersion
                            ct
                    )
            )
