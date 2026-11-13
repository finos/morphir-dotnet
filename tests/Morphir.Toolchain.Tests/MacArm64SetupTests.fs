// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Controls for morphirup on macOS ARM64: the preview.3 documents with the osx-arm64 platform and its tar.gz host
/// archive, the platform gates, the Mach-O entry point, the executable modes of an installed host and the Unix launch.
/// Synthetic inputs only. The real producer output is proven by the native journey, not by these tests.
module Morphir.Toolchain.Tests.MacArm64SetupTests

open System
open System.Formats.Tar
open System.IO
open System.IO.Compression
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.TestSupport
open Morphirup

let private parsed =
    function
    | Ok value -> value
    | Error error -> failtestf "Bad unit input: %A" error

let private sha (bytes: byte array) = (Sha256.ofBytes bytes).Text

let private candidateFor (platform: string) (format: string) (entry: string) =
    Value.Object [
        "$schema", Value.String "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/candidate.schema.json"
        "origin", Value.String "local-developer"
        "implementation", Value.String "dotnet"
        "deployment", Value.String "self-contained"
        "platform", Value.String platform
        "version", Value.String "0.4.0-alpha.5"
        "sourceRepository", Value.String "https://github.com/finos/morphir-dotnet"
        "sourceCommit", Value.String(String('c', 40))
        "buildIdentity", Value.String "synthetic-unit-only"
        "archive", Value.Object [ "format", Value.String format; "sha256", Value.String(String('a', 64)); "bytes", Value.Number "1024" ]
        "entryPoint", Value.Object [ "path", Value.String entry; "sha256", Value.String(String('a', 64)) ]
        "files", Value.Array [ Value.Object [ "path", Value.String entry; "sha256", Value.String(String('a', 64)); "bytes", Value.Number "32" ] ]
        "extensions", Value.Object [
            "selectionSha256", Value.String(String('d', 64))
            "frontendId", Value.String "org.finos.morphir.fsharp-frontend"
            "backendId", Value.String "org.finos.morphir.fsharp-backend" ]
    ]
    |> JsonText.toBytes

let private bundleFor (platform: string) (archiveExtension: string) =
    let asset role =
        let name =
            match role with
            | "host-candidate" -> $"morphir-host-candidate-{platform}.json"
            | "selection" -> $"morphir-extensions-{platform}.json"
            | _ -> $"{role}-{platform}{archiveExtension}"
        Value.Object [
            "name", Value.String name
            "url", Value.String(ReleaseBundleDocument.repository + "/releases/download/v0.4.0-alpha.5/" + name)
            "bytes", Value.Number "3"
            "sha256", Value.String(String('a', 64))
            "role", Value.String role ]
    Value.Object [
        "$schema", Value.String(ReleaseBundleDocument.schema "bundle")
        "version", Value.String "0.4.0-alpha.5"
        "sourceCommit", Value.String(String('b', 40))
        "platform", Value.String platform
        "assets", Value.Array(List.map asset ReleaseBundleDocument.roles) ]
    |> JsonText.toBytes

type private CountingTransport(files: Map<string, byte array>) =
    let mutable calls = 0
    member _.Calls = calls
    interface IAcquisitionTransport with
        member _.OpenAsync(source, ct) = task {
            ct.ThrowIfCancellationRequested()
            calls <- calls + 1
            let bytes =
                files.TryFind(Path.GetFileName source.AbsolutePath)
                |> Option.defaultWith (fun () -> raise (HttpRequestException "Missing scripted asset."))
            let stream = new MemoryStream(bytes, false)
            return {
                new IAcquisitionResponse with
                    member _.Content = stream
                    member _.DeclaredLength = Some(int64 bytes.Length)
                interface IAsyncDisposable with
                    member _.DisposeAsync() = stream.DisposeAsync()
            }
        }

let private refusal (operation: unit -> 'T) =
    try
        operation () |> ignore
        None
    with LocalCandidate.Refused(code, _) -> Some code

[<Tests>]
let documents =
    testList "morphirup macOS ARM64 documents" [
        test "a preview.3 osx-arm64 candidate with a tar-gzip archive and a morphir entry point is read" {
            let candidate = LocalComposedDocument.readReleaseCandidate (candidateFor "osx-arm64" "tar-gzip" "morphir") |> parsed
            Expect.equal candidate.Host.Platform Platform.OsxArm64 "the platform is the manager platform to match"
            Expect.equal candidate.Host.EntryPoint.Target.Text "morphir" "the Unix entry point has no .exe"
        }
        test "an osx-arm64 candidate that declares a ZIP archive is refused" {
            match LocalComposedDocument.readReleaseCandidate (candidateFor "osx-arm64" "zip" "morphir") with
            | Ok _ -> failtest "A ZIP archive was accepted for osx-arm64."
            | Error error -> Expect.equal error.Path (DocumentPath.root.Child("archive").Child "format") "the archive format follows the platform"
        }
        test "a Windows candidate that declares a tar-gzip archive is refused" {
            match LocalComposedDocument.readReleaseCandidate (candidateFor "win-x64" "tar-gzip" "morphir.exe") with
            | Ok _ -> failtest "A tar-gzip archive was accepted for win-x64."
            | Error error -> Expect.equal error.Path (DocumentPath.root.Child("archive").Child "format") "Windows keeps ZIP"
        }
        test "platforms without delivery stay invalid in preview.3 candidates and bundles" {
            for platform in [ "osx-x64"; "linux-x64"; "linux-arm64" ] do
                Expect.isError (LocalComposedDocument.readReleaseCandidate (candidateFor platform "tar-gzip" "morphir")) $"{platform} candidate"
                Expect.isError (ReleaseBundleDocument.read (bundleFor platform ".tar.gz")) $"{platform} bundle"
        }
        test "a preview.3 osx-arm64 bundle with tar.gz assets is read" {
            let bundle = ReleaseBundleDocument.read (bundleFor "osx-arm64" ".tar.gz") |> parsed
            Expect.equal bundle.Platform Platform.OsxArm64 "the bundle platform"
        }
        test "release planning accepts an osx-arm64 host on an osx-arm64 manager only" {
            let host = (LocalComposedDocument.readReleaseCandidate (candidateFor "osx-arm64" "tar-gzip" "morphir") |> parsed).Host
            Expect.isOk (LocalCandidatePlanning.planRelease Platform.OsxArm64 (1UL <<< 30) host) "native manager"
            Expect.equal (LocalCandidatePlanning.planRelease Platform.WinArm64 (1UL <<< 30) host) (Error LocalCandidateRefusal.PlatformMismatch) "no cross-platform plan"
        }
        test "historical preview.1 planning stays win-x64 only" {
            let host = (LocalComposedDocument.readReleaseCandidate (candidateFor "osx-arm64" "tar-gzip" "morphir") |> parsed).Host
            Expect.isError (LocalCandidatePlanning.plan Platform.OsxArm64 (Some LocalConsent.LocalDeveloper) (1UL <<< 30) host) "preview.1 is unchanged"
        }
    ]

[<Tests>]
let gates =
    testList "morphirup macOS ARM64 platform gates" [
        test "a macOS manager uses its process architecture for the setup platform" {
            Expect.equal
                (Cli.executionPlatform Runtime.InteropServices.OSPlatform.OSX Runtime.InteropServices.Architecture.Arm64 (Some Platform.OsxArm64) [ "setup"; "--yes" ])
                (Some Platform.OsxArm64) "native ARM64"
            Expect.equal
                (Cli.executionPlatform Runtime.InteropServices.OSPlatform.OSX Runtime.InteropServices.Architecture.X64 (Some Platform.OsxArm64) [ "run"; "--"; "--version" ])
                (Some Platform.OsxX64) "an x64 manager under emulation does not claim ARM64"
        }
        testCaseTask "release acquisition reads the osx-arm64 bundle" (fun () -> task {
            let raw = bundleFor "osx-arm64" ".tar.gz"
            let bundle = ReleaseBundleDocument.read raw |> parsed
            let sums =
                sha raw + "  morphir-toolchain-osx-arm64.json\n"
                + (bundle.Assets |> List.map (fun asset -> asset.Sha256.Text + "  " + asset.Name.Text + "\n") |> String.concat "")
            let transport = CountingTransport(Map.ofList [ "morphir-toolchain-osx-arm64.json", raw; "SHA256SUMS", Encoding.UTF8.GetBytes sums ])
            let! inspected, _, _ = AcquisitionDelivery.inspectAsync transport Platform.OsxArm64 "0.4.0-alpha.5" CancellationToken.None
            Expect.equal inspected.Platform Platform.OsxArm64 "the release bundle of this platform"
            Expect.equal transport.Calls 2 "checksums and bundle only"
        })
        testCaseTask "release acquisition still refuses osx-x64 before a request" (fun () -> task {
            let transport = CountingTransport Map.empty
            let! code = task {
                try
                    let! _ = AcquisitionDelivery.inspectAsync transport Platform.OsxX64 "0.4.0-alpha.5" CancellationToken.None
                    return None
                with LocalCandidate.Refused(code, _) -> return Some code
            }
            Expect.equal code (Some "acquisition.platform_mismatch") "Intel macOS is not delivered"
            Expect.equal transport.Calls 0 "no request"
        })
        testCaseTask "release setup on an osx-arm64 manager passes the platform gate" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let transport = CountingTransport Map.empty
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            let! exit =
                Cli.runWithTransportAsync
                    { Out = output; Error = errors; CurrentDirectory = sandbox.Work; Platform = Some Platform.OsxArm64 }
                    input [ "setup"; "--dry-run"; "--home"; Path.Combine(sandbox.Work, "home") ] transport CancellationToken.None
            let text = Encoding.UTF8.GetString(output.ToArray())
            Expect.equal exit 1 "the scripted transport has no assets"
            Expect.isFalse (text.Contains "setup.platform_mismatch") text
            Expect.isGreaterThan transport.Calls 0 "acquisition started"
        })
        testCaseTask "explicit local setup on an osx-arm64 manager passes the platform gate" (fun () -> task {
            use sandbox = TestSandbox.Create()
            let candidate = Path.Combine(sandbox.Work, "candidate.json")
            File.WriteAllBytes(candidate, Encoding.UTF8.GetBytes "{\"$schema\":\"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/candidate.schema.json\",\"origin\":\"release-checksum\"}")
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            let home = Path.Combine(sandbox.Work, "home")
            let! exit =
                Cli.runAsync
                    { Out = output; Error = errors; CurrentDirectory = sandbox.Work; Platform = Some Platform.OsxArm64 }
                    input
                    [ "setup"; "--candidate"; candidate; "--local-archive"; Path.Combine(sandbox.Work, "host.tar.gz")
                      "--local-bundle"; Path.Combine(sandbox.Work, "bundle.json"); "--local-developer"; "--dry-run"; "--home"; home ]
                    CancellationToken.None
            let text = Encoding.UTF8.GetString(output.ToArray())
            Expect.equal exit 1 "the candidate is a release-checksum candidate"
            Expect.stringContains text "local.origin_mismatch" "the gate is lifted; the next check refuses"
            Expect.isFalse (Directory.Exists home) "no state"
        })
        testCaseTask "explicit local setup on an osx-x64 manager is still refused" (fun () -> task {
            use sandbox = TestSandbox.Create()
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            let! exit =
                Cli.runAsync
                    { Out = output; Error = errors; CurrentDirectory = sandbox.Work; Platform = Some Platform.OsxX64 }
                    input
                    [ "setup"; "--candidate"; "c.json"; "--local-archive"; "a.tar.gz"; "--local-bundle"; "b.json"; "--local-developer"; "--dry-run"; "--home"; Path.Combine(sandbox.Work, "home") ]
                    CancellationToken.None
            Expect.equal exit 1 "unsupported manager"
            Expect.stringContains (Encoding.UTF8.GetString(output.ToArray())) "local.platform_mismatch" "platform refusal"
        })
    ]

// A Mach-O header: 64-bit magic, CPU type, CPU subtype, file type, and zero load commands.
let private machO (cpu: uint32) (fileType: uint32) =
    let bytes = Array.zeroCreate<byte> 64
    BitConverter.GetBytes(0xFEEDFACFu).CopyTo(bytes, 0)
    BitConverter.GetBytes(cpu).CopyTo(bytes, 4)
    BitConverter.GetBytes(fileType).CopyTo(bytes, 12)
    bytes

let private arm64 = 0x0100000Cu
let private x86_64 = 0x01000007u

let private runtimeFiles entry (entryBytes: byte array) =
    let text (value: string) = Encoding.UTF8.GetBytes value
    [ entry, entryBytes, true
      "createdump", Array.create 16 1uy, true
      "morphir.dll", Array.create 16 2uy, false
      "libhostfxr.dylib", Array.create 16 3uy, false
      "libhostpolicy.dylib", Array.create 16 4uy, false
      "libcoreclr.dylib", Array.create 16 5uy, false
      "System.Private.CoreLib.dll", Array.create 16 6uy, false
      "FSharp.Core.dll", Array.create 16 7uy, false
      "FSharp.Compiler.Service.dll", Array.create 16 8uy, false
      "Morphir.CodeGeneration.FSharp.dll", Array.create 16 9uy, false
      "morphir.runtimeconfig.json", text "{\"runtimeOptions\":{\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"11.0.0\"}]}}", false
      "morphir.deps.json", text "{\"runtimeTarget\":{\"name\":\".NETCoreApp,Version=v11.0/osx-arm64\"}}", false ]

let private tarGz (files: (string * byte array * bool) list) =
    use output = new MemoryStream()
    do
        use gzip = new GZipStream(output, CompressionLevel.Fastest, true)
        use tar = new TarWriter(gzip, TarEntryFormat.Ustar, false)
        for name, bytes, executable in files do
            let entry = UstarTarEntry(TarEntryType.RegularFile, name)
            entry.Mode <-
                if executable then enum<UnixFileMode> 0o755 else enum<UnixFileMode> 0o644
            entry.DataStream <- new MemoryStream(bytes)
            tar.WriteEntry entry
    output.ToArray()

let private zip (files: (string * byte array * bool) list) =
    use output = new MemoryStream()
    do
        use archive = new ZipArchive(output, ZipArchiveMode.Create, true)
        for name, bytes, _ in files do
            use stream = archive.CreateEntry(name).Open()
            stream.Write bytes
    output.ToArray()

let private hostCandidate entry (files: (string * byte array * bool) list) (archive: byte array) : LocalHostCandidate =
    let binding (path: string) (bytes: byte array) : TargetBinding =
        { Target = TargetPath.tryParse path |> parsed; Sha256 = Sha256.ofBytes bytes }
    let entryBytes = files |> List.pick (fun (name, bytes, _) -> if name = entry then Some bytes else None)
    { ManifestSha256 = Sha256.ofBytes [| 1uy |]
      Host = { Implementation = Token.tryParse "dotnet" |> parsed; Deployment = Token.tryParse "self-contained" |> parsed; Version = SemVer.tryParse "0.4.0-alpha.5" |> parsed }
      Platform = Platform.OsxArm64
      SourceCommit = NonBlankText.tryParse (String('c', 40)) |> parsed
      BuildIdentity = NonBlankText.tryParse "synthetic-unit-only" |> parsed
      ArchiveSha256 = Sha256.ofBytes archive
      ArchiveBytes = uint64 archive.Length
      EntryPoint = binding entry entryBytes
      Files = files |> List.map (fun (name, bytes, _) -> { Artifact = binding name bytes; Bytes = uint64 bytes.Length }) }

let private inspect (archive: byte array) candidate =
    refusal (fun () ->
        use source = new MemoryStream(archive, false)
        LocalInstallation.validateArchive source candidate ignore CancellationToken.None)

[<Tests>]
let hostArchives =
    testList "morphirup macOS ARM64 host archives" [
        test "a tar.gz closure with an ARM64 Mach-O executable entry point passes inspection" {
            let files = runtimeFiles "morphir" (machO arm64 2u)
            let archive = tarGz files
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) None "the closure is complete and native"
        }
        test "an x86_64 Mach-O entry point is refused" {
            let files = runtimeFiles "morphir" (machO x86_64 2u)
            let archive = tarGz files
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) (Some "local.platform_mismatch") "architecture"
        }
        test "a Mach-O library entry point is refused" {
            let files = runtimeFiles "morphir" (machO arm64 6u)
            let archive = tarGz files
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) (Some "local.platform_mismatch") "an executable, not a dylib"
        }
        test "a PE entry point is refused on osx-arm64" {
            let pe = Array.zeroCreate<byte> 64
            pe[0] <- byte 'M'
            pe[1] <- byte 'Z'
            let files = runtimeFiles "morphir" pe
            let archive = tarGz files
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) (Some "local.platform_mismatch") "not a Mach-O image"
        }
        test "an entry point named morphir.exe is refused on osx-arm64" {
            let files = runtimeFiles "morphir.exe" (machO arm64 2u)
            let archive = tarGz files
            Expect.equal (inspect archive (hostCandidate "morphir.exe" files archive)) (Some "local.launch_structure") "the Unix entry point"
        }
        test "a ZIP archive is refused for an osx-arm64 candidate" {
            let files = runtimeFiles "morphir" (machO arm64 2u)
            let archive = zip files
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) (Some "local.archive_format") "the declared tar.gz format"
        }
        test "a tar.gz archive with a file outside the inventory is refused" {
            let files = runtimeFiles "morphir" (machO arm64 2u)
            let archive = tarGz (files @ [ "extra.dll", [| 0uy |], false ])
            Expect.equal (inspect archive (hostCandidate "morphir" files archive)) (Some "local.closure_mismatch") "complete inventory only"
        }
    ]

[<Tests>]
let installedModes =
    testList "morphirup macOS ARM64 installed modes" [
        test "extraction keeps the executable modes of the archive and the closure check needs them" {
            if OperatingSystem.IsWindows() then skiptest "Unix file modes."
            use sandbox = TestSandbox.Create()
            let files = runtimeFiles "morphir" (machO arm64 2u)
            let archive = tarGz files
            let candidate = hostCandidate "morphir" files archive
            let folder = Path.Combine(sandbox.Work, "host")
            Directory.CreateDirectory folder |> ignore
            do
                use source = new MemoryStream(archive, false)
                use root = Morphir.Extensions.Installation.SafeDirectory.Open(folder, false)
                LocalInstallation.extractHost source candidate root CancellationToken.None
            let mode name = File.GetUnixFileMode(Path.Combine(folder, name))
            Expect.isTrue (mode "morphir" &&& UnixFileMode.UserExecute = UnixFileMode.UserExecute) "entry point is executable"
            Expect.isTrue (mode "createdump" &&& UnixFileMode.UserExecute = UnixFileMode.UserExecute) "createdump is executable"
            Expect.equal (mode "morphir.dll" &&& (UnixFileMode.UserExecute ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherExecute)) (enum<UnixFileMode> 0) "a library is not executable"
            Expect.equal (File.ReadAllBytes(Path.Combine(folder, "morphir"))) (machO arm64 2u) "the Mach-O bytes are unchanged"
            let check () =
                refusal (fun () ->
                    use root = Morphir.Extensions.Installation.SafeDirectory.Open(folder, false)
                    LocalInstallation.checkFolder root candidate true CancellationToken.None)
            Expect.equal (check ()) None "the installed closure is complete"
            File.SetUnixFileMode(Path.Combine(folder, "morphir"), enum<UnixFileMode> 0o644)
            Expect.equal (check ()) (Some "local.launch_structure") "an entry point without its executable mode is refused"
        }
    ]
    |> testLabel "PlatformSpecific"

[<Tests>]
let unixLaunch =
    testList "morphirup Unix launch" [
        test "the launch environment removes the .NET overrides and sets the isolation variables" {
            let environment = Collections.Generic.Dictionary<string, string | null>(StringComparer.Ordinal)
            for name in [ "DOTNET_ROOT"; "DOTNET_ROOT_X64"; "DOTNET_ROOT_ARM64"; "DOTNET_ROOT(x86)"; "DOTNET_SHARED_STORE"; "DOTNET_ADDITIONAL_DEPS"; "DOTNET_STARTUP_HOOKS"; "PATH" ] do
                environment[name] <- "/somewhere"
            LocalProcess.isolate environment "/home/x" "/work" (Some "/overlay")
            for name in [ "DOTNET_ROOT"; "DOTNET_ROOT_X64"; "DOTNET_ROOT_ARM64"; "DOTNET_ROOT(x86)"; "DOTNET_SHARED_STORE"; "DOTNET_ADDITIONAL_DEPS"; "DOTNET_STARTUP_HOOKS" ] do
                Expect.isFalse (environment.ContainsKey name) name
            Expect.equal environment["PATH"] "/somewhere" "other variables stay"
            Expect.equal environment["MORPHIR_HOME"] "/home/x" "home"
            Expect.equal environment["MORPHIR_ISOLATED"] "1" "isolated"
            Expect.equal environment["MORPHIR_DISCOVERY_BOUNDARY"] "/work" "boundary"
            Expect.equal environment["DOTNET_MULTILEVEL_LOOKUP"] "0" "no global lookup"
            Expect.equal environment["MORPHIR_EXTENSION_OVERLAY"] "/overlay" "overlay"
        }
        testCaseTask "a Unix child starts with the isolated environment and its exit code" (fun () -> task {
            if OperatingSystem.IsWindows() then skiptest "Unix launch."
            use sandbox = TestSandbox.Create()
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            let! exit = LocalProcess.runAsync "/usr/bin/env" [] sandbox.Work sandbox.MorphirHome (LocalProcess.Input.Redirect input) output errors CancellationToken.None
            let text = Encoding.UTF8.GetString(output.ToArray())
            Expect.equal exit 0 (errors.ToString())
            Expect.stringContains text "MORPHIR_ISOLATED=1" "isolation variable"
            Expect.stringContains text $"MORPHIR_HOME={sandbox.MorphirHome}" "home"
            Expect.stringContains text "DOTNET_MULTILEVEL_LOOKUP=0" "no global lookup"
        })
        testCaseTask "a Unix child forwards arguments without a shell and returns its exit code" (fun () -> task {
            if OperatingSystem.IsWindows() then skiptest "Unix launch."
            use sandbox = TestSandbox.Create()
            use input = new MemoryStream(Encoding.UTF8.GetBytes "in")
            use output = new MemoryStream()
            use errors = new StringWriter()
            let! exit = LocalProcess.runAsync "/bin/sh" [ "-c"; "cat; printf ' %s' \"$0\"; exit 7"; "a b;c" ] sandbox.Work sandbox.MorphirHome (LocalProcess.Input.Redirect input) output errors CancellationToken.None
            Expect.equal exit 7 "exit code"
            Expect.equal (Encoding.UTF8.GetString(output.ToArray())) "in a b;c" "stdin and one argument vector item"
        })
        testCaseTask "cancellation terminates a Unix child" (fun () -> task {
            if OperatingSystem.IsWindows() then skiptest "Unix launch."
            use sandbox = TestSandbox.Create()
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            use cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds 300.)
            let started = Diagnostics.Stopwatch.StartNew()
            let! cancelled = task {
                try
                    let! _ = LocalProcess.runAsync "/bin/sleep" [ "30" ] sandbox.Work sandbox.MorphirHome (LocalProcess.Input.Redirect input) output errors cancellation.Token
                    return false
                with :? OperationCanceledException -> return true
            }
            Expect.isTrue cancelled "cancellation is reported"
            Expect.isLessThan started.Elapsed (TimeSpan.FromSeconds 10.) "the child did not run to its end"
        })
    ]
    |> testLabel "PlatformSpecific"

[<Tests>]
let homePaths =
    testList "morphirup Unix Home path" [
        test "a Home below a link is its physical path, including a part that does not exist yet" {
            if OperatingSystem.IsWindows() then skiptest "Unix links."
            use sandbox = TestSandbox.Create()
            let physical = Directory.CreateDirectory(Path.Combine(sandbox.Work, "physical")).FullName
            let link = Path.Combine(sandbox.Work, "link")
            Directory.CreateSymbolicLink(link, physical) |> ignore
            // The sandbox itself can be below a link, as $TMPDIR is below /var on macOS.
            let expected = LocalCandidate.homePath (Path.Combine(physical, "home", "next"))
            Expect.equal (LocalCandidate.homePath (Path.Combine(link, "home", "next"))) expected "the link is resolved"
            Expect.isFalse (expected.Contains "/link/") "no link in the result"
            Expect.equal (LocalCandidate.homePath "/var/x") (if OperatingSystem.IsMacOS() then "/private/var/x" else "/var/x") "the system link of macOS"
        }
        test "a Home that is itself a link keeps its last part, so the Home check sees the link" {
            if OperatingSystem.IsWindows() then skiptest "Unix links."
            use sandbox = TestSandbox.Create()
            let physical = Directory.CreateDirectory(Path.Combine(sandbox.Work, "physical")).FullName
            let link = Path.Combine(sandbox.Work, "home-link")
            Directory.CreateSymbolicLink(link, physical) |> ignore
            Expect.equal (LocalCandidate.homePath link) (Path.Combine(LocalCandidate.homePath sandbox.Work, "home-link")) "the last part is not followed"
        }
        testCaseTask "setup refuses a Home that is a link, before it reads an input" (fun () -> task {
            if OperatingSystem.IsWindows() then skiptest "Unix links."
            use sandbox = TestSandbox.Create()
            let physical = Directory.CreateDirectory(Path.Combine(sandbox.Work, "physical")).FullName
            let link = Path.Combine(sandbox.Work, "home-link")
            Directory.CreateSymbolicLink(link, physical) |> ignore
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            let! exit =
                Cli.runAsync
                    { Out = output; Error = errors; CurrentDirectory = sandbox.Work; Platform = Some Platform.OsxArm64 }
                    input
                    [ "setup"; "--candidate"; Path.Combine(sandbox.Work, "absent.json"); "--local-archive"; "a.tar.gz"
                      "--local-bundle"; "b.json"; "--local-developer"; "--dry-run"; "--home"; link ]
                    CancellationToken.None
            let text = Encoding.UTF8.GetString(output.ToArray())
            let diagnostic =
                match Syntax.parseJson (output.ToArray()) |> parsed with
                | Value.Object fields ->
                    match fields |> List.tryFind (fun (name, _) -> name = "diagnostic") with
                    | Some(_, Value.Object diagnostic) -> diagnostic |> List.tryPick (function "code", Value.String code -> Some code | _ -> None)
                    | _ -> None
                | _ -> None
            Expect.equal exit 1 text
            Expect.equal diagnostic (Some "local.extension.path_escape") text
            Expect.isEmpty (Directory.GetFileSystemEntries physical) "nothing is written into the link's target"
        })
        test "a Home below a cycle of links is refused" {
            if OperatingSystem.IsWindows() then skiptest "Unix links."
            use sandbox = TestSandbox.Create()
            let a = Path.Combine(sandbox.Work, "a")
            let b = Path.Combine(sandbox.Work, "b")
            Directory.CreateSymbolicLink(a, b) |> ignore
            Directory.CreateSymbolicLink(b, a) |> ignore
            Expect.equal (refusal (fun () -> LocalCandidate.homePath (Path.Combine(a, "home")))) (Some "local.home_links") "bounded resolution"
        }
    ]
    |> testLabel "PlatformSpecific"

[<Tests>]
let unsafeModes =
    testList "morphirup host archive unsafe modes" [
        test "setuid, setgid and world-writable archive modes are not installed" {
            if OperatingSystem.IsWindows() then skiptest "Unix file modes."
            use sandbox = TestSandbox.Create()
            let files = runtimeFiles "morphir" (machO arm64 2u)
            let modes = Map.ofList [ "morphir", 0o4777; "createdump", 0o2755; "morphir.dll", 0o666 ]
            use memory = new MemoryStream()
            do
                use gzip = new GZipStream(memory, CompressionLevel.Fastest, true)
                use tar = new TarWriter(gzip, TarEntryFormat.Ustar, false)
                for name, bytes, executable in files do
                    let entry = UstarTarEntry(TarEntryType.RegularFile, name)
                    entry.Mode <- enum<UnixFileMode> (modes.TryFind name |> Option.defaultValue (if executable then 0o755 else 0o644))
                    entry.DataStream <- new MemoryStream(bytes)
                    tar.WriteEntry entry
            let archive = memory.ToArray()
            let candidate = hostCandidate "morphir" files archive
            let folder = Directory.CreateDirectory(Path.Combine(sandbox.Work, "host")).FullName
            do
                use source = new MemoryStream(archive, false)
                use root = Morphir.Extensions.Installation.SafeDirectory.Open(folder, false)
                LocalInstallation.extractHost source candidate root CancellationToken.None
            let mode name = File.GetUnixFileMode(Path.Combine(folder, name))
            Expect.equal (mode "morphir") (enum<UnixFileMode> 0o755) "no setuid, no group or other write"
            Expect.equal (mode "createdump") (enum<UnixFileMode> 0o755) "no setgid"
            Expect.equal (mode "morphir.dll") (enum<UnixFileMode> 0o644) "no group or other write"
        }
    ]
    |> testLabel "PlatformSpecific"

[<Tests>]
let unixDescendants =
    testList "morphirup Unix launch descendants" [
        testCaseTask "cancellation also ends a grandchild that the host started" (fun () -> task {
            if OperatingSystem.IsWindows() then skiptest "Unix launch."
            use sandbox = TestSandbox.Create()
            use input = new MemoryStream()
            use output = new MemoryStream()
            use errors = new StringWriter()
            use cancellation = new CancellationTokenSource()
            let run = LocalProcess.runAsync "/bin/sh" [ "-c"; "/bin/sleep 300 & echo $!; wait" ] sandbox.Work sandbox.MorphirHome (LocalProcess.Input.Redirect input) output errors cancellation.Token
            let started = Diagnostics.Stopwatch.StartNew()
            while output.Length = 0L && started.Elapsed < TimeSpan.FromSeconds 10. do
                do! Task.Delay 50
            let grandchild = int (Encoding.ASCII.GetString(output.ToArray()).Trim())
            cancellation.Cancel()
            let! cancelled = task {
                try
                    let! _ = run
                    return false
                with :? OperationCanceledException -> return true
            }
            Expect.isTrue cancelled "cancellation is reported"
            let alive () =
                try
                    use p = Diagnostics.Process.GetProcessById grandchild
                    not p.HasExited
                with :? ArgumentException -> false
            let waited = Diagnostics.Stopwatch.StartNew()
            while alive () && waited.Elapsed < TimeSpan.FromSeconds 5. do
                do! Task.Delay 50
            let survived = alive ()
            if survived then
                try (Diagnostics.Process.GetProcessById grandchild).Kill() with _ -> ()
            Expect.isFalse survived "the grandchild was terminated with the host"
        })
    ]
    |> testLabel "PlatformSpecific"
