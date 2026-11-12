// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The guest as a framework-dependent dotnet tool (task 8.4): dotnet pack gives an installable package, and the
/// installed tool answers morphir.extension.describe with its claim set.
module Morphir.Frontend.FSharp.Extension.Tests.PackageTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Frontend.FSharp.Extension.Tests.GuestSupport
open Morphir.Mep
open Morphir.TestSupport

let private repository =
    let rec find (directory: DirectoryInfo | null) =
        match directory with
        | null -> failtest "The repository root has no Morphir.slnx"
        | found when File.Exists(Path.Combine(found.FullName, "Morphir.slnx")) -> found.FullName
        | found -> find found.Parent

    find (DirectoryInfo AppContext.BaseDirectory)

/// The build configuration of this test run, such as Debug or Release, from the output path.
let private configuration =
    let parts = AppContext.BaseDirectory.Split([| '/'; '\\' |], StringSplitOptions.RemoveEmptyEntries)
    let index = Array.LastIndexOf(parts, "bin")
    if index >= 0 && index + 1 < parts.Length then parts[index + 1] else "Debug"

let private host =
    match Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
    | null -> "dotnet"
    | path -> path

let private run (sandbox: TestSandbox) (arguments: string list) (directory: string) =
    let start = ChildProcess.StartInfo(host, Array.ofList arguments)
    start.WorkingDirectory <- directory
    sandbox.Apply start
    let result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 5.0).GetAwaiter().GetResult()
    let command = String.Join(' ', arguments)
    Expect.equal result.ExitCode 0 $"dotnet {command}: {result.CombinedOutput}"

[<Tests>]
let tests =
    testSequenced
    <| testList
        "F# MEP guest package"
        [ testCase "dotnet pack gives a tool package, and the installed tool answers describe with its claims"
          <| fun _ ->
              use sandbox = TestSandbox.Create "morphir-guest-pack-"
              let root = sandbox.Work
              let feed = Path.Combine(root, "feed")
              let tools = Path.Combine(root, "tools")
              Directory.CreateDirectory feed |> ignore

              let project =
                  Path.Combine(repository, "src", "Morphir.Frontend.FSharp.Extension", "Morphir.Frontend.FSharp.Extension.fsproj")

              // Pack at the version of the built guest. Without -p:Version, a `pack --no-build` can take another
              // version than the build (on Windows CI the package was 1.0.0 while the guest reported the CHANGELOG
              // version), and then the package and the extension version cannot be compared.
              let builtVersion =
                  let product =
                      Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-frontend.dll")).ProductVersion
                      |> nonNull

                  product.Split('+').[0]

              run sandbox [ "pack"; project; "-c"; configuration; "--no-build"; "-o"; feed; "-nologo"; $"-p:Version={builtVersion}" ] repository

              let package =
                  Directory.GetFiles(feed, "Morphir.Frontend.FSharp.Extension.*.nupkg") |> Array.exactlyOne

              let version =
                  (Path.GetFileNameWithoutExtension package).Substring("Morphir.Frontend.FSharp.Extension.".Length)

              // The repository uses package source mapping, so the install reads a configuration with only the
              // local feed.
              let config = Path.Combine(root, "nuget.config")

              File.WriteAllText(
                  config,
                  $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
<clear />
<add key="local" value="{feed}" />
  </packageSources>
</configuration>
"""
              )

              run
                  sandbox
                  [ "tool"
                    "install"
                    "Morphir.Frontend.FSharp.Extension"
                    "--tool-path"
                    tools
                    "--configfile"
                    config
                    "--version"
                    version ]
                  root

              let executable =
                  Path.Combine(tools, if OperatingSystem.IsWindows() then "morphir-fsharp-frontend.exe" else "morphir-fsharp-frontend")

              let describe =
                  """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""

              let start = ChildProcess.StartInfo executable
              start.WorkingDirectory <- root
              start.StandardInputEncoding <- UTF8Encoding false
              sandbox.Apply start

              // stdin closes after the frame, and the guest exits at the end of its input.
              let result =
                  ChildProcess
                      .RunAsync(
                          start,
                          TimeSpan.FromMinutes 1.0,
                          CancellationToken.None,
                          null,
                          $"Content-Length: {Encoding.UTF8.GetByteCount describe}\r\n\r\n{describe}"
                      )
                      .GetAwaiter()
                      .GetResult()

              Expect.equal result.ExitCode 0 $"The installed guest exits at the end of its input: {result.CombinedOutput}"
              use output = new MemoryStream(Encoding.UTF8.GetBytes result.StandardOutput)

              let reply =
                  match ContentLengthFraming.ReadAsync(output, CancellationToken.None).GetAwaiter().GetResult() with
                  | FrameRead.Message body -> JsonNode.Parse(ReadOnlySpan body) |> nonNull
                  | other -> failtest $"Expected a describe reply: {other}; {result.CombinedOutput}"

              let claims = reply["result"] |> nonNull
              let claimsVersion = (claims["claimsVersion"] |> nonNull).ToJsonString()
              Expect.equal claimsVersion "\"0.1.0-draft.2\"" "claims version"

              let extensionVersion = ((claims["extension"] |> nonNull)["version"] |> nonNull).ToJsonString()
              Expect.equal extensionVersion $"\"{version}\"" "The extension version is the package version"

              let tuples =
                  ((claims["capabilities"] |> nonNull)["modelTransport"] |> nonNull)["tuples"]

              Expect.isTrue
                  (JsonNode.DeepEquals(tuples, JsonNode.Parse $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]"""))
                  "The claimed compileModel tuples" ]

