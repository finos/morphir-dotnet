// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphirup.ComposedInstallation

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.Extensions.Installation
open LocalCandidate

let private value = Value.String
let private successful (result: InstallationResult<'T>) =
    result.Match(Func<'T,'T>(id),Func<InstallationFailure,'T>(fun failure -> raise (InstallationException failure)))
let private path home (relative: string) = Path.Combine(home,relative.Replace('/',Path.DirectorySeparatorChar))
let private read root name = GenerationBinding.read root name
let private fixedBytes name =
    use input = typeof<LocalProcess.LimitedCapture>.Assembly.GetManifestResourceStream("Morphirup.Fixed."+name)
                |> Option.ofObj |> Option.defaultWith(fun () -> invalidOp "Missing fixed readiness resource.")
    use output = new MemoryStream()
    input.CopyTo output
    output.ToArray()

let private asset (bundle: ReleaseBundle) role = bundle.Assets |> List.find(fun asset -> asset.Role=role)

let private checkHost (root: SafeDirectory) (candidate: LocalHostCandidate) full ct =
    FileSafety.withDirectories root ((LocalCandidateDocument.installationPath candidate).Split('/') |> List.ofArray) false
        (fun folder -> LocalInstallation.checkFolder folder candidate full ct)

let private validateHome home ct =
    if Directory.Exists home then
        use root = SafeDirectory.Open(home,false)
        LocalInstallation.validateOwnedState root
        StateOwnership.checkUnderLock root ct

let private currentSnapshot home directory ct = task {
    let! result = InstalledExtensions.ReadGenerationAsync(home,directory,ct)
    return successful result
}

let readLaunchReport code (output: byte array) (errors: string) =
    let parsed = Syntax.parseJson output
    let failed fallback =
        let diagnostics =
            match parsed with
            | Ok(Value.Object fields) ->
                match List.tryFind (fun (name,_) -> name="diagnostics") fields |> Option.map snd with
                | Some(Value.Array diagnostics) ->
                    diagnostics |> List.choose (function
                        | Value.Object fields ->
                            match List.tryFind (fun (name,_) -> name="code") fields |> Option.map snd,
                                  List.tryFind (fun (name,_) -> name="message") fields |> Option.map snd with
                            | Some(Value.String code),Some(Value.String message) when code<>"" && message<>"" -> Some(code+": "+message)
                            | _ -> None
                        | _ -> None)
                | _ -> []
            | _ -> []
        let reason = if diagnostics.IsEmpty then fallback else String.concat "; " diagnostics
        refuse "setup.readiness_failed" ("The installed host refused readiness: "+reason)
    if code <> 0 then failed errors
    let report = parsed |> reader
    let fields = match report with Value.Object fields -> fields | _ -> refuse "setup.readiness_failed" "The host did not return a report."
    if (List.tryFind(fun (name,_) -> name="success") fields |> Option.map snd) <> Some(Value.Boolean true) then
        failed "The host report did not record success."
    let provider = fields |> List.tryFind(fun (name,_) -> name="provider") |> Option.map snd
                   |> Option.defaultWith(fun () -> refuse "setup.readiness_failed" "An installed process observation is missing.")
    provider,fields

let private launchReport program args directory home overlay (ct: CancellationToken) = task {
    use timeout = CancellationTokenSource.CreateLinkedTokenSource ct
    timeout.CancelAfter(TimeSpan.FromSeconds 90.0)
    use input = new MemoryStream()
    use output = new LocalProcess.LimitedCapture(4L <<< 20)
    use errorBytes = new LocalProcess.LimitedCapture(2L <<< 20)
    use errors = new StreamWriter(errorBytes,UTF8Encoding(false),AutoFlush=true)
    let! code = LocalProcess.runWithOverlayAsync program args directory home (LocalProcess.Input.Redirect input) output errors overlay timeout.Token
    return readLaunchReport code (output.ToArray()) (Encoding.UTF8.GetString(errorBytes.ToArray()))
}

let checkGeneratedSet (entries: string array) artifacts =
    match artifacts with
    | Value.Array [Value.String "Pricing.fs"] -> ()
    | _ -> refuse "setup.readiness_failed" "The publisher's complete generated artifact set differs from the fixed expectation."
    // The existing publisher emits its ownership metadata alongside the artifacts.
    // It is not a generated F# source file and does not become a workflow output.
    if Array.sort entries <> [|".morphir-generated.json";"Pricing.fs"|] then
        refuse "setup.readiness_failed" "The complete generated directory differs from the fixed artifacts and publisher metadata."

let private readiness (root: SafeDirectory) (stage: SafeDirectory) (candidate: LocalComposedCandidate) (catalog: byte array) (captures: IReadOnlyList<CapturedExtension>) home ct = task {
    let workPath = Path.Combine(stage.Path,"work")
    FileSafety.withDirectories stage ["work"] true (fun work ->
        LocalInstallation.writeFile work "Pricing.fs" (fixedBytes "Pricing.fs"))
    let program = path home (LocalCandidateDocument.installationPath candidate.Host+"/"+candidate.Host.EntryPoint.Target.Text)
    let overlay = Path.Combine(stage.Path,"generation")
    let! frontend,_ = launchReport program ["compile";"--language";"fsharp";"--input";Path.Combine(workPath,"Pricing.fs");"--package-name";"acme/demo";"--ir-version";"4";"--output";Path.Combine(workPath,"compiled");"--out-dir";Path.Combine(workPath,"out");"--extension-id";candidate.FrontendId.Text;"--json"] workPath home overlay ct
    use work = stage.Child("work",false)
    let ir = read work "compiled/morphir-ir.json"
    if not(JsonNode.DeepEquals(JsonNode.Parse ir,JsonNode.Parse(fixedBytes "ir.json"))) then
        refuse "setup.readiness_failed" "Compiled IR differs from the fixed semantic expectation."
    let! backend,backendReport = launchReport program ["generate";"--target";"fsharp";"--input";Path.Combine(workPath,"compiled/morphir-ir.json");"--output";Path.Combine(workPath,"generated");"--option";"format=false";"--extension-id";candidate.BackendId.Text;"--json"] workPath home overlay ct
    use generated = work.Child("generated",false)
    checkGeneratedSet (generated.Entries()) (SharedJson.required "report" "artifacts" backendReport)
    read generated ".morphir-generated.json" |> ignore
    let source = read work "Pricing.fs"
    let generatedBytes = read generated "Pricing.fs"
    if generatedBytes <> fixedBytes "generated.fs" then refuse "setup.readiness_failed" "Generated F# differs from the fixed expected bytes."
    for provider in [frontend;backend] do
        let fields = SharedJson.members "provider" provider
        let id = SharedJson.required "provider" "extensionId" fields |> SharedJson.text "extensionId"
        let version = SharedJson.required "provider" "version" fields |> SharedJson.text "version"
        let record = captures |> Seq.tryFind(fun capture -> capture.Record.ExtensionId.Value=id)
                     |> Option.defaultWith(fun () -> refuse "setup.readiness_failed" "Observed provider is outside the captured selection.")
        if record.Record.Version.Text <> version then refuse "setup.readiness_failed" "Observed provider version differs from the captured record."
    root.ValidateLocation()
    stage.ValidateLocation()
    return Value.Object [
        "case",value "fsharp-compile-generate"
        "caseSha256",value (Sha256.ofBytes(fixedBytes "case.json")).Text
        "sourceSha256",value (Sha256.ofBytes source).Text
        "ir",Value.Object ["sha256",value (Sha256.ofBytes ir).Text;"semanticMatch",Value.Boolean true]
        "generatedFiles",Value.Array [Value.Object ["path",value "Pricing.fs";"bytes",Value.Number(string generatedBytes.Length);"sha256",value (Sha256.ofBytes generatedBytes).Text]]
        "providers",Value.Array [frontend;backend]
        "checks",Value.Array [Value.Object ["check",value "workflow";"status",value "passed";"workflow",value "fsharp-compile-generate"]]
    ]
}

// Both operations use the real installer leases; the parameters also let tests
// coordinate contention without timing the guest or changing filesystem behavior.
let publishGenerationAsync (acquireMutation: unit -> Task<FileStream>) (publishExpected: Func<Task> -> Task<unit>) (publish: unit -> Task<unit>) = task {
    // Run retains mutation while its host captures installer state. Publication
    // must use that same order, and release mutation before selection reacquires it.
    use! lease = acquireMutation ()
    do! publishExpected(Func<Task>(fun () -> publish () :> Task))
}

let private publishSelection (root: SafeDirectory) (snapshot: GenerationSnapshot) ct = task {
    let manifest = snapshot.Manifest |> Option.defaultWith(fun () -> refuse "setup.incomplete_generation" "An unpublished preparation cannot be selected.")
    let receipt = snapshot.Receipt |> Option.defaultWith(fun () -> refuse "setup.incomplete_generation" "A completed receipt is required.")
    checkHost root snapshot.Candidate.Host true ct
    for locked in snapshot.Locks do
        let! result = PackageInstaller.VerifyAsync(root.Path,PackagePlan.FromState(locked.Entry,locked),ct)
        successful result |> ignore
    use! lease = FileSafety.mutationLeaseAsync root ct
    ct.ThrowIfCancellationRequested()
    let current = GenerationBinding.load root snapshot.Directory
    if current.Receipt <> snapshot.Receipt then refuse "setup.generation_changed" "The completed generation changed before selection."
    FileSafety.withDirectories root ["toolchains"] false (fun folder ->
        folder.ReplaceStateFile("default.json",ComposedDocuments.pointer snapshot.Family snapshot.Candidate manifest.Identity receipt))
}

// Both explicitly consented local inputs and acquired releases drive this same
// installation/readiness core. Only acquired releases carry retained acquisition bytes.
let private setupCoreAsync platform home (bundle: ReleaseBundle) (rawCandidate: byte array) (transport: IAcquisitionTransport) (acquisition: (byte array * byte array) option) dryRun ct = task {
    let metadata role =
        let asset = asset bundle role
        AcquisitionDelivery.metadataAsync transport asset.Url (8UL <<< 20) (Some {Version=1UL;Length=asset.Bytes;Sha256=asset.Sha256}) ct
    let candidateAsset = asset bundle "host-candidate"
    if uint64 rawCandidate.LongLength <> candidateAsset.Bytes || Sha256.ofBytes rawCandidate <> candidateAsset.Sha256 then
        refuse "acquisition.binding_mismatch" "The exact candidate bytes differ from the bundle."
    let candidate = LocalComposedDocument.readReleaseCandidate rawCandidate |> reader
    let expectedOrigin = if Option.isSome acquisition then ComposedOrigin.ReleaseChecksum else ComposedOrigin.LocalDeveloper
    if candidate.Origin <> expectedOrigin then
        refuse "acquisition.origin_mismatch" "The candidate origin differs from the actual input route."
    let! selection = metadata "selection"
    let pins = ComposedDocuments.readPinsFor candidate.Host.Platform selection |> reader
    if Sha256.ofBytes selection <> candidate.SelectionSha256 || candidate.Host.SourceCommit.Text <> bundle.SourceCommit
       || candidate.Host.ArchiveSha256 <> (asset bundle "host-archive").Sha256
       || candidate.Host.ArchiveBytes <> (asset bundle "host-archive").Bytes
       || candidate.Host.Platform <> bundle.Platform
       || candidate.Host.Host.Version.Text.Split('+')[0] <> bundle.Version.Text then
        refuse "acquisition.binding_mismatch" "The release host, selection and bundle identities disagree."
    match LocalCandidatePlanning.planRelease platform LocalInstallation.maximumArchive candidate.Host with
    | Error _ -> refuse "acquisition.plan_refused" "The declared host closure is not a matching Windows runtime-bundled toolchain."
    | Ok _ -> ()
    let descriptors = ResizeArray<string * byte array * PackagePlan>()
    for role,selected in ["frontend",candidate.FrontendId;"backend",candidate.BackendId] do
        let! raw = metadata (role+"-release")
        let plan = PackagePlan.Read(raw,InstallationPlatform.Create(Platform.name candidate.Host.Platform)) |> successful
        let pin = pins |> List.tryFind(fun pin -> pin.Id=selected) |> Option.defaultWith(fun () -> refuse "acquisition.selection_mismatch" "The required process role is not selected.")
        if plan.Descriptor.ExtensionId.Value <> selected.Text || plan.ArchiveDigest.Value <> pin.ArchiveSha256.Text || plan.Descriptor.Version <> pin.Version
           || plan.ArchiveDigest.Value <> (asset bundle (role+"-archive")).Sha256.Text
           || plan.Artifact.Filename.Text <> (asset bundle (role+"-archive")).Name.Text
           || plan.TreeDigest.Value <> (asset bundle (role+"-manifest")).Sha256.Text then
            refuse "acquisition.binding_mismatch" "The release descriptor differs from its independently selected archive or canonical manifest."
        descriptors.Add(role,raw,plan)
    if pins.Length <> descriptors.Count then refuse "acquisition.selection_mismatch" "This release setup supports the two selected F# processes only."
    if dryRun then return candidate.Host.ManifestSha256,None
    else
        use root = SafeDirectory.Open(home,true)
        let stageName = "local-toolchain-"+Guid.NewGuid().ToString("N")
        FileSafety.withDirectories root ["tmp";stageName] true ignore
        use stage = SafeDirectory.Open(path home ("tmp/"+stageName),false)
        use owner = stage.File(".owner",true,true)
        return! LocalInstallation.withPrivateStageAsync root stage owner (fun () -> task {
            FileSafety.withDirectories stage ["downloads"] true ignore
            let downloads = Path.Combine(stage.Path,"downloads")
            for asset in bundle.Assets do
                let! _ = AcquisitionDelivery.downloadAsync home downloads transport asset ct
                ()
            let local role = Path.Combine(downloads,(asset bundle role).Name.Text)
            do! LocalInstallation.prepareComposedHostAsync home rawCandidate candidate.Host (local "host-archive") false ct
            for role,raw,plan in descriptors do
                let repository = Path.Combine(stage.Path,"repositories",role)
                FileSafety.withDirectories stage ["repositories";role] true ignore
                let! result = InstalledExtensions.InstallLocalUncheckedAsync(home,plan,local(role+"-archive"),repository,Array.AsReadOnly [||],candidate.Host.Host.Version,ct)
                use installed = successful result
                ()
            let ids = Array.AsReadOnly(pins |> List.map(fun pin -> Morphir.Extensions.ExtensionId.Create pin.Id.Text) |> Array.ofList)
            let! result = InstalledExtensions.CaptureManyAsync(home,ids,candidate.Host.Host.Version,ct)
            use captures = successful result
            let locks = captures.Captures |> Seq.map(fun capture ->
                let id = ExtensionId.tryParsePreview capture.Record.ExtensionId.Value |> function Ok id -> id | Error _ -> invalidOp "Validated installed id."
                id,capture.Record.LockBytes) |> List.ofSeq
            let metadata = descriptors |> Seq.map(fun (role,raw,plan) ->
                let id = ExtensionId.tryParsePreview plan.Descriptor.ExtensionId.Value |> function Ok id -> id | Error _ -> invalidOp "Validated installed id."
                let path = TargetPath.tryParse $"extensions/metadata/{id.Text}/release.json" |> function Ok path -> path | Error _ -> invalidOp "Validated metadata path."
                id,path,raw) |> List.ofSeq
            FileSafety.withDirectories stage ["generation"] true (fun generation ->
                LocalInstallation.writeFile generation "candidate.json" rawCandidate
                LocalInstallation.writeFile generation "extensions/selection.json" selection
                LocalInstallation.writeFile generation "extensions/catalog.json" captures.CatalogBytes
                for id,bytes in locks do LocalInstallation.writeFile generation $"extensions/locks/{id.Text}.json" bytes
                for _,path,bytes in metadata do LocalInstallation.writeFile generation path.Text bytes
                match acquisition with
                | Some(rawBundle,checksums) ->
                    LocalInstallation.writeFile generation "acquisition/bundle.json" rawBundle
                    LocalInstallation.writeFile generation "acquisition/SHA256SUMS" checksums
                | None -> ())
            let! workflow = readiness root stage candidate captures.CatalogBytes captures.Captures home ct
            let proof = acquisition |> Option.map(fun (rawBundle,checksums) -> Value.Object [
                "repository",value AcquisitionDelivery.repository;"tag",value("v"+bundle.Version.Text)
                "bundle",Value.Object ["path",value "acquisition/bundle.json";"sha256",value (Sha256.ofBytes rawBundle).Text]
                "checksums",Value.Object ["path",value "acquisition/SHA256SUMS";"sha256",value (Sha256.ofBytes checksums).Text]])
            let manifestBytes = ComposedDocuments.writeManifest "0.1.0-preview.3" candidate captures.CatalogBytes locks metadata workflow proof
            let manifest = ComposedDocuments.readManifest "0.1.0-preview.3" candidate manifestBytes |> reader
            let receipt = ComposedDocuments.receipt "0.1.0-preview.3" candidate manifest.Identity
            let preparedIdentity =
                use generation = stage.Child("generation",false)
                LocalInstallation.writeFile generation "generation.json" manifestBytes
                LocalInstallation.writeFile generation "receipt.json" receipt
                generation.Identity
            let expectedLocks = locks |> List.map(fun (id,bytes) -> id.Text,bytes) |> readOnlyDict
            let publishExpected callback = task {
                let! result = InstalledExtensions.PublishExpectedAsync(home,captures.CatalogBytes,expectedLocks,candidate.Host.Host.Version,callback,ct)
                successful result
            }
            do! publishGenerationAsync (fun () -> FileSafety.mutationLeaseAsync root ct) publishExpected (fun () -> task {
                stage.ValidateLocation()
                use prepared = stage.Child("generation",false)
                if prepared.Identity <> preparedIdentity then refuse "setup.generation_changed" "The prepared generation directory changed before publication."
                // Windows directory publication requires closing this child handle,
                // as in the installer's existing package publication path.
                (prepared :> IDisposable).Dispose()
                FileSafety.withDirectories root ["toolchains";"generations"] true (fun parent ->
                    let published = parent.PublishDirectory(stage,"generation",manifest.Identity.Text)
                    use final = parent.Child(manifest.Identity.Text,false)
                    if published && final.Identity <> preparedIdentity then refuse "setup.generation_changed" "Published generation identity differs from the prepared directory."
                    let current = GenerationBinding.load root final.Path
                    if current.Receipt <> Some receipt then refuse "setup.generation_conflict" "Existing generation is not the verified publication.")
            })
            let snapshot = GenerationBinding.load root (path home (LocalCandidateDocument.generationPath manifest.Identity))
            if Option.isSome acquisition then do! publishSelection root snapshot ct
            return candidate.Host.ManifestSha256,Some manifest.Identity
        })
}

/// The one release setup path. Dry-run reads metadata and plans without creating Home or running code.
let setupWithTransportAsync platform home version (transport: IAcquisitionTransport) dryRun ct = task {
    validateHome home ct
    let! bundle,rawBundle,checksums = AcquisitionDelivery.inspectAsync transport platform version ct
    let candidate = asset bundle "host-candidate"
    let! rawCandidate = AcquisitionDelivery.metadataAsync transport candidate.Url (8UL <<< 20) (Some {Version=1UL;Length=candidate.Bytes;Sha256=candidate.Sha256}) ct
    return! setupCoreAsync platform home bundle rawCandidate transport (Some(rawBundle,checksums)) dryRun ct
}

/// Explicit local files remain local developer input, even when their bundle uses official release URL spellings.
let setupLocalAsync platform home candidatePath archivePath bundlePath dryRun ct = task {
    validateHome home ct
    let rawCandidate = LocalCandidate.readBytes LocalInstallation.documentBound candidatePath
    let fields = Syntax.parseJson rawCandidate |> reader |> SharedJson.members "candidate"
    let origin = SharedJson.required "candidate" "origin" fields |> SharedJson.text "origin"
    if origin <> "local-developer" then refuse "local.origin_mismatch" "Explicit local setup requires a local-developer candidate."
    let rawBundle = LocalCandidate.readBytes (8L <<< 20) bundlePath
    let bundle = ReleaseBundleDocument.read rawBundle |> reader
    if bundle.Platform <> platform then refuse "local.platform_mismatch" "The local bundle differs from the actual manager process platform."
    let directory = Path.GetDirectoryName(Path.GetFullPath bundlePath) |> Option.ofObj
                    |> Option.defaultWith(fun () -> refuse "local.invalid_path" "The local bundle must have a parent directory.")
    let files = bundle.Assets |> List.map(fun asset ->
        let file = match asset.Role with "host-candidate" -> candidatePath | "host-archive" -> archivePath | _ -> Path.Combine(directory,asset.Name.Text)
        asset.Url.AbsoluteUri,file) |> Map.ofList
    let transport = {
        new IAcquisitionTransport with
            member _.OpenAsync(source,ct) = task {
                ct.ThrowIfCancellationRequested()
                let file = files.TryFind source.AbsoluteUri |> Option.defaultWith(fun () -> refuse "local.asset_missing" "The local bundle does not contain this asset.")
                let stream = FileSafety.openInput file
                return {
                    new IAcquisitionResponse with
                        member _.Content = stream
                        member _.DeclaredLength = Some stream.Length
                    interface IAsyncDisposable with member _.DisposeAsync() = stream.DisposeAsync()
                }
            }
    }
    return! setupCoreAsync platform home bundle rawCandidate transport None dryRun ct
}

let selected home ct = task {
    use root = SafeDirectory.Open(home,false)
    let raw = read root LocalCandidateDocument.selectionPath
    let schema = Syntax.parseJson raw |> reader |> SharedJson.members "selection" |> SharedJson.required "selection" "$schema" |> SharedJson.text "$schema"
    let family = if schema=ComposedDocuments.schemaFor "0.1.0-preview.3" "selection" then "0.1.0-preview.3" else "0.1.0-preview.2"
    let identity,receiptHash,origin = ComposedDocuments.readPointer family raw |> reader
    let! snapshot = currentSnapshot home (path home (LocalCandidateDocument.generationPath identity)) ct
    if snapshot.Family <> family || snapshot.Candidate.Origin <> origin then refuse "setup.origin_mismatch" "The selected origin or family differs from its bound candidate."
    if (snapshot.Receipt |> Option.map Sha256.ofBytes) <> Some receiptHash then refuse "setup.receipt_changed" "Selected receipt bytes differ."
    checkHost root snapshot.Candidate.Host false ct
    return snapshot
}

let isSelected home =
    let pointer = path home LocalCandidateDocument.selectionPath
    if not(File.Exists pointer) then false
    else
        let raw = LocalCandidate.readBytes LocalInstallation.documentBound pointer
        let document = Syntax.parseJson raw |> reader
        let fields = SharedJson.members "selection" document
        let schema = SharedJson.required "selection" "$schema" fields |> SharedJson.text "$schema"
        schema = ComposedDocuments.schemaFor "0.1.0-preview.3" "selection" || schema = ComposedDocuments.schemaFor "0.1.0-preview.2" "selection"

let defaultHome () =
    match Environment.GetEnvironmentVariable "MORPHIR_HOME" with
    | null | "" -> Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".morphir")
    | home -> LocalCandidate.homePath home

let releaseVersion directory (ownVersion: string) =
    let rec discover (folder: string) =
        let file = Path.Combine(folder,".morphir/toolchain.toml")
        if File.Exists file then Some file
        else
            match Directory.GetParent folder with
            | null -> None
            | parent -> discover parent.FullName
    match discover directory with
    | None -> ownVersion.Split('+')[0]
    | Some file ->
        let raw = LocalCandidate.readBytes (1L <<< 20) file
        let request =
            let first = UTF8Encoding(false,true).GetString raw
            if first.StartsWith("#:schema ",StringComparison.Ordinal) then RequestDocument.readPreview raw |> reader
            else RequestDocument.read raw |> reader
        if request.Host.Implementation.Text <> "dotnet" || request.Host.Deployment.Text <> "self-contained" || request.Toolset.Preset.Text <> "fsharp"
           || not request.Toolset.Providers.IsEmpty then
            refuse "setup.unsupported_request" "This Windows release setup supports the .NET self-contained F# toolset with its two packaged process providers."
        match request.Host.Release with
        | HostRelease.Exact version -> version.Text.Split('+')[0]
        | HostRelease.Channel _ -> refuse "setup.exact_version_required" "Release setup requires an exact project host version; moving channels are not selected."

let private executionPlatform platform (snapshot: GenerationSnapshot) =
    if snapshot.Candidate.Host.Platform <> platform then
        refuse "local.platform_mismatch" "The selected host platform differs from the actual manager process."

let selectAsync platform home identity dryRun ct = task {
    let! snapshot = currentSnapshot home (path home (LocalCandidateDocument.generationPath identity)) ct
    executionPlatform platform snapshot
    if not dryRun then
        use root = SafeDirectory.Open(home,false)
        do! publishSelection root snapshot ct
    return snapshot
}

let private optionToken names (token: string) =
    names |> List.tryPick (fun name ->
        if token.StartsWith(name+"=",StringComparison.Ordinal) || token.StartsWith(name+":",StringComparison.Ordinal) then
            Some(token.Substring(name.Length+1))
        elif name="-l" && token.StartsWith(name,StringComparison.Ordinal) && token.Length>name.Length then
            Some(token.Substring(name.Length))
        else None)

let private optionValue names args =
    let rec find = function
        | [] | "--"::_ -> None
        | token::rest ->
            if List.contains token names then rest |> List.tryHead
            else match optionToken names token with Some value -> Some value | None -> find rest
    find args

let providerArguments (frontend: Morphir.Toolchain.ExtensionId) (backend: Morphir.Toolchain.ExtensionId) args =
    let explicitSelection =
        args |> List.takeWhile ((<>) "--") |> List.exists (fun token ->
            List.contains token ["--extension";"--extension-id"] || Option.isSome(optionToken ["--extension";"--extension-id"] token))
    let selected =
        if explicitSelection then None
        else match args with
             | "compile"::_ when optionValue ["--language";"-l"] args = Some "fsharp" -> Some frontend
             | "generate"::_ when optionValue ["--target"] args = Some "fsharp" -> Some backend
             | _ -> None
    match selected with
    | None -> args
    | Some id ->
        let before,after = List.splitAt (args |> List.tryFindIndex ((=) "--") |> Option.defaultValue args.Length) args
        before @ ["--extension-id";id.Text] @ after

let runAsync platform home args directory input output (errors: TextWriter) ct = task {
    let! snapshot = selected home ct
    executionPlatform platform snapshot
    let candidate = snapshot.Candidate
    let args = providerArguments candidate.FrontendId candidate.BackendId args
    use root = SafeDirectory.Open(home,false)
    use! lease = FileSafety.mutationLeaseAsync root ct
    checkHost root candidate.Host false ct
    errors.WriteLine($"morphirup: {candidate.Origin.WireValue} delivery, NOT publisher-authenticated.")
    return! LocalProcess.runWithOverlayAsync (path home (LocalCandidateDocument.installationPath candidate.Host+"/"+candidate.Host.EntryPoint.Target.Text)) args directory home input output errors snapshot.Directory ct
}
