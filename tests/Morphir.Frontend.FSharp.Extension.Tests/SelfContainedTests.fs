// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The self-contained process delivery of the guest (deployment task 3.1): a folder published with the
/// SelfContainedProcess profile runs from a clean directory whose path has a space, in an environment where no
/// dotnet can be found, and at the same runtime version gives the same IR and diagnostics as the framework-dependent
/// delivery. The folder is published once by the build's Test target (MORPHIR_FSHARP_FRONTEND_PROCESS_DIR); the
/// negative control is a separate test, reported as skipped with its reason where it cannot run.
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

/// The variable that names a published self-contained folder. The Test target of the build publishes the folder
/// once, before the test assemblies run, and sets it for this assembly.
let private processDirVariable = "MORPHIR_FSHARP_FRONTEND_PROCESS_DIR"

/// The variable that asks the test to publish the folder itself, as a local fallback: set it to "local".
let private publishVariable = "MORPHIR_FSHARP_FRONTEND_PROCESS_PUBLISH"

/// The portable runtime identifier of this machine (win-, linux- or osx- and the process architecture), as the build's
/// HostRid gives it, not the distribution-specific one that a source-built runtime can report.
let private portableRid () =
    let os =
        if OperatingSystem.IsWindows() then "win"
        elif OperatingSystem.IsMacOS() then "osx"
        else "linux"

    $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}"

/// The published self-contained folder for this machine. MORPHIR_FSHARP_FRONTEND_PROCESS_DIR names a folder that is
/// already published (by the Test target, or a release artifact). Only when MORPHIR_FSHARP_FRONTEND_PROCESS_PUBLISH
/// is "local" does the test publish the guest itself, for the portable RID, with its intermediate files in the
/// sandbox, which the test deletes. Otherwise the test is skipped and says how to run it.
let private publishedFolder (sandbox: TestSandbox) =
    match Environment.GetEnvironmentVariable processDirVariable, Environment.GetEnvironmentVariable publishVariable with
    | NonNull folder, _ when folder.Length > 0 -> folder
    | _, NonNull "local" ->
        let project =
            Path.Combine(repository, "src", "Morphir.Frontend.FSharp.Extension", "Morphir.Frontend.FSharp.Extension.fsproj")

        let dotnet =
            match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
            | null -> "dotnet"
            | path -> path

        let output = sandbox.PathOf "publish"

        let start =
            ChildProcess.StartInfo(
                dotnet,
                "publish",
                project,
                "-c",
                configuration,
                "-r",
                portableRid (),
                "-p:PublishProfile=SelfContainedProcess",
                "-p:MorphirRidSpecificPublish=true",
                "--artifacts-path",
                sandbox.PathOf "artifacts",
                "-o",
                output,
                "--disable-build-servers",
                "-maxcpucount:4",
                "-nologo"
            )

        start.WorkingDirectory <- repository
        start.Environment["MSBUILDDISABLENODEREUSE"] <- "1"
        let result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 10.0).GetAwaiter().GetResult()
        Expect.equal result.ExitCode 0 $"dotnet publish: {result.CombinedOutput}"
        output
    | _ ->
        skiptest
            $"No published self-contained folder: set {processDirVariable} to one (the build's Test target does), or set {publishVariable}=local to publish it in the test."

/// Final paths: symbolic links, junctions and short names resolved, as the operating system names the open file.
module private FinalPath =
    [<DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern uint32 GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file, Text.StringBuilder path, uint32 length, uint32 flags)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint realpath(string path, nativeint resolved)

    [<DllImport("libc")>]
    extern void free(nativeint pointer)

    /// The final path of an existing file.
    let ofFile (path: string) =
        if OperatingSystem.IsWindows() then
            use handle =
                File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

            let buffer = Text.StringBuilder 1024
            let length = GetFinalPathNameByHandleW(handle, buffer, uint32 buffer.Capacity, 0u)

            if length = 0u || length >= uint32 buffer.Capacity then
                failtest $"GetFinalPathNameByHandle failed for {path}: {Marshal.GetLastPInvokeError()}"

            match buffer.ToString() with
            | unc when unc.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) -> @"\\" + unc.Substring 8
            | local when local.StartsWith(@"\\?\", StringComparison.Ordinal) -> local.Substring 4
            | other -> other
        else
            let resolved = realpath (path, 0n)

            if resolved = 0n then
                failtest $"realpath failed for {path}: {Marshal.GetLastPInvokeError()}"

            try
                Marshal.PtrToStringUTF8 resolved |> nonNull
            finally
                free resolved

    /// How final paths compare on this platform.
    let comparison =
        if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal

let rec private copy (source: string) (target: string) =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy(file, Path.Combine(target, Path.GetFileName file))

    for directory in Directory.GetDirectories source do
        copy directory (Path.Combine(target, Path.GetFileName directory))

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

/// The architecture of a Windows executable, from its PE header, as the dotnet install keys name it.
let private architectureOf (executable: string) =
    use stream = File.OpenRead executable
    use reader = new Reflection.PortableExecutable.PEReader(stream)

    match reader.PEHeaders.CoffHeader.Machine with
    | Reflection.PortableExecutable.Machine.Amd64 -> "x64"
    | Reflection.PortableExecutable.Machine.Arm64 -> "arm64"
    | Reflection.PortableExecutable.Machine.I386 -> "x86"
    | other -> failtest $"{executable} has the machine type {other}"

/// Where a framework-dependent Windows apphost of the architecture finds a global .NET without PATH or DOTNET_ROOT,
/// if anywhere: the install location registered under HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>, which the
/// installer writes and hostfxr reads in the 32-bit registry view, or the default folder of the architecture
/// (Program Files\dotnet; Program Files\dotnet\x64 for x64 on ARM64; Program Files (x86)\dotnet for x86).
let private globalInstall (architecture: string) : string option =
    let hasHost folder = Directory.Exists(Path.Combine(folder, "host", "fxr"))

    let registered =
        use hive =
            Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32)

        use key = hive.OpenSubKey($@"SOFTWARE\dotnet\Setup\InstalledVersions\{architecture}")

        match key with
        | null -> None
        | key ->
            match key.GetValue "InstallLocation" with
            | :? string as location when location.Length > 0 && hasHost location ->
                Some $"the registered InstallLocation {location} (32-bit view, InstalledVersions\\{architecture})"
            | _ -> None

    let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles
    let osArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()

    let defaultFolder =
        match architecture with
        | "x86" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86, "dotnet")
        | "x64" when osArchitecture = "arm64" -> Path.Combine(programFiles, "dotnet", "x64")
        | _ -> Path.Combine(programFiles, "dotnet")

    registered
    |> Option.orElse (if hasHost defaultFolder then Some $"the default folder {defaultFolder}" else None)

/// The native modules that load the .NET runtime, by file name: the host resolver, the host policy, the runtime and
/// its JIT.
let private runtimeModules =
    if OperatingSystem.IsWindows() then
        [ "coreclr.dll"; "clrjit.dll"; "hostfxr.dll"; "hostpolicy.dll" ]
    else
        [ "libcoreclr.so"; "libclrjit.so"; "libhostfxr.so"; "libhostpolicy.so" ]

let private within (folder: string) (path: string) =
    let folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath folder) + string Path.DirectorySeparatorChar
    Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase)

/// The modules of the guest process with the id that the harness gave, observed from outside the guest. The process
/// must run the executable: their final paths are equal.
let private modulesOf (processId: int) (executable: string) =
    use guest = Process.GetProcessById processId
    let main = guest.MainModule |> nonNull |> _.FileName

    Expect.isTrue
        (String.Equals(FinalPath.ofFile main, FinalPath.ofFile executable, FinalPath.comparison))
        $"The process {processId} runs {main}, which is the guest executable {executable}"

    [ for item in guest.Modules |> Seq.cast<ProcessModule> -> item.FileName ]

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
        [ testCase "Negative control: in the stripped environment, the framework-dependent apphost of the same guest cannot start"
          <| fun _ ->
              // If it could start, the environment would not hide this machine's runtime, and the self-contained run
              // below would prove nothing.
              if not (OperatingSystem.IsWindows()) then
                  skiptest
                      "The negative control runs on Windows only: on Linux and macOS a framework-dependent apphost also looks in /etc/dotnet/install_location and the default install folders, which this control does not check."

              let apphost = executableIn AppContext.BaseDirectory
              let architecture = architectureOf apphost

              match globalInstall architecture with
              | Some found ->
                  skiptest
                      $"This machine has a global .NET install for {architecture} that the framework-dependent apphost finds without PATH or DOTNET_ROOT ({found}), so the stripped environment cannot hide it."
              | None ->
                  use sandbox = TestSandbox.Create "morphir-frontend-sc-control-"
                  let control, _ = strippedStart sandbox AppContext.BaseDirectory
                  let result = ChildProcess.RunAsync(control, TimeSpan.FromMinutes 1.0, standardInput = "").GetAwaiter().GetResult()
                  Expect.notEqual result.ExitCode 0 "The framework-dependent apphost does not start without dotnet"
                  Expect.stringContains result.CombinedOutput "You must install .NET" "The apphost found no .NET"

          testCase "Without any dotnet, the self-contained guest loads its own runtime and compiles the fixed package as the framework-dependent guest does"
          <| fun _ ->
              if not applicable then
                  skiptest "The runtime modules of a child process are observed on Windows and Linux only."

              use sandbox = TestSandbox.Create "morphir-frontend-sc-run-"
              let folder = sandbox.PathOf("clean dir with space", "morphir-fsharp-frontend")
              copy (publishedFolder sandbox) folder

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

              let executable = executableIn folder
              let loaded = ref []
              let sent = messages ()
              let replies, exitCode = exchangeWith start (fun processId -> loaded.Value <- modulesOf processId executable) sent
              Expect.equal exitCode 0 "The self-contained guest exits with 0 after exit"

              // The evidence that no system runtime ran: each native module that loads the runtime is in the guest
              // process, as the copy in the clean folder, and no module of the guest comes from a dotnet location of
              // this machine.
              let finalFolder = Path.GetDirectoryName(FinalPath.ofFile executable) |> nonNull

              for name in runtimeModules do
                  let copies =
                      loaded.Value
                      |> List.filter (fun path -> String.Equals(Path.GetFileName path, name, FinalPath.comparison))

                  Expect.isNonEmpty copies $"The guest loaded {name}: {loaded.Value}"

                  for path in copies do
                      Expect.isTrue (within finalFolder (FinalPath.ofFile path)) $"{path} is loaded from the clean folder {finalFolder}"

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
    |> testLabel "PlatformSpecific"