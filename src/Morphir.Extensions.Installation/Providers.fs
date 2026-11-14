// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Extensions.Installation

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Runtime.InteropServices
open System.Diagnostics.CodeAnalysis
open System.Threading
open System.Threading.Tasks
open Morphir.Toolchain
open Morphir.Mep

/// Which release of a GitHub repository to install.
[<RequireQualifiedAccess>]
type GitHubReleaseSelector =
    /// The latest release, as the GitHub API reports it: never a draft or a prerelease.
    | Latest
    /// A SemVer version: the tag <c>v&lt;version&gt;</c>, else the tag <c>&lt;version&gt;</c>.
    | Version of SemVer
    /// An exact tag that is not a SemVer version.
    | Tag of string

/// An explicit local developer declaration: a release descriptor and its archive inside a registered repository.
type internal LocalDeclaration = {
    Home: string
    Repository: string
    Release: string
    Archive: string
    ExpectedId: Morphir.Extensions.ExtensionId
}

type internal SourceRequestKind =
    | Local of LocalDeclaration
    | GitHub of GitHubRepository * GitHubReleaseSelector

/// What the user asked to install, parsed once. The scheme selects the provider.
[<Sealed>]
type ExtensionSourceRequest internal (kind: SourceRequestKind, text: string) =
    member internal _.Kind = kind

    /// The provider scheme: <c>github</c> or <c>local</c>.
    member _.Scheme =
        match kind with
        | Local _ -> "local"
        | GitHub _ -> "github"

    override _.ToString() = text

    /// Parses <c>github:&lt;owner&gt;/&lt;repo&gt;[@&lt;version-or-tag&gt;]</c>.
    static member Parse(text: string | null) : Result<ExtensionSourceRequest, InstallationFailure> =
        let refuse message =
            Parsing.refuse "extension.invalid_source" message

        match text with
        | null -> refuse "The source is empty."
        | text ->
            match text.IndexOf ':' with
            | -1 -> refuse $"The source {text} names no provider; use github:<owner>/<repo>[@<version-or-tag>]."
            | colon ->
                match text.Substring(0, colon) with
                | "github" ->
                    let rest = text.Substring(colon + 1)

                    let repository, selector =
                        match rest.IndexOf '@' with
                        | -1 -> rest, None
                        | at -> rest.Substring(0, at), Some(rest.Substring(at + 1))

                    GitHubRepository.Parse repository
                    |> Result.bind (fun repository ->
                        match selector with
                        | None -> Ok GitHubReleaseSelector.Latest
                        | Some selector when not (SourceDocuments.validTag selector) ->
                            refuse $"Not a version or Git tag: {selector}"
                        | Some selector ->
                            match SemVer.tryParse selector with
                            | Ok version when version.Text = selector -> Ok(GitHubReleaseSelector.Version version)
                            | _ -> Ok(GitHubReleaseSelector.Tag selector)
                        |> Result.map (fun selector ->
                            ExtensionSourceRequest(GitHub(repository, selector), text)
                        )
                    )
                | scheme -> refuse $"Unknown source provider {scheme}; the providers are github."

    /// Parses a source for C# callers.
    static member TryParse
        (text: string | null,
         [<Out; MaybeNullWhen(false)>] value: byref<ExtensionSourceRequest>,
         [<Out; MaybeNullWhen(true)>] failure: byref<InstallationFailure>)
        =
        Parsing.complete (ExtensionSourceRequest.Parse text) &value &failure

    /// A local developer declaration in a registered repository.
    static member Local(home: string, repository: string, release: string, archive: string, expectedId: Morphir.Extensions.ExtensionId) =
        ExtensionSourceRequest(
            Local {
                Home = home
                Repository = repository
                Release = release
                Archive = archive
                ExpectedId = expectedId
            },
            $"local:{repository}"
        )

/// Where the selected artifact bytes are. A provider reads its own case.
type internal ArtifactLocation =
    /// A file the local provider already checked inside its repository.
    | LocalArchive of repositoryRoot: string * archivePath: string
    /// An HTTPS download of an exact length.
    | Download of uri: Uri * length: uint64 * provenance: IndexProvenance * source: ArtifactSource

/// A release a provider resolved: the descriptor, the plan for this platform and where the artifact is.
[<Sealed>]
type ResolvedExtensionRelease internal (descriptor: byte array, plan: PackagePlan, location: ArtifactLocation) =
    /// The extension the descriptor names.
    member _.ExtensionId = plan.Descriptor.ExtensionId
    /// The release version.
    member _.Version = plan.Descriptor.Version
    /// The package plan for the host platform.
    member _.Plan = plan
    member internal _.DescriptorBytes = Array.copy descriptor
    member internal _.Location = location
    override _.ToString() = $"Resolved {plan.Descriptor.ExtensionId.Value} {plan.Descriptor.Version.Text}."

type internal FetchedArchive =
    | ExistingFile of string
    | Staged of SealedArtifactStage

/// Artifact bytes the provider fetched and checked, with the provenance the installer records.
[<Sealed>]
type FetchedExtensionArtifact
    internal (archive: FetchedArchive, record: Func<string list, CancellationToken, Task<Value * Value>>) =
    member internal _.Record = record

    /// Runs <paramref name="operation"/> with a function that opens the archive for reading. A staged download is
    /// read through the stage's own open handle: on Windows a second open by path is a sharing violation.
    member internal _.UseAsync<'T>(operation: Func<Func<Stream>, Task<'T>>, ct: CancellationToken) : Task<'T> =
        match archive with
        | ExistingFile path -> operation.Invoke(Func<Stream>(fun () -> FileSafety.openInput path :> Stream))
        | Staged stage ->
            stage.PublishAsync(
                Func<StageResources, Task<'T>>(fun resources ->
                    resources.Validate()

                    operation.Invoke(
                        Func<Stream>(fun () ->
                            resources.Output.Position <- 0L
                            new BorrowedStream(resources.Output) :> Stream)
                    )),
                Action(ignore),
                ct
            )

    override _.ToString() = "Fetched extension artifact."

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            match archive with
            | ExistingFile _ -> ValueTask.CompletedTask
            | Staged stage -> (stage :> IAsyncDisposable).DisposeAsync()

/// A source of extension releases. A provider only resolves and fetches. Validation, store publication,
/// catalog, lock and journal writes and activation stay in the one installer, the same for every provider.
type IExtensionSourceProvider =
    /// The scheme of the requests this provider reads, such as <c>github</c>.
    abstract Scheme: string
    /// Reads the release descriptor and selects the artifact for <paramref name="platform"/>.
    abstract ResolveAsync:
        request: ExtensionSourceRequest * platform: InstallationPlatform * ct: CancellationToken ->
            Task<InstallationResult<ResolvedExtensionRelease>>
    /// Brings the artifact bytes to a private local file and checks them against the descriptor digest.
    abstract FetchAsync:
        release: ResolvedExtensionRelease * home: string * ct: CancellationToken ->
            Task<InstallationResult<FetchedExtensionArtifact>>

module internal ProviderResults =
    let ofResult (result: InstallationResult<'T>) =
        result.Match(Func<'T, Result<'T, InstallationFailure>>(Ok), Func<InstallationFailure, Result<'T, InstallationFailure>>(Error))

    let unwrap result = ofResult result |> StateOwnership.unwrap

    let ok value = InstallationResult(Ok value)

    let readBounded (input: Stream) (maximum: int) (code: string) (ct: CancellationToken) = task {
        use output = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 65536
        let mutable reading = true

        while reading do
            let! count = input.ReadAsync(buffer, 0, buffer.Length, ct)

            if count = 0 then
                reading <- false
            else
                if output.Length + int64 count > int64 maximum then
                    Operations.fail code $"The document exceeds its {maximum} byte bound."

                output.Write(buffer, 0, count)

        return output.ToArray()
    }

/// The local developer provider: an explicit descriptor and archive inside a registered repository.
[<Sealed>]
type LocalRepositoryProvider() =
    static let maximumDescriptor = 8 * 1024 * 1024

    override _.ToString() = "local"

    interface IExtensionSourceProvider with
        member _.Scheme = "local"

        member _.ResolveAsync(request, platform, ct) =
            Operations.runAsync ct (fun () -> task {
                let declaration =
                    match request.Kind with
                    | Local declaration -> declaration
                    | GitHub _ -> Operations.fail "extension.provider_mismatch" "The local provider reads only local declarations."

                if not (Path.IsPathFullyQualified declaration.Home) then
                    Operations.fail "extension.invalid_home" "--home must be absolute."

                let! resolved = InstalledState.ResolveLocalRepositoryAsync(declaration.Home, declaration.Repository, ct)
                let sourceRoot = ProviderResults.unwrap resolved
                use repository = SafeDirectory.Open(sourceRoot, false)

                use release = FileSafety.openRelative repository (Path.GetFullPath declaration.Release)

                if release.Length > int64 maximumDescriptor then
                    Operations.fail "extension.local_refused" "Local declaration exceeds its 8 MiB bound."

                let! descriptor = ProviderResults.readBounded release maximumDescriptor "extension.local_refused" ct

                let plan = PackagePlan.Read(descriptor, platform) |> ProviderResults.unwrap

                if not (plan.Descriptor.ExtensionId.Equals declaration.ExpectedId) then
                    Operations.fail "extension.identity_mismatch" "Local release does not name the requested installed identity."

                if not (StringComparer.Ordinal.Equals(Path.GetFileName declaration.Archive, plan.Artifact.Filename.Text)) then
                    Operations.fail "extension.invalid_descriptor" "Archive filename differs from the selected descriptor artifact."

                use archive = FileSafety.openRelative repository (Path.GetFullPath declaration.Archive)

                if archive.Length > InstallationLimits.MaxArchiveBytes then
                    Operations.fail "extension.archive_limit" "Local archive exceeds its recorded bound."

                let digest, _ =
                    Copying.hashCopy archive Stream.Null InstallationLimits.MaxArchiveBytes "extension.archive_limit" ct

                if digest <> plan.ArchiveDigest.Value then
                    Operations.fail "extension.digest_mismatch" "Local archive differs from the release digest."

                archive.Position <- 0L
                ArchiveChecks.inspect archive ct |> ignore
                repository.ValidateLocation()

                return
                    ResolvedExtensionRelease(
                        descriptor,
                        plan,
                        LocalArchive(sourceRoot, Path.GetFullPath declaration.Archive)
                    )
            })

        member _.FetchAsync(release, _home, ct) =
            Operations.runAsync ct (fun () -> task {
                match release.Location with
                | LocalArchive(root, archive) ->
                    let record =
                        Func<string list, CancellationToken, Task<Value * Value>>(fun args ct ->
                            InstalledExtensions.PublishLocalAsync(root, release.Plan, archive, args, ct))

                    return new FetchedExtensionArtifact(ExistingFile archive, record)
                | Download _ ->
                    return Operations.fail "extension.provider_mismatch" "The local provider fetches only local archives."
            })

/// The GitHub release provider. The release carries the extension family <c>release.json</c> as an asset; each
/// artifact <c>filename</c> names another asset of the same release. Uses the anonymous GitHub API, or
/// <c>GITHUB_TOKEN</c> or <c>GH_TOKEN</c> when set.
[<Sealed>]
type GitHubReleaseProvider(http: HttpClient, apiBase: Uri, token: string | null) =
    static let maximumDocument = 8 * 1024 * 1024
    let token = Option.ofObj token

    static let environmentToken () : string | null =
        [ "GITHUB_TOKEN"; "GH_TOKEN" ]
        |> List.tryPick (fun name ->
            match Environment.GetEnvironmentVariable name with
            | null -> None
            | value when String.IsNullOrWhiteSpace value -> None
            | value -> Some value)
        |> Option.toObj

    let request (uri: Uri) (accept: string) =
        if uri.Scheme <> Uri.UriSchemeHttps && not apiBase.IsLoopback then
            Operations.fail "extension.github_refused" $"GitHub URL is not HTTPS: {uri}"

        if not (StringComparer.OrdinalIgnoreCase.Equals(uri.Host, apiBase.Host)) || uri.Port <> apiBase.Port then
            Operations.fail "extension.github_refused" $"GitHub URL is not on the API host: {uri}"

        let message = new HttpRequestMessage(HttpMethod.Get, uri)
        message.Headers.UserAgent.ParseAdd "morphir-extension-installer"
        message.Headers.Accept.ParseAdd accept
        message.Headers.Add("X-GitHub-Api-Version", "2022-11-28")

        token
        |> Option.iter (fun token -> message.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token))

        message

    let header (response: HttpResponseMessage) (name: string) =
        match response.Headers.TryGetValues name with
        | true, NonNull values -> Seq.tryHead values
        | _ -> None

    let refuse (response: HttpResponseMessage) (what: string) =
        let status = int response.StatusCode

        let limited =
            status = 429
            || (status = 403 && header response "x-ratelimit-remaining" = Some "0")

        if limited then
            let reset =
                header response "x-ratelimit-reset"
                |> Option.bind (fun text ->
                    match Int64.TryParse text with
                    | true, seconds -> Some(DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("u"))
                    | _ -> None)
                |> Option.map (fun time -> $" It resets at {time}.")
                |> Option.defaultValue ""

            Operations.fail
                "extension.github_rate_limited"
                $"The GitHub API rate limit is reached while reading {what}.{reset} Set GITHUB_TOKEN or GH_TOKEN to raise the limit."
        elif status = 404 then
            Operations.fail "extension.github_not_found" $"GitHub has no {what}."
        else
            Operations.fail "extension.github_http_error" $"GitHub answered {status} for {what}."

    let send (uri: Uri) (accept: string) (ct: CancellationToken) = task {
        use message = request uri accept

        try
            return! http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
        with :? HttpRequestException as error ->
            return Operations.fail "extension.github_unreachable" $"Cannot reach GitHub: {error.Message}"
    }

    /// The bytes of a release asset. The asset API URL returns its metadata unless the request asks for octet-stream.
    let assetBytes (uri: Uri) (what: string) (ct: CancellationToken) = task {
        use! response = send uri "application/octet-stream" ct

        if not response.IsSuccessStatusCode then
            refuse response what

        use! body = response.Content.ReadAsStreamAsync ct
        let! bytes = ProviderResults.readBounded body maximumDocument "extension.github_refused" ct
        return bytes
    }

    /// The release metadata, or None on 404 so the caller can try the next tag spelling.
    let release (repository: GitHubRepository) (path: string) (what: string) (ct: CancellationToken) = task {
        let uri = Uri(apiBase, $"repos/{repository.Owner}/{repository.Name}/releases/{path}")
        use! response = send uri "application/vnd.github+json" ct

        if response.StatusCode = HttpStatusCode.NotFound then
            return None
        else
            if not response.IsSuccessStatusCode then
                refuse response what

            use! body = response.Content.ReadAsStreamAsync ct
            let! bytes = ProviderResults.readBounded body maximumDocument "extension.github_refused" ct

            return
                match Syntax.parseJson bytes with
                | Ok value -> Some value
                | Error _ -> Operations.fail "extension.github_refused" $"GitHub returned malformed JSON for {what}."
    }

    let releaseFor (repository: GitHubRepository) selector ct = task {
        let byTag (tag: string) =
            release repository $"tags/{Uri.EscapeDataString tag}" $"release tagged {tag} in {repository.Text}" ct

        match selector with
        | GitHubReleaseSelector.Latest ->
            match! release repository "latest" $"latest release in {repository.Text}" ct with
            | Some value -> return value
            | None -> return Operations.fail "extension.github_not_found" $"{repository.Text} has no published release."
        | GitHubReleaseSelector.Tag tag ->
            match! byTag tag with
            | Some value -> return value
            | None -> return Operations.fail "extension.github_not_found" $"{repository.Text} has no release tagged {tag}."
        | GitHubReleaseSelector.Version version ->
            match! byTag $"v{version.Text}" with
            | Some value -> return value
            | None ->
                match! byTag version.Text with
                | Some value -> return value
                | None ->
                    return
                        Operations.fail
                            "extension.github_not_found"
                            $"{repository.Text} has no release tagged v{version.Text} or {version.Text}."
    }

    let asset (assets: (string * (Uri * uint64)) list) (name: string) =
        assets |> List.tryFind (fun (candidate, _) -> StringComparer.Ordinal.Equals(candidate, name)) |> Option.map snd

    /// Uses <see cref="HttpClient"/> with <c>https://api.github.com/</c> and a token from the environment.
    new(http: HttpClient) = GitHubReleaseProvider(http, Uri "https://api.github.com/", environmentToken ())

    override _.ToString() = "github"

    interface IExtensionSourceProvider with
        member _.Scheme = "github"

        member _.ResolveAsync(source, platform, ct) =
            Operations.runAsync ct (fun () -> task {
                let repository, selector =
                    match source.Kind with
                    | GitHub(repository, selector) -> repository, selector
                    | Local _ -> Operations.fail "extension.provider_mismatch" "The GitHub provider reads only github: sources."

                let! metadata = releaseFor repository selector ct

                let parsedRelease =
                    SharedJson.protect (fun () ->
                        let items = SharedJson.members "release" metadata
                        let tag = SharedJson.required "release" "tag_name" items |> SharedJson.text "release.tag_name"

                        let draft =
                            SharedJson.optional "draft" items
                            |> Option.map (SharedJson.boolean "release.draft")
                            |> Option.defaultValue false

                        let assets =
                            SharedJson.required "release" "assets" items
                            |> SharedJson.array "release.assets"
                            |> List.map (fun value ->
                                let fields = SharedJson.members "release.assets" value
                                let name = SharedJson.required "asset" "name" fields |> SharedJson.text "asset.name"
                                let url = SharedJson.required "asset" "url" fields |> SharedJson.text "asset.url"

                                let size =
                                    match SharedJson.required "asset" "size" fields with
                                    | Value.Number spelling ->
                                        match UInt64.TryParse spelling with
                                        | true, size -> size
                                        | _ -> SharedJson.invalid "asset.size" "Not a byte count."
                                    | _ -> SharedJson.invalid "asset.size" "Not a byte count."

                                name, (Uri url, size))

                        tag, draft, assets)

                let tag, draft, assets =
                    match parsedRelease with
                    | Ok value -> value
                    | Error failure -> Operations.fail "extension.github_refused" $"GitHub release metadata: {failure.Message}"

                if draft then
                    Operations.fail "extension.github_refused" $"The release {tag} of {repository.Text} is a draft."

                if not (SourceDocuments.validTag tag) then
                    Operations.fail "extension.github_refused" $"GitHub returned a tag that is not a Git tag: {tag}"

                let descriptorUri, _ =
                    asset assets "release.json"
                    |> Option.defaultWith (fun () ->
                        Operations.fail
                            "extension.release_descriptor_missing"
                            $"The release {tag} of {repository.Text} has no release.json asset.")

                let! descriptor = assetBytes descriptorUri $"release.json of {repository.Text} {tag}" ct

                let plan = PackagePlan.Read(descriptor, platform) |> ProviderResults.unwrap

                match selector with
                | GitHubReleaseSelector.Version version when plan.Descriptor.Version.Text <> version.Text ->
                    Operations.fail
                        "extension.version_mismatch"
                        $"The release {tag} describes version {plan.Descriptor.Version.Text}, not {version.Text}."
                | _ -> ()

                let filename = plan.Artifact.Filename.Text

                let uri, length =
                    asset assets filename
                    |> Option.defaultWith (fun () ->
                        Operations.fail
                            "extension.artifact_missing"
                            $"The release {tag} of {repository.Text} has no asset {filename} for {platform.DescriptorTriple}.")

                let provenance = IndexProvenance.GitHubRelease(repository, tag, Sha256Digest.Create(Digest.ofBytes descriptor))
                let source = ArtifactSource.GitHubReleaseAsset(repository, tag, filename)
                return ResolvedExtensionRelease(descriptor, plan, Download(uri, length, provenance, source))
            })

        member _.FetchAsync(resolved, home, ct) =
            Operations.runAsync ct (fun () -> task {
                let uri, length, provenance, source =
                    match resolved.Location with
                    | Download(uri, length, provenance, source) -> uri, length, provenance, source
                    | LocalArchive _ -> Operations.fail "extension.provider_mismatch" "The GitHub provider fetches only downloads."

                let! begun =
                    PrivateStaging.BeginAsync(
                        home,
                        length,
                        resolved.Plan.ArchiveDigest,
                        uint64 InstallationLimits.MaxArtifactBytes,
                        ct
                    )

                let stage = ProviderResults.unwrap begun
                let mutable owned = true

                try
                    use! response = send uri "application/octet-stream" ct

                    if not response.IsSuccessStatusCode then
                        refuse response $"asset {SourceDocuments.filenameOf source}"

                    use! body = response.Content.ReadAsStreamAsync ct
                    let buffer = Array.zeroCreate<byte> 65536
                    let mutable reading = true

                    while reading do
                        let! count = body.ReadAsync(buffer, 0, buffer.Length, ct)

                        if count = 0 then
                            reading <- false
                        else
                            do! stage.WriteAsync(ReadOnlyMemory(buffer, 0, count), ct)

                    let! sealedResult = stage.SealAsync ct
                    let sealedStage = ProviderResults.unwrap sealedResult
                    owned <- false

                    let values = SourceDocuments.provenanceValue provenance, SourceDocuments.sourceValue source

                    return
                        new FetchedExtensionArtifact(
                            Staged sealedStage,
                            Func<string list, CancellationToken, Task<Value * Value>>(fun _ _ -> Task.FromResult values)
                        )
                finally
                    if owned then
                        (stage :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
            })

/// The one entry point for every provider: resolve, fetch, then the installer.
[<AbstractClass; Sealed>]
type ExtensionSources private () =
    /// The provider for a request's scheme. Adding a source means adding a provider here and nothing else.
    static member ProviderFor(request: ExtensionSourceRequest, http: HttpClient) : IExtensionSourceProvider =
        match request.Kind with
        | Local _ -> LocalRepositoryProvider()
        | GitHub _ -> GitHubReleaseProvider(http)

    /// Resolves only, for a dry run. A GitHub source reads the release metadata and descriptor; no artifact is downloaded.
    static member ResolveAsync(provider: IExtensionSourceProvider, request: ExtensionSourceRequest, ct: CancellationToken) = task {
        match ActivationPlatforms.current () with
        | Error failure -> return InstallationResult(Error failure)
        | Ok platform -> return! provider.ResolveAsync(request, platform, ct)
    }

    /// Installs without probing: resolve, fetch and check the digest, then the shared installer.
    static member InstallAsync
        (
            home: string,
            provider: IExtensionSourceProvider,
            request: ExtensionSourceRequest,
            args: IReadOnlyList<string>,
            hostVersion: SemVer,
            ct: CancellationToken
        ) : Task<InstallationResult<CapturedExtension>> =
        task {
            if not (Path.IsPathFullyQualified home) then
                return InstallationResult(Error(InstallationFailure("extension.invalid_home", "--home must be absolute.")))
            else
                let! resolved = ExtensionSources.ResolveAsync(provider, request, ct)

                match ProviderResults.ofResult resolved with
                | Error failure -> return InstallationResult(Error failure)
                | Ok release ->
                    let! fetched = provider.FetchAsync(release, home, ct)

                    match ProviderResults.ofResult fetched with
                    | Error failure -> return InstallationResult(Error failure)
                    | Ok artifact ->
                        try
                            return!
                                artifact.UseAsync(
                                    Func<Func<Stream>, Task<InstallationResult<CapturedExtension>>>(fun openArchive ->
                                        InstalledExtensions.InstallPreparedAsync(
                                            home,
                                            release.Plan,
                                            openArchive,
                                            artifact.Record,
                                            args,
                                            hostVersion,
                                            Action<int>(ignore),
                                            ct
                                        )),
                                    ct
                                )
                        finally
                            (artifact :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
        }
