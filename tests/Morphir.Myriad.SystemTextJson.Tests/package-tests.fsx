// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// Package-consumer checks for Morphir.Myriad.SystemTextJson and its annotations package.
// Run: dotnet fsi tests/Morphir.Myriad.SystemTextJson.Tests/package-tests.fsx
open System
open System.Diagnostics
open System.IO
open System.IO.Compression

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let dotnet =
    Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
    |> Option.ofObj
    |> Option.defaultValue "dotnet"

let workspace =
    Path.Combine(Path.GetTempPath(), "stj package tests " + Guid.NewGuid().ToString("N"))

let feed = Path.Combine(workspace, "feed")
let version = "0.1.0-package." + Guid.NewGuid().ToString("N")

let run directory expectedSuccess (arguments: string list) =
    let start = ProcessStartInfo(dotnet)
    start.WorkingDirectory <- directory
    start.UseShellExecute <- false
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true

    for argument in arguments do
        start.ArgumentList.Add argument

    use child = Process.Start start
    let stdout = child.StandardOutput.ReadToEndAsync()
    let stderr = child.StandardError.ReadToEndAsync()

    if not (child.WaitForExit 300000) then
        child.Kill true
        failwith "dotnet exceeded the package-test timeout"

    let output = stdout.Result + stderr.Result

    if (child.ExitCode = 0) <> expectedSuccess then
        failwith $"Unexpected dotnet exit code {child.ExitCode}:\n{output}"

    output

let assertEqual expected actual =
    if expected <> actual then
        failwithf "Expected %A; received %A" expected actual

let assertContains (needle: string) (actual: string) =
    if not (actual.Contains needle) then
        failwithf "Expected '%s' in '%s'" needle actual

let types =
    """namespace Consumer

open Morphir.Myriad.SystemTextJson

type Person = { Name: string; Age: int }

[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind")>]
type Shape =
    | Circle of radius: float
    | Square of side: float
"""

let program =
    """open System.Text.Json
open Consumer
open Consumer.Json

printfn "%s" (JsonSerializer.Serialize({ Name = "Ada"; Age = 36 }, AppJson.Default.Person))
printfn "%s" (JsonSerializer.Serialize(Circle 1.5, AppJson.Default.Shape))
"""

let withConsumer action =
    let consumer = Path.Combine(workspace, "consumer " + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory consumer |> ignore

    File.WriteAllText(
        Path.Combine(consumer, "Consumer.fsproj"),
        """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><RestorePackagesPath>__PACKAGES__</RestorePackagesPath></PropertyGroup>
<ItemGroup>
<PackageReference Include="Morphir.Myriad.SystemTextJson" Version="[__VERSION__]" PrivateAssets="all" />
<PackageReference Include="Morphir.Myriad.SystemTextJson.Annotations" Version="[__VERSION__]" />
</ItemGroup>
<ItemGroup>
<Compile Include="Types.fs" />
<FSharpJsonContext Include="Types.fs" Name="AppJson" Namespace="Consumer.Json" />
<Compile Include="Program.fs" />
</ItemGroup>
</Project>"""
            .Replace("__VERSION__", version)
            .Replace("__PACKAGES__", System.Security.SecurityElement.Escape(Path.Combine(consumer, "packages")))
    )

    File.WriteAllText(
        Path.Combine(consumer, "NuGet.config"),
        """<configuration><packageSources><clear/><add key="local" value="__FEED__"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>"""
            .Replace("__FEED__", System.Security.SecurityElement.Escape feed)
    )

    File.WriteAllText(Path.Combine(consumer, "Types.fs"), types)
    File.WriteAllText(Path.Combine(consumer, "Program.fs"), program)
    action consumer

let build consumer success =
    run workspace success [ "build"; Path.Combine(consumer, "Consumer.fsproj"); "-c"; "Release"; "--nologo" ]

let execute consumer =
    run consumer true [ "run"; "--project"; "Consumer.fsproj"; "-c"; "Release"; "--no-build" ]
    |> fun output -> output.Trim().Replace("\r\n", "\n")

let generated consumer =
    Directory.GetFiles(Path.Combine(consumer, "obj"), "AppJson.g.fs", SearchOption.AllDirectories)

let entries (package: string) =
    use archive = ZipFile.OpenRead package
    archive.Entries |> Seq.map (fun entry -> entry.FullName) |> Set.ofSeq

let tests =
    [ "the plugin package has the host, the plugin, the task and the build assets",
      fun () ->
          let names = entries (Path.Combine(feed, $"Morphir.Myriad.SystemTextJson.{version}.nupkg"))

          for expected in
              [ "tools/host/Myriad.dll"
                "tools/host/FSharp.Core.dll"
                "tools/plugin/Morphir.Myriad.SystemTextJson.dll"
                "tasks/net10.0/Morphir.Myriad.SystemTextJson.Build.dll"
                "tasks/net10.0/Morphir.Myriad.Hosting.dll"
                "build/Morphir.Myriad.SystemTextJson.props"
                "build/Morphir.Myriad.SystemTextJson.targets"
                "README.md" ] do
              if not (names.Contains expected) then
                  failwithf "The plugin package has no %s" expected

      "the annotations package has only its library",
      fun () ->
          let names = entries (Path.Combine(feed, $"Morphir.Myriad.SystemTextJson.Annotations.{version}.nupkg"))
          assertEqual true (names.Contains "lib/netstandard2.0/Morphir.Myriad.SystemTextJson.Annotations.dll")
          assertEqual true (names.Contains "README.md")

      "a clean consumer with an empty package folder builds and runs a context",
      fun () ->
          withConsumer (fun consumer ->
              build consumer true |> ignore
              assertEqual 1 (generated consumer |> Array.length)
              assertEqual "{\"Name\":\"Ada\",\"Age\":36}\n{\"kind\":\"Circle\",\"radius\":1.5}" (execute consumer))

      "a repeat build does not rewrite the generated file",
      fun () ->
          withConsumer (fun consumer ->
              build consumer true |> ignore
              let file = generated consumer |> Array.exactlyOne
              let first = File.GetLastWriteTimeUtc file
              Threading.Thread.Sleep 1100
              build consumer true |> ignore
              assertEqual first (File.GetLastWriteTimeUtc file))

      "a context that the generator refuses blocks compilation",
      fun () ->
          withConsumer (fun consumer ->
              let project = Path.Combine(consumer, "Consumer.fsproj")
              File.WriteAllText(project, File.ReadAllText(project).Replace("Name=\"AppJson\"", "Name=\"Not A Name\""))
              let output = build consumer false
              assertContains "Not A Name" output
              assertEqual false (File.Exists(Path.Combine(consumer, "bin", "Release", "net10.0", "Consumer.dll")))) ]

Directory.CreateDirectory workspace |> ignore

try
    for project in
        [ "src/Morphir.Myriad.SystemTextJson.Annotations/Morphir.Myriad.SystemTextJson.Annotations.csproj"
          "src/Morphir.Myriad.SystemTextJson/Morphir.Myriad.SystemTextJson.fsproj" ] do
        run root true [ "pack"; Path.Combine(root, project); "-c"; "Release"; "-o"; feed; "-p:PackageVersion=" + version; "--nologo" ]
        |> ignore

    let mutable failures = []

    for name, test in tests do
        try
            test ()
            printfn "PASS %s" name
        with error ->
            eprintfn "FAIL %s\n%s" name error.Message
            failures <- name :: failures

    if not failures.IsEmpty then
        failwithf "%d package-consumer checks failed" failures.Length

    printfn "%d package-consumer checks passed" tests.Length
finally
    Directory.Delete(workspace, true)
