// Spike probe: does each NuGet client operation a NuGet extension source needs work in a Native AOT binary?
// Each step prints one line: PASS or FAIL, the step name and what it saw. The exit code is the failure count.
//
//   NuGetProbe --feed <folder feed> --work <empty folder> [--online] [--plugin <credential provider plugin path>]
module NuGetProbe.Program

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open System.Xml.Linq

let mutable failures = 0

let step (name: string) (body: unit -> Task<string>) = task {
    try
        let! seen = body ()
        Console.WriteLine("PASS " + name + ": " + seen)
    with error ->
        failures <- failures + 1
        let error = match error with :? AggregateException as a when not (isNull a.InnerException) -> a.InnerException | e -> e
        Console.WriteLine("FAIL " + name + ": " + error.GetType().FullName + ": " + error.Message.Replace('\n', ' '))
        let mutable inner = error.InnerException
        while not (isNull inner) do
            Console.WriteLine("     caused by " + inner.GetType().FullName + ": " + inner.Message.Replace('\n', ' '))
            inner <- inner.InnerException
}

let sha256 (stream: Stream) = Convert.ToHexStringLower(SHA256.HashData stream)

let argument (args: string array) (name: string) =
    match Array.tryFindIndex ((=) name) args with
    | Some index when index + 1 < args.Length -> Some args[index + 1]
    | _ -> None

/// The descriptor artifacts: the same reader for every level, System.Text.Json only.
let artifacts (json: Stream) =
    use document = JsonDocument.Parse json
    [ for artifact in document.RootElement.GetProperty("artifacts").EnumerateArray() ->
          artifact.GetProperty("runtime").GetString(), artifact.GetProperty("filename").GetString() ]

#if NUGET_CORE
open NuGet.Configuration
open NuGet.Packaging
open NuGet.Packaging.Signing
open NuGet.Versioning

let mainId = "Acme.Morphir.Python"

/// A package from nuget.org in the local NuGet cache: it carries the nuget.org repository signature.
let signedPackage =
    Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget/packages/nuget.versioning/7.9.0/nuget.versioning.7.9.0.nupkg")

let writeConfig (work: string) (feed: string) =
    let path = Path.Combine(work, "nuget.config")
    File.WriteAllText(
        path,
        $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spike" value="{feed}" />
    <add key="private" value="https://pkgs.example.invalid/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="spike"><package pattern="Acme.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <private>
      <add key="Username" value="spike-user" />
      <add key="ClearTextPassword" value="spike-secret" />
    </private>
  </packageSourceCredentials>
</configuration>"""
    )

let core (feed: string) (work: string) (ct: CancellationToken) = task {
    do! step "versioning.parse" (fun () -> task {
        let version = NuGetVersion.Parse "1.2.3-Beta.1+build.5"
        let range = VersionRange.Parse "[1.0.0, 2.0.0)"
        return $"normalized {version.ToNormalizedString()}, full {version.ToFullString()}, in [1,2): {range.Satisfies version}"
    })

    writeConfig work feed

    do! step "configuration.sources" (fun () -> task {
        let settings = Settings.LoadDefaultSettings work
        let sources = PackageSourceProvider(settings).LoadPackageSources() |> Seq.filter _.IsEnabled |> Seq.toList
        let names = sources |> List.map (fun source -> source.Name) |> String.concat ","
        let credential = sources |> List.find (fun source -> source.Name = "private") |> _.Credentials

        let user =
            match credential with
            | null -> "none"
            | credential -> $"{credential.Username} (clear text {credential.IsPasswordClearText}, length {credential.Password.Length})"

        return $"sources {names}; private credential {user}"
    })

    do! step "configuration.source-mapping" (fun () -> task {
        let mapping = PackageSourceMapping.GetPackageSourceMapping(Settings.LoadDefaultSettings work)
        let acme = mapping.GetConfiguredPackageSources mainId |> String.concat ","
        let other = mapping.GetConfiguredPackageSources "Newtonsoft.Json" |> String.concat ","
        return $"enabled {mapping.IsEnabled}; {mainId} -> {acme}; Newtonsoft.Json -> {other}"
    })

    let mainPath = Path.Combine(feed, $"{mainId}.1.2.3.nupkg")

    do! step "packaging.main-package" (fun () -> task {
        use reader = new PackageArchiveReader(mainPath)
        let nuspec = reader.NuspecReader
        let types = nuspec.GetPackageTypes() |> Seq.map _.Name |> String.concat ","
        use release = reader.GetStream "morphir/release.json"
        let described = artifacts release |> List.map (fun (runtime, filename) -> $"{runtime}:{filename}") |> String.concat " "

        let settings =
            reader.GetFiles() |> Seq.find (fun file -> file.EndsWith("/DotnetToolSettings.xml", StringComparison.Ordinal))

        use settingsStream = reader.GetStream settings
        let document = XDocument.Load settingsStream

        let ridPackages =
            document.Descendants(XName.Get "RuntimeIdentifierPackage")
            |> Seq.map (fun element -> element.Attribute(XName.Get "RuntimeIdentifier").Value + "=" + element.Attribute(XName.Get "Id").Value)
            |> String.concat ","

        return $"{nuspec.GetId()} {nuspec.GetVersion().ToNormalizedString()} types {types}; artifacts {described}; {settings} {ridPackages}"
    })

    do! step "packaging.rid-package-subtree" (fun () -> task {
        let path = Path.Combine(feed, $"{mainId}.osx-arm64.1.2.3.nupkg")
        use reader = new PackageArchiveReader(path)
        let root = "tools/net11.0/osx-arm64/"
        let files = reader.GetFiles() |> Seq.toList
        let inside, outside = files |> List.partition (fun file -> file.StartsWith(root, StringComparison.Ordinal))

        // A stand-in for treeSha256: the digest of the sorted "<relative path> <sha256>" lines of the subtree.
        let lines =
            inside
            |> List.sort
            |> List.map (fun file ->
                use entry = reader.GetStream file
                file.Substring(root.Length) + " " + sha256 entry)

        let tree = Convert.ToHexStringLower(SHA256.HashData(Text.Encoding.UTF8.GetBytes(String.concat "\n" lines)))
        let types = reader.NuspecReader.GetPackageTypes() |> Seq.map _.Name |> String.concat ","
        let fileBytes = using (File.OpenRead path) sha256
        let outside = String.concat "," outside
        return $"types {types}; {inside.Length} files under root, outside root: {outside}; tree {tree.Substring(0, 12)}; file {fileBytes.Substring(0, 12)}"
    })

    do! step "packaging.signature-read" (fun () -> task {
        use reader = new PackageArchiveReader(signedPackage)
        let! isSigned = reader.IsSignedAsync ct
        let! primary = reader.GetPrimarySignatureAsync ct
        let countersigned = not (isNull (RepositoryCountersignature.GetRepositoryCountersignature primary))
        do! reader.ValidateIntegrityAsync(primary.SignatureContent, ct)
        return $"signed {isSigned}; primary {primary.Type}; repository countersignature {countersigned}; content hash matches signature"
    })

    do! step "packaging.signature-verify" (fun () -> task {
        use reader = new PackageArchiveReader(signedPackage)
        let providers: ISignatureVerificationProvider list =
            [ IntegrityVerificationProvider(); SignatureTrustAndValidityVerificationProvider() ]
        let verifier = PackageSignatureVerifier(providers)
        let! result = verifier.VerifySignaturesAsync(reader, SignedPackageVerifierSettings.GetVerifyCommandDefaultPolicy(), ct)
        let issues = result.Results |> Seq.collect (fun r -> r.Issues) |> Seq.map (fun i -> $"{i.Code} {i.Level}") |> String.concat ","
        return $"valid {result.IsValid}; issues [{issues}]"
    })
}
#endif

#if NUGET_PROTOCOL
open NuGet.Common
open NuGet.Protocol
open NuGet.Protocol.Core.Types
open NuGet.Protocol.Plugins
open NuGet.Credentials

/// NuGet log messages on stderr, so plugin failures are visible.
type StderrLogger() =
    inherit LoggerBase(LogLevel.Information)
    override _.Log(message: ILogMessage) = Console.Error.WriteLine("nuget " + string message.Level + ": " + message.Message)
    override this.LogAsync(message: ILogMessage) = this.Log message; Task.CompletedTask

let protocol (feed: string) (online: bool) (plugin: string option) (ct: CancellationToken) = task {
    use cache = new SourceCacheContext(NoCache = true)

    do! step "protocol.local-feed" (fun () -> task {
        let repository = Repository.Factory.GetCoreV3 feed
        let! finder = repository.GetResourceAsync<FindPackageByIdResource> ct
        let! versions = finder.GetAllVersionsAsync(mainId, cache, NullLogger.Instance, ct)
        let latest = versions |> Seq.filter (fun v -> not v.IsPrerelease) |> Seq.max
        use output = new MemoryStream()
        let! copied = finder.CopyNupkgToStreamAsync(mainId, latest, output, cache, NullLogger.Instance, ct)
        output.Position <- 0L
        use reader = new PackageArchiveReader(output, true)
        let versions = versions |> Seq.map string |> String.concat ","
        return $"{finder.GetType().Name}; versions {versions}; copied {copied}, {output.Length} bytes, id {reader.NuspecReader.GetId()}"
    })

    do! step "credentials.plugin-service" (fun () -> task {
        // With --plugin, NuGet finds only that plugin. Without it, only an empty folder, so no installed provider starts.
        let plugins =
            match plugin with
            | Some path -> path
            | None ->
                let empty = Path.Combine(Path.GetTempPath(), "nuget-probe-plugins-" + string Environment.ProcessId)
                Directory.CreateDirectory empty |> ignore
                empty

        // --plugin path: NUGET_PLUGIN_PATHS names that plugin. --plugin PATH: no NUGET_PLUGIN_PATHS, so NuGet also
        // looks for nuget-plugin-* executables on PATH (the caller puts one there).
        if plugin <> Some "PATH" then
            Environment.SetEnvironmentVariable("NUGET_PLUGIN_PATHS", plugins)
        // The Azure Artifacts Credential Provider answers from this variable for any listed endpoint, with no sign-in.
        let endpoint = "https://pkgs.example.invalid/v3/index.json"
        Environment.SetEnvironmentVariable(
            "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS",
            $"""{{"endpointCredentials":[{{"endpoint":"{endpoint}","username":"spike-user","password":"spike-token"}}]}}"""
        )

        let watch = Diagnostics.Stopwatch.StartNew()
        let builder = SecurePluginCredentialProviderBuilder(PluginManager.Instance, false, StderrLogger())
        let! providers = builder.BuildAllAsync()
        let providers = Seq.toList providers
        let service =
            CredentialService(AsyncLazy<seq<ICredentialProvider>>(fun () -> Task.FromResult(Seq.ofList providers)), true, false)
        let! credentials = service.GetCredentialsAsync(Uri endpoint, null, CredentialRequestType.Unauthorized, "probe", ct)

        let found =
            match credentials with
            | null -> "none"
            | credentials ->
                match credentials.GetCredential(Uri endpoint, "Basic") with
                | null -> "no basic credential"
                | basic -> $"{basic.UserName}, password length {basic.Password.Length}"

        return $"{providers.Length} plugin providers; credentials {found}; {watch.ElapsedMilliseconds} ms"
    })

    if online then
        let nugetOrg = Repository.Factory.GetCoreV3 "https://api.nuget.org/v3/index.json"

        do! step "protocol.http-service-index" (fun () -> task {
            let! index = nugetOrg.GetResourceAsync<ServiceIndexResourceV3> ct
            let flat = index.GetServiceEntryUri(ServiceTypes.PackageBaseAddress)
            return $"{index.Entries.Count} entries; PackageBaseAddress {flat}"
        })

        do! step "protocol.http-flat-container" (fun () -> task {
            let! finder = nugetOrg.GetResourceAsync<FindPackageByIdResource> ct
            let! versions = finder.GetAllVersionsAsync("NuGet.Versioning", cache, NullLogger.Instance, ct)
            use output = new MemoryStream()
            let! copied = finder.CopyNupkgToStreamAsync("NuGet.Versioning", NuGetVersion.Parse "7.9.0", output, cache, NullLogger.Instance, ct)
            output.Position <- 0L
            let downloaded = sha256 output
            let cached = using (File.OpenRead signedPackage) sha256
            return $"{finder.GetType().Name}; {Seq.length versions} versions; copied {copied}; same bytes as the local cache {downloaded = cached}"
        })

        do! step "protocol.http-registration-metadata" (fun () -> task {
            let! metadata = nugetOrg.GetResourceAsync<PackageMetadataResource> ct
            let! found = metadata.GetMetadataAsync("NuGet.Versioning", false, false, cache, NullLogger.Instance, ct)
            let latest = found |> Seq.maxBy (fun m -> m.Identity.Version)
            return $"{Seq.length found} stable versions; latest {latest.Identity.Version}; description length {latest.Description.Length}"
        })
}
#endif

[<EntryPoint>]
let main args =
    let feed = argument args "--feed" |> Option.map Path.GetFullPath
    let work = argument args "--work" |> Option.map Path.GetFullPath
    let online = Array.contains "--online" args
    let plugin = argument args "--plugin" |> Option.map (fun path -> if path = "PATH" then path else Path.GetFullPath path)
    use cancel = new CancellationTokenSource(TimeSpan.FromMinutes 5.0)

    match feed, work with
    | Some feed, Some work ->
        Directory.CreateDirectory work |> ignore

        (task {
            do! step "baseline.descriptor-json" (fun () -> task {
                let path = Directory.GetFiles(feed, "*.nupkg") |> Array.head
                use zip = IO.Compression.ZipFile.OpenRead path
                use release = zip.GetEntry("morphir/release.json").Open()
                return $"{List.length (artifacts release)} artifacts read with System.IO.Compression and System.Text.Json"
            })
#if NUGET_CORE
            do! core feed work cancel.Token
#endif
#if NUGET_PROTOCOL
            do! protocol feed online plugin cancel.Token
#endif
        })
            .GetAwaiter()
            .GetResult()

        failures
    | _ ->
        Console.Error.WriteLine "usage: NuGetProbe --feed <folder feed> --work <empty folder> [--online] [--plugin <path>]"
        64
