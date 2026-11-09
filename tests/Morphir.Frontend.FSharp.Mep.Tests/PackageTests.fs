// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The guest as a framework-dependent dotnet tool (task 8.4): dotnet pack gives an installable package, and the
/// installed tool answers morphir.extension.describe with its claim set.
module Morphir.Frontend.FSharp.Mep.Tests.PackageTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open Expecto
open Morphir.Frontend.FSharp.Mep.Tests.GuestSupport
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

let private run (arguments: string list) (directory: string) =
    let start = ChildProcess.StartInfo(host, Array.ofList arguments)
    start.WorkingDirectory <- directory
    let result = ChildProcess.RunAsync(start, TimeSpan.FromMinutes 5.0).GetAwaiter().GetResult()
    Expect.equal result.ExitCode 0 $"dotnet {String.Join(' ', arguments)}: {result.CombinedOutput}"

[<Tests>]
let tests =
    testSequenced
    <| testList
        "F# MEP guest package"
        [ testCase "dotnet pack gives a tool package, and the installed tool answers describe with its claims"
          <| fun _ ->
              let root = Path.Combine(Path.GetTempPath(), "morphir-guest-pack-" + Guid.NewGuid().ToString("N"))
              let feed = Path.Combine(root, "feed")
              let tools = Path.Combine(root, "tools")
              Directory.CreateDirectory feed |> ignore

              try
                  let project =
                      Path.Combine(repository, "src", "Morphir.Frontend.FSharp.Mep", "Morphir.Frontend.FSharp.Mep.fsproj")

                  run [ "pack"; project; "-c"; configuration; "--no-build"; "-o"; feed; "-nologo" ] repository

                  let package =
                      Directory.GetFiles(feed, "Morphir.Frontend.FSharp.Mep.*.nupkg") |> Array.exactlyOne

                  let version =
                      (Path.GetFileNameWithoutExtension package |> nonNull).Substring("Morphir.Frontend.FSharp.Mep.".Length)

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
                      [ "tool"
                        "install"
                        "Morphir.Frontend.FSharp.Mep"
                        "--tool-path"
                        tools
                        "--configfile"
                        config
                        "--version"
                        version ]
                      root

                  let executable =
                      Path.Combine(tools, if OperatingSystem.IsWindows() then "morphir-fsharp-frontend.exe" else "morphir-fsharp-frontend")

                  let start = ProcessStartInfo(executable)
                  start.RedirectStandardInput <- true
                  start.RedirectStandardOutput <- true
                  start.RedirectStandardError <- true
                  start.UseShellExecute <- false
                  use guest = Process.Start start |> nonNull
                  let errors = guest.StandardError.ReadToEndAsync()

                  let describe =
                      """{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}"""

                  ContentLengthFraming
                      .WriteAsync(guest.StandardInput.BaseStream, ReadOnlyMemory(Encoding.UTF8.GetBytes describe), CancellationToken.None)
                      .Wait()

                  use timeout = new CancellationTokenSource(TimeSpan.FromMinutes 1.0)

                  let reply =
                      match ContentLengthFraming.ReadAsync(guest.StandardOutput.BaseStream, timeout.Token).GetAwaiter().GetResult() with
                      | FrameRead.Message body -> JsonNode.Parse(ReadOnlySpan body) |> nonNull
                      | other -> failtest $"Expected a describe reply: {other}; stderr: {errors.Result}"

                  guest.StandardInput.Close()

                  if not (guest.WaitForExit(TimeSpan.FromSeconds 30.0)) then
                      guest.Kill true
                      failtest "The installed guest did not exit at the end of its input"

                  let claims = reply["result"] |> nonNull
                  let claimsVersion = (claims["claimsVersion"] |> nonNull).ToJsonString()
                  Expect.equal claimsVersion "\"0.1.0-draft.2\"" "claims version"

                  let extensionVersion = ((claims["extension"] |> nonNull)["version"] |> nonNull).ToJsonString()
                  Expect.equal extensionVersion $"\"{version}\"" "The extension version is the package version"

                  let tuples =
                      ((claims["capabilities"] |> nonNull)["modelTransport"] |> nonNull)["tuples"]

                  Expect.isTrue
                      (JsonNode.DeepEquals(tuples, JsonNode.Parse $"""[{jsonTuple "4.0.0"},{jsonTuple "3.0.0"}]"""))
                      "The claimed compileModel tuples"
              finally
                  TolerantDirectory.TryDelete root |> ignore ]
