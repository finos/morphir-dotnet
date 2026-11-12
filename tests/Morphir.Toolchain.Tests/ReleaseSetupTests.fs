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
    let asset role = Value.Object [
        "name",Value.String(role+".zip")
        "url",Value.String(ReleaseBundleDocument.repository+"/releases/download/v0.4.0-alpha.5/"+role+".zip")
        "bytes",Value.Number "3";"sha256",Value.String hash;"role",Value.String role]
    Value.Object [
        "$schema",Value.String(ReleaseBundleDocument.schema "bundle")
        "version",Value.String "0.4.0-alpha.5";"sourceCommit",Value.String(String('b',40));"platform",Value.String "win-x64"
        "assets",Value.Array(List.map asset ReleaseBundleDocument.roles)] |> JsonText.toBytes

[<Tests>]
let tests = testList "WindowsReleaseSetup" [
    testCaseTask "wrong release checksum refuses before Home, staging or installation" (fun () -> task {
        use sandbox = TestSandbox.Create()
        let raw = tinyBundle ()
        let sums = String('c',64)+"  morphir-toolchain-win-x64.json\n" + (ReleaseBundleDocument.roles |> List.map(fun role -> String('a',64)+"  "+role+".zip\n") |> String.concat "")
        let transport = BytesTransport(Map.ofList ["morphir-toolchain-win-x64.json",raw;"SHA256SUMS",Encoding.UTF8.GetBytes sums])
        let home = Path.Combine(sandbox.Work,"absent-home")
        let! refused = task {
            try
                let! _ = ComposedInstallation.setupWithTransportAsync home "0.4.0-alpha.5" transport false CancellationToken.None
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
]

/// The producer supplies actual full folders, archives and metadata. No test creates executable substitutes.
[<Tests>]
let acquiredJourney = testList "WindowsReleaseSetup" [testCaseTask "actual installed release setup and offline process workflow" (fun () -> task {
    let folder = Environment.GetEnvironmentVariable "MORPHIR_WINDOWS_TOOLCHAIN_ASSETS" |> Option.ofObj |> Option.defaultWith(fun () -> skiptest "Actual PackWindowsToolchain assets were not supplied.")
    if not(OperatingSystem.IsWindows()) || RuntimeInformation.ProcessArchitecture <> Architecture.X64 then
        skiptest "The actual Windows x64 process composition requires an x64 test process."
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
    let bundleBytes = File.ReadAllBytes(Path.Combine(folder,"morphir-toolchain-win-x64.json"))
    let bundle = ReleaseBundleDocument.read bundleBytes |> function Ok bundle -> bundle | Error _ -> failtest "Actual producer bundle is invalid."
    let home = Path.Combine(sandbox.Work,"home")
    let! _,planned = ComposedInstallation.setupWithTransportAsync home bundle.Version.Text transport true CancellationToken.None
    Expect.isNone planned "a dry run cannot invent a generation"
    Expect.isFalse (Directory.Exists home) "no locks or staging during planning"
    let! _,installed = ComposedInstallation.setupWithTransportAsync home bundle.Version.Text transport false CancellationToken.None
    Expect.isSome installed "completed acquisition, readiness and selection"
    let pointer = File.ReadAllBytes(Path.Combine(home,"toolchains/default.json"))
    let project = Path.Combine(sandbox.Work,"project")
    Directory.CreateDirectory project |> ignore
    File.Copy(Path.Combine(Fixtures.workflow,"source/Pricing.fs"),Path.Combine(project,"Pricing.fs"))
    File.WriteAllText(Path.Combine(project,"morphir.toml"),"[project]\nname='acme/demo'\nsource_directory='.'\n[frontend]\nlanguage='fsharp'\n[ir]\nformat_version='4'\n")
    let run args = task {
        use input = new MemoryStream()
        use output = new MemoryStream()
        use errors = new StringWriter()
        let! exit = Cli.runAsync {Out=output;Error=errors;CurrentDirectory=project;Platform=Some Platform.WinX64} input (["run";"--home";home;"--"] @ args) CancellationToken.None
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
