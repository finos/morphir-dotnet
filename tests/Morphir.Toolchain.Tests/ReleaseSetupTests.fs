// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Toolchain.Tests.ReleaseSetupTests

open System
open System.IO
open System.Text
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
open Expecto
open Morphir.Toolchain
open Morphir.Toolchain.IO
open Morphir.TestSupport
open Morphirup

type private BytesTransport(files: Map<string,byte array>) =
    let mutable calls=0
    member _.Calls = calls
    interface IAcquisitionTransport with
        member _.OpenAsync(source,ct) = task {
            ct.ThrowIfCancellationRequested()
            calls <- calls+1
            let bytes = files.TryFind(Path.GetFileName source.AbsolutePath) |> Option.defaultWith(fun () -> raise (HttpRequestException "Missing scripted asset."))
            let stream = new MemoryStream(bytes,false)
            return {
                new IAcquisitionResponse with
                    member _.Content = stream
                    member _.DeclaredLength = Some(int64 bytes.Length)
                interface IAsyncDisposable with member _.DisposeAsync() = stream.DisposeAsync()
            }
        }

type private RedirectHandler() =
    inherit HttpMessageHandler()
    let mutable calls=0
    member _.Calls = calls
    override _.SendAsync(_,_) =
        calls <- calls+1
        let response = new HttpResponseMessage(HttpStatusCode.Redirect)
        response.Headers.Location <- Uri "https://unrelated.example/asset.zip"
        Task.FromResult response

let private tinyBundle () =
    let hash = String('a',64)
    let asset role =
      let name = match role with "host-candidate" -> "morphir-host-candidate-win-x64.json" | "selection" -> "morphir-extensions-win-x64.json" | _ -> role+"-win-x64.zip"
      Value.Object [
        "name",Value.String name
        "url",Value.String(ReleaseBundleDocument.repository+"/releases/download/v0.4.0-alpha.5/"+name)
        "bytes",Value.Number "3";"sha256",Value.String hash;"role",Value.String role]
    Value.Object [
        "$schema",Value.String(ReleaseBundleDocument.schema "bundle")
        "version",Value.String "0.4.0-alpha.5";"sourceCommit",Value.String(String('b',40));"platform",Value.String "win-x64"
        "assets",Value.Array(List.map asset ReleaseBundleDocument.roles)] |> JsonText.toBytes

let private readinessRefusal report =
    try
        report () |> ignore
        failtest "Readiness unexpectedly succeeded."
    with LocalCandidate.Refused(code,message) ->
        Expect.equal code "setup.readiness_failed" "manager refusal remains stable"
        message

[<Tests>]
let readinessReports = testList "ReadinessReports" [
    testCase "failed host stdout carries its own diagnostic instead of a stderr log" (fun () ->
        let output = Encoding.UTF8.GetBytes """{"success":false,"diagnostics":[{"code":"extension.unavailable","message":"The F# frontend did not start: CreateProcessW"}]}"""
        let message = readinessRefusal (fun () -> ComposedInstallation.readLaunchReport 1 output "Foundatio.Mediator registered 3 handlers")
        Expect.stringContains message "extension.unavailable: The F# frontend did not start: CreateProcessW" "actual host cause survives"
    )
    testCase "unsuccessful JSON also preserves diagnostics when the host exits zero" (fun () ->
        let output = Encoding.UTF8.GetBytes """{"success":false,"diagnostics":[{"code":"extension.timeout","message":"The frontend deadline expired."}]}"""
        let message = readinessRefusal (fun () -> ComposedInstallation.readLaunchReport 0 output "")
        Expect.stringContains message "extension.timeout: The frontend deadline expired." "success flag is checked independently"
    )
    testCase "failed host without a JSON report retains stderr" (fun () ->
        let message = readinessRefusal (fun () -> ComposedInstallation.readLaunchReport 1 (Encoding.UTF8.GetBytes "not JSON") "host launch failed")
        Expect.stringContains message "host launch failed" "fallback retains available failure evidence"
    )
    testCase "successful host report retains its provider observation" (fun () ->
        let output = Encoding.UTF8.GetBytes """{"success":true,"provider":{"extensionId":"org.finos.frontend"},"diagnostics":[]}"""
        let provider,_ = ComposedInstallation.readLaunchReport 0 output "a harmless host log"
        Expect.equal provider (Value.Object ["extensionId",Value.String "org.finos.frontend"]) "existing observation validation follows unchanged"
    )
]

[<Tests>]
let stageCleanup = testLabel "PlatformSpecific" <| testList "SetupStageCleanup" [
    testCaseTask "cleanup contention preserves completed setup result and writes one stderr warning" (fun () -> task {
        use sandbox = TestSandbox.Create()
        use root = Morphir.Extensions.Installation.SafeDirectory.Open(Path.Combine(sandbox.Work,"home"),true)
        use parent = root.Child("tmp",true)
        use stage = parent.Child("local-toolchain-contention",true)
        use owner = stage.File(".owner",true,true)
        use errors = new StringWriter()
        use runningHost = Morphir.Extensions.Installation.FileSafety.mutationLease root
        let folder = stage.Path
        let elapsed = System.Diagnostics.Stopwatch.StartNew()
        let! achieved = LocalInstallation.withPrivateStageAsync root stage owner errors (fun () -> task {
            return "installed",0
        })
        Expect.equal achieved ("installed",0) "cleanup must not rewrite an achieved setup result or exit code"
        Expect.isTrue (elapsed.Elapsed >= TimeSpan.FromSeconds 4.5) "the real five-second lease deadline was exercised"
        Expect.isTrue runningHost.CanRead "the host still holds its lease when setup completes"
        Expect.isTrue (Directory.Exists folder) "deferred private stage is retained"
        let lines = errors.ToString().Split([|'\r';'\n'|],StringSplitOptions.RemoveEmptyEntries)
        Expect.equal lines.Length 1 "one stderr line, no additional result output"
        Expect.stringContains lines[0] folder "warning identifies the retained stage"
        Expect.stringContains lines[0] "cleanup deferred" "warning explains why bytes remain"
    })
    testCaseTask "successful setup removes its readonly private stage and preserves shared tmp siblings" (fun () -> task {
        use sandbox = TestSandbox.Create()
        use root = Morphir.Extensions.Installation.SafeDirectory.Open(Path.Combine(sandbox.Work,"home"),true)
        use parent = root.Child("tmp",true)
        use stage = parent.Child("local-toolchain-test",true)
        use owner = stage.File(".owner",true,true)
        File.WriteAllText(Path.Combine(parent.Path,"unrelated.txt"),"shared tmp state")
        let stagePath = stage.Path
        let! result = LocalInstallation.withPrivateStageAsync root stage owner TextWriter.Null (fun () -> task {
            use child = stage.Child("work",true)
            LocalInstallation.writeFile child "readonly.txt" [|1uy|]
            File.SetAttributes(Path.Combine(child.Path,"readonly.txt"),FileAttributes.ReadOnly)
            return 42
        })
        Expect.equal result 42 "operation result survives cleanup"
        Expect.isFalse (Directory.Exists stagePath) "completed private stage is removed"
        Expect.equal (File.ReadAllText(Path.Combine(parent.Path,"unrelated.txt"))) "shared tmp state" "unknown siblings are not traversed"
    })
    testCaseTask "refused setup removes its stage before returning its original refusal" (fun () -> task {
        use sandbox = TestSandbox.Create()
        use root = Morphir.Extensions.Installation.SafeDirectory.Open(Path.Combine(sandbox.Work,"home"),true)
        use parent = root.Child("tmp",true)
        use stage = parent.Child("local-toolchain-test",true)
        use owner = stage.File(".owner",true,true)
        let stagePath = stage.Path
        let! refused = task {
            try
                do! LocalInstallation.withPrivateStageAsync root stage owner TextWriter.Null (fun () -> task {
                    LocalInstallation.writeFile stage "partial.zip" [|1uy;2uy|]
                    return LocalCandidate.refuse "setup.readiness_failed" "original host refusal"
                })
                return false
            with LocalCandidate.Refused(code,message) ->
                Expect.equal (code,message) ("setup.readiness_failed","original host refusal") "original cause is preserved"
                return true
        }
        Expect.isTrue refused "refusal remains a refusal"
        Expect.isFalse (Directory.Exists stagePath) "refused private stage is removed"
    })
]

[<Tests>]
let providerRouting =
    let id text = ExtensionId.tryParsePreview text |> function Ok value -> value | Error _ -> failtest "Invalid test identity."
    let frontend,backend = id "org.finos.frontend",id "org.finos.backend"
    let route = ComposedInstallation.providerArguments frontend backend
    testList "InstalledProviderRouting" [
        testCase "CSharp target with an output folder named fsharp keeps the built-in route" (fun () ->
            let args = ["generate";"--target";"csharp";"--output";"fsharp"]
            Expect.equal (route args) args "a folder name is not the target option value")
        for options in [["--target";"fsharp"];["--target=fsharp"];["--target:fsharp"]] do
            testCase ("FSharp target " + String.concat " " options) (fun () ->
                let args = "generate"::options
                Expect.equal (route args) (args@["--extension-id";backend.Text]) "selected FSharp backend")
        testCase "the host has no short target option" (fun () ->
            let args = ["generate";"-t";"fsharp"]
            Expect.equal (route args) args "leave the host's usage refusal unchanged")
        for options in [["--language";"fsharp"];["--language=fsharp"];["--language:fsharp"];["-l";"fsharp"];["-l=fsharp"];["-l:fsharp"];["-lfsharp"]] do
            testCase ("explicit FSharp language " + String.concat " " options) (fun () ->
                let args = "compile"::options
                Expect.equal (route args) (args@["--extension-id";frontend.Text]) "selected FSharp frontend")
        testCase "another explicit language keeps its host-selected frontend" (fun () ->
            let args = ["compile";"--language";"python"]
            Expect.equal (route args) args "never force the FSharp frontend")
        testCase "absent language leaves project configuration with the host" (fun () ->
            Expect.equal (route ["compile"]) ["compile"] "the manager does not duplicate configuration resolution")
        for selection in ["--extension=python";"--extension-id:invalid"] do
            testCase ("explicit selection remains terminal " + selection) (fun () ->
                let args = ["compile";"--language";"fsharp";selection]
                Expect.equal (route args) args "no additional installed override")
    ]

[<Tests>]
let launchPathAdmission = testList "WindowsLaunchPathAdmission" [
    for platform in [Platform.WinX64;Platform.WinArm64] do
        testCase (Platform.name platform + " accepts a 259-character executable path") (fun () ->
            let home = Path.Combine(Path.GetTempPath(),"example-home")
            let executable = Path.Combine(home,String('a',259-home.Length-5)+".exe")
            Expect.equal executable.Length 259 "boundary fixture"
            LocalInstallation.checkExecutablePaths platform home [executable])
        testCase (Platform.name platform + " refuses a 260-character executable path with the Home and limit") (fun () ->
            let home = Path.Combine(Path.GetTempPath(),"example-home")
            let executable = Path.Combine(home,String('a',260-home.Length-5)+".exe")
            let refused =
                try LocalInstallation.checkExecutablePaths platform home [executable]; false
                with LocalCandidate.Refused(code,message) ->
                    Expect.equal code "local.executable_path_too_long" "specific up-front refusal"
                    Expect.stringContains message home "name the Home to shorten"
                    Expect.stringContains message "259" "usable launch path limit"
                    true
            Expect.isTrue refused "path is refused before launch")
]

[<Tests>]
let longHomeSetup = testLabel "PlatformSpecific" <| testList "WindowsReleaseSetup" [
    testCaseTask "provider path length refuses dry-run and apply before Home or archive transfer" (fun () -> task {
        let folder = Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_ASSETS" |> Option.ofObj |> Option.defaultWith (fun () -> skiptest "The official produced Windows archives are required.")
        let platform = match OperatingSystem.IsWindows(),RuntimeInformation.ProcessArchitecture with
                       | true,Architecture.Arm64 -> Platform.WinArm64
                       | true,Architecture.X64 -> Platform.WinX64
                       | _ -> skiptest "The installed Windows setup is a Windows product path."
        use sandbox = TestSandbox.Create("long-home-")
        let raw = File.ReadAllBytes(Path.Combine(folder,ReleaseBundleDocument.fileName platform))
        let bundle = ReleaseBundleDocument.read raw |> function Ok value -> value | Error _ -> failtest "Invalid produced bundle."
        let candidateName = bundle.Assets |> List.find(fun asset -> asset.Role="host-candidate") |> fun asset -> asset.Name.Text
        let candidate = File.ReadAllBytes(Path.Combine(folder,candidateName)) |> LocalComposedDocument.readReleaseCandidate |> function Ok value -> value | Error _ -> failtest "Invalid produced candidate."
        let hostRelative = LocalCandidateDocument.installationPath candidate.Host + "/" + candidate.Host.EntryPoint.Target.Text
        let homeLength = 259-hostRelative.Length-1
        let home = Path.Combine(sandbox.Work,String('h',homeLength-sandbox.Work.Length-1))
        Expect.equal (Path.Combine(home,hostRelative.Replace('/',Path.DirectorySeparatorChar))).Length 259 "host itself fits; the longer provider path must be checked"
        let files = Directory.GetFiles(folder) |> Array.filter(fun name -> not(name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))) |> Array.map(fun name -> Path.GetFileName name,File.ReadAllBytes name) |> Map.ofArray
        for dryRun in [true;false] do
            let transport = BytesTransport files
            let! refused = task {
                try
                    let! _ = ComposedInstallation.setupWithTransportAsync platform home bundle.Version.Text transport dryRun CancellationToken.None
                    return false
                with LocalCandidate.Refused(code,message) ->
                    Expect.equal code "local.executable_path_too_long" "provider admission runs before transfer or effects"
                    Expect.stringContains message home "actionable Home diagnostic"
                    return true
            }
            Expect.isTrue refused "no success with an unlaunchable provider"
            Expect.isFalse (Directory.Exists home) "no Home or staging created"
    })
]

[<Tests>]
let publicationLocks = testLabel "PlatformSpecific" <| testList "ComposedPublicationLocks" [
    testCaseTask "running host captures state while publication waits for its mutation lease" (fun () -> task {
        use sandbox = TestSandbox.Create()
        use root = Morphir.Extensions.Installation.SafeDirectory.Open(Path.Combine(sandbox.Work,"home"),true)
        use locks = root.Child("locks",true)
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.)
        let ct = deadline.Token
        let! runningHost = Morphir.Extensions.Installation.FileSafety.mutationLeaseAsync root ct
        let requested = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let mutable published = false
        let acquireMutation () =
            requested.TrySetResult() |> ignore
            Morphir.Extensions.Installation.FileSafety.mutationLeaseAsync root ct
        let publishExpected (callback: Func<Task>) = task {
            use! state = locks.NamedLeaseAsync("extensions.state.lock",true,true,ct)
            do! callback.Invoke()
        }
        let publication = ComposedInstallation.publishGenerationAsync acquireMutation publishExpected (fun () -> task {
            Expect.throwsT<Morphir.Extensions.Installation.FileLeaseBusyException>
                (fun () -> use unexpected = Morphir.Extensions.Installation.FileSafety.mutationLease root in ())
                "publication still protects host files"
            Expect.throwsT<Morphir.Extensions.Installation.FileLeaseBusyException>
                (fun () -> use unexpected = locks.NamedLease("extensions.state.lock",true,false) in ())
                "publication still holds installer state"
            published <- true
        })
        let mutable captured = false
        let mutable observationError: System.Runtime.ExceptionServices.ExceptionDispatchInfo option = None
        try
            try
                do! requested.Task.WaitAsync(TimeSpan.FromSeconds 3.)
                Expect.isFalse publication.IsCompleted "publication must wait for the running host"
                use captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds 1.)
                try
                    use! state = locks.NamedLeaseAsync("extensions.state.lock",true,true,captureDeadline.Token)
                    captured <- true
                with :? OperationCanceledException -> ()
            with error ->
                observationError <- Some(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture error)
                deadline.Cancel()
        finally
            runningHost.Dispose()
        // Settle the publication before reporting failure, including on the RED path.
        let mutable publicationError: System.Runtime.ExceptionServices.ExceptionDispatchInfo option = None
        try
            try
                do! publication.WaitAsync(TimeSpan.FromSeconds 3.)
            with :? TimeoutException as error ->
                let timeout = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture error
                deadline.Cancel()
                // WaitAsync only times out its wrapper. Await the original task
                // after cancellation before disposing its roots or token source.
                try do! publication with _ -> ()
                timeout.Throw()
        with error ->
            publicationError <- Some(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture error)
        match observationError,publicationError with
        | Some error,_ | None,Some error -> error.Throw()
        | None,None -> ()
        Expect.isTrue captured "host state capture must not wait for a publisher blocked on that host"
        Expect.isTrue published "publication resumes after the host releases its lease"
        use! selectionLease = Morphir.Extensions.Installation.FileSafety.mutationLeaseAsync root ct
        Expect.isTrue selectionLease.CanRead "publication releases mutation before selection reacquires it"
    })
]

[<Tests>]
let tests = testLabel "PlatformSpecific" <| testList "WindowsReleaseSetup" [
    testCaseTask "wrong release checksum refuses before Home, staging or installation" (fun () -> task {
        use sandbox = TestSandbox.Create()
        let raw = tinyBundle ()
        let bundle = ReleaseBundleDocument.read raw |> function Ok value -> value | Error _ -> failtest "Invalid scripted bundle."
        let sums = String('c',64)+"  morphir-toolchain-win-x64.json\n" + (bundle.Assets |> List.map(fun asset -> asset.Sha256.Text+"  "+asset.Name.Text+"\n") |> String.concat "")
        let transport = BytesTransport(Map.ofList ["morphir-toolchain-win-x64.json",raw;"SHA256SUMS",Encoding.UTF8.GetBytes sums])
        let home = Path.Combine(sandbox.Work,"absent-home")
        let! refused = task {
            try
                let! _ = ComposedInstallation.setupWithTransportAsync Platform.WinX64 home "0.4.0-alpha.5" transport false CancellationToken.None
                return false
            with LocalCandidate.Refused(code,_) ->
                Expect.equal code "local.inconsistent_document" "checksum coverage refusal"
                return true
        }
        Expect.isTrue refused "no success on checksum mismatch"
        Expect.isFalse (Directory.Exists home) "no state or stage"
        Expect.equal transport.Calls 2 "only checksums and bundle were fetched"
    })
    testCaseTask "a redirect outside the release storage allowlist is not followed" (fun () -> task {
        let handler = new RedirectHandler()
        use transport = new AcquisitionDelivery.ReleaseTransport(handler)
        let! refused = task {
            try
                let! response = (transport :> IAcquisitionTransport).OpenAsync(Uri(ReleaseBundleDocument.repository+"/releases/download/v0.4.0-alpha.5/host.zip"),CancellationToken.None)
                do! response.DisposeAsync()
                return false
            with :? HttpRequestException -> return true
        }
        Expect.isTrue refused "release redirect policy"
        Expect.equal handler.Calls 1 "the unapproved host received no request"
    })
    test "the default release is the manager version without build metadata" {
        use sandbox = TestSandbox.Create()
        Expect.equal (ComposedInstallation.releaseVersion sandbox.Work "0.4.0-alpha.5+actual-source") "0.4.0-alpha.5" "no discovery or moving channel"
    }
    test "readiness classifies existing ownership metadata separately from generated artifacts" {
        ComposedInstallation.checkGeneratedSet [|".morphir-generated.json";"Pricing.fs"|] (Value.Array [Value.String "Pricing.fs"])
    }
    test "readiness still refuses an extra generated output" {
        Expect.throwsT<LocalCandidate.Refused> (fun () ->
            ComposedInstallation.checkGeneratedSet [|".morphir-generated.json";"Pricing.fs";"other.fs"|] (Value.Array [Value.String "Pricing.fs"])) "complete output set"
    }
    testCaseTask "an ARM64 request cannot acquire an x64 bundle" (fun () -> task {
        let raw = tinyBundle ()
        let bundle = ReleaseBundleDocument.read raw |> function Ok value -> value | Error _ -> failtest "Invalid scripted bundle."
        let sums = (Sha256.ofBytes raw).Text+"  morphir-toolchain-win-arm64.json\n" + (bundle.Assets |> List.map(fun asset -> asset.Sha256.Text+"  "+asset.Name.Text+"\n") |> String.concat "")
        let transport = BytesTransport(Map.ofList ["morphir-toolchain-win-arm64.json",raw;"SHA256SUMS",Encoding.UTF8.GetBytes sums])
        try
            let! _ = AcquisitionDelivery.inspectAsync transport Platform.WinArm64 "0.4.0-alpha.5" CancellationToken.None
            failtest "Cross-RID bundle was accepted."
        with LocalCandidate.Refused(code,_) -> Expect.equal code "acquisition.platform_mismatch" "no archive or guest fallback"
    })
    testCaseTask "explicit local setup cannot relabel a release-checksum candidate" (fun () -> task {
        use sandbox = TestSandbox.Create()
        let candidate = Path.Combine(sandbox.Work,"candidate.json")
        let raw = Encoding.UTF8.GetBytes("{\"$schema\":\"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.4/candidate.schema.json\",\"origin\":\"release-checksum\"}")
        File.WriteAllBytes(candidate,raw)
        let home = Path.Combine(sandbox.Work,"absent-home")
        try
            let! _ = ComposedInstallation.setupLocalAsync Platform.WinArm64 home candidate (Path.Combine(sandbox.Work,"archive.zip")) (Path.Combine(sandbox.Work,"bundle.json")) true CancellationToken.None
            failtest "Local flags granted release acquisition authority."
        with LocalCandidate.Refused(code,_) -> Expect.equal code "local.origin_mismatch" "consent does not replace official acquisition"
        Expect.isFalse (Directory.Exists home) "no local stage or state"
    })
]

/// A Home that an earlier composed family set up: its selection pointer and one generation with its candidate.
/// Only the $schema of each document matters; the manager must not read any other member.
let earlierHomeAt (home: string) (family: string) =
    let generation = String('e', 64)
    let folder = Path.Combine(home, "toolchains", "generations", generation)
    Directory.CreateDirectory folder |> ignore
    let schema kind = $"https://morphir.finos.org/schemas/local-toolchains/{family}/{kind}.schema.json"
    File.WriteAllText(
        Path.Combine(home, "toolchains", "default.json"),
        $"""{{"$schema":"{schema "selection"}","origin":"local-developer","generationSha256":"{generation}","receiptSha256":"{String('f', 64)}","receiptPath":"toolchains/generations/{generation}/receipt.json"}}""")
    File.WriteAllText(Path.Combine(folder, "generation.json"), $"""{{"$schema":"{schema "generation"}"}}""")
    File.WriteAllText(Path.Combine(folder, "candidate.json"), $"""{{"$schema":"{schema "candidate"}"}}""")
    File.WriteAllText(Path.Combine(folder, "receipt.json"), $"""{{"$schema":"{schema "receipt"}"}}""")
    LocalCandidate.homePath home, generation

/// The producer supplies actual full folders, archives and metadata. No test creates executable substitutes.
[<Tests>]
let earlierHomeSetup = testLabel "PlatformSpecific" <| testList "WindowsReleaseSetup" [
    for family in [ "0.1.0-preview.2"; "0.1.0-preview.3" ] do
        testCaseTask $"release setup in a {family} Home installs preview.4 beside it and selects it" (fun () -> task {
            let folder = Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_ASSETS" |> Option.ofObj |> Option.defaultWith(fun () -> skiptest "Actual PackWindowsToolchain assets were not supplied.")
            let platform =
                match Platform.current () with
                | Some platform when Platform.isLocalToolchain platform && File.Exists(Path.Combine(folder, ReleaseBundleDocument.fileName platform)) -> platform
                | _ -> skiptest "The supplied producer assets are not for this test process."
            match Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_RID" |> Option.ofObj with
            | Some rid -> Expect.equal rid (Platform.name platform) "the supplied producer RID must match the actual test process"
            | None -> ()
            use sandbox = TestSandbox.Create("earlier-home-")
            let home, earlier = earlierHomeAt (Path.Combine(sandbox.Work, "home")) family
            let generationFolder = Path.Combine(home, "toolchains", "generations", earlier)
            let before = Directory.GetFiles generationFolder |> Array.sort |> Array.map (fun file -> Path.GetFileName file, File.ReadAllBytes file)
            let transport = {
                new IAcquisitionTransport with
                    member _.OpenAsync(source,ct) = task {
                        ct.ThrowIfCancellationRequested()
                        let stream = File.OpenRead(Path.Combine(folder,Path.GetFileName source.AbsolutePath))
                        return {
                            new IAcquisitionResponse with
                                member _.Content = stream
                                member _.DeclaredLength = Some stream.Length
                            interface IAsyncDisposable with member _.DisposeAsync() = stream.DisposeAsync()
                        }
                    }
            }
            let bundle = ReleaseBundleDocument.read (File.ReadAllBytes(Path.Combine(folder,ReleaseBundleDocument.fileName platform))) |> function Ok bundle -> bundle | Error _ -> failtest "Actual producer bundle is invalid."
            let! _,installed = ComposedInstallation.setupWithTransportAsync platform home bundle.Version.Text transport false CancellationToken.None
            let generation = installed |> Option.defaultWith (fun () -> failtest "setup --yes published no generation")
            let pointer = Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(home, "toolchains", "default.json")))
            Expect.equal (pointer.RootElement.GetProperty("$schema").GetString()) "https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.4/selection.schema.json" "the pointer is preview.4"
            Expect.equal (pointer.RootElement.GetProperty("generationSha256").GetString()) generation.Text "the pointer names the new generation"
            let! snapshot = ComposedInstallation.selected home CancellationToken.None
            Expect.equal snapshot.Family "0.1.0-preview.4" "the Home selects preview.4"
            let after = Directory.GetFiles generationFolder |> Array.sort |> Array.map (fun file -> Path.GetFileName file, File.ReadAllBytes file)
            Expect.equal after before "the earlier generation stays, unread and unchanged"
        })
]

/// The producer supplies actual full folders, archives and metadata. No test creates executable substitutes.
[<Tests>]
let acquiredJourney = testLabel "PlatformSpecific" <| testList "WindowsReleaseSetup" [testCaseTask "actual installed release setup and offline process workflow" (fun () -> task {
    let folder = Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_ASSETS" |> Option.ofObj |> Option.defaultWith(fun () -> skiptest "Actual PackWindowsToolchain assets were not supplied.")
    let platform =
        match OperatingSystem.IsWindows(),RuntimeInformation.ProcessArchitecture with
        | true,Architecture.X64 -> Platform.WinX64
        | true,Architecture.Arm64 -> Platform.WinArm64
        | _ -> skiptest "The actual Windows composition requires an x64 or ARM64 Windows test process."
    match Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_RID" |> Option.ofObj with
    | Some rid -> Expect.equal rid (Platform.name platform) "the supplied producer RID must match the actual test process"
    | None -> ()
    use sandbox = TestSandbox.Create("release-setup-")
    let mutable calls=0
    let mutable withdrawn=false
    let transport = {
        new IAcquisitionTransport with
            member _.OpenAsync(source,ct) = task {
                ct.ThrowIfCancellationRequested()
                calls <- calls+1
                if withdrawn then raise (HttpRequestException "The acquisition inputs have been withdrawn.")
                let stream = File.OpenRead(Path.Combine(folder,Path.GetFileName source.AbsolutePath))
                return {
                    new IAcquisitionResponse with
                        member _.Content = stream
                        member _.DeclaredLength = Some stream.Length
                    interface IAsyncDisposable with member _.DisposeAsync() = stream.DisposeAsync()
                }
            }
    }
    let bundleBytes = File.ReadAllBytes(Path.Combine(folder,ReleaseBundleDocument.fileName platform))
    let bundle = ReleaseBundleDocument.read bundleBytes |> function Ok bundle -> bundle | Error _ -> failtest "Actual producer bundle is invalid."
    let home = Path.Combine(sandbox.Work,"home")
    let! _,planned = ComposedInstallation.setupWithTransportAsync platform home bundle.Version.Text transport true CancellationToken.None
    Expect.isNone planned "a dry run cannot invent a generation"
    Expect.isFalse (Directory.Exists home) "no locks or staging during planning"
    let! _,installed = ComposedInstallation.setupWithTransportAsync platform home bundle.Version.Text transport false CancellationToken.None
    Expect.isSome installed "completed acquisition, readiness and selection"
    Expect.isEmpty (Directory.GetDirectories(Path.Combine(home,"tmp"),"local-toolchain-*")) "setup removes both completed private stages"
    let pointer = File.ReadAllBytes(Path.Combine(home,"toolchains/default.json"))
    let project = Path.Combine(sandbox.Work,"project")
    Directory.CreateDirectory project |> ignore
    Directory.CreateDirectory(Path.Combine(project,"src")) |> ignore
    File.Copy(Path.Combine(Fixtures.workflow,"source/Pricing.fs"),Path.Combine(project,"src/Pricing.fs"))
    File.WriteAllText(Path.Combine(project,"morphir.toml"),"[project]\nname='acme/demo'\nsource_directory='src'\n[frontend]\nlanguage='fsharp'\n[ir]\nformat_version=4\n")
    let run args = task {
        use input = new MemoryStream()
        use output = new MemoryStream()
        use errors = new StringWriter()
        let! exit = Cli.runAsync {Out=output;Error=errors;CurrentDirectory=project;Platform=Some platform} input (["run";"--home";home;"--"] @ args) CancellationToken.None
        Expect.equal exit 0 (errors.ToString())
    }
    do! run ["compile"]
    do! run ["generate";"--target";"fsharp";"--output";"generated";"--option";"format=false"]
    Expect.equal (File.ReadAllBytes(Path.Combine(project,"generated/Pricing.fs"))) (File.ReadAllBytes(Path.Combine(Fixtures.workflow,"expected/generated/Pricing.fs"))) "frozen 112-byte output"
    let before=calls
    withdrawn <- true
    do! run ["compile"]
    do! run ["generate";"--target";"fsharp";"--output";"generated";"--option";"format=false"]
    Expect.equal calls before "retained run performs no acquisition"
    Expect.equal (File.ReadAllBytes(Path.Combine(home,"toolchains/default.json"))) pointer "selection remains unchanged offline"
})]
