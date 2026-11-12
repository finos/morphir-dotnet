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

[<Tests>]
let publicationLocks = testList "ComposedPublicationLocks" [
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
        try
            do! requested.Task.WaitAsync(TimeSpan.FromSeconds 3.)
            Expect.isFalse publication.IsCompleted "publication must wait for the running host"
            use captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds 1.)
            try
                use! state = locks.NamedLeaseAsync("extensions.state.lock",true,true,captureDeadline.Token)
                captured <- true
            with :? OperationCanceledException -> ()
        finally
            runningHost.Dispose()
        // Settle the publication before reporting failure, including on the RED path.
        do! publication.WaitAsync(TimeSpan.FromSeconds 3.)
        Expect.isTrue captured "host state capture must not wait for a publisher blocked on that host"
        Expect.isTrue published "publication resumes after the host releases its lease"
        use! selectionLease = Morphir.Extensions.Installation.FileSafety.mutationLeaseAsync root ct
        Expect.isTrue selectionLease.CanRead "publication releases mutation before selection reacquires it"
    })
]

[<Tests>]
let tests = testList "WindowsReleaseSetup" [
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
        let raw = Encoding.UTF8.GetBytes("{\"$schema\":\"https://morphir.finos.org/schemas/local-toolchains/0.1.0-preview.3/candidate.schema.json\",\"origin\":\"release-checksum\"}")
        File.WriteAllBytes(candidate,raw)
        let home = Path.Combine(sandbox.Work,"absent-home")
        try
            let! _ = ComposedInstallation.setupLocalAsync Platform.WinArm64 home candidate (Path.Combine(sandbox.Work,"archive.zip")) (Path.Combine(sandbox.Work,"bundle.json")) true CancellationToken.None
            failtest "Local flags granted release acquisition authority."
        with LocalCandidate.Refused(code,_) -> Expect.equal code "local.origin_mismatch" "consent does not replace official acquisition"
        Expect.isFalse (Directory.Exists home) "no local stage or state"
    })
]

/// The producer supplies actual full folders, archives and metadata. No test creates executable substitutes.
[<Tests>]
let acquiredJourney = testList "WindowsReleaseSetup" [testCaseTask "actual installed release setup and offline process workflow" (fun () -> task {
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
