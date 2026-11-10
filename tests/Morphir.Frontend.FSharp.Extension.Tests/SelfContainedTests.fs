// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The self-contained process delivery of the guest (deployment task 3.1): a folder published with the
/// SelfContainedProcess profile runs from a clean directory whose path has a space, in an environment where no
/// dotnet can be found, and gives the same IR and diagnostics as the framework-dependent delivery.
module Morphir.Frontend.FSharp.Extension.Tests.SelfContainedTests

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text.Json.Nodes
open Expecto
open Morphir.Conformance
open Morphir.Frontend.FSharp.Extension.Tests.GuestSupport
open Morphir.Frontend.FSharp.Extension.Tests.ProcessReplayTests
open Morphir.Mep
open Morphir.TestSupport

let private repository =
    let rec find (directory: DirectoryInfo | null) =
        match directory with
        | null -> failtest "The repository root has no Morphir.slnx"
        | found when File.Exists(Path.Combine(found.FullName, "Morphir.slnx")) -> found.FullName
        | found -> find found.Parent

    find (DirectoryInfo AppContext.BaseDirectory)

/// The fixed package of the readiness check. Until finos/morphir publishes its frozen F# source package (its task
/// 1.4), it is the conformance case frontend-fsharp-0100 (a record type and a field read). To switch to the frozen
/// package, change only this module.
module FixedPackage =
    /// The conformance case.
    let caseId = "frontend-fsharp-0100"

    /// The package name, the exposed modules and the documents (URI and text) of the case.
    let load () =
        let root = Path.Combine(repository, "conformance")

        let artifacts =
            [ for file in Directory.GetFiles(root, "*", SearchOption.AllDirectories) ->
                  { Path = Path.GetRelativePath(root, file).Replace('\\', '/')
                    Bytes = File.ReadAllBytes file } ]

        let options =
            { Suite = "frontend-fsharp"
              VocabularyVersion = "1.1"
              IrVersions = [ "1"; "2"; "3"; "4" ]
              InputKinds = [ "files"; "directory"; "project file" ] }

        match Corpus.read options artifacts with
        | Error issues -> failtest $"The corpus is valid: {issues}"
        | Ok suite ->
            let case =
                suite.Cases
                |> List.find (fun case -> case.Id = caseId && case.Input.InputKind = "files")

            case.Input.Package,
            case.Input.ExposedModules,
            [ for document in case.Input.Documents -> document.Uri, document.Text ]

/// The executable of the guest in a published folder.
let private executableIn (folder: string) =
    Path.Combine(folder, (if OperatingSystem.IsWindows() then "morphir-fsharp-frontend.exe" else "morphir-fsharp-frontend"))

/// The build configuration of this test run, such as Debug or Release, from the output path.
let private configuration =
    let parts = AppContext.BaseDirectory.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
    let index = Array.LastIndexOf(parts, "bin")
    if index >= 0 && index + 1 < parts.Length then parts[index + 1] else "Debug"

/// The published self-contained folder for this machine's runtime identifier. MORPHIR_FSHARP_FRONTEND_PROCESS_DIR
/// names a folder that is already published (for example a release artifact); otherwise the test publishes the
/// guest with the SelfContainedProcess profile, as the PublishFSharpFrontendProcess build target does.
let private publish (output: string) =
    match Environment.GetEnvironmentVariable "MORPHIR_FSHARP_FRONTEND_PROCESS_DIR" with
    | NonNull folder when folder.Length > 0 -> folder
    | _ ->

             let project =
                 Path.Combine(repository, "src", "Morphir.Frontend.FSharp.Extension", "Morphir.Frontend.FSharp.Extension.fsproj")

             let dotnet =
                 match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
                 | null -> "dotnet"
                 | path -> path

             let start =
                 ChildProcess.StartInfo(
                     dotnet,
                     "publish",
                     project,
                     "-c",
                     configuration,
                     "-r",
                     RuntimeInformation.RuntimeIdentifier,
                     "-p:PublishProfile=SelfContainedProcess",
                     "-o",
                     output,
                     "-maxcpucount:4",
                     "-nologo"
                 )

             start.WorkingDirectory <- repository
             start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
             let result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 10.0).GetAwaiter().GetResult()
             Expect.equal result.ExitCode 0 $"dotnet publish: {result.CombinedOutput}"
             output

let rec private copy (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy(file, Path.Combine(target, Path.GetFileName file |> nonNull))

    for directory in Directory.GetDirectories source do
        copy directory (Path.Combine(target, Path.GetFileName directory |> nonNull))

/// The directories of every dotnet that this machine could resolve: the runtime of this test host, DOTNET_ROOT
/// variables, every PATH entry with a dotnet executable, and the default install locations.
let private systemDotnetRoots () =
    let dotnetName = if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet"

    let fromRuntime =
        // <root>/shared/Microsoft.NETCore.App/<version>/ is the runtime directory of a framework-dependent host.
        let runtime = DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory())
        [ runtime.FullName; (runtime.Parent |> nonNull).Parent |> nonNull |> _.Parent |> nonNull |> _.FullName ]

    let fromVariables =
        [ for entry in Environment.GetEnvironmentVariables() |> Seq.cast<Collections.DictionaryEntry> do
              let key = string entry.Key

              if key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) then
                  yield string entry.Value ]

    let fromPath =
        (Environment.GetEnvironmentVariable "PATH" |> Option.ofObj |> Option.defaultValue "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun entry -> File.Exists(Path.Combine(entry, dotnetName)))
        |> List.ofArray

    let defaults =
        if OperatingSystem.IsWindows() then
            [ Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles, "dotnet") ]
        else
            [ "/usr/share/dotnet"; "/usr/lib/dotnet"; "/usr/local/share/dotnet"; "/opt/dotnet" ]

    fromRuntime @ fromVariables @ fromPath @ defaults
    |> List.filter (fun path -> path.Length > 0)
    |> List.map (fun path -> Path.TrimEndingDirectorySeparator(Path.GetFullPath path))
    |> List.distinct

/// A start info for the guest in a clean folder, in an environment where no dotnet can be found: no DOTNET_* or
/// COREHOST_* variable except DOTNET_MULTILEVEL_LOOKUP=0, no PATH entry with a dotnet executable, and an empty
/// NuGet packages folder.
let private strippedStart (sandbox: TestSandbox) (folder: string) =
    let start = ChildProcess.StartInfo(executableIn folder)
    start.WorkingDirectory <- sandbox.Work
    sandbox.Apply start
    let dotnetName = if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet"

    for key in List.ofSeq start.Environment.Keys do
        if
            key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("COREHOST_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)
        then
            start.Environment.Remove key |> ignore

    start.Environment["DOTNET_MULTILEVEL_LOOKUP"] <- "0"
    let packages = Directory.CreateDirectory(sandbox.PathOf "empty nuget packages").FullName
    start.Environment["NUGET_PACKAGES"] <- packages

    let path =
        (start.Environment["PATH"] |> Option.ofObj |> Option.defaultValue "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun entry ->
            not (File.Exists(Path.Combine(entry, dotnetName)))
            && not (entry.Contains("dotnet", StringComparison.OrdinalIgnoreCase)))

    start.Environment["PATH"] <- String.Join(Path.PathSeparator, path)
    start, packages

/// True when Windows has a global .NET install that a framework-dependent apphost finds without PATH or
/// DOTNET_ROOT: the default folder or a registered install location.
let private globalInstallExists () =
    let folder =
        File.Exists(Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles, "dotnet", "dotnet.exe"))

    let registered =
        OperatingSystem.IsWindows()
        && (let architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()

            use key =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{architecture}")

            match key with
            | null -> false
            | key -> not (isNull (key.GetValue "InstallLocation")))

    folder || registered

/// The native modules that load the .NET runtime, by file name.
let private runtimeModules =
    set
        [ "coreclr.dll"; "clrjit.dll"; "hostpolicy.dll"; "hostfxr.dll"
          "libcoreclr.so"; "libclrjit.so"; "libhostpolicy.so"; "libhostfxr.so" ]

let private within (folder: string) (path: string) =
    let folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath folder) + string Path.DirectorySeparatorChar
    Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase)

/// The modules of the running guest started from the executable, observed from outside the guest.
let private modulesOf (executable: string) =
    let candidates =
        [ for proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension executable) do
              let path =
                  try
                      proc.MainModule |> Option.ofObj |> Option.map _.FileName
                  with _ ->
                      None

              match path with
              | Some path when String.Equals(Path.GetFullPath path, Path.GetFullPath executable, StringComparison.OrdinalIgnoreCase) ->
                  yield proc
              | _ -> proc.Dispose() ]

    match candidates with
    | [ guest ] ->
        use guest = guest
        [ for item in guest.Modules |> Seq.cast<ProcessModule> -> item.FileName ]
    | other -> failtest $"Expected one running guest from {executable}, found {other.Length}"

/// The messages of the readiness check: describe, initialize with JSON tuples for 4.0.0 and 3.0.0, the fixed
/// package at both versions, a probe that names a namespace of the guest's own assemblies, then shutdown and exit.
let private messages () =
    let package, exposed, documents = FixedPackage.load ()

    let probe =
        [ "Probe.fs",
          "namespace Acme.Demo\nmodule Probe =\n    open Morphir.Mep\n    let one : int = 1\n" ]

    [ """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""
      (initialize [ jsonTuple "4.0.0"; jsonTuple "3.0.0" ]).Replace("\"id\":0", "\"id\":2")
      """{"jsonrpc":"2.0","method":"morphir.initialized","params":{}}"""
      compileMessage 10 (compileRequestFor package "4.0.0" documents (Some exposed))
      compileMessage 11 (compileRequestFor package "3.0.0" documents (Some exposed))
      compileMessage 12 (compileRequestFor package "4.0.0" probe None)
      """{"jsonrpc":"2.0","id":99,"method":"morphir.shutdown"}"""
      """{"jsonrpc":"2.0","method":"morphir.exit"}""" ]

let private byId (replies: JsonNode list) (id: int) =
    replies
    |> List.tryFind (fun reply -> (reply["id"] |> nonNull).GetValue<int>() = id)
    |> Option.defaultWith (fun () -> failtest $"No reply with the id {id}")

/// What a compileModel reply gives, without the envelope revision: the revision derives from the frontend identity,
/// which names the runtime version, so it is equal only when both deliveries run the same runtime patch.
let private outcome (reply: JsonNode) =
    match resultOf reply with
    | CompileModelResult.Compiled(model, diagnostics, modules) ->
        let ir = Text.Encoding.UTF8.GetString(Convert.FromBase64String model.Payload.Content)
        $"compiled {model.ModelId} IR {model.IrVersion}\n{ir}\n{List.ofSeq diagnostics}\n{List.ofSeq modules}"
    | CompileModelResult.Failed diagnostics -> $"failed\n{List.ofSeq diagnostics}"

[<Tests>]
let tests =
    let applicable = OperatingSystem.IsWindows() || OperatingSystem.IsLinux()

    testSequenced
    <| testList
        "F# MEP guest, self-contained process delivery"
        [ testCase "Without any dotnet, the self-contained guest loads its own runtime and compiles the fixed package as the framework-dependent guest does"
          <| fun _ ->
              if not applicable then
                  skiptest "The runtime modules of a child process are observed on Windows and Linux only."

              use sandbox = TestSandbox.Create "morphir-frontend-sc-run-"
              let folder = sandbox.PathOf("clean dir with space", "morphir-fsharp-frontend")
              copy (publish (sandbox.PathOf "publish")) folder

              let roots = systemDotnetRoots ()
              Expect.isNonEmpty roots "The machine's dotnet locations are known"

              for root in roots do
                  Expect.isFalse (within root folder) $"The clean folder is not inside {root}"

              let start, packages = strippedStart sandbox folder
              let dotnetName = if OperatingSystem.IsWindows() then "dotnet.exe" else "dotnet"

              for entry in start.Environment["PATH"] |> nonNull |> _.Split(Path.PathSeparator) do
                  Expect.isFalse (File.Exists(Path.Combine(entry, dotnetName))) $"No dotnet on PATH ({entry})"

              Expect.isFalse
                  (start.Environment.Keys |> Seq.exists (fun key -> key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase)))
                  "No DOTNET_ROOT variable"

              // Negative control: on a machine with no global .NET install, the framework-dependent apphost of the
              // same guest cannot start in the same stripped environment. If it could, the environment would not
              // hide this machine's runtime.
              if OperatingSystem.IsWindows() && not (globalInstallExists ()) then
                  let control, _ = strippedStart sandbox AppContext.BaseDirectory
                  control.WorkingDirectory <- sandbox.Work
                  let result = ChildProcess.RunAsync(control, TimeSpan.FromMinutes 1.0, standardInput = "").GetAwaiter().GetResult()
                  Expect.notEqual result.ExitCode 0 "The framework-dependent apphost does not start without dotnet"
                  Expect.stringContains result.CombinedOutput "You must install .NET" "The apphost found no .NET"

              let loaded = ref []
              let sent = messages ()
              let replies, exitCode = exchangeWith start (fun () -> loaded.Value <- modulesOf (executableIn folder)) sent
              Expect.equal exitCode 0 "The self-contained guest exits with 0 after exit"

              // The evidence that no system runtime ran: the runtime's native modules in the guest process are the
              // copies in the clean folder, and no module of the guest comes from a dotnet location of this machine.
              let runtime =
                  loaded.Value |> List.filter (fun path -> runtimeModules.Contains(Path.GetFileName path |> nonNull))

              Expect.isTrue
                  (runtime |> List.exists (fun path -> (Path.GetFileName path |> nonNull).Contains("coreclr")))
                  $"The guest loaded coreclr: {loaded.Value}"

              for path in runtime do
                  Expect.isTrue (within folder path) $"{path} is loaded from the clean folder"

              for path in loaded.Value do
                  for root in roots do
                      Expect.isFalse (within root path) $"{path} is not loaded from the machine's dotnet at {root}"

              Expect.isEmpty (Directory.GetFileSystemEntries packages) "The guest restored no package"

              let expected, expectedExit = exchange sent
              Expect.equal expectedExit 0 "The framework-dependent guest exits with 0"

              let describe (replies: JsonNode list) = (byId replies 1)["result"] |> nonNull |> _.ToJsonString()
              Expect.equal (describe replies) (describe expected) "describe returns the same claims"

              let agreed (replies: JsonNode list) =
                  (((byId replies 2)["result"] |> nonNull)["capabilities"] |> nonNull)["modelTransport"]
                  |> nonNull
                  |> _.ToJsonString()

              Expect.equal (agreed replies) (agreed expected) "initialize agrees the same tuples"

              for id in [ 10; 11; 12 ] do
                  Expect.equal (outcome (byId replies id)) (outcome (byId expected id)) $"compileModel {id} gives the same IR and diagnostics"

              match resultOf (byId expected 10), resultOf (byId expected 12) with
              | CompileModelResult.Compiled _, CompileModelResult.Failed diagnostics ->
                  Expect.exists
                      diagnostics
                      (fun diagnostic -> diagnostic.Severity = DiagnosticSeverity.Error)
                      "The probe cannot open a namespace of the frontend's own assemblies"
              | fixedPackage, probe -> failtest $"The fixed package compiles and the probe fails: {fixedPackage}; {probe}" ]
