// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Extensions.Installation.Tests.NuGetProviderTests

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json.Nodes
open Expecto
open Morphir.Extensions.Installation
open Morphir.Toolchain
open Morphir.Mep
open Morphir.TestSupport
open IoTestSupport

let private host =
    SemVer.tryParse "0.4.0-alpha.4"
    |> function
        | Ok value -> value
        | Error _ -> failtest "literal"

let private utf8 (text: string) = Encoding.UTF8.GetBytes text

let private platform () =
    ActivationPlatforms.current () |> StateOwnership.unwrap

let private entryPoint () =
    if OperatingSystem.IsWindows() then "acme-python.exe" else "acme-python"

/// One file of a fixture package: its entry name and bytes.
type FixtureEntry = { Name: string; Bytes: byte array }

/// Fixture packages built in the test run: a .nupkg is a ZIP with a .nuspec, as dotnet pack writes it.
module NuGetFixtures =
    let nuspec (id: string) (version: string) (types: string list) =
        let packageTypes =
            match types with
            | [] -> ""
            | types ->
                let items = types |> List.map (fun name -> $"<packageType name=\"{name}\" />") |> String.concat ""
                $"<packageTypes>{items}</packageTypes>"

        $"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{id}</id>
    <version>{version}</version>
    <authors>Acme</authors>
    <description>A Morphir extension test package.</description>
    {packageTypes}
  </metadata>
</package>"""

    /// Writes <c>&lt;id&gt;.&lt;version&gt;.nupkg</c> in lower case into <paramref name="feed"/>.
    let pack (feed: string) (id: string) (version: string) (types: string list) (entries: FixtureEntry list) =
        Directory.CreateDirectory feed |> ignore
        let path = Path.Combine(feed, $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg")
        use file = File.Create path
        use zip = new ZipArchive(file, ZipArchiveMode.Create)

        let add (name: string) (bytes: byte array) =
            use output = zip.CreateEntry(name).Open()
            output.Write bytes

        add $"{id}.nuspec" (utf8 (nuspec id version types))
        add "[Content_Types].xml" (utf8 "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />")
        add "_rels/.rels" (utf8 "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />")

        for entry in entries do
            add entry.Name entry.Bytes

        path

    /// The tree digest of package files, by the package contract's canonical manifest.
    let treeDigest (files: (string * byte array * bool) list) =
        let facts =
            files
            |> List.map (fun (path, bytes, executable) ->
                PackageFile.Create(PackagePaths.read "package.path" path, int64 bytes.Length, Sha256Digest.Create(Digest.ofBytes bytes), executable)
                |> get)
            |> List.toArray

        let manifest = PackageManifest.FromFiles(Array.AsReadOnly facts) |> get
        Digest.ofBytes (manifest.ToBytes())

    let claims (version: string) =
        $"""{{"protocolVersions":["0.2.0-draft.1"],"extension":{{"id":"com.acme.python","name":"Acme Python","version":"{version}","types":["frontend"]}},"capabilities":{{"frontend":{{"languages":[{{"id":"python","fileExtensions":[".py"]}}],"irVersions":["4"],"compile":true}}}}}}"""

    /// A release.json of the extension family with the given artifacts.
    let descriptor (version: string) (artifacts: string list) =
        let artifacts = String.concat "," artifacts

        utf8
            $"""{{"$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json","extensionId":"com.acme.python","shortId":"acme-python","version":"{version}","platformDifferences":"declared","artifacts":[{artifacts}],"critical":["artifacts.package"]}}"""

    let processArtifact (version: string) (triple: string) (filename: string) (root: string) (entryPoint: string) (tree: string) =
        $"""{{"runtime":"process","platform":"{triple}","filename":"{filename}","package":{{"format":"nupkg","root":"{root}","entryPoint":"{entryPoint}","treeSha256":"{tree}"}},"claims":{claims version}}}"""

    let wasmArtifact (version: string) (filename: string) (sha256: string) =
        $"""{{"runtime":"wasm","filename":"{filename}","sha256":"{sha256}","claims":{claims version}}}"""

/// A folder feed with the running example of the nuget-extension-source spec, for the host platform.
type private Feed = {
    Folder: string
    Payload: byte array
    Helper: byte array
    Tree: string
}

let private payload = utf8 "#!/bin/sh\necho acme\n"
let private helper = utf8 "helper library"

/// Layout A: one package whose root morphir/<rid> holds the payload.
let private layoutA (sandbox: TestSandbox) (version: string) =
    let folder = Path.Combine(sandbox.Root, "feed")
    let rid = (platform ()).RuntimeIdentifier
    let root = $"morphir/{rid}"
    let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true; "lib/helper.dll", helper, false ]
    let filename = $"acme.morphir.python.{version.ToLowerInvariant()}.nupkg"

    let release =
        NuGetFixtures.descriptor version [ NuGetFixtures.processArtifact version (platform ()).DescriptorTriple filename root (entryPoint ()) tree ]

    NuGetFixtures.pack folder "Acme.Morphir.Python" version [ "MorphirExtension" ] [
        { Name = "morphir/release.json"; Bytes = release }
        { Name = $"{root}/{entryPoint ()}"; Bytes = payload }
        { Name = $"{root}/lib/helper.dll"; Bytes = helper }
        { Name = "README.md"; Bytes = utf8 "outside the root" }
    ]
    |> ignore

    { Folder = folder; Payload = payload; Helper = helper; Tree = tree }

let private request (text: string) (source: string option) =
    match ExtensionSourceRequest.Parse text with
    | Error failure -> failtestf "Bad request %s: %O" text failure
    | Ok request ->
        request.WithNuGetOptions(Option.toObj source, null, false) |> get

let private install (sandbox: TestSandbox) (text: string) (source: string option) =
    let request = request text source
    let provider = NuGetPackageProvider(home sandbox, sandbox.Root) :> IExtensionSourceProvider

    ExtensionSources
        .InstallAsync(home sandbox, provider, request, Array.AsReadOnly [||], host, ct)
        .GetAwaiter()
        .GetResult()

let private lockNode (sandbox: TestSandbox) =
    nonNull (JsonNode.Parse(File.ReadAllText(Path.Combine(home sandbox, "locks", "extensions", "com.acme.python.json"))))

let private withSandbox action =
    use sandbox = TestSandbox.Create()
    let cleanup = PackageTests.packageCleanup sandbox

    try
        action sandbox
    finally
        cleanup.DisposeAsync().GetAwaiter().GetResult()

let private refusedWith code (result: InstallationResult<'T>) =
    result.Match(
        Func<'T, unit>(fun _ -> failtestf "Expected %s" code),
        Func<InstallationFailure, unit>(fun failure -> Expect.equal failure.Code code failure.Message)
    )

let private parsing =
    testList "NuGetSourceRequest" [
        for text in [ "nuget:Acme.Morphir.Python"; "nuget:Acme.Morphir.Python@1.2.3"; "nuget:Acme.Morphir.Python@1.3.0-beta.1" ] do
            testCase $"parses {text}" (fun () -> Expect.equal (request text None).Scheme "nuget" "scheme")
        for text in [ "nuget:Acme.Morphir.Python@1.x"; "nuget:"; "nuget:bad id"; "nuget:Acme..Python"; "nuget:Acme.Python@" ] do
            testCase
                $"refuses {text}"
                (fun () ->
                    match ExtensionSourceRequest.Parse text with
                    | Ok _ -> failtest "Expected refusal"
                    | Error failure -> Expect.equal failure.Code "extension.invalid_source" failure.Message)
        test "an unknown scheme names github and nuget" {
            match ExtensionSourceRequest.Parse "npm:acme-python" with
            | Ok _ -> failtest "Expected refusal"
            | Error failure ->
                Expect.equal failure.Code "extension.invalid_source" "code"
                Expect.stringContains failure.Message "github" "names github"
                Expect.stringContains failure.Message "nuget" "names nuget"
        }
        test "NuGet options apply only to a nuget: source" {
            match ExtensionSourceRequest.Parse "github:finos/morphir-python" with
            | Error failure -> failtestf "%O" failure
            | Ok github ->
                let options: (string | null * string | null * bool) list =
                    [ "./feed", null, false; null, "nuget.config", false; null, null, true ]

                for source, configFile, interactive in options do
                    github.WithNuGetOptions(source, configFile, interactive) |> refusedWith "extension.invalid_source"

                Expect.equal (github.WithNuGetOptions(null, null, false) |> get).Scheme "github" "no option, no change"
        }
    ]

let private folderFeed =
    testList "NuGetFolderFeed" [
        test "layout A installs the subtree under root and records NuGet provenance" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                let packages = Path.Combine(sandbox.Root, "nuget-packages")
                let httpCache = Path.Combine(sandbox.Root, "nuget-http-cache")
                Directory.CreateDirectory packages |> ignore
                Directory.CreateDirectory httpCache |> ignore
                let previous = Environment.GetEnvironmentVariable "NUGET_PACKAGES", Environment.GetEnvironmentVariable "NUGET_HTTP_CACHE_PATH"
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", packages)
                Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", httpCache)

                try
                    use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some feed.Folder) |> get
                    let tree = Path.GetDirectoryName(captured.Activation.EntryPointPath) |> nonNull

                    let files =
                        Directory.GetFiles(tree, "*", SearchOption.AllDirectories)
                        |> Array.map (fun path -> Path.GetRelativePath(tree, path).Replace('\\', '/'))
                        |> Array.sort

                    Expect.equal files ([| entryPoint (); "lib/helper.dll" |] |> Array.sort) "only the files under root"
                    Expect.equal (File.ReadAllBytes captured.Activation.EntryPointPath) feed.Payload "the payload"

                    if not (OperatingSystem.IsWindows()) then
                        let mode = File.GetUnixFileMode captured.Activation.EntryPointPath
                        Expect.isTrue (mode.HasFlag UnixFileMode.UserExecute) "the entry point is executable with no stored Unix mode"

                    let lock = lockNode sandbox
                    let index = nonNull lock["index"]
                    let source = nonNull lock["source"]
                    Expect.equal (string index["kind"]) "nuget-package" "provenance kind"
                    Expect.equal (string index["feed"]) (Path.GetFullPath feed.Folder) "the folder feed"
                    Expect.equal (string index["id"]) "acme.morphir.python" "lower-case id"
                    Expect.equal (string index["version"]) "1.2.3" "normalized version"
                    Expect.equal (string source["kind"]) "nuget-package-entry" "source kind"
                    Expect.equal (string source["id"]) "acme.morphir.python" "the main package (layout A)"
                    Expect.equal (string source["path"]) $"morphir/{(platform ()).RuntimeIdentifier}" "the root"
                    Expect.isNull lock["digest"] "a nupkg package has no archive digest"
                    let package = nonNull lock["package"]
                    Expect.equal (string package["format"]) "nupkg" "package format"

                    Expect.isEmpty (Directory.GetFileSystemEntries packages) "NUGET_PACKAGES is untouched"
                    Expect.isEmpty (Directory.GetFileSystemEntries httpCache) "NUGET_HTTP_CACHE_PATH is untouched"

                    let tmp = Path.Combine(home sandbox, "tmp")
                    Expect.isEmpty (if Directory.Exists tmp then Directory.GetDirectories tmp else [||]) "no private stage remains"
                finally
                    Environment.SetEnvironmentVariable("NUGET_PACKAGES", fst previous)
                    Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", snd previous))
        }
        test "the installed tree re-verifies offline from the lock" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                (install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some feed.Folder) |> get :> IDisposable).Dispose()
                Directory.Delete(feed.Folder, true)

                use captured =
                    InstalledExtensions.CaptureAsync(home sandbox, Morphir.Extensions.ExtensionId.Create "com.acme.python", host, ct).GetAwaiter().GetResult()
                    |> get

                Expect.equal captured.Record.TreeDigest.Value feed.Tree "the tree the descriptor pins")
        }
        test "an ordinary NuGet package is not an extension" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                NuGetFixtures.pack folder "Newtonsoft.Json" "13.0.3" [] [ { Name = "lib/net8.0/Newtonsoft.Json.dll"; Bytes = helper } ] |> ignore
                install sandbox "nuget:Newtonsoft.Json@13.0.3" (Some folder) |> refusedWith "extension.release_descriptor_missing"
                Expect.isFalse (Directory.Exists(Path.Combine(home sandbox, "catalog"))) "nothing is written to the Home")
        }
        test "a descriptor version that differs from the package version is refused" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let release = NuGetFixtures.descriptor "1.2.4" [ NuGetFixtures.wasmArtifact "1.2.4" "acme.wasm" (String('a', 64)) ]
                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [ { Name = "morphir/release.json"; Bytes = release } ] |> ignore
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.version_mismatch")
        }
        test "a changed file under root installs nothing" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let rid = (platform ()).RuntimeIdentifier
                let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
                let release = NuGetFixtures.descriptor "1.2.3" [ NuGetFixtures.processArtifact "1.2.3" (platform ()).DescriptorTriple "acme.morphir.python.1.2.3.nupkg" $"morphir/{rid}" (entryPoint ()) tree ]

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = release }
                    { Name = $"morphir/{rid}/{entryPoint ()}"; Bytes = utf8 "changed" }
                ]
                |> ignore

                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "package.tree_digest_mismatch"
                Expect.isFalse (File.Exists(Path.Combine(home sandbox, "locks", "extensions", "com.acme.python.json"))) "no lock")
        }
        test "an entry outside root is neither refused nor extracted" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let rid = (platform ()).RuntimeIdentifier
                let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
                let release = NuGetFixtures.descriptor "1.2.3" [ NuGetFixtures.processArtifact "1.2.3" (platform ()).DescriptorTriple "acme.morphir.python.1.2.3.nupkg" $"morphir/{rid}" (entryPoint ()) tree ]

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = release }
                    { Name = $"morphir/{rid}/{entryPoint ()}"; Bytes = payload }
                    { Name = "../evil"; Bytes = utf8 "outside" }
                ]
                |> ignore

                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                Expect.isFalse (File.Exists(Path.Combine(sandbox.Root, "evil"))) "nothing is written outside the stage"
                Expect.isFalse (File.Exists(Path.Combine(home sandbox, "evil"))) "nothing is written beside the Home"
                Expect.equal (File.ReadAllBytes captured.Activation.EntryPointPath) payload "the root is installed")
        }
        test "a .dll entry point is refused" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let rid = (platform ()).RuntimeIdentifier
                let tree = NuGetFixtures.treeDigest [ "acme-python.dll", payload, true ]
                let release = NuGetFixtures.descriptor "1.2.3" [ NuGetFixtures.processArtifact "1.2.3" (platform ()).DescriptorTriple "acme.morphir.python.1.2.3.nupkg" $"morphir/{rid}" "acme-python.dll" tree ]

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = release }
                    { Name = $"morphir/{rid}/acme-python.dll"; Bytes = payload }
                ]
                |> ignore

                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.unsupported_package")
        }
        test "an unknown package names the sources it tried" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"

                match (install sandbox "nuget:Acme.Morphir.Missing@1.0.0" (Some feed.Folder)).Match(Func<_, _>(fun _ -> None), Func<_, _>(Some)) with
                | None -> failtest "Expected refusal"
                | Some(failure: InstallationFailure) ->
                    Expect.equal failure.Code "extension.nuget_not_found" failure.Message
                    Expect.stringContains failure.Message (Path.GetFullPath feed.Folder) "the source")
        }
        test "without a version the highest stable version is selected" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                layoutA sandbox "1.3.0-beta.1" |> ignore
                use captured = install sandbox "nuget:Acme.Morphir.Python" (Some feed.Folder) |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "a prerelease is not selected")
        }
        test "a named prerelease is selected" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                layoutA sandbox "1.3.0-beta.1" |> ignore
                use captured = install sandbox "nuget:Acme.Morphir.Python@1.3.0-beta.1" (Some feed.Folder) |> get
                Expect.equal captured.Record.Version.Text "1.3.0-beta.1" "the named prerelease")
        }
    ]

[<Tests>]
let tests = testLabel "PlatformSpecific" <| testList "NuGetProvider" [ parsing; folderFeed ]
