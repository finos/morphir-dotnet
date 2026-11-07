// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Myriad.SystemTextJson.Tests.BuildTests

open System
open System.Diagnostics
open System.IO
open Expecto

/// The repository root: the first parent folder of the test assembly that holds Morphir.slnx.
let private repositoryRoot =
    let rec find (directory: DirectoryInfo | null) =
        match directory with
        | null -> failwith "Morphir.slnx was not found above the test assembly."
        | directory when File.Exists(Path.Combine(directory.FullName, "Morphir.slnx")) -> directory.FullName
        | directory -> find directory.Parent

    find (DirectoryInfo AppContext.BaseDirectory)

/// The configuration that built this test assembly (bin/<configuration>/net10.0).
let private configuration =
    DirectoryInfo(AppContext.BaseDirectory).Parent |> Option.ofObj |> Option.map _.Name |> Option.defaultValue "Debug"

let private projectFile (inputFile: string) (contextItem: string) =
    let path relative = Path.Combine(repositoryRoot, relative)

    $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{inputFile}" />
    {contextItem}
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="{path "src/Morphir.Myriad.SystemTextJson/Morphir.Myriad.SystemTextJson.fsproj"}" ReferenceOutputAssembly="false" PrivateAssets="all" />
    <ProjectReference Include="{path "src/Morphir.Myriad.SystemTextJson.Build/Morphir.Myriad.SystemTextJson.Build.csproj"}" ReferenceOutputAssembly="false" PrivateAssets="all" />
    <ProjectReference Include="{path "src/Morphir.Myriad.SystemTextJson.Annotations/Morphir.Myriad.SystemTextJson.Annotations.csproj"}" />
  </ItemGroup>
  <Import Project="{path "src/Morphir.Myriad.SystemTextJson/repository/Morphir.Myriad.SystemTextJson.Repository.targets"}" />
</Project>
"""

/// Builds a fixture project in a new folder and returns the exit code, the output and the folder.
let private buildFixture (inputSource: string) (contextItem: string) =
    let folder = Path.Combine(Path.GetTempPath(), "stj-fixture-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory folder |> ignore
    File.WriteAllText(Path.Combine(folder, "Types.fs"), inputSource)
    File.WriteAllText(Path.Combine(folder, "Fixture.fsproj"), projectFile "Types.fs" contextItem)
    let start = ProcessStartInfo("dotnet", WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for argument in [ "build"; "Fixture.fsproj"; "-c"; configuration; "-p:BuildProjectReferences=false"; "-nologo" ] do
        start.ArgumentList.Add argument

    use proc = Process.Start start |> nonNull
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()

    if not (proc.WaitForExit(TimeSpan.FromMinutes 5.0)) then
        proc.Kill true
        failtest "the fixture build exceeded five minutes"

    proc.ExitCode, stdout.Result + stderr.Result, folder

[<Tests>]
let tests =
    testSequenced
    <| testList "build integration" [
        test "a context that the generator refuses blocks compilation" {
            let exitCode, output, folder =
                buildFixture "namespace Fixture\n\nmodule Marker =\n    let value = 0\n" """<FSharpJsonContext Include="Types.fs" Name="Not A Name" Namespace="Fixture.Json" />"""

            try
                Expect.notEqual exitCode 0 "the build fails"
                Expect.stringContains output "Not A Name" "the error names the refused value"
                Expect.isFalse (File.Exists(Path.Combine(folder, "bin", configuration, "net10.0", "Fixture.dll"))) "no assembly is compiled"
            finally
                Directory.Delete(folder, true)
        }

        test "a valid context builds, and a repeat build does not rewrite the generated file" {
            let source = "namespace Fixture\n\nmodule Marker =\n    let value = 0\n"
            let item = """<FSharpJsonContext Include="Types.fs" Name="FixtureJson" Namespace="Fixture.Json" />"""
            let exitCode, output, folder = buildFixture source item

            try
                Expect.equal exitCode 0 $"the build succeeds: {output}"
                let generated = Directory.GetFiles(folder, "FixtureJson.g.fs", SearchOption.AllDirectories)
                Expect.hasLength generated 1 "one generated file"
                let firstWrite = File.GetLastWriteTimeUtc generated[0]
                let start = ProcessStartInfo("dotnet", WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true)

                for argument in [ "build"; "Fixture.fsproj"; "-c"; configuration; "-p:BuildProjectReferences=false"; "-nologo" ] do
                    start.ArgumentList.Add argument

                use again = Process.Start start |> nonNull
                again.StandardOutput.ReadToEnd() |> ignore
                again.WaitForExit()
                Expect.equal again.ExitCode 0 "the repeat build succeeds"
                Expect.equal (File.GetLastWriteTimeUtc generated[0]) firstWrite "the generated file is not rewritten"
            finally
                Directory.Delete(folder, true)
        }
    ]
