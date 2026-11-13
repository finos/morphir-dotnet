// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.SourceProviderTests

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
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

let private apiBase = Uri "http://127.0.0.1/api/"

type private Answer = { Status: HttpStatusCode; Headers: (string * string) list; Body: byte array }

/// Serves fixed answers by request path, records each request, and never touches the network.
type private FakeGitHub() =
    inherit HttpMessageHandler()
    let answers = ConcurrentDictionary<string, Answer>()
    member val Requests = ConcurrentQueue<HttpRequestMessage>()
    member _.Answer(path: string, answer: Answer) = answers[path] <- answer
    member this.Json(path: string, json: string) = this.Answer(path, { Status = HttpStatusCode.OK; Headers = []; Body = Encoding.UTF8.GetBytes json })
    member this.Bytes(path: string, bytes: byte array) = this.Answer(path, { Status = HttpStatusCode.OK; Headers = []; Body = bytes })

    override this.SendAsync(request, _ct) =
        this.Requests.Enqueue request
        let path = (nonNull request.RequestUri).AbsolutePath

        let answer =
            match answers.TryGetValue path with
            | true, answer -> answer
            | _ -> { Status = HttpStatusCode.NotFound; Headers = []; Body = Encoding.UTF8.GetBytes "{\"message\":\"Not Found\"}" }

        let response = new HttpResponseMessage(answer.Status, Content = new ByteArrayContent(answer.Body))

        for name, value in answer.Headers do
            response.Headers.TryAddWithoutValidation(name, value) |> ignore

        Task.FromResult response

/// A GitHub release of the host-platform package fixture: release metadata, release.json and the archive.
type private Release = {
    Github: FakeGitHub
    Provider: IExtensionSourceProvider
    Plan: PackagePlan
    Descriptor: byte array
    Archive: byte array
    Version: string
}

let private assetJson (name: string) (id: int) (size: int) =
    $"{{\"name\":\"{name}\",\"size\":{size},\"url\":\"{apiBase}repos/finos/morphir-python/releases/assets/{id}\",\"browser_download_url\":\"https://github.com/finos/morphir-python/releases/download/x/{name}\"}}"

let private releaseJson (tag: string) (assets: string list) =
    let assets = String.Join(",", assets)
    $"{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":false,\"assets\":[{assets}]}}"

let private publish (sandbox: TestSandbox) (tag: string) (path: string) (token: string option) =
    let plan, archivePath, _ = PackageTests.fixtureForHost sandbox
    let descriptor = JsonText.toBytes plan.Descriptor.Json
    let archive = File.ReadAllBytes archivePath
    let github = new FakeGitHub()
    let filename = plan.Artifact.Filename.Text

    github.Json(
        $"/api/repos/finos/morphir-python/releases/{path}",
        releaseJson tag [ assetJson "release.json" 1 descriptor.Length; assetJson filename 2 archive.Length ]
    )

    github.Bytes("/api/repos/finos/morphir-python/releases/assets/1", descriptor)
    github.Bytes("/api/repos/finos/morphir-python/releases/assets/2", archive)
    let provider = GitHubReleaseProvider(new HttpClient(github), apiBase, Option.toObj token) :> IExtensionSourceProvider

    {
        Github = github
        Provider = provider
        Plan = plan
        Descriptor = descriptor
        Archive = archive
        Version = plan.Descriptor.Version.Text
    }

/// Serves the same release metadata at another API path, such as a tag spelling.
let private serve (release: Release) (tag: string) (path: string) =
    release.Github.Json(
        $"/api/repos/finos/morphir-python/releases/{path}",
        releaseJson tag [ assetJson "release.json" 1 release.Descriptor.Length; assetJson release.Plan.Artifact.Filename.Text 2 release.Archive.Length ]
    )

let private request text =
    match ExtensionSourceRequest.Parse text with
    | Ok request -> request
    | Error failure -> failtestf "Bad request %s: %O" text failure

let private install (sandbox: TestSandbox) (release: Release) text =
    ExtensionSources
        .InstallAsync(home sandbox, release.Provider, request text, Array.AsReadOnly [||], host, ct)
        .GetAwaiter()
        .GetResult()

let private lockText (sandbox: TestSandbox) (release: Release) =
    File.ReadAllText(Path.Combine(home sandbox, "locks", "extensions", $"{release.Plan.Descriptor.ExtensionId.Value}.json"))

let private withRelease tag path action =
    use sandbox = TestSandbox.Create()
    let cleanup = PackageTests.packageCleanup sandbox

    try
        action sandbox (publish sandbox tag path None)
    finally
        cleanup.DisposeAsync().GetAwaiter().GetResult()

let private parsing =
    testList "ExtensionSourceRequest" [
        for text, expected in
            [
                "github:finos/morphir-python", "github"
                "github:finos/morphir-python@0.6.2", "github"
                "github:finos/morphir-python@v0.6.2", "github"
                "github:finos/morphir-python@extension/elm/v0.5.0", "github"
            ] do
            test $"parses {text}" { Expect.equal (request text).Scheme expected "scheme" }
        for text, code in
            [
                "finos/morphir-python", "extension.invalid_source"
                "oci:ghcr.io/finos/x", "extension.invalid_source"
                "github:finos", "extension.invalid_source"
                "github:finos/morphir python", "extension.invalid_source"
                "github:finos/../x", "extension.invalid_source"
                "github:finos/morphir-python@", "extension.invalid_source"
                "github:finos/morphir-python@a b", "extension.invalid_source"
            ] do
            test $"refuses {text}" {
                match ExtensionSourceRequest.Parse text with
                | Ok _ -> failtest "Expected refusal"
                | Error failure -> Expect.equal failure.Code code "code"
            }
    ]

[<Tests>]
let tests =
    testLabel "PlatformSpecific" <| testList "SourceProviders" [
        parsing
        test "a GitHub release installs through the one installer and records GitHub provenance" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let release = publish sandbox "unused" "unused" None
                serve release $"v{release.Version}" $"tags/v{release.Version}"
                use captured = install sandbox release $"github:finos/morphir-python@{release.Version}" |> get
                Expect.equal captured.Record.Version.Text release.Version "version"
                Expect.equal captured.Record.ArchiveDigest release.Plan.ArchiveDigest "archive digest"
                let lock = nonNull (JsonNode.Parse(lockText sandbox release))
                let index = nonNull lock["index"]
                let source = nonNull lock["source"]
                Expect.equal (string index["kind"]) "github-release" "provenance kind"
                Expect.equal (string index["repository"]) "finos/morphir-python" "repository"
                Expect.equal (string index["tag"]) $"v{release.Version}" "tag"
                Expect.equal (string index["revision"]) (Digest.ofBytes release.Descriptor) "revision is the release.json digest"
                Expect.equal (string source["kind"]) "github-release-asset" "source kind"
                Expect.equal (string source["asset"]) release.Plan.Artifact.Filename.Text "asset"

                use reread =
                    InstalledExtensions.CaptureAsync(home sandbox, release.Plan.Descriptor.ExtensionId, host, ct).GetAwaiter().GetResult()
                    |> get

                Expect.equal reread.Record.TreeDigest release.Plan.TreeDigest "the installed lock reads back"

                let stages =
                    let tmp = Path.Combine(home sandbox, "tmp")
                    if Directory.Exists tmp then Directory.GetDirectories(tmp, "morphir-dotnet-stage-*") else [||]

                Expect.isEmpty stages "the private download stage is removed"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "a local declaration installs through the same installer and records local provenance" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let plan, archivePath, _ = PackageTests.fixtureForHost sandbox
                let repository = Path.Combine(sandbox.Root, "local-repository")
                Directory.CreateDirectory repository |> ignore
                let release = Path.Combine(repository, "release.json")
                File.WriteAllBytes(release, JsonText.toBytes plan.Descriptor.Json)
                let archive = Path.Combine(repository, plan.Artifact.Filename.Text)
                File.Copy(archivePath, archive)

                InstalledState.RegisterLocalRepositoryAsync(home sandbox, "local-one", repository, false, ct).GetAwaiter().GetResult()
                |> get
                |> ignore

                let request = ExtensionSourceRequest.Local(home sandbox, "local-one", release, archive, plan.Descriptor.ExtensionId)
                Expect.equal request.Scheme "local" "scheme"

                use captured =
                    ExtensionSources
                        .InstallAsync(home sandbox, LocalRepositoryProvider(), request, Array.AsReadOnly [||], host, ct)
                        .GetAwaiter()
                        .GetResult()
                    |> get

                Expect.equal captured.Record.ArchiveDigest plan.ArchiveDigest "archive digest"
                let lock = nonNull (JsonNode.Parse(File.ReadAllText(Path.Combine(home sandbox, "locks", "extensions", $"{plan.Descriptor.ExtensionId.Value}.json"))))
                let index = nonNull lock["index"]
                let source = nonNull lock["source"]
                Expect.equal (string index["kind"]) "local-directory" "provenance kind"
                Expect.equal (string source["kind"]) "local-file" "source kind"
                Expect.equal (string source["path"]) $"artifacts/{plan.Artifact.Filename.Text}" "source path"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "the GitHub provider refuses a local declaration and the local provider refuses a github source" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                let local = ExtensionSourceRequest.Local(home sandbox, "x", "/r.json", "/a.zip", release.Plan.Descriptor.ExtensionId)
                ExtensionSources.ResolveAsync(release.Provider, local, ct).GetAwaiter().GetResult()
                |> refused "extension.provider_mismatch"
                ExtensionSources.ResolveAsync(LocalRepositoryProvider(), request "github:finos/morphir-python", ct).GetAwaiter().GetResult()
                |> refused "extension.provider_mismatch")
        }
        test "a version without a v tag falls back to the bare version tag" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let release = publish sandbox "unused" "unused" None
                serve release release.Version $"tags/{release.Version}"
                use _ = install sandbox release $"github:finos/morphir-python@{release.Version}" |> get

                let paths = release.Github.Requests |> Seq.map (fun r -> (nonNull r.RequestUri).AbsolutePath) |> List.ofSeq
                Expect.contains paths $"/api/repos/finos/morphir-python/releases/tags/v{release.Version}" "tries v<version> first"
                Expect.contains paths $"/api/repos/finos/morphir-python/releases/tags/{release.Version}" "then <version>"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
        test "no version selects the latest release" {
            withRelease "v9.9.9" "latest" (fun sandbox release ->
                use _ = install sandbox release "github:finos/morphir-python" |> get
                Expect.stringContains (lockText sandbox release) "\"tag\": \"v9.9.9\"" "the latest tag is recorded")
        }
        test "a release without release.json is refused before any download" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                release.Github.Json("/api/repos/finos/morphir-python/releases/latest", releaseJson "v1.0.0" [ assetJson "other.zip" 9 3 ])
                install sandbox release "github:finos/morphir-python" |> refused "extension.release_descriptor_missing"
                Expect.isFalse (Directory.Exists(Path.Combine(home sandbox, "store"))) "nothing installed")
        }
        test "an artifact whose bytes differ from the descriptor digest is refused and nothing is installed" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                let tampered = Array.copy release.Archive
                tampered[tampered.Length - 1] <- tampered[tampered.Length - 1] ^^^ 0xFFuy
                release.Github.Bytes("/api/repos/finos/morphir-python/releases/assets/2", tampered)
                install sandbox release "github:finos/morphir-python" |> refused "extension.digest_mismatch"
                Expect.isFalse (Directory.Exists(Path.Combine(home sandbox, "store"))) "nothing installed"
                Expect.isFalse (Directory.Exists(Path.Combine(home sandbox, "locks", "extensions"))) "no lock")
        }
        test "a release with no artifact for this platform is refused" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                let node = nonNull (JsonNode.Parse(Encoding.UTF8.GetString release.Descriptor))
                let artifact = nonNull ((nonNull node["artifacts"])[0])
                artifact["platform"] <- JsonValue.Create "unknown-unknown-unknown"
                let foreign = Encoding.UTF8.GetBytes(node.ToJsonString())
                release.Github.Bytes("/api/repos/finos/morphir-python/releases/assets/1", foreign)
                let result = install sandbox release "github:finos/morphir-python"
                Expect.isTrue (result.Match((fun _ -> false), (fun _ -> true))) "refused"
                Expect.isFalse (Directory.Exists(Path.Combine(home sandbox, "store"))) "nothing installed")
        }
        test "a release whose descriptor names a missing asset is refused" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                release.Github.Json(
                    "/api/repos/finos/morphir-python/releases/latest",
                    releaseJson "v1.0.0" [ assetJson "release.json" 1 release.Descriptor.Length ]
                )

                install sandbox release "github:finos/morphir-python" |> refused "extension.artifact_missing")
        }
        test "a requested version that the descriptor does not describe is refused" {
            withRelease "v7.7.7" "tags/v7.7.7" (fun sandbox release ->
                install sandbox release "github:finos/morphir-python@7.7.7" |> refused "extension.version_mismatch")
        }
        test "an unknown repository is not found" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                install sandbox release "github:finos/missing@1.0.0" |> refused "extension.github_not_found")
        }
        test "a rate limit names the token variables" {
            withRelease "v1.0.0" "latest" (fun sandbox release ->
                release.Github.Answer(
                    "/api/repos/finos/morphir-python/releases/latest",
                    {
                        Status = HttpStatusCode.Forbidden
                        Headers = [ "x-ratelimit-remaining", "0"; "x-ratelimit-reset", "1900000000" ]
                        Body = Encoding.UTF8.GetBytes "{}"
                    }
                )

                let result = install sandbox release "github:finos/morphir-python"
                result |> refused "extension.github_rate_limited"
                result.Match((fun _ -> ()), (fun failure -> Expect.stringContains failure.Message "GITHUB_TOKEN" "hint")))
        }
        test "an asset URL off the API host is refused, so a token never leaves the API host" {
            use sandbox = TestSandbox.Create()
            let cleanup = PackageTests.packageCleanup sandbox

            try
                let release = publish sandbox "v1.0.0" "latest" (Some "secret")

                release.Github.Json(
                    "/api/repos/finos/morphir-python/releases/latest",
                    releaseJson "v1.0.0" [ "{\"name\":\"release.json\",\"size\":3,\"url\":\"https://example.org/release.json\"}" ]
                )

                install sandbox release "github:finos/morphir-python" |> refused "extension.github_refused"

                for sent in release.Github.Requests do
                    Expect.equal (nonNull sent.RequestUri).Host "127.0.0.1" "only the API host is contacted"
                    Expect.equal (string sent.Headers.Authorization) "Bearer secret" "the token goes to the API host"
            finally
                cleanup.DisposeAsync().GetAwaiter().GetResult()
        }
    ]
