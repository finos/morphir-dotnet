// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
// Run: dotnet fsi tests/Morphir.Myriad.Tests/package-tests.fsx
open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Text.Json
open System.Text.Json.Nodes

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let dotnet =
    Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
    |> Option.ofObj
    |> Option.defaultValue "dotnet"

let workspace =
    Path.Combine(Path.GetTempPath(), "morphir package tests " + Guid.NewGuid().ToString("N"))

let feed = Path.Combine(workspace, "feed")
let version = "0.1.0-package." + Guid.NewGuid().ToString("N")

let run directory expectedSuccess arguments =
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

    if not (child.WaitForExit 180000) then
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

let loadModel () =
    File.ReadAllText(Path.Combine(root, "tests/Fixtures/MorphirIR/daily-rate-v3.json"))
    |> JsonNode.Parse

let moduleJson (name: string) (rate: int) =
    let module' = (loadModel ()).["distribution"].[3].["modules"].[0].DeepClone()
    module'.[0] <- JsonSerializer.SerializeToNode([| [| name |] |])
    module'.[1].["value"].["values"].[0].[1].["value"].["body"].[2].[1] <- JsonValue.Create(rate)
    module'

let model (modules: JsonNode list) =
    let fixture = loadModel ()
    let output = fixture.["distribution"].[3].["modules"].AsArray()
    output.Clear()

    for module' in modules do
        output.Add(module'.DeepClone())

    fixture.ToJsonString()

let withConsumer action =
    let consumer = Path.Combine(workspace, "consumer " + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory consumer |> ignore

    File.WriteAllText(
        Path.Combine(consumer, "Consumer.fsproj"),
        """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><RestorePackagesPath>__PACKAGES__</RestorePackagesPath></PropertyGroup>
<ItemGroup><PackageReference Include="Morphir.Myriad" Version="[__VERSION__]" /></ItemGroup>
<ItemGroup><MorphirModel Include="model.json" /><Compile Include="Program.fs" /></ItemGroup>
</Project>"""
            .Replace("__VERSION__", version)
            .Replace("__PACKAGES__", System.Security.SecurityElement.Escape(Path.Combine(workspace, "packages")))
    )

    File.WriteAllText(
        Path.Combine(consumer, "NuGet.config"),
        """<configuration><packageSources><clear/><add key="local" value="__FEED__"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>"""
            .Replace("__FEED__", System.Security.SecurityElement.Escape feed)
    )

    File.WriteAllText(Path.Combine(consumer, "Program.fs"), "printfn \"%d\" Car.Rental.Inventory.dailyRate\n")
    action consumer

let writeModel consumer (content: string) =
    File.WriteAllText(Path.Combine(consumer, "model.json"), content)

let build consumer success =
    run
        workspace
        success
        [ "build"
          Path.Combine(consumer, "Consumer.fsproj")
          "-c"
          "Release"
          "--nologo" ]

let execute consumer =
    run consumer true [ "run"; "--project"; "Consumer.fsproj"; "-c"; "Release"; "--no-build" ]
    |> fun output -> output.Trim()

let files consumer (pattern: string) =
    Directory.GetFiles(Path.Combine(consumer, "obj"), pattern, SearchOption.AllDirectories)

let editProject consumer (before: string) (after: string) =
    let project = Path.Combine(consumer, "Consumer.fsproj")
    File.WriteAllText(project, File.ReadAllText(project).Replace(before, after))

let tests =
    [ "repeat builds preserve source and manifest timestamps",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore
              assertEqual "5000" (execute consumer)
              let owned = Array.append (files consumer "*.fs") (files consumer "manifest.json")

              let snapshot () =
                  owned
                  |> Array.map (fun path -> path, File.ReadAllText path, File.GetLastWriteTimeUtc path)

              let before = snapshot ()
              Thread.Sleep 50
              build consumer true |> ignore
              assertEqual before (snapshot ())
      "model edits regenerate executable behavior",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore
              assertEqual "5000" (execute consumer)
              writeModel consumer (model [ moduleJson "inventory" 6000 ])
              build consumer true |> ignore
              assertEqual "6000" (execute consumer)
      "module addition and removal update compilation and clean only owned output",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore
              writeModel consumer (model [ moduleJson "inventory" 5000; moduleJson "other" 42 ])

              File.WriteAllText(
                  Path.Combine(consumer, "Program.fs"),
                  "printfn \"%d\" (Car.Rental.Inventory.dailyRate + Car.Rental.Other.dailyRate)\n"
              )

              build consumer true |> ignore
              assertEqual "5042" (execute consumer)
              let obsolete = files consumer "Other.fs" |> Array.exactlyOne
              let unrelated = Path.Combine(Path.GetDirectoryName obsolete, "user-file.txt")
              File.WriteAllText(unrelated, "keep")
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              File.WriteAllText(Path.Combine(consumer, "Program.fs"), "printfn \"%d\" Car.Rental.Inventory.dailyRate\n")
              build consumer true |> ignore
              assertEqual false (File.Exists obsolete)
              assertEqual "keep" (File.ReadAllText unrelated)
              assertEqual "5000" (execute consumer)
      "invalid model after success fails instead of using stale source",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore
              writeModel consumer (File.ReadAllText(Path.Combine(root, "tests/Fixtures/MorphirIR/malformed-model.txt")))
              build consumer false |> assertContains "model.json"
      "missing model failure includes the input path",
      fun () ->
          withConsumer
          <| fun consumer -> build consumer false |> assertContains "model.json"
      "models with the same basename have distinct output directories",
      fun () ->
          withConsumer
          <| fun consumer ->
              for folder, name, rate in [ "first", "inventory", 5000; "second", "other", 42 ] do
                  let directory = Path.Combine(consumer, folder)
                  Directory.CreateDirectory directory |> ignore
                  File.WriteAllText(Path.Combine(directory, "model.json"), model [ moduleJson name rate ])

              editProject
                  consumer
                  "<MorphirModel Include=\"model.json\" />"
                  "<MorphirModel Include=\"first/model.json\" /><MorphirModel Include=\"second/model.json\" />"

              File.WriteAllText(
                  Path.Combine(consumer, "Program.fs"),
                  "printfn \"%d\" (Car.Rental.Inventory.dailyRate + Car.Rental.Other.dailyRate)\n"
              )

              build consumer true |> ignore
              assertEqual "5042" (execute consumer)
              assertEqual 2 (files consumer "manifest.json" |> Array.length)
      "explicit output directories support spaces",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])

              editProject
                  consumer
                  "<MorphirModel Include=\"model.json\" />"
                  "<MorphirModel Include=\"model.json\" OutputDirectory=\"generated modules\" />"

              build consumer true |> ignore
              assertEqual "5000" (execute consumer)
              assertEqual true (File.Exists(Path.Combine(consumer, "generated modules", "Inventory.fs")))
      "duplicate output ownership is rejected",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              File.WriteAllText(Path.Combine(consumer, "other.json"), model [ moduleJson "other" 42 ])

              editProject
                  consumer
                  "<MorphirModel Include=\"model.json\" />"
                  "<MorphirModel Include=\"model.json\" OutputDirectory=\"same\" /><MorphirModel Include=\"other.json\" OutputDirectory=\"same\" />"

              build consumer false |> assertContains "output collision"
              assertEqual false (Directory.Exists(Path.Combine(consumer, "same")))
      "symbolic-link output roots cannot redirect generation",
      fun () ->
          withConsumer
          <| fun consumer ->
              if not (OperatingSystem.IsWindows()) then
                  writeModel consumer (model [ moduleJson "inventory" 5000 ])
                  let outside = Path.Combine(consumer, "outside")
                  Directory.CreateDirectory outside |> ignore

                  Directory.CreateSymbolicLink(Path.Combine(consumer, "generated"), outside)
                  |> ignore

                  editProject
                      consumer
                      "<MorphirModel Include=\"model.json\" />"
                      "<MorphirModel Include=\"model.json\" OutputDirectory=\"generated\" />"

                  build consumer false |> assertContains "symbolic link"
                  assertEqual [||] (Directory.GetFiles outside)
      "concurrent configurations use isolated generated outputs",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore

              let builds =
                  [| "Debug"; "Release" |]
                  |> Array.map (fun configuration ->
                      System.Threading.Tasks.Task.Run(fun () ->
                          run
                              workspace
                              true
                              [ "build"
                                Path.Combine(consumer, "Consumer.fsproj")
                                "-c"
                                configuration
                                "--no-restore"
                                "--nologo" ]))

              System.Threading.Tasks.Task.WhenAll(builds).GetAwaiter().GetResult() |> ignore
              assertEqual 2 (files consumer "manifest.json" |> Array.length)
              assertEqual 2 (files consumer "Inventory.fs" |> Array.length)
              assertEqual "5000" (execute consumer)
      "clean removes default intermediate generated artifacts",
      fun () ->
          withConsumer
          <| fun consumer ->
              writeModel consumer (model [ moduleJson "inventory" 5000 ])
              build consumer true |> ignore
              let generatedFile = files consumer "Inventory.fs" |> Array.exactlyOne

              run consumer true [ "clean"; "Consumer.fsproj"; "-c"; "Release"; "--nologo" ]
              |> ignore

              assertEqual false (File.Exists generatedFile)
              assertEqual 0 (files consumer "manifest.json" |> Array.length) ]

Directory.CreateDirectory workspace |> ignore

try
    run
        root
        true
        [ "pack"
          Path.Combine(root, "src/Morphir.Myriad/Morphir.Myriad.fsproj")
          "-c"
          "Release"
          "-o"
          feed
          "-p:PackageVersion=" + version
          "--nologo" ]
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
