// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open NuGet.Common
open NuGet.Configuration
open NuGet.Packaging
open NuGet.Packaging.Core
open NuGet.Packaging.Signing
open NuGet.Protocol
open NuGet.Protocol.Core.Types
open NuGet.Versioning
open Morphir.Toolchain
open Morphir.Mep

/// NuGet log messages, warnings and errors only, on stderr: stdout is command output (the CLI logging rule).
/// NuGet writes no credential into a log message.
[<Sealed>]
type internal StderrNuGetLogger() =
    inherit LoggerBase(LogLevel.Warning)

    override _.Log(message: ILogMessage) =
        if message.Level >= LogLevel.Warning then
            Console.Error.WriteLine("nuget " + message.Level.ToString().ToLowerInvariant() + ": " + message.Message)

    override this.LogAsync(message: ILogMessage) =
        this.Log message
        Task.CompletedTask

/// Reading <c>nuget.config</c>: sources, package source mapping and the source a request names (design SRC-2).
module internal NuGetSettings =
    let load (workingDirectory: string) (configFile: string option) : ISettings =
        match configFile with
        | Some file ->
            let full = Path.GetFullPath(file, workingDirectory)

            if not (File.Exists full) then
                Operations.fail "extension.invalid_source" $"--configfile names no file: {full}"

            Settings.LoadSpecificSettings(Path.GetDirectoryName full |> nonNull, Path.GetFileName full)
        | None -> Settings.LoadDefaultSettings workingDirectory

    let private isUrl (text: string) =
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)

    /// The sources to look in for a package id: <c>--source</c> alone when given, else the enabled configured
    /// sources, narrowed by package source mapping when one is configured.
    let applicable (settings: ISettings) (workingDirectory: string) (source: string option) (id: string) =
        let configured =
            PackageSourceProvider(settings).LoadPackageSources()
            |> Seq.filter _.IsEnabled
            |> Seq.toList

        match source with
        | Some named ->
            match configured |> List.tryFind (fun item -> StringComparer.OrdinalIgnoreCase.Equals(item.Name, named)) with
            | Some item -> [ item ]
            | None ->
                let value = if isUrl named then named else Path.GetFullPath(named, workingDirectory)

                // The credentials of a configured source with the same URL still apply.
                match configured |> List.tryFind (fun item -> StringComparer.OrdinalIgnoreCase.Equals(item.Source, value)) with
                | Some item -> [ item ]
                | None -> [ PackageSource(value, value) ]
        | None ->
            let mapping = PackageSourceMapping.GetPackageSourceMapping settings

            if mapping.IsEnabled then
                let names = mapping.GetConfiguredPackageSources id |> Set.ofSeq
                configured |> List.filter (fun item -> names.Contains item.Name)
            else
                configured

    /// The feed the extension family records for a source: its https service index or its absolute folder.
    let feedOf (source: PackageSource) =
        let text =
            if source.IsLocal then
                let full = Path.GetFullPath source.Source

                // The family spells a Windows drive letter in upper case; Path.GetFullPath keeps the user's case.
                if full.Length >= 3 && Char.IsAsciiLetterLower full[0] && full[1] = ':' && full[2] = '\\' then
                    string (Char.ToUpperInvariant full[0]) + full.Substring 1
                else
                    full
            else
                source.Source

        match NuGetFeed.Parse text with
        | Ok feed -> feed
        | Error failure ->
            Operations.fail
                "extension.invalid_source"
                $"The NuGet source {source.Name} cannot be recorded: {failure.Message} An installed artifact records only an https V3 feed or a folder."

    /// True when the lock can record the source: an https V3 service index or a folder.
    let recordable (source: PackageSource) =
        try
            feedOf source |> ignore
            true
        with InstallationException _ ->
            false

    let describe (sources: PackageSource list) =
        match sources with
        | [] -> "no source"
        | sources -> sources |> List.map (fun source -> source.Source) |> String.concat ", "

/// NuGet's trusted roots for Linux and macOS (TrustedRoots/README.md). NuGet reads them only from
/// AppContext.BaseDirectory/trustedroots, and the Native AOT morphir is one file, so they are embedded, written to the
/// Home, and the base directory points there only while X509TrustStore builds its chain factories.
module internal NuGetTrustRoots =
    let private gate = obj ()
    let mutable private initialized = false
    let private files = [ "codesignctl.pem"; "timestampctl.pem" ]

    let write (folder: string) =
        let roots = Path.Combine(folder, "trustedroots")
        Directory.CreateDirectory roots |> ignore

        for name in files do
            let resource = $"Morphir.Extensions.Installation.TrustedRoots.{name}"

            use stream =
                match typeof<StderrNuGetLogger>.Assembly.GetManifestResourceStream resource with
                | null -> Operations.fail "extension.io_error" $"The NuGet trusted root list {name} is not embedded."
                | stream -> stream

            use memory = new MemoryStream()
            stream.CopyTo memory
            let bytes = memory.ToArray()
            let path = Path.Combine(roots, name)

            if not (File.Exists path) || not (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes.AsSpan())) then
                File.WriteAllBytes(path, bytes)

    /// Sets up NuGet's trust store as the .NET SDK does, once per process: the OS store on Windows, and on Linux and
    /// macOS a system code signing bundle when there is one, else these lists.
    let initialize (home: string) =
        lock gate (fun () ->
            if not initialized then
                let logger = StderrNuGetLogger()

                if OperatingSystem.IsWindows() then
                    X509TrustStore.InitializeForDotNetSdk logger
                else
                    let folder = Path.Combine(home, "tmp", "morphir-dotnet-nuget-trust")
                    write folder
                    let previous = AppContext.GetData "APP_CONTEXT_BASE_DIRECTORY"
                    AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", folder + string Path.DirectorySeparatorChar)

                    try
                        X509TrustStore.InitializeForDotNetSdk logger
                    finally
                        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", previous)

                initialized <- true)

/// Signature verification as the dotnet CLI does it on each OS, with the client policy of nuget.config (NUG-3).
module internal NuGetSignatures =

    /// dotnet verifies by default on Windows and Linux, not on macOS; DOTNET_NUGET_SIGNATURE_VERIFICATION overrides
    /// it on Linux and macOS. A require policy always verifies.
    let enabled (policy: ClientPolicyContext) =
        if policy.Policy = SignatureValidationMode.Require || OperatingSystem.IsWindows() then
            true
        else
            match Environment.GetEnvironmentVariable "DOTNET_NUGET_SIGNATURE_VERIFICATION" with
            | NonNull value when StringComparer.OrdinalIgnoreCase.Equals(value, "true") -> true
            | NonNull value when StringComparer.OrdinalIgnoreCase.Equals(value, "false") -> false
            | _ -> OperatingSystem.IsLinux()

    let verifyAsync (home: string) (settings: ISettings) (reader: PackageArchiveReader) (package: PackageIdentity) (ct: CancellationToken) = task {
        let logger = StderrNuGetLogger()
        let policy = ClientPolicyContext.GetClientPolicy(settings, logger)

        if enabled policy then
            NuGetTrustRoots.initialize home

            let providers: ISignatureVerificationProvider list = [
                IntegrityVerificationProvider()
                SignatureTrustAndValidityVerificationProvider()
                AllowListVerificationProvider(
                    ResizeArray<VerificationAllowListEntry>(policy.AllowList |> Seq.map (fun entry -> entry :> VerificationAllowListEntry)),
                    policy.Policy = SignatureValidationMode.Require,
                    "nuget.config requires trusted signers, and its trustedSigners list is empty.",
                    "The package is not signed by a trusted signer of nuget.config."
                )
            ]

            let verifier = PackageSignatureVerifier(providers)
            let! result = verifier.VerifySignaturesAsync(reader, policy.VerifierSettings, ct)

            if not result.IsValid then
                let issues =
                    result.Results
                    |> Seq.collect _.Issues
                    |> Seq.filter (fun issue -> issue.Level = LogLevel.Error)
                    |> Seq.map (fun issue -> $"{issue.Code}: {issue.Message}")
                    |> String.concat "; "

                Operations.fail
                    "extension.nuget_signature_invalid"
                    $"The signature of {package.Id} {package.Version.ToNormalizedString()} is refused by the {policy.Policy} policy: {issues}"
    }

/// A write-only view that refuses more than <paramref name="limit"/> bytes, so a feed cannot fill the Home.
[<Sealed>]
type internal BoundedWriteStream(inner: Stream, limit: int64) =
    inherit Stream()
    let mutable written = 0L

    let check count =
        written <- written + int64 count

        if written > limit then
            Operations.fail "package.archive_too_large" "The NuGet package exceeds 256 MiB."

    override _.CanRead = false
    override _.CanSeek = false
    override _.CanWrite = true
    override _.Length = written
    override _.Position
        with get () = written
        and set _ = raise (NotSupportedException())
    override _.Flush() = inner.Flush()
    override _.Read(_: byte array, _: int, _: int) = raise (NotSupportedException())
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())

    override _.Write(buffer: byte array, offset: int, count: int) =
        check count
        inner.Write(buffer, offset, count)

    override _.Write(buffer: ReadOnlySpan<byte>) =
        check buffer.Length
        inner.Write buffer

    override _.WriteAsync(buffer: ReadOnlyMemory<byte>, ct: CancellationToken) =
        check buffer.Length
        inner.WriteAsync(buffer, ct)

    override _.WriteAsync(buffer: byte array, offset: int, count: int, ct: CancellationToken) =
        check count
        inner.WriteAsync(buffer, offset, count, ct)

/// Version selection and downloads from NuGet sources. Every call uses a cache context with NoCache and
/// DirectDownload and a temporary folder under the Home, so nothing is written to NuGet's HTTP cache, global
/// packages folder or temporary folder.
module internal NuGetFeeds =
    let private logger = StderrNuGetLogger()

    let rec private statusOf (error: exn) =
        match error with
        | :? HttpRequestException as http when http.StatusCode.HasValue -> Some http.StatusCode.Value
        | _ ->
            match error.InnerException with
            | null -> None
            | inner -> statusOf inner

    /// Maps a NuGet client failure to a stable code naming the package, the version and the source.
    let refuse (source: PackageSource) (what: string) (interactive: bool) (error: exn) : 'T =
        match error with
        | InstallationException _ -> raise error
        | :? OperationCanceledException -> raise error
        | _ ->
            match statusOf error with
            | Some HttpStatusCode.Unauthorized
            | Some HttpStatusCode.Forbidden ->
                let hint =
                    if interactive then
                        ""
                    else
                        " Add packageSourceCredentials to nuget.config, install a credential provider, or use --interactive."

                Operations.fail
                    "extension.nuget_unauthorized"
                    $"The NuGet source {source.Source} refused the credential for {what}.{hint}"
            | _ ->
                let rec innermost (error: exn) =
                    match error.InnerException with
                    | null -> error.Message
                    | inner -> error.Message + " " + innermost inner

                Operations.fail
                    "extension.nuget_unreachable"
                    $"Cannot read the NuGet source {source.Source} for {what}: {innermost error}"

    /// A cache context that writes nothing outside <paramref name="temporary"/>.
    let cache (temporary: string) =
        new SourceCacheContext(NoCache = true, DirectDownload = true, GeneratedTempFolder = temporary)

    let private finder (source: PackageSource) (ct: CancellationToken) = task {
        let repository = Repository.Factory.GetCoreV3 source
        let! resource = repository.GetResourceAsync<FindPackageByIdResource> ct

        return
            match resource with
            | NonNull resource -> resource
            | Null -> Operations.fail "extension.nuget_unreachable" $"The NuGet source {source.Source} has no package resource."
    }

    /// The first source, in configuration order, that has the exact version.
    let findExactAsync sources (id: string) (version: NuGetVersion) interactive cache ct = task {
        let mutable found = None

        for source: PackageSource in sources do
            if found.IsNone then
                try
                    let! resource = finder source ct
                    let! exists = resource.DoesPackageExistAsync(id, version, cache, logger, ct)

                    if exists then
                        found <- Some source
                with error ->
                    refuse source $"{id} {version.ToNormalizedString()}" interactive error

        return found
    }

    /// The highest stable listed version across the sources, as <c>dotnet tool install</c> without a version.
    /// A folder feed has no listing, so all its stable versions count. Ties keep the first source.
    let findLatestAsync sources (id: string) interactive cache ct = task {
        let mutable best: (NuGetVersion * PackageSource) option = None

        for source: PackageSource in sources do
            try
                let repository = Repository.Factory.GetCoreV3 source
                let! metadata = repository.GetResourceAsync<PackageMetadataResource> ct

                let metadata =
                    match metadata with
                    | NonNull metadata -> metadata
                    | Null -> Operations.fail "extension.nuget_unreachable" $"The NuGet source {source.Source} has no package metadata."

                let! found = metadata.GetMetadataAsync(id, false, false, cache, logger, ct)

                for item in found do
                    let version = item.Identity.Version

                    if not version.IsPrerelease && item.IsListed then
                        match best with
                        | Some(current, _) when VersionComparer.Default.Compare(current, version) >= 0 -> ()
                        | _ -> best <- Some(version, source)
            with error ->
                refuse source id interactive error

        return best
    }

    /// Copies a package into a private stage under the Home, bounded by the artifact limit.
    let downloadAsync (home: string) (source: PackageSource) (package: PackageIdentity) interactive cache (ct: CancellationToken) = task {
        let! resources = StageResources.CreateAsync(home, ct)
        let mutable owned = true

        try
            let! resource =
                task {
                    try
                        return! finder source ct
                    with error ->
                        return refuse source package.Id interactive error
                }

            use bounded = new BoundedWriteStream(resources.Output, InstallationLimits.MaxArtifactBytes)

            let! copied =
                task {
                    try
                        return! resource.CopyNupkgToStreamAsync(package.Id, package.Version, bounded, cache, logger, ct)
                    with error ->
                        return refuse source $"{package.Id} {package.Version.ToNormalizedString()}" interactive error
                }

            resources.Output.Flush true
            resources.Output.Position <- 0L
            owned <- false
            return if copied then Some resources else (resources.CloseAsync() |> ignore; None)
        finally
            if owned then
                resources.CloseAsync().GetAwaiter().GetResult() |> ignore
    }

/// The NuGet package convention of the extension family (NUG-2): the main package carries
/// <c>morphir/release.json</c>; a process artifact is a subtree under <c>root</c> of the main package (layout A)
/// or of another package of the same source and version (layout B); a WASM artifact is the entry
/// <c>morphir/&lt;filename&gt;</c> of the main package.
module internal NuGetConvention =
    let descriptorEntry = "morphir/release.json"
    let private maximumDescriptor = 8 * 1024 * 1024

    let fileName (package: PackageIdentity) =
        $"{package.Id.ToLowerInvariant()}.{package.Version.ToNormalizedString().ToLowerInvariant()}.nupkg"

    /// The package an artifact <c>filename</c> names: the main package, or <c>&lt;id&gt;.&lt;version&gt;.nupkg</c>
    /// whose id extends the main id with a <c>.</c>, at the main version.
    let artifactPackage (main: PackageIdentity) (filename: string) =
        if filename = fileName main then
            main
        else
            let suffix = $".{main.Version.ToNormalizedString().ToLowerInvariant()}.nupkg"
            let mainId = main.Id.ToLowerInvariant()

            if not (filename.EndsWith(suffix, StringComparison.Ordinal)) then
                Operations.fail
                    "extension.invalid_descriptor"
                    $"The artifact {filename} is not a package of version {main.Version.ToNormalizedString()} of {main.Id}."

            let id = filename.Substring(0, filename.Length - suffix.Length)

            if not (id.StartsWith(mainId + ".", StringComparison.Ordinal)) || not (PackageIdValidator.IsValidPackageId id) then
                Operations.fail
                    "extension.invalid_descriptor"
                    $"The artifact package {id} does not extend {main.Id}; a descriptor names only packages of its own publisher."

            PackageIdentity(id, main.Version)

    /// A package that NuGet cannot read: no or a broken .nuspec, or a broken ZIP.
    let isMalformed (error: exn) =
        match error with
        | :? PackagingException
        | :? System.Xml.XmlException
        | :? InvalidDataException -> true
        | _ -> false

    let malformed (package: PackageIdentity) (error: exn) : 'T =
        Operations.fail
            "extension.invalid_descriptor"
            $"{package.Id} {package.Version.ToNormalizedString()} is not a readable NuGet package: {error.Message}"

    /// Checks the .nuspec identity of a package against what was asked for.
    let checkIdentity (reader: PackageArchiveReader) (expected: PackageIdentity) =
        let actual = reader.GetIdentity()

        if
            not (StringComparer.OrdinalIgnoreCase.Equals(actual.Id, expected.Id))
            || not (actual.Version.Equals expected.Version)
        then
            Operations.fail
                "extension.invalid_descriptor"
                $"The package is {actual.Id} {actual.Version.ToNormalizedString()}, not {expected.Id} {expected.Version.ToNormalizedString()}."

    let readDescriptorAsync (reader: PackageArchiveReader) (package: PackageIdentity) (ct: CancellationToken) = task {
        let entry =
            reader.GetFiles()
            |> Seq.tryFind (fun file -> StringComparer.Ordinal.Equals(file, descriptorEntry))

        match entry with
        | None ->
            return
                Operations.fail
                    "extension.release_descriptor_missing"
                    $"{package.Id} {package.Version.ToNormalizedString()} has no {descriptorEntry}; it is not a Morphir extension package."
        | Some entry ->
            use stream = reader.GetStream entry
            return! ProviderResults.readBounded stream maximumDescriptor "extension.invalid_descriptor" ct
    }

    /// Copies the WASM entry of the main package into a sealed private stage, checked against the plan digest.
    let stageModuleAsync (home: string) (resources: StageResources) (plan: PackagePlan) (ct: CancellationToken) = task {
        let entryName = "morphir/" + plan.Artifact.Filename.Text
        resources.Output.Position <- 0L
        use zip = new System.IO.Compression.ZipArchive(new BorrowedStream(resources.Output), System.IO.Compression.ZipArchiveMode.Read, false)

        let entry =
            match zip.GetEntry entryName with
            | null -> Operations.fail "extension.artifact_missing" $"The package has no entry {entryName}."
            | entry -> entry

        if entry.Length > InstallationLimits.MaxArtifactBytes then
            Operations.fail "extension.artifact_too_large" $"The entry {entryName} exceeds 256 MiB."

        let digest =
            match plan.Integrity with
            | PayloadIntegrity.ModuleFile digest -> digest
            | _ -> Operations.fail "package.runtime" "The plan is not a WASM module."

        let! begun = PrivateStaging.BeginAsync(home, uint64 entry.Length, digest, uint64 InstallationLimits.MaxArtifactBytes, ct)
        let stage = ProviderResults.unwrap begun
        let mutable owned = true

        try
            use input = entry.Open()
            let buffer = Array.zeroCreate<byte> 65536
            let mutable reading = true

            while reading do
                let! count = input.ReadAsync(buffer, 0, buffer.Length, ct)

                if count = 0 then
                    reading <- false
                else
                    do! stage.WriteAsync(ReadOnlyMemory(buffer, 0, count), ct)

            let! sealedResult = stage.SealAsync ct
            let sealedStage = ProviderResults.unwrap sealedResult
            owned <- false
            return sealedStage
        finally
            if owned then
                (stage :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
    }

    /// A self-contained or Native AOT payload only: a .dll entry point, or a runtimeconfig.json that names a shared
    /// framework, needs an installed .NET runtime to start.
    let checkSelfContained (reader: PackageArchiveReader) (plan: PackagePlan) =
        let entryPoint = plan.EntryPoint.Text

        if entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) then
            Operations.fail
                "extension.unsupported_package"
                $"The entry point {entryPoint} is a framework-dependent .NET assembly; publish the extension self-contained or as Native AOT."

        let stem =
            if entryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) then
                entryPoint.Substring(0, entryPoint.Length - 4)
            else
                entryPoint

        let root = plan.Root |> Option.map (fun root -> root + "/") |> Option.defaultValue ""
        let runtimeConfig = $"{root}{stem}.runtimeconfig.json"

        match reader.GetFiles() |> Seq.tryFind (fun file -> StringComparer.Ordinal.Equals(file, runtimeConfig)) with
        | None -> ()
        | Some entry ->
            use stream = reader.GetStream entry
            let bytes = ProviderResults.readBounded stream (1024 * 1024) "extension.unsupported_package" CancellationToken.None
            let bytes = bytes.GetAwaiter().GetResult()

            let names =
                try
                    use document = JsonDocument.Parse(bytes: byte array)
                    let mutable options = Unchecked.defaultof<JsonElement>

                    if document.RootElement.TryGetProperty("runtimeOptions", &options) then
                        [ "framework"; "frameworks"; "includedFrameworks" ]
                        |> List.filter (fun name ->
                            let mutable value = Unchecked.defaultof<JsonElement>
                            options.TryGetProperty(name, &value))
                    else
                        []
                with :? JsonException ->
                    Operations.fail "extension.unsupported_package" $"{runtimeConfig} is not JSON."

            if List.contains "framework" names || List.contains "frameworks" names then
                Operations.fail
                    "extension.unsupported_package"
                    $"{runtimeConfig} names a shared framework, so the tool needs an installed .NET runtime; publish it self-contained or as Native AOT."

/// The NuGet source provider: <c>nuget:&lt;package-id&gt;[@&lt;version&gt;]</c> from nuget.org, V3 feeds and folder
/// feeds, with the sources, mapping, credentials and signature policy of nuget.config. It only resolves and fetches;
/// the installer checks and writes.
[<Sealed>]
type NuGetPackageProvider(home: string, workingDirectory: string) =
    static let gate = obj ()
    static let mutable credentialsInteractive: bool option = None

    /// NuGet's credential service: packageSourceCredentials of nuget.config, then credential provider plugins found
    /// as NuGet finds them. Non-interactive unless --interactive.
    static let configureCredentials (interactive: bool) =
        lock gate (fun () ->
            if credentialsInteractive <> Some interactive then
                NuGet.Credentials.DefaultCredentialServiceUtility.SetupDefaultCredentialService(StderrNuGetLogger(), not interactive)
                credentialsInteractive <- Some interactive)

    static let request (release: ExtensionSourceRequest) =
        match release.Kind with
        | NuGet request -> request
        | _ -> Operations.fail "extension.provider_mismatch" "The NuGet provider reads only nuget: sources."

    static let temporaryFolder (home: string) =
        let path = Path.Combine(home, "tmp", "morphir-dotnet-nuget-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory path |> ignore
        path

    /// NuGet keeps its HTTP cache (service index, repository signatures) and its lock files in its own folders even
    /// with NoCache. The provider points both into its private temporary folder under the Home for the duration of
    /// one operation, and restores them after it.
    static let withPrivateFolders (temporary: string) (work: unit -> Task<'T>) : Task<'T> = task {
        let names = [ "NUGET_HTTP_CACHE_PATH", "http-cache" ]
        let previous = names |> List.map (fun (name, _) -> name, Environment.GetEnvironmentVariable name)

        for name, folder in names do
            let path = Path.Combine(temporary, folder)
            Directory.CreateDirectory path |> ignore
            Environment.SetEnvironmentVariable(name, path)

        try
            return! work ()
        finally
            for name, value in previous do
                Environment.SetEnvironmentVariable(name, value)
    }

    /// NuGet reads its scratch folder (lock files) once per process and keeps it. So before its first NuGet call the
    /// provider points NUGET_SCRATCH at a stable folder under the Home, which a later operation of the same Home
    /// finds again. A process that used NuGet before keeps the folder NuGet chose then.
    static let usePrivateScratch (home: string) =
        lock gate (fun () ->
            let scratch = Path.Combine(home, "tmp", "morphir-dotnet-nuget-scratch")
            Directory.CreateDirectory scratch |> ignore
            Environment.SetEnvironmentVariable("NUGET_SCRATCH", scratch))

    static let removeFolder (path: string) =
        try
            if Directory.Exists path then
                Directory.Delete(path, true)
        with _ ->
            ()

    /// Downloads under <paramref name="home"/> and uses the nuget.config hierarchy from the current directory.
    new(home: string) = NuGetPackageProvider(home, Environment.CurrentDirectory)

    override _.ToString() = "nuget"

    interface IExtensionSourceProvider with
        member _.Scheme = "nuget"

        member _.ResolveAsync(release, platform, ct) =
            Operations.runAsync ct (fun () -> task {
                let request = request release

                if not (Path.IsPathFullyQualified home) then
                    Operations.fail "extension.invalid_home" "--home must be absolute."

                usePrivateScratch home
                configureCredentials request.Interactive
                let settings = NuGetSettings.load workingDirectory request.ConfigFile
                let sources = NuGetSettings.applicable settings workingDirectory request.Source request.Id

                if List.isEmpty sources then
                    Operations.fail
                        "extension.nuget_not_found"
                        $"No NuGet source applies to {request.Id}; check the sources and package source mapping of nuget.config, or use --source."

                // A source the lock cannot record (http, a V2 URL) is not asked at all, so no credential goes to it.
                // When no applicable source is left, the first refusal names the reason.
                let sources =
                    match sources |> List.filter NuGetSettings.recordable with
                    | [] -> NuGetSettings.feedOf sources.Head |> fun _ -> []
                    | recordable -> recordable

                if not (Path.IsPathFullyQualified home) then
                    Operations.fail "extension.invalid_home" "--home must be absolute."

                // NuGet's temporary files go under the Home, never to NuGet's own folders.
                let temporary = temporaryFolder home

                try
                  return! withPrivateFolders temporary (fun () -> task {
                    use cache = NuGetFeeds.cache temporary

                    let! selected =
                        match request.Version with
                        | Some version ->
                            task {
                                let! found = NuGetFeeds.findExactAsync sources request.Id version request.Interactive cache ct
                                return found |> Option.map (fun source -> version, source)
                            }
                        | None -> NuGetFeeds.findLatestAsync sources request.Id request.Interactive cache ct

                    let version, source =
                        match selected with
                        | Some value -> value
                        | None ->
                            let what =
                                match request.Version with
                                | Some version -> $"{request.Id} {version.ToNormalizedString()}"
                                | None -> $"a stable listed version of {request.Id}"

                            Operations.fail
                                "extension.nuget_not_found"
                                $"No NuGet source has {what}; tried {NuGetSettings.describe sources}."

                    let main = PackageIdentity(request.Id, version)
                    let! downloaded = NuGetFeeds.downloadAsync home source main request.Interactive cache ct

                    let resources =
                        downloaded
                        |> Option.defaultWith (fun () ->
                            Operations.fail
                                "extension.nuget_not_found"
                                $"{source.Source} lists {request.Id} {version.ToNormalizedString()} but has no package for it.")

                    try
                      try
                        use reader = new PackageArchiveReader(new BorrowedStream(resources.Output), false)
                        NuGetConvention.checkIdentity reader main
                        do! NuGetSignatures.verifyAsync home settings reader main ct
                        let! descriptor = NuGetConvention.readDescriptorAsync reader main ct
                        let described = Descriptors.read descriptor |> StateOwnership.unwrap

                        if not (NuGetVersion.Parse(described.Version.Text).Equals version) then
                            Operations.fail
                                "extension.version_mismatch"
                                $"{request.Id} {version.ToNormalizedString()} describes version {described.Version.Text}."

                        let plan = PackagePlan.Read(descriptor, platform) |> ProviderResults.unwrap

                        let feed = NuGetSettings.feedOf source
                        let mainKey = NuGetPackageKey.Of(main.Id, version)
                        let provenance = IndexProvenance.NuGetPackage(feed, mainKey, Sha256Digest.Create(Digest.ofBytes descriptor))
                        let filename = plan.Artifact.Filename.Text

                        let location =
                            match plan.Artifact.Runtime, plan.Format with
                            | ArtifactRuntime.Wasm, _ ->
                                let entry = "morphir/" + filename

                                if not (reader.GetFiles() |> Seq.contains entry) then
                                    Operations.fail
                                        "extension.artifact_missing"
                                        $"{request.Id} {version.ToNormalizedString()} has no entry {entry}."

                                {
                                    Source = source
                                    Settings = settings
                                    Package = main
                                    IsModule = true
                                    Interactive = request.Interactive
                                },
                                ArtifactSource.NuGetPackageEntry(feed, mainKey, entry)
                            | _, "nupkg" ->
                                let package = NuGetConvention.artifactPackage main filename
                                let root = plan.Root |> Option.defaultWith (fun () -> Operations.fail "extension.invalid_descriptor" "A nupkg package requires root.")

                                if StringComparer.OrdinalIgnoreCase.Equals(package.Id, main.Id) then
                                    NuGetConvention.checkSelfContained reader plan

                                {
                                    Source = source
                                    Settings = settings
                                    Package = package
                                    IsModule = false
                                    Interactive = request.Interactive
                                },
                                ArtifactSource.NuGetPackageEntry(feed, NuGetPackageKey.Of(package.Id, version), root)
                            | _, format ->
                                Operations.fail
                                    "extension.invalid_descriptor"
                                    $"A NuGet release names a {format} archive; a NuGet package is the archive, so use package format nupkg."

                        return ResolvedExtensionRelease(descriptor, plan, NuGetPackage(fst location, provenance, snd location))
                      with error when NuGetConvention.isMalformed error ->
                        return NuGetConvention.malformed main error
                    finally
                        resources.CloseAsync().GetAwaiter().GetResult() |> ignore
                  })
                finally
                    removeFolder temporary
            })

        member _.FetchAsync(resolved, fetchHome, ct) =
            Operations.runAsync ct (fun () -> task {
                let location, provenance, source =
                    match resolved.Location with
                    | NuGetPackage(location, provenance, source) -> location, provenance, source
                    | _ -> Operations.fail "extension.provider_mismatch" "The NuGet provider fetches only NuGet packages."

                usePrivateScratch fetchHome
                configureCredentials location.Interactive
                let temporary = temporaryFolder fetchHome

                try
                  return! withPrivateFolders temporary (fun () -> task {
                    use cache = NuGetFeeds.cache temporary
                    let! downloaded = NuGetFeeds.downloadAsync fetchHome location.Source location.Package location.Interactive cache ct

                    let resources =
                        downloaded
                        |> Option.defaultWith (fun () ->
                            Operations.fail
                                "extension.artifact_missing"
                                $"{location.Source.Source} has no {location.Package.Id} {location.Package.Version.ToNormalizedString()}.")

                    let mutable owned = true

                    try
                        try
                            use reader = new PackageArchiveReader(new BorrowedStream(resources.Output), false)
                            NuGetConvention.checkIdentity reader location.Package
                            NuGetSignatures.verifyAsync fetchHome location.Settings reader location.Package ct |> fun work -> work.GetAwaiter().GetResult()

                            if not location.IsModule then
                                NuGetConvention.checkSelfContained reader resolved.Plan
                        with error when NuGetConvention.isMalformed error ->
                            NuGetConvention.malformed location.Package error

                        resources.Output.Position <- 0L
                        let values = SourceDocuments.provenanceValue provenance, SourceDocuments.sourceValue source

                        let record =
                            Func<string list, CancellationToken, Task<Value * Value>>(fun _ _ -> Task.FromResult values)

                        if location.IsModule then
                            // The module is the entry morphir/<filename> of the main package. It is copied into a
                            // sealed private stage, which checks its bytes against the descriptor sha256.
                            let! staged = NuGetConvention.stageModuleAsync fetchHome resources resolved.Plan ct
                            return new FetchedExtensionArtifact(Staged staged, record)
                        else
                            owned <- false
                            return new FetchedExtensionArtifact(Unhashed resources, record)
                    finally
                        if owned then
                            resources.CloseAsync().GetAwaiter().GetResult() |> ignore
                  })
                finally
                    removeFolder temporary
            })
