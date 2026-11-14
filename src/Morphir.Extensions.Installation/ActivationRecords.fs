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
    member _.ArchiveDigest =
        match entry.Digest with
        | Some digest -> digest
        | None -> Operations.fail "package.no_archive_digest" "A nupkg package has no archive digest; its tree digest pins it."
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
    /// The underlying extension-state record uses local-directory installer provenance.
    /// This does not describe a composed toolchain candidate's origin or grant publisher authentication.
    member _.IsLocalDeveloper = true
    /// Validates the actual negotiated session before an installed operation. This does not prove invocation.
    member _.CheckSession(session: NegotiatedSession) =
        InstallationResult(Claims.agreeSession entry.Claims session)
    member internal _.HomeIdentity = homeIdentity
    member internal _.Plan = plan
    member internal _.Entry = entry

    /// True when the lock records runtime <c>wasm</c>.
    member _.IsWasm = entry.Runtime = ArtifactRuntime.Wasm

    override _.ToString() =
        if entry.Runtime = ArtifactRuntime.Wasm then
            $"{entry.Id}@{entry.Version.Text}, WASM module {plan.TreeDigest.Value}."
        else
            $"{entry.Id}@{entry.Version.Text}, tree {plan.TreeDigest.Value}."

/// How a captured extension runs: a process from its package tree, or a WASM module from the single-file store.
[<RequireQualifiedAccess>]
type internal CapturedRuntime =
    | Process of PackageActivation
    | Wasm of InstalledModule

/// A snapshot and its already-acquired package lease or verified module, captured atomically under the state lock.
[<Sealed>]
type CapturedExtension internal (record: InstalledActivationRecord, runtime: CapturedRuntime) =
    /// Immutable metadata that remains valid independently of global id replacement.
    member _.Record = record

    /// True for an installed WASM extension; then use <see cref="Module"/>, not <see cref="Activation"/>.
    member _.IsWasm =
        match runtime with
        | CapturedRuntime.Wasm _ -> true
        | CapturedRuntime.Process _ -> false

    /// The package lease of a process extension. Retain until every consumer child and output pump has settled.
    /// A WASM extension has none and throws <see cref="InvalidOperationException"/>.
    member _.Activation =
        match runtime with
        | CapturedRuntime.Process activation -> activation
        | CapturedRuntime.Wasm _ -> raise (InvalidOperationException "A WASM extension has no package activation; use Module.")

    /// The verified module of a WASM extension, or null for a process extension.
    member _.Module: InstalledModule | null =
        match runtime with
        | CapturedRuntime.Wasm installed -> installed
        | CapturedRuntime.Process _ -> null

    override _.ToString() = record.ToString()

    interface IDisposable with
        member _.Dispose() =
            match runtime with
            | CapturedRuntime.Process activation -> (activation :> IDisposable).Dispose()
            | CapturedRuntime.Wasm installed -> (installed :> IDisposable).Dispose()

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            match runtime with
            | CapturedRuntime.Process activation -> (activation :> IAsyncDisposable).DisposeAsync()
            | CapturedRuntime.Wasm installed -> (installed :> IAsyncDisposable).DisposeAsync()

[<Sealed>]
type internal CapturedExtensionSet internal (captures: CapturedExtension array, catalog: byte array) =
    member _.Captures: IReadOnlyList<CapturedExtension> = Array.AsReadOnly captures
    member _.CatalogBytes = Array.copy catalog
    interface IDisposable with
        member _.Dispose() = for capture in Array.rev captures do (capture :> IDisposable).Dispose()

type internal GenerationSnapshot = {
    Family: string
    Directory: string
    Candidate: LocalComposedCandidate
    Manifest: ComposedManifest option
    Catalog: StateCatalog
    Locks: StateLock list
    Selection: byte array
    Receipt: byte array option
}

/// One shared retained/preparation binding reader for the manager and separate managed host.
module internal GenerationBinding =
    let unwrap = function
        | Ok value -> value
        | Error(failure: ReaderRefusal) -> Operations.fail "extension.generation_invalid" $"Generation document refused at {DocumentPath.render failure.Path}."
    let read (root: SafeDirectory) (path: string) =
        use input = FileSafety.openRelative root (Path.Combine(root.Path,path.Replace('/',Path.DirectorySeparatorChar)))
        if input.Length > (8L <<< 20) then Operations.fail "extension.generation_invalid" "Generation document exceeds its bound."
        use memory = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 65536
        let mutable count = input.Read(buffer,0,buffer.Length)
        while count <> 0 do
            if memory.Length + int64 count > (8L <<< 20) then Operations.fail "extension.generation_invalid" "Generation document grew past its bound."
            memory.Write(buffer,0,count)
            count <- input.Read(buffer,0,buffer.Length)
        memory.ToArray()
    let private same expected actual =
        if expected <> actual then Operations.fail "extension.generation_mismatch" "Exact generation byte binding differs."
    let private get name value = SharedJson.members "generation" value |> SharedJson.required "generation" name
    let private hash path root = read root path |> Sha256.ofBytes
    let load (root: SafeDirectory) (directory: string) =
        let relative = Path.GetRelativePath(root.Path,Path.GetFullPath directory).Replace('\\','/')
        let parts = relative.Split('/')
        let published = parts.Length=3 && parts[0]="toolchains" && parts[1]="generations"
        let preparation = parts.Length=3 && parts[0]="tmp" && parts[1].StartsWith("local-toolchain-",StringComparison.Ordinal) && parts[2]="generation"
        if not(published || preparation) then Operations.fail "extension.generation_path" "An overlay must be an owned complete generation or a current-inventory preparation."
        let bytes name = read root (relative+"/"+name)
        let rawCandidate = bytes "candidate.json"
        let family = ComposedDocuments.familyOf rawCandidate |> unwrap
        let candidate = ComposedDocuments.candidateForFamily family rawCandidate |> unwrap
        let selection = bytes "extensions/selection.json"
        same candidate.SelectionSha256 (Sha256.ofBytes selection)
        let pins = ComposedDocuments.readPinsFor candidate.Host.Platform selection |> unwrap
        let catalogBytes = bytes "extensions/catalog.json"
        let catalog = StateDocuments.readCatalog catalogBytes |> StateOwnership.unwrap
        let locks = pins |> List.map(fun pin ->
            let locked = bytes $"extensions/locks/{pin.Id.Text}.json" |> StateDocuments.readLock |> StateOwnership.unwrap
            let entry = catalog.Entries |> List.tryFind(fun entry -> entry.Id = pin.Id.Text)
                        |> Option.defaultWith(fun () -> Operations.fail "extension.generation_mismatch" "A selected catalog identity is missing.")
            StateDocuments.bind entry locked |> StateOwnership.unwrap
            if family=ReleaseBundleDocument.family then
                let retained = PackagePlan.FromState(entry,locked)
                if retained.Artifact.Platform <> Some (InstallationPlatform.Create(Platform.name candidate.Host.Platform).DescriptorTriple) then
                    Operations.fail "extension.generation_mismatch" "The retained process platform differs from the host candidate."
            if entry.Version <> pin.Version || (entry.Digest |> Option.map _.Value) <> Some pin.ArchiveSha256.Text then
                Operations.fail "extension.generation_mismatch" "Portable pin differs from the retained installed record."
            locked)
        let ids = pins |> List.map(fun pin -> pin.Id.Text) |> Set.ofList
        if ids <> (catalog.Entries |> List.map _.Id |> Set.ofList) || not(ids.Contains candidate.FrontendId.Text && ids.Contains candidate.BackendId.Text) then
            Operations.fail "extension.generation_mismatch" "The selected catalog projection is not the complete portable selection."
        use folder = SafeDirectory.Open(directory,false)
        let names = folder.Entries()
        if family=ReleaseBundleDocument.family && candidate.Origin=ComposedOrigin.LocalDeveloper && Array.contains "acquisition" names then
            Operations.fail "extension.generation_mismatch" "Explicit local development cannot retain acquired-release proof files."
        let hasManifest = names |> Array.contains "generation.json"
        let hasReceipt = names |> Array.contains "receipt.json"
        let manifest,receipt =
            if published || hasManifest || hasReceipt then
                if not published || not(hasManifest && hasReceipt) then
                    Operations.fail "extension.generation_incomplete" "Incomplete publication cannot use current-inventory fallback."
                let raw = bytes "generation.json"
                let manifest = ComposedDocuments.readManifest family candidate raw |> unwrap
                if manifest.Identity.Text <> parts[2] then Operations.fail "extension.generation_mismatch" "Generation directory differs from exact manifest bytes."
                same manifest.CatalogSha256 (Sha256.ofBytes catalogBytes)
                let actualLocks = locks |> List.map(fun locked -> locked.Entry.Id,Sha256.ofBytes locked.Bytes) |> List.sortBy fst
                same (manifest.Locks |> List.map(fun (id,hash) -> id.Text,hash)) actualLocks
                let receipt = bytes "receipt.json"
                ComposedDocuments.receiptMatches family candidate manifest.Identity receipt |> unwrap
                for id,path,digest in manifest.Metadata do
                    let raw = bytes path.Text
                    same digest (Sha256.ofBytes raw)
                    let result = PackagePlan.Read(raw,InstallationPlatform.Create(Platform.name candidate.Host.Platform))
                    let plan = result.Match(Func<PackagePlan,_>(fun value -> value),Func<InstallationFailure,_>(fun failure -> raise (InstallationException failure)))
                    let locked = locks |> List.find(fun locked -> locked.Entry.Id = id.Text)
                    let retained = PackagePlan.FromState(locked.Entry,locked)
                    if plan.ArchiveDigest <> retained.ArchiveDigest || plan.TreeDigest <> retained.TreeDigest || plan.EntryPoint <> retained.EntryPoint
                       || plan.Descriptor.ExtensionId.Value <> id.Text || plan.Descriptor.Version <> locked.Entry.Version
                       || (SharedJson.difference "claims" plan.Artifact.Claims.Json locked.Entry.Claims.Json |> Option.isSome) then
                        Operations.fail "extension.generation_mismatch" "Retained release envelope differs from the installed launch record."
                if candidate.Origin=ComposedOrigin.ReleaseChecksum then
                    let proof = manifest.Acquisition |> Option.defaultWith(fun () -> Operations.fail "extension.generation_incomplete" "Missing release acquisition proof.")
                    let bundleRaw = bytes "acquisition/bundle.json"
                    let sums = bytes "acquisition/SHA256SUMS"
                    let proofHash memberName = get memberName proof |> get "sha256" |> SharedJson.text "sha256"
                    same (proofHash "bundle") (Sha256.ofBytes bundleRaw).Text
                    same (proofHash "checksums") (Sha256.ofBytes sums).Text
                    let bundle = ReleaseBundleDocument.read bundleRaw |> unwrap
                    same candidate.Host.Platform bundle.Platform
                    ReleaseBundleDocument.verifyChecksums (ReleaseBundleDocument.fileName bundle.Platform) bundleRaw sums bundle |> unwrap
                    same ("v"+bundle.Version.Text) (get "tag" proof |> SharedJson.text "tag")
                    same bundle.SourceCommit candidate.Host.SourceCommit.Text
                    same bundle.Version.Text (candidate.Host.Host.Version.Text.Split('+')[0])
                    let asset role = bundle.Assets |> List.find(fun asset -> asset.Role=role)
                    same (asset "host-candidate").Sha256 candidate.Host.ManifestSha256
                    same (asset "host-archive").Sha256 candidate.Host.ArchiveSha256
                    same (asset "host-archive").Bytes candidate.Host.ArchiveBytes
                    same (asset "selection").Sha256 candidate.SelectionSha256
                    for role,id in ["frontend",candidate.FrontendId;"backend",candidate.BackendId] do
                        let locked = locks |> List.find(fun locked -> locked.Entry.Id=id.Text)
                        same (Some (asset (role+"-archive")).Sha256.Text) (locked.Entry.Digest |> Option.map _.Value)
                        let _,path,digest =
                            manifest.Metadata |> List.tryFind(fun (candidate,_,_) -> candidate=id)
                            |> Option.defaultWith(fun () -> Operations.fail "extension.generation_incomplete" "Release checksum generation requires its original provider release envelopes.")
                        same (asset (role+"-release")).Sha256 digest
                        let plan = PackagePlan.FromState(locked.Entry,locked)
                        same (asset (role+"-manifest")).Sha256.Text plan.TreeDigest.Value
                Some manifest,Some receipt
            else None,None
        {Family=family;Directory=directory;Candidate=candidate;Manifest=manifest;Catalog=catalog;Locks=locks;Selection=selection;Receipt=receipt}

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

            new CapturedExtension(record, CapturedRuntime.Process activation)
        with _ ->
            (activation :> IDisposable).Dispose()
            reraise ()

    let fromModule (root: SafeDirectory) (catalog: StateCatalog) (entry: StateEntry) locked plan (installed: InstalledModule) =
        try
            let selected =
                withFields [ "extensions", Value.Array[entry.Json] ] catalog.Json
                |> JsonText.toBytes

            let record = InstalledActivationRecord(entry, locked, selected, [||], root.Identity, plan)
            new CapturedExtension(record, CapturedRuntime.Wasm installed)
        with _ ->
            (installed :> IDisposable).Dispose()
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

        if plan.IsModule then
            let installed = ModuleInstaller.Acquire(root.Path, root.Identity, plan, ct)
            fromModule root catalog entry locked plan installed
        else
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

        let platform () =
            InstallationPlatform.FromDescriptorTriple(plan.Artifact.Platform.Value)
            |> StateOwnership.unwrap
            |> fun p ->
                p.GetIndexPlatform()
                |> StateOwnership.unwrap

        // A WASM module (design WASM-1) has no platform, no arguments and no package, and is not executable.
        let runtime =
            if plan.IsModule then
                [ "runtime", stringValue "wasm" ]
            else
                let platform = platform ()

                [
                    "runtime", stringValue "process"
                    "platform",
                    Value.Object["os", stringValue platform.Os
                                 "arch", stringValue platform.Arch]
                ]

        let common =
            runtime
            @ [
                "args", Value.Array(if plan.IsModule then [] else List.map stringValue args)
                yield!
                    match plan.Integrity with
                    | PayloadIntegrity.FileThenTree(file, _) -> [ "digest", stringValue file.Value ]
                    | PayloadIntegrity.ModuleFile digest -> [ "digest", stringValue digest.Value ]
                    | PayloadIntegrity.TreeOnly _ -> []
                "capabilities", value "types" extension
                "mepVersions", value "protocolVersions" claims.Json
                "executable", Value.Boolean(not plan.IsModule)
                yield!
                    match plan.Artifact.Package with
                    | Some package -> [ "package", package ]
                    | None -> []
            ]
            @ summaries
            |> fun items ->
                [
                    "extensionId", stringValue descriptor.ExtensionId.Value
                    "name", value "name" extension
                    "version", stringValue descriptor.Version.Text
                ]
                @ items

        let storePath =
            if plan.IsModule then
                $"store/extensions/sha256/{plan.TreeDigest.Value}/{plan.Artifact.Filename.Text}"
            else
                $"store/extensions/packages/sha256/{plan.TreeDigest.Value}/tree/{plan.EntryPoint.Text}"

        let catalog =
            Value.Object(
                common
                @ [
                    "storePath", stringValue storePath
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
                SourceDocuments.sourceValue (ArtifactSource.LocalFile("artifacts/" + plan.Artifact.Filename.Text))

            let artifact =
                if plan.IsModule then
                    Value.Object["runtime", stringValue "wasm"
                                 "filename", stringValue plan.Artifact.Filename.Text
                                 "sha256", stringValue plan.ArchiveDigest.Value
                                 "claims", claims.Json
                                 "source", source
                                 "args", Value.Array []
                                 "executable", Value.Boolean false
                                 "claimCheck", stringValue "unchecked"]
                else
                    let platform =
                        InstallationPlatform.FromDescriptorTriple(plan.Artifact.Platform.Value)
                        |> StateOwnership.unwrap
                        |> fun p ->
                            p.GetIndexPlatform()
                            |> StateOwnership.unwrap

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
                        yield!
                            if plan.IsModule then []
                            else [ "critical", Value.Array[stringValue "artifacts.package"] ]
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
                SourceDocuments.provenanceValue (
                    IndexProvenance.LocalDirectory(identity, Sha256Digest.Create(Digest.ofBytes historyBytes))
                )

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

        let commit () =
            StateTransactions.commit
                root
                locked.Entry.Id
                (Some locked.Bytes)
                (Some catalog.Bytes)
                afterPhase
                ct

        if retainedPlan.IsModule then
            let installed = ModuleInstaller.Acquire(root.Path, root.Identity, retainedPlan, ct)

            try
                commit ()
            with _ ->
                (installed :> IDisposable).Dispose()
                reraise ()

            fromModule root catalog entry locked retainedPlan installed
        else
            let activation = acquire root.Path root.Identity retainedPlan true ct

            try
                commit ()
                fromActivation root catalog entry locked retainedPlan activation
            with _ ->
                (activation :> IDisposable).Dispose()
                reraise ()

/// Installation and activation through the existing extension catalog/lock contracts.
[<AbstractClass; Sealed>]
type InstalledExtensions private () =
    static member internal ReadGenerationAsync(home: string, directory: string, ct: CancellationToken) : Task<InstallationResult<GenerationSnapshot>> =
        Operations.runAsync ct (fun () -> task {
            use root = SafeDirectory.Open(home,false)
            StateOwnership.checkUnderLock root ct
            ct.ThrowIfCancellationRequested()
            return GenerationBinding.load root directory
        })

    /// Internal composition only: the complete generation binding is checked before any retained record is acquired.
    static member internal CaptureOverlayAsync(home: string, directory: string, selectedId: Morphir.Extensions.ExtensionId, host: SemVer, ct: CancellationToken) : Task<InstallationResult<CapturedExtension>> =
        Operations.runAsync ct (fun () -> StateOwnership.withLockAsync home ct (fun root ->
            let snapshot = GenerationBinding.load root directory
            let locks = snapshot.Locks |> List.map(fun locked -> locked.Entry.Id,locked.Bytes) |> readOnlyDict
            let captures =
                if snapshot.Manifest.IsNone then
                    ActivationState.captureMany root (snapshot.Locks |> List.map(fun locked -> locked.Entry.Id)) (Some(snapshot.Catalog.Bytes,locks)) host ct
                else
                    // Current security/state validation still runs. It does not replace a retained record by global id.
                    StateOwnership.readCurrent root |> ignore
                    let captured = ResizeArray<CapturedExtension>()
                    try
                        for locked in snapshot.Locks do
                            let entry = snapshot.Catalog.Entries |> List.find(fun entry -> entry.Id=locked.Entry.Id)
                            captured.Add(ActivationState.capture root snapshot.Catalog entry locked host ct)
                        new CapturedExtensionSet(captured.ToArray(),snapshot.Catalog.Bytes)
                    with _ ->
                        for capture in Seq.rev captured do (capture :> IDisposable).Dispose()
                        reraise()
            let selected = captures.Captures |> Seq.tryFind(fun capture -> capture.Record.ExtensionId.Equals selectedId)
            match selected with
            | None ->
                (captures :> IDisposable).Dispose()
                Operations.fail "extension.not_selected" "The requested process is not in the selected generation."
            | Some selected ->
                for capture in captures.Captures do
                    if not(obj.ReferenceEquals(capture,selected)) then (capture :> IDisposable).Dispose()
                selected))

    /// The callback publishes only verified manager metadata. No network or guest runs under this state lock.
    static member internal PublishExpectedAsync(home: string, catalog: byte array, locks: IReadOnlyDictionary<string,byte array>, host: SemVer, publish: Func<Task>, ct: CancellationToken) : Task<InstallationResult<unit>> =
        Operations.runAsync ct (fun () -> task {
            use root = SafeDirectory.Open(home,false)
            do! StateOwnership.checkRepositoriesAsync root ct
            use lockRoot = root.Child("locks",true)
            use! stateLease = lockRoot.NamedLeaseAsync("extensions.state.lock",true,true,ct)
            StateOwnership.checkUnderLock root ct
            use captures = ActivationState.captureMany root (StateDocuments.readCatalog catalog |> StateOwnership.unwrap |> fun catalog -> catalog.Entries |> List.map _.Id) (Some(catalog,locks)) host ct
            ct.ThrowIfCancellationRequested()
            do! publish.Invoke()
        })

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
        InstalledExtensions.InstallPreparedAsync(
            home,
            plan,
            Func<Stream>(fun () -> FileSafety.openInput archivePath :> Stream),
            Func<string list, CancellationToken, Task<Value * Value>>(fun args ct ->
                ActivationState.publishLocalAsync repositoryRoot plan archivePath args ct),
            args,
            hostVersion,
            afterPhase,
            ct
        )

    /// Publishes a local declaration into its registered repository: the artifact copy and the one-release history.
    static member internal PublishLocalAsync
        (repositoryRoot: string, plan: PackagePlan, archivePath: string, args: string list, ct: CancellationToken)
        : Task<Value * Value> =
        ActivationState.publishLocalAsync repositoryRoot plan archivePath args ct

    /// The one install path for every source provider: preflight, package tree, then the catalog, lock and journal.
    /// <paramref name="record"/> supplies the provider's provenance and source after the tree is published.
    static member internal InstallPreparedAsync
        (
            home: string,
            plan: PackagePlan,
            openArchive: Func<Stream>,
            record: Func<string list, CancellationToken, Task<Value * Value>>,
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
                        if not plan.IsModule then
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
                let! installedFailure =
                    if plan.IsModule then
                        task {
                            let! stored = ModuleInstaller.InstallAsync(home, plan, openArchive, ct)

                            return
                                stored.Match(
                                    Func<StoredArtifact, InstallationFailure option>(fun _ -> None),
                                    Func<InstallationFailure, InstallationFailure option>(Some)
                                )
                        }
                    else
                        task {
                            let! tree = PackageInstaller.InstallFromAsync(home, plan, openArchive, ct)

                            return
                                tree.Match(
                                    Func<InstalledPackage, InstallationFailure option>(fun _ -> None),
                                    Func<InstallationFailure, InstallationFailure option>(Some)
                                )
                        }

                match installedFailure with
                | Some failure -> return InstallationResult(Error failure)
                | None ->
                    return!
                        Operations.runAsync
                            ct
                            (fun () -> task {
                                let args = Seq.toList args

                                let! provenance, source = record.Invoke(args, ct)

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
