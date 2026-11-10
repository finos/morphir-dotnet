// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The guest as a framework-dependent dotnet tool: dotnet pack gives an installable package, and the installed tool
/// answers morphir.extension.describe with its claim set and its own package version.
module Morphir.CodeGeneration.FSharp.Extension.Tests.PackageTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.CodeGeneration.FSharp.Extension.Tests.GuestSupport
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
        "F# backend MEP guest package"
        [ testCase "dotnet pack gives a tool package, and the installed tool answers describe with its claims and its package version"
          <| fun _ ->
              use sandbox = TestSandbox.Create "morphir-backend-pack-"
              let root = sandbox.Work
              let feed = Path.Combine(root, "feed")
              let tools = Path.Combine(root, "tools")
              Directory.CreateDirectory feed |> ignore

              let project =
                  Path.Combine(
                      repository,
                      "src",
                      "Morphir.CodeGeneration.FSharp.Extension",
                      "Morphir.CodeGeneration.FSharp.Extension.fsproj"
                  )

              // Pack at the version of the built guest, as the frontend package test does: without -p:Version, a
              // `pack --no-build` can take another version than the build.
              let builtVersion =
                  let product =
                      Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "morphir-fsharp-backend.dll")).ProductVersion
                      |> nonNull

                  product.Split('+').[0]

              run sandbox [ "pack"; project; "-c"; configuration; "--no-build"; "-o"; feed; "-nologo"; $"-p:Version={builtVersion}" ] repository

              let package =
                  Directory.GetFiles(feed, "Morphir.CodeGeneration.FSharp.Extension.*.nupkg") |> Array.exactlyOne

              let version =
                  (Path.GetFileNameWithoutExtension package |> nonNull)
                      .Substring("Morphir.CodeGeneration.FSharp.Extension.".Length)

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
                    "Morphir.CodeGeneration.FSharp.Extension"
                    "--tool-path"
                    tools
                    "--configfile"
                    config
                    "--version"
                    version ]
                  root

              let executable =
                  Path.Combine(tools, if OperatingSystem.IsWindows() then "morphir-fsharp-backend.exe" else "morphir-fsharp-backend")

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
              let extension = claims["extension"] |> nonNull
              Expect.equal ((extension["id"] |> nonNull).GetValue<string>()) Morphir.CodeGeneration.FSharp.Extension.FSharpGuest.extension.Id "extension id"
              Expect.equal ((extension["version"] |> nonNull).ToJsonString()) $"\"{version}\"" "The extension version is the package version"

              let tuples = ((claims["capabilities"] |> nonNull)["modelTransport"] |> nonNull)["tuples"]

              Expect.isTrue
                  (JsonNode.DeepEquals(tuples, JsonNode.Parse $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]"""))
                  "The claimed generateModel tuples" ]
