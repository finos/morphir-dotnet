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

    /// A release.json of the extension family with the given artifacts, and optional extra top-level members.
    let descriptorWith (version: string) (extra: string) (artifacts: string list) =
        let artifacts = String.concat "," artifacts

        utf8
            $"""{{"$schema":"https://morphir.finos.org/schemas/extensions/0.1.0-preview.3/release.schema.json","extensionId":"com.acme.python","shortId":"acme-python","version":"{version}","platformDifferences":"declared","artifacts":[{artifacts}]{extra}}}"""

    let descriptor (version: string) (artifacts: string list) =
        descriptorWith version ""","critical":["artifacts.package"]""" artifacts

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
                    let written = Directory.GetFiles(httpCache, "*", SearchOption.AllDirectories) |> Array.map (fun path -> Path.GetRelativePath(httpCache, path))
                    let listed = String.Join("; ", written)
                    Expect.isEmpty written $"NUGET_HTTP_CACHE_PATH is untouched: {listed}"

                    let tmp = Path.Combine(home sandbox, "tmp")
                    let left =
                        if Directory.Exists tmp then
                            Directory.GetDirectories tmp
                            |> Array.map Path.GetFileName
                            |> Array.filter (fun name -> name <> "morphir-dotnet-nuget-scratch")
                        else
                            [||]

                    Expect.isEmpty left "no private stage or NuGet temporary folder remains"
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


/// Layout B: the main package holds only the descriptor; the artifact package of this platform holds the payload.
let private layoutB (sandbox: TestSandbox) (artifactId: string) (packArtifact: bool) (entries: FixtureEntry list option) =
    let folder = Path.Combine(sandbox.Root, "feed")
    let rid = (platform ()).RuntimeIdentifier
    let root = $"tools/net11.0/{rid}"
    let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
    let filename = $"{artifactId.ToLowerInvariant()}.1.2.3.nupkg"

    let release =
        NuGetFixtures.descriptor "1.2.3" [ NuGetFixtures.processArtifact "1.2.3" (platform ()).DescriptorTriple filename root (entryPoint ()) tree ]

    NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [ "DotnetTool"; "MorphirExtension" ] [ { Name = "morphir/release.json"; Bytes = release } ]
    |> ignore

    if packArtifact then
        let files =
            entries
            |> Option.defaultValue [
                { Name = $"{root}/{entryPoint ()}"; Bytes = payload }
                { Name = "morphir/release.json"; Bytes = utf8 "a stale copy outside the root" }
            ]

        NuGetFixtures.pack folder artifactId "1.2.3" [ "DotnetTool" ] files |> ignore

    folder

let private writeConfig (sandbox: TestSandbox) (text: string) =
    let path = Path.Combine(sandbox.Root, "nuget.config")
    File.WriteAllText(path, text)
    path

let private installWith (sandbox: TestSandbox) (text: string) (source: string option) (configFile: string option) =
    let request =
        match ExtensionSourceRequest.Parse text with
        | Ok request -> request.WithNuGetOptions(Option.toObj source, Option.toObj configFile, false) |> get
        | Error failure -> failtestf "Bad request %s: %O" text failure

    let provider = NuGetPackageProvider(home sandbox, sandbox.Root) :> IExtensionSourceProvider

    ExtensionSources
        .InstallAsync(home sandbox, provider, request, Array.AsReadOnly [||], host, ct)
        .GetAwaiter()
        .GetResult()

let private layouts =
    testList "NuGetLayouts" [
        test "layout B installs the subtree of the artifact package of the same source and version" {
            withSandbox (fun sandbox ->
                let folder = layoutB sandbox $"Acme.Morphir.Python.{(platform ()).RuntimeIdentifier}" true None
                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                let tree = Path.GetDirectoryName(captured.Activation.EntryPointPath) |> nonNull
                let files = Directory.GetFiles(tree, "*", SearchOption.AllDirectories)
                Expect.equal files.Length 1 "only the root of the artifact package, no stale morphir/release.json"
                let source = nonNull ((lockNode sandbox).["source"])
                Expect.equal (string source["id"]) $"acme.morphir.python.{(platform ()).RuntimeIdentifier}" "the artifact package"
                Expect.equal (string source["path"]) $"tools/net11.0/{(platform ()).RuntimeIdentifier}" "its root")
        }
        test "an artifact package of another publisher is refused" {
            withSandbox (fun sandbox ->
                let folder = layoutB sandbox "Contoso.Tools" true None
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.invalid_descriptor")
        }
        test "an id that only starts with the main id is another publisher" {
            withSandbox (fun sandbox ->
                let folder = layoutB sandbox "Acme.Morphir.Pythonx" true None
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.invalid_descriptor")
        }
        test "a missing artifact package is refused" {
            withSandbox (fun sandbox ->
                let folder = layoutB sandbox $"Acme.Morphir.Python.{(platform ()).RuntimeIdentifier}" false None
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.artifact_missing")
        }
        test "a framework-dependent .NET tool is refused" {
            withSandbox (fun sandbox ->
                let rid = (platform ()).RuntimeIdentifier
                let stem = "acme-python"
                let config = utf8 """{"runtimeOptions":{"tfm":"net11.0","framework":{"name":"Microsoft.NETCore.App","version":"11.0.0"}}}"""

                let folder =
                    layoutB sandbox $"Acme.Morphir.Python.{rid}" true (Some [
                        { Name = $"tools/net11.0/{rid}/{entryPoint ()}"; Bytes = payload }
                        { Name = $"tools/net11.0/{rid}/{stem}.runtimeconfig.json"; Bytes = config }
                    ])

                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.unsupported_package")
        }
    ]

let private configuration =
    testList "NuGetConfiguration" [
        test "a named source of nuget.config is used" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"

                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="acme" value="{feed.Folder}" /></packageSources></configuration>"""

                use captured = installWith sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some "acme") (Some config) |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "installed from the named source")
        }
        test "package source mapping leaves only the mapped source" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                // A newer version in the other source: without mapping, the highest version would come from it.
                let other = Path.Combine(sandbox.Root, "other-feed")
                let rid = (platform ()).RuntimeIdentifier
                let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
                let release = NuGetFixtures.descriptor "1.2.4" [ NuGetFixtures.processArtifact "1.2.4" (platform ()).DescriptorTriple "acme.morphir.python.1.2.4.nupkg" $"morphir/{rid}" (entryPoint ()) tree ]

                NuGetFixtures.pack other "Acme.Morphir.Python" "1.2.4" [] [
                    { Name = "morphir/release.json"; Bytes = release }
                    { Name = $"morphir/{rid}/{entryPoint ()}"; Bytes = payload }
                ]
                |> ignore

                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="other" value="{other}" /><add key="acme" value="{feed.Folder}" /></packageSources>
  <packageSourceMapping>
    <packageSource key="acme"><package pattern="Acme.*" /></packageSource>
    <packageSource key="other"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>"""

                use captured = installWith sandbox "nuget:Acme.Morphir.Python" None (Some config) |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "the version of the mapped source, not 1.2.4"
                let index = nonNull ((lockNode sandbox).["index"])
                Expect.equal (string index["feed"]) (Path.GetFullPath feed.Folder) "the mapped source")
        }
        test "mapping that excludes every source names no source" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"

                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="acme" value="{feed.Folder}" /></packageSources>
  <packageSourceMapping><packageSource key="acme"><package pattern="Contoso.*" /></packageSource></packageSourceMapping>
</configuration>"""

                installWith sandbox "nuget:Acme.Morphir.Python" None (Some config) |> refusedWith "extension.nuget_not_found")
        }
        test "a configured source that the lock cannot record is not asked, and the others still install" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"

                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
  <add key="legacy" value="http://127.0.0.1:9/v3/index.json" />
  <add key="acme" value="{feed.Folder}" />
</packageSources></configuration>"""

                use captured = installWith sandbox "nuget:Acme.Morphir.Python@1.2.3" None (Some config) |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "installed from the folder")
        }
        test "a package NuGet cannot read is an invalid descriptor" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                Directory.CreateDirectory folder |> ignore
                File.WriteAllBytes(Path.Combine(folder, "acme.morphir.python.1.2.3.nupkg"), utf8 "not a zip")

                match (install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder)).Match(Func<_, _>(fun _ -> None), Func<_, _>(Some)) with
                | None -> failtest "Expected refusal"
                | Some(failure: InstallationFailure) ->
                    Expect.contains [ "extension.invalid_descriptor"; "extension.nuget_not_found"; "extension.nuget_unreachable" ] failure.Code failure.Message
                    Expect.isFalse (failure.Code = "extension.io_error") "a stable NuGet code")
        }
        test "a Windows folder feed is recorded with an upper-case drive letter" {
            if OperatingSystem.IsWindows() then
                let feed = NuGetSettings.feedOf (NuGet.Configuration.PackageSource(@"c:\feed"))
                Expect.equal feed.Text @"C:\feed" "the family spelling"
        }
        test "an http source is refused, because a lock records only https or a folder" {
            withSandbox (fun sandbox ->
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some "http://feed.example/v3/index.json")
                |> refusedWith "extension.invalid_source")
        }
        test "a source that does not answer fails as unreachable and names the source" {
            withSandbox (fun sandbox ->
                // Port 9 (discard) on loopback: refused at once, with no live network.
                let source = "https://127.0.0.1:9/v3/index.json"

                match (install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some source)).Match(Func<_, _>(fun _ -> None), Func<_, _>(Some)) with
                | None -> failtest "Expected refusal"
                | Some(failure: InstallationFailure) ->
                    Expect.equal failure.Code "extension.nuget_unreachable" failure.Message
                    Expect.stringContains failure.Message source "the source")
        }
        test "the embedded NuGet trusted root lists are the pinned dotnet/sdk blobs" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "trust")
                NuGetTrustRoots.write folder

                for name, expected in [
                    "codesignctl.pem", "aab671f52e5229906b2100727370007ef4b6d2e360b23f2cf12a6d87773be611"
                    "timestampctl.pem", "5ccb03367b52f047099f07e1653160e8578a83711cb18560c979febb65f8cb5d"
                ] do
                    Expect.equal (Digest.ofBytes (File.ReadAllBytes(Path.Combine(folder, "trustedroots", name)))) expected name

                NuGetTrustRoots.write folder)
        }
        test "require mode refuses an unsigned package" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"

                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="acme" value="{feed.Folder}" /></packageSources>
  <config><add key="signatureValidationMode" value="require" /></config>
  <trustedSigners>
    <author name="acme"><certificate fingerprint="3F9001EA83C560D712C24CF213C3D312CB3BFF51EE89435D3430BD06B5D0EECE" hashAlgorithm="SHA256" allowUntrustedRoot="false" /></author>
  </trustedSigners>
</configuration>"""

                installWith sandbox "nuget:Acme.Morphir.Python@1.2.3" None (Some config) |> refusedWith "extension.nuget_signature_invalid")
        }
        test "accept mode installs an unsigned package with the default policy" {
            withSandbox (fun sandbox ->
                let feed = layoutA sandbox "1.2.3"
                // On macOS dotnet does not verify by default; on Windows and Linux an unsigned package passes accept mode.
                use _ = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some feed.Folder) |> get
                ())
        }
    ]

/// The bytes of a WASM module fixture. The guest never runs during installation, so any bytes do.
let private wasmModule = [| 0uy; 0x61uy; 0x73uy; 0x6duy; 1uy; 0uy; 0uy; 0uy |]

/// A WASM release in the main package: the entry morphir/acme-python.wasm, with optional process artifacts beside it.
let private wasmRelease (sandbox: TestSandbox) (sha256: string) (extra: string) (processes: string list) =
    let folder = Path.Combine(sandbox.Root, "feed")
    let wasm = NuGetFixtures.wasmArtifact "1.2.3" "acme-python.wasm" sha256
    let extra = if extra = "" then ""","critical":["artifacts.package"]""" else extra
    let release = NuGetFixtures.descriptorWith "1.2.3" extra (processes @ [ wasm ])

    NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [ "MorphirExtension" ] [
        { Name = "morphir/release.json"; Bytes = release }
        { Name = "morphir/acme-python.wasm"; Bytes = wasmModule }
    ]
    |> ignore

    folder

let private wasm =
    testList "NuGetWasm" [
        test "a WASM-only release installs the module unchecked, in the single-file store" {
            withSandbox (fun sandbox ->
                let digest = Digest.ofBytes wasmModule
                let folder = wasmRelease sandbox digest "" []
                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get

                Expect.isTrue captured.IsWasm "a WASM extension"
                let installed = nonNull captured.Module
                Expect.equal installed.Digest.Value digest "the locked digest"
                Expect.equal (File.ReadAllBytes installed.ModulePath) wasmModule "the stored module"
                Expect.equal installed.ModulePath (Path.Combine(home sandbox, "store", "extensions", "sha256", digest, "acme-python.wasm")) "the single-file store"

                let lock = lockNode sandbox
                Expect.equal (string lock["runtime"]) "wasm" "lock runtime"
                Expect.equal (string lock["digest"]) digest "lock digest"
                Expect.isNull lock["platform"] "no platform"
                Expect.isNull lock["package"] "no package"
                Expect.equal (lock["executable"] |> nonNull |> fun value -> value.GetValue<bool>()) false "not executable"
                let source = nonNull lock["source"]
                Expect.equal (string source["path"]) "morphir/acme-python.wasm" "the package entry"

                let catalog = nonNull (JsonNode.Parse(File.ReadAllText(Path.Combine(home sandbox, "catalog", "extensions.json"))))
                let entries = nonNull catalog["extensions"]
                let entry = nonNull entries[0]
                Expect.equal (string entry["claimCheck"]) "unchecked" "claims stay unchecked")
        }
        test "a WASM artifact whose filename does not end in .wasm installs and captures" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let wasm = NuGetFixtures.wasmArtifact "1.2.3" "acme-python.module" (Digest.ofBytes wasmModule)

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = NuGetFixtures.descriptor "1.2.3" [ wasm ] }
                    { Name = "morphir/acme-python.module"; Bytes = wasmModule }
                ]
                |> ignore

                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                Expect.equal (Path.GetFileName (nonNull captured.Module).ModulePath) "acme-python.module" "the stored module")
        }
        test "a WASM module that differs from its sha256 installs nothing" {
            withSandbox (fun sandbox ->
                let folder = wasmRelease sandbox (String('a', 64)) "" []
                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.digest_mismatch"
                Expect.isFalse (File.Exists(Path.Combine(home sandbox, "locks", "extensions", "com.acme.python.json"))) "no lock")
        }
        test "a WASM release that requires another host installs nothing" {
            withSandbox (fun sandbox ->
                let folder = Path.Combine(sandbox.Root, "feed")
                let plain = NuGetFixtures.claims "1.2.3"
                let claims = plain.Substring(0, plain.Length - 1) + ""","requires":{"host":[">=99.0.0"]},"critical":["requires.host"]}"""
                let wasm = $"""{{"runtime":"wasm","filename":"acme-python.wasm","sha256":"{Digest.ofBytes wasmModule}","claims":{claims}}}"""

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = NuGetFixtures.descriptor "1.2.3" [ wasm ] }
                    { Name = "morphir/acme-python.wasm"; Bytes = wasmModule }
                ]
                |> ignore

                install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> refusedWith "extension.host_incompatible"
                Expect.isFalse (File.Exists(Path.Combine(home sandbox, "locks", "extensions", "com.acme.python.json"))) "no lock")
        }
        test "a mixed release on a platform with no process artifact installs the WASM artifact" {
            withSandbox (fun sandbox ->
                let other =
                    if (platform ()).DescriptorTriple = "x86_64-unknown-linux-gnu" then "aarch64-apple-darwin" else "x86_64-unknown-linux-gnu"

                let tree = NuGetFixtures.treeDigest [ "acme-python", payload, true ]
                let processArtifact = NuGetFixtures.processArtifact "1.2.3" other "acme.morphir.python.1.2.3.nupkg" "morphir/other" "acme-python" tree
                let folder = wasmRelease sandbox (Digest.ofBytes wasmModule) "" [ processArtifact ]
                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                Expect.isTrue captured.IsWasm "the WASM fallback"
                Expect.equal (string (lockNode sandbox).["runtime"]) "wasm" "the lock records the selection")
        }
        test "a mixed release on a matching platform installs the process artifact" {
            withSandbox (fun sandbox ->
                let rid = (platform ()).RuntimeIdentifier
                let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
                let processArtifact = NuGetFixtures.processArtifact "1.2.3" (platform ()).DescriptorTriple "acme.morphir.python.1.2.3.nupkg" $"morphir/{rid}" (entryPoint ()) tree
                let folder = Path.Combine(sandbox.Root, "feed")
                let release = NuGetFixtures.descriptor "1.2.3" [ processArtifact; NuGetFixtures.wasmArtifact "1.2.3" "acme-python.wasm" (Digest.ofBytes wasmModule) ]

                NuGetFixtures.pack folder "Acme.Morphir.Python" "1.2.3" [] [
                    { Name = "morphir/release.json"; Bytes = release }
                    { Name = $"morphir/{rid}/{entryPoint ()}"; Bytes = payload }
                    { Name = "morphir/acme-python.wasm"; Bytes = wasmModule }
                ]
                |> ignore

                use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                Expect.isFalse captured.IsWasm "the matching process artifact"
                Expect.equal (string (lockNode sandbox).["runtime"]) "process" "the lock records the selection")
        }
        test "an installed WASM module is verified offline, and a changed module is refused before any guest runs" {
            withSandbox (fun sandbox ->
                let folder = wasmRelease sandbox (Digest.ofBytes wasmModule) "" []
                let modulePath =
                    use captured = install sandbox "nuget:Acme.Morphir.Python@1.2.3" (Some folder) |> get
                    (nonNull captured.Module).ModulePath

                Directory.Delete(folder, true)
                let id = Morphir.Extensions.ExtensionId.Create "com.acme.python"

                do
                    use captured = InstalledExtensions.CaptureAsync(home sandbox, id, host, ct).GetAwaiter().GetResult() |> get
                    Expect.isTrue captured.IsWasm "captured as WASM"
                    Expect.throwsT<InvalidOperationException> (fun () -> captured.Activation |> ignore) "no package activation"

                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(modulePath, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

                File.SetAttributes(modulePath, FileAttributes.Normal)
                File.WriteAllBytes(modulePath, Array.append wasmModule [| 1uy |])

                InstalledExtensions.CaptureAsync(home sandbox, id, host, ct).GetAwaiter().GetResult()
                |> refusedWith "extension.digest_mismatch")
        }
    ]

/// One package of the fake V3 feed: id, normalized version, listed, and the .nupkg bytes.
type private FeedPackage = { Id: string; Version: string; Listed: bool; Bytes: byte array }

/// An in-process NuGet V3 feed at https://feed.test: service index, flat container and registration. It never
/// touches the network. With a credential, it answers 401 until NuGet's own authentication handler has put that
/// credential on the HTTP client handler of the source.
type private FakeV3Handler(packages: FeedPackage list, credential: (string * string) option, clientHandler: System.Net.Http.HttpClientHandler, requests: System.Collections.Concurrent.ConcurrentQueue<string>) =
    inherit System.Net.Http.HttpMessageHandler()

    let json (text: string) =
        let response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        response.Content <- new System.Net.Http.StringContent(text, Encoding.UTF8, "application/json")
        response

    let status code = new System.Net.Http.HttpResponseMessage(code, Content = new System.Net.Http.ByteArrayContent([||]))

    let authorized (uri: Uri) =
        match credential with
        | None -> true
        | Some(user, password) ->
            match clientHandler.Credentials with
            | null -> false
            | credentials ->
                match credentials.GetCredential(uri, "Basic") with
                | null -> false
                | given -> given.UserName = user && given.Password = password

    override _.SendAsync(request, _ct) =
        let uri = nonNull request.RequestUri
        requests.Enqueue(uri.ToString())
        let path = uri.AbsolutePath
        let lower (text: string) = text.ToLowerInvariant()

        let response =
            if uri.Host <> "feed.test" then
                status System.Net.HttpStatusCode.NotFound
            elif not (authorized uri) then
                let refused = status System.Net.HttpStatusCode.Unauthorized
                refused.Headers.WwwAuthenticate.ParseAdd "Basic realm=\"feed\""
                refused
            elif path = "/v3/index.json" then
                json """{"version":"3.0.0","resources":[{"@id":"https://feed.test/v3-flatcontainer/","@type":"PackageBaseAddress/3.0.0"},{"@id":"https://feed.test/v3/registration/","@type":"RegistrationsBaseUrl/3.6.0"}]}"""
            else
                let parts = path.Trim('/').Split('/')

                match parts with
                | [| "v3-flatcontainer"; id; "index.json" |] ->
                    match packages |> List.filter (fun package -> lower package.Id = id) with
                    | [] -> status System.Net.HttpStatusCode.NotFound
                    | found ->
                        let versions = found |> List.map (fun package -> $"\"{lower package.Version}\"") |> String.concat ","
                        json $"""{{"versions":[{versions}]}}"""
                | [| "v3-flatcontainer"; id; version; _file |] ->
                    match packages |> List.tryFind (fun package -> lower package.Id = id && lower package.Version = version) with
                    | Some package ->
                        let response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        response.Content <- new System.Net.Http.ByteArrayContent(package.Bytes)
                        response
                    | None -> status System.Net.HttpStatusCode.NotFound
                | [| "v3"; "registration"; id; "index.json" |] ->
                    match packages |> List.filter (fun package -> lower package.Id = id) with
                    | [] -> status System.Net.HttpStatusCode.NotFound
                    | found ->
                        let leaf (package: FeedPackage) =
                            let published = if package.Listed then "2026-11-01T00:00:00+00:00" else "1900-01-01T00:00:00+00:00"
                            let entry = $"https://feed.test/v3/catalog/{id}.{lower package.Version}.json"
                            let content = $"https://feed.test/v3-flatcontainer/{id}/{lower package.Version}/{id}.{lower package.Version}.nupkg"
                            $"""{{"@id":"https://feed.test/v3/registration/{id}/{lower package.Version}.json","catalogEntry":{{"@id":"{entry}","id":"{package.Id}","version":"{package.Version}","listed":{(if package.Listed then "true" else "false")},"published":"{published}","packageContent":"{content}"}},"packageContent":"{content}"}}"""

                        let items = found |> List.map leaf |> String.concat ","
                        let versions = found |> List.map (fun package -> NuGet.Versioning.NuGetVersion.Parse package.Version) |> List.sort
                        let low = versions.Head.ToNormalizedString()
                        let high = (List.last versions).ToNormalizedString()

                        json
                            $"""{{"count":1,"items":[{{"@id":"https://feed.test/v3/registration/{id}/index.json#page","count":{found.Length},"lower":"{low}","upper":"{high}","items":[{items}]}}]}}"""
                | _ -> status System.Net.HttpStatusCode.NotFound

        System.Threading.Tasks.Task.FromResult response

/// NuGet's V3 providers with the fake feed as the HTTP handler, behind NuGet's own authentication handler.
type private FakeHttpHandlerProvider(packages: FeedPackage list, credential: (string * string) option, requests) =
    inherit NuGet.Protocol.Core.Types.ResourceProvider(typeof<NuGet.Protocol.Core.Types.HttpHandlerResource>, "FakeV3Feed", "HttpHandlerResourceV3Provider")

    override _.TryCreate(source, _ct) =
        let clientHandler = new System.Net.Http.HttpClientHandler()

        let credentials =
            match NuGet.Protocol.HttpHandlerResourceV3.CredentialService with
            | null -> null
            | service -> service.Value

        let authentication =
            new NuGet.Protocol.HttpSourceAuthenticationHandler(source.PackageSource, clientHandler, credentials)

        authentication.InnerHandler <- new FakeV3Handler(packages, credential, clientHandler, requests)
        let resource = NuGet.Protocol.HttpHandlerResourceV3(clientHandler, authentication) :> NuGet.Protocol.Core.Types.INuGetResource
        System.Threading.Tasks.Task.FromResult(Tuple.Create(true, resource))

let private feedUrl = "https://feed.test/v3/index.json"

let private package (sandbox: TestSandbox) (version: string) (listed: bool) =
    let folder = Path.Combine(sandbox.Root, $"pack-{version}")
    let rid = (platform ()).RuntimeIdentifier
    let tree = NuGetFixtures.treeDigest [ entryPoint (), payload, true ]
    let filename = $"acme.morphir.python.{version.ToLowerInvariant()}.nupkg"
    let release = NuGetFixtures.descriptor version [ NuGetFixtures.processArtifact version (platform ()).DescriptorTriple filename $"morphir/{rid}" (entryPoint ()) tree ]

    let path =
        NuGetFixtures.pack folder "Acme.Morphir.Python" version [] [
            { Name = "morphir/release.json"; Bytes = release }
            { Name = $"morphir/{rid}/{entryPoint ()}"; Bytes = payload }
        ]

    { Id = "Acme.Morphir.Python"; Version = version; Listed = listed; Bytes = File.ReadAllBytes path }

let private installFromFeed (sandbox: TestSandbox) (packages: FeedPackage list) (credential: (string * string) option) (config: string option) (text: string) =
    let requests = System.Collections.Concurrent.ConcurrentQueue<string>()

    let repositoryOf (source: NuGet.Configuration.PackageSource) =
        let providers =
            Seq.append
                (NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3())
                [ Lazy<NuGet.Protocol.Core.Types.INuGetResourceProvider>(fun () -> FakeHttpHandlerProvider(packages, credential, requests) :> NuGet.Protocol.Core.Types.INuGetResourceProvider) ]

        NuGet.Protocol.Core.Types.Repository.CreateSource(providers, source)

    let request =
        match ExtensionSourceRequest.Parse text with
        | Ok request -> request.WithNuGetOptions((match config with Some _ -> null | None -> feedUrl), Option.toObj config, false) |> get
        | Error failure -> failtestf "Bad request %s: %O" text failure

    let provider = NuGetPackageProvider(home sandbox, sandbox.Root, repositoryOf) :> IExtensionSourceProvider

    let result =
        ExtensionSources.InstallAsync(home sandbox, provider, request, Array.AsReadOnly [||], host, ct).GetAwaiter().GetResult()

    result, requests

let private stateBytes (sandbox: TestSandbox) =
    [ "catalog"; "locks"; "journal" ]
    |> List.map (fun name -> Path.Combine(home sandbox, name))
    |> List.filter Directory.Exists
    |> List.collect (fun folder -> Directory.GetFiles(folder, "*", SearchOption.AllDirectories) |> Array.toList)
    |> List.map (fun path -> Encoding.UTF8.GetString(File.ReadAllBytes path))
    |> String.concat "\n"

let private v3Feed =
    testList "NuGetV3Feed" [
        test "an exact version installs from a V3 feed and records its https service index" {
            withSandbox (fun sandbox ->
                let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] None None "nuget:Acme.Morphir.Python@1.2.3"
                use captured = result |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "the version"
                let index = nonNull ((lockNode sandbox).["index"])
                Expect.equal (string index["feed"]) feedUrl "the recorded feed")
        }
        test "without a version a newer prerelease and an unlisted version are not selected" {
            withSandbox (fun sandbox ->
                let packages = [ package sandbox "1.2.3" true; package sandbox "1.3.0-beta.1" true; package sandbox "1.4.0" false ]
                let result, _ = installFromFeed sandbox packages None None "nuget:Acme.Morphir.Python"
                use captured = result |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "the highest stable listed version")
        }
        test "a named unlisted version is selected" {
            withSandbox (fun sandbox ->
                let packages = [ package sandbox "1.2.3" true; package sandbox "1.4.0" false ]
                let result, _ = installFromFeed sandbox packages None None "nuget:Acme.Morphir.Python@1.4.0"
                use captured = result |> get
                Expect.equal captured.Record.Version.Text "1.4.0" "the named version")
        }
        test "a package the feed does not have is not found" {
            withSandbox (fun sandbox ->
                let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] None None "nuget:Acme.Morphir.Missing@1.0.0"
                result |> refusedWith "extension.nuget_not_found")
        }
        test "a version the feed does not have is not found and names the feed" {
            withSandbox (fun sandbox ->
                let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] None None "nuget:Acme.Morphir.Python@1.9.9"

                match result.Match(Func<_, _>(fun _ -> None), Func<_, _>(Some)) with
                | None -> failtest "Expected refusal"
                | Some(failure: InstallationFailure) ->
                    Expect.equal failure.Code "extension.nuget_not_found" failure.Message
                    Expect.stringContains failure.Message "1.9.9" "the version"
                    Expect.stringContains failure.Message feedUrl "the feed")
        }
        test "a feed that requires a credential, with none, is unauthorized and names the source and --interactive" {
            withSandbox (fun sandbox ->
                let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] (Some("acme", "s3cret")) None "nuget:Acme.Morphir.Python@1.2.3"

                match result.Match(Func<_, _>(fun _ -> None), Func<_, _>(Some)) with
                | None -> failtest "Expected refusal"
                | Some(failure: InstallationFailure) ->
                    Expect.equal failure.Code "extension.nuget_unauthorized" failure.Message
                    Expect.stringContains failure.Message feedUrl "the source"
                    Expect.stringContains failure.Message "--interactive" "the way to sign in")
        }
        test "packageSourceCredentials of nuget.config are used and never written to the catalog, lock or journal" {
            withSandbox (fun sandbox ->
                let config =
                    writeConfig sandbox $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="acme" value="{feedUrl}" /></packageSources>
  <packageSourceCredentials><acme><add key="Username" value="acme-user" /><add key="ClearTextPassword" value="s3cret-value" /></acme></packageSourceCredentials>
</configuration>"""

                let result, requests =
                    installFromFeed sandbox [ package sandbox "1.2.3" true ] (Some("acme-user", "s3cret-value")) (Some config) "nuget:Acme.Morphir.Python@1.2.3"

                use captured = result |> get
                Expect.equal captured.Record.Version.Text "1.2.3" "installed with the credential"
                Expect.all requests (fun uri -> Uri(uri).Host = "feed.test") "requests go only to the feed"
                let state = stateBytes sandbox
                Expect.isNonEmpty state "the state was written"
                Expect.isFalse (state.Contains "s3cret-value") "no password in the state"
                Expect.isFalse (state.Contains "acme-user") "no user name in the state")
        }
        test "an https install writes nothing to NuGet's HTTP cache or scratch folders outside the Home" {
            withSandbox (fun sandbox ->
                let httpCache = Path.Combine(sandbox.Root, "nuget-http-cache")
                Directory.CreateDirectory httpCache |> ignore
                let previous = Environment.GetEnvironmentVariable "NUGET_HTTP_CACHE_PATH"
                Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", httpCache)

                try
                    let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] None None "nuget:Acme.Morphir.Python@1.2.3"
                    use _ = result |> get
                    let written = Directory.GetFiles(httpCache, "*", SearchOption.AllDirectories) |> Array.map (fun path -> Path.GetRelativePath(httpCache, path))
                    let listed = String.Join("; ", written)
                    Expect.isEmpty written $"NUGET_HTTP_CACHE_PATH is untouched: {listed}"
                    Expect.equal (Environment.GetEnvironmentVariable "NUGET_HTTP_CACHE_PATH") httpCache "restored after the operation"
                finally
                    Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", previous))
        }
        test "NuGet finds a credential provider plugin in NUGET_PLUGIN_PATHS, or as nuget-plugin-* on PATH" {
            withSandbox (fun sandbox ->
                let previous = Environment.GetEnvironmentVariable "NUGET_PLUGIN_PATHS", Environment.GetEnvironmentVariable "PATH"

                try
                    // A .dll in NUGET_PLUGIN_PATHS (NuGet starts it with the dotnet host).
                    let dll = Path.Combine(sandbox.Root, "Acme.CredentialProvider.dll")
                    File.WriteAllBytes(dll, [| 0uy |])
                    Environment.SetEnvironmentVariable("NUGET_PLUGIN_PATHS", dll)

                    let found =
                        use discoverer = new NuGet.Protocol.Plugins.PluginDiscoverer()
                        discoverer.DiscoverAsync(ct).GetAwaiter().GetResult() |> Seq.toList

                    Expect.exists found (fun result -> result.PluginFile.Path = dll) "the NUGET_PLUGIN_PATHS plugin"

                    // With no NUGET_PLUGIN_PATHS, a nuget-plugin-* executable on PATH (NuGet starts it directly).
                    Environment.SetEnvironmentVariable("NUGET_PLUGIN_PATHS", null)
                    let bin = Path.Combine(sandbox.Root, "bin")
                    Directory.CreateDirectory bin |> ignore
                    let name = if OperatingSystem.IsWindows() then "nuget-plugin-acme.exe" else "nuget-plugin-acme"
                    let executable = Path.Combine(bin, name)
                    File.WriteAllText(executable, "#!/bin/sh\nexit 0\n")

                    if not (OperatingSystem.IsWindows()) then
                        File.SetUnixFileMode(executable, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

                    Environment.SetEnvironmentVariable("PATH", bin + string Path.PathSeparator + (snd previous |> Option.ofObj |> Option.defaultValue ""))

                    let found =
                        use discoverer = new NuGet.Protocol.Plugins.PluginDiscoverer()
                        discoverer.DiscoverAsync(ct).GetAwaiter().GetResult() |> Seq.toList

                    Expect.exists found (fun result -> result.PluginFile.Path = executable) "the nuget-plugin-* executable on PATH"
                finally
                    Environment.SetEnvironmentVariable("NUGET_PLUGIN_PATHS", fst previous)
                    Environment.SetEnvironmentVariable("PATH", snd previous))
        }
        test "the credential service of NuGet, with its plugin providers, is set up non-interactively" {
            withSandbox (fun sandbox ->
                let result, _ = installFromFeed sandbox [ package sandbox "1.2.3" true ] None None "nuget:Acme.Morphir.Python@1.2.3"
                use _ = result |> get

                match NuGet.Protocol.HttpHandlerResourceV3.CredentialService with
                | null -> failtest "No credential service"
                | service -> Expect.isTrue (service.Value :? NuGet.Credentials.CredentialService) "NuGet's credential service with plugin providers")
        }
    ]

/// The checked-in feed of tests/Fixtures/NuGetExtension: a Native AOT RID-specific .NET tool that dotnet pack packed
/// as DotnetTool;MorphirExtension in two passes, with one RID package, linux-arm64.
let private dotnetToolFeed = Path.Combine(AppContext.BaseDirectory, "NuGetExtensionFeed")

let private dotnetTool =
    testList "NuGetDotnetTool" [
        test "the main package of a dotnet pack tool has both package types and the release descriptor" {
            use reader = new NuGet.Packaging.PackageArchiveReader(Path.Combine(dotnetToolFeed, "Acme.Morphir.Tool.1.0.0.nupkg"))
            let types = reader.GetPackageTypes() |> Seq.map _.Name |> Seq.toList
            Expect.containsAll types [ "DotnetTool"; "MorphirExtension" ] "both package types"
            use stream = reader.GetStream "morphir/release.json"
            use memory = new MemoryStream()
            stream.CopyTo memory
            let descriptor = Descriptors.read (memory.ToArray()) |> StateOwnership.unwrap
            Expect.equal descriptor.ExtensionId.Value "com.acme.tool" "the extension"
        }
        test "the RID package of a dotnet pack tool installs only its tools folder, pinned by the guide's tree digest" {
            withSandbox (fun sandbox ->
                use reader = new NuGet.Packaging.PackageArchiveReader(Path.Combine(dotnetToolFeed, "Acme.Morphir.Tool.1.0.0.nupkg"))
                use stream = reader.GetStream "morphir/release.json"
                use memory = new MemoryStream()
                stream.CopyTo memory

                let plan =
                    PackagePlan.Read(memory.ToArray(), InstallationPlatform.Create "linux-arm64") |> get

                Expect.equal plan.Integrity (PayloadIntegrity.TreeOnly plan.TreeDigest) "pinned by the tree"
                let ridPackage = Path.Combine(dotnetToolFeed, "Acme.Morphir.Tool.linux-arm64.1.0.0.nupkg")

                let installed =
                    PackageInstaller.InstallAsync(home sandbox, plan, staged sandbox (File.ReadAllBytes ridPackage), ct).GetAwaiter().GetResult()
                    |> get

                let files =
                    Directory.GetFiles(Path.Combine(installed.DirectoryPath, "tree"), "*", SearchOption.AllDirectories)
                    |> Array.map (fun path -> Path.GetRelativePath(Path.Combine(installed.DirectoryPath, "tree"), path).Replace('\\', '/'))
                    |> Array.sort

                Expect.equal files [| "DotnetToolSettings.xml"; "acme-tool" |] "the tools/any/linux-arm64 folder, no .nuspec or morphir/release.json"

                if not (OperatingSystem.IsWindows()) then
                    let mode = File.GetUnixFileMode(Path.Combine(installed.DirectoryPath, "tree", "acme-tool"))
                    Expect.isTrue (mode.HasFlag UnixFileMode.UserExecute) "the entry point is executable")
        }
        test "a dotnet pack tool installs from its folder feed on linux-arm64" {
            if (platform ()).RuntimeIdentifier <> "linux-arm64" then
                skiptest "The fixture tool has one RID package, linux-arm64."

            withSandbox (fun sandbox ->
                use captured = install sandbox "nuget:Acme.Morphir.Tool@1.0.0" (Some dotnetToolFeed) |> get
                Expect.equal captured.Record.ExtensionId.Value "com.acme.tool" "installed"
                let lock = nonNull (JsonNode.Parse(File.ReadAllText(Path.Combine(home sandbox, "locks", "extensions", "com.acme.tool.json"))))
                let source = nonNull lock["source"]
                Expect.equal (string source["id"]) "acme.morphir.tool.linux-arm64" "the RID package")
        }
    ]

[<Tests>]
// Sequenced: the tests set NUGET_* variables, which are process-wide.
let tests = testLabel "PlatformSpecific" <| testSequenced (testList "NuGetProvider" [ parsing; folderFeed; layouts; configuration; wasm; v3Feed; dotnetTool ])
