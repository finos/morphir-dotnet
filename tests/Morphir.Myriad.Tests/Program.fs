namespace Morphir.Myriad.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open Expecto
open Myriad.Core
open Morphir.Myriad
open Morphir.Myriad.Hosting
open Morphir.TestSupport

module Program =
    let model = Morphir.Testing.ModelResources.read "daily-rate-v3.json"

    let withModel action =
        let directory =
            Path.Combine(
                Path.GetTempPath(),
                "morphir myriad tests "
                + Guid.NewGuid().ToString("N")
            )

        Directory.CreateDirectory directory
        |> ignore

        try
            let path = Path.Combine(directory, "model.json")
            File.WriteAllText(path, model)
            action directory path
        finally
            TolerantDirectory.TryDelete directory
            |> ignore

    let context path selector =
        let parameters = Dictionary<string, string>()

        selector
        |> Option.iter (fun value -> parameters.Add("MorphirModule", value))

        GeneratorContext.Create(None, (fun _ -> Seq.empty), path, None, parameters)

    let prepareHost directory (plugin: string) =
        let host =
            Path.Combine(
                TestSandbox.RealNuGetPackagesFolder,
                "myriad",
                "0.85.0",
                "tools",
                "net9.0",
                "any",
                "Myriad.dll"
            )

        MyriadHost.Prepare(
            host,
            Path.Combine(Path.GetDirectoryName plugin, "FSharp.Core.dll"),
            directory
        )

    [<Tests>]
    let tests =
        testList "Morphir Myriad" [
            testCase "a scaffolded plugin builds and runs with the current Myriad host"
            <| fun _ ->
                let tripwire = MachineTripwire.Capture()

                use guard =
                    { new IDisposable with
                        member _.Dispose() = tripwire.AssertUnchanged()
                    }

                use sandbox = TestSandbox.Create("myriad-scaffold-")

                let skill =
                    Path.GetFullPath(
                        Path.Combine(
                            __SOURCE_DIRECTORY__,
                            "../../.claude/skills/elm-to-fsharp-guru"
                        )
                    )

                let run arguments =
                    let start = ChildProcess.StartInfo("dotnet", Array.ofList arguments)
                    start.WorkingDirectory <- sandbox.Work
                    sandbox.Apply start

                    let result =
                        ChildProcess
                            .RunAsync(start, TimeSpan.FromMinutes 2.0)
                            .GetAwaiter()
                            .GetResult()

                    Expect.equal result.ExitCode 0 result.CombinedOutput

                run [
                    "fsi"
                    Path.Combine(skill, "scripts/generate-myriad-plugin.fsx")
                    "UpgradePlugin"
                ]

                let project = Path.Combine(sandbox.Work, "UpgradePlugin", "UpgradePlugin.fsproj")
                let pluginOutput = Path.Combine(sandbox.Work, "plugin output")
                run [ "build"; project; "--output"; pluginOutput; "--nologo" ]

                let plugin = Path.Combine(pluginOutput, "UpgradePlugin.dll")

                let host = prepareHost sandbox.Work plugin
                let input = Path.Combine(sandbox.Work, "Types.fs")
                let output = Path.Combine(sandbox.Work, "Generated.fs")
                File.WriteAllText(input, "namespace Input\n\ntype Person = { Name: string }\n")

                run [
                    "--roll-forward"
                    "LatestMajor"
                    host
                    "--inputfile"
                    input
                    "--outputfile"
                    output
                    "--plugin"
                    plugin
                    "--generator-filter"
                    "UpgradePluginGenerator"
                ]

                Expect.isTrue (File.Exists output) "the host accepts the scaffold's AST output"
            testCase "a missing module selector fails explicitly"
            <| fun _ ->
                let generator = MorphirFSharpGenerator() :> IMyriadGenerator

                Expect.throwsT<ArgumentException>
                    (fun () ->
                        generator.Generate(context "missing.json" None)
                        |> ignore
                    )
                    "selector is mandatory"
            testCase "the JSON generator emits the selected model module"
            <| fun _ ->
                withModel
                <| fun _ path ->
                    let generator = MorphirFSharpGenerator() :> IMyriadGenerator

                    Expect.sequenceEqual
                        generator.ValidInputExtensions
                        [ ".json" ]
                        "model input extension"

                    match generator.Generate(context path (Some "Inventory")) with
                    | Output.Source source ->
                        Expect.stringContains source "dailyRate" "emitted declaration"
                    | _ -> failtest "formatted source was expected"
            testCase "an unknown module selector fails explicitly"
            <| fun _ ->
                withModel
                <| fun _ path ->
                    let generator = MorphirFSharpGenerator() :> IMyriadGenerator

                    Expect.throwsT<ArgumentException>
                        (fun () ->
                            generator.Generate(context path (Some "Absent"))
                            |> ignore
                        )
                        "module must exist"
            testCase "the real Myriad host loads the JSON plugin"
            <| fun _ ->
                withModel
                <| fun directory path ->
                    let plugin = typeof<MorphirFSharpGenerator>.Assembly.Location
                    let compatibleHost = prepareHost directory plugin
                    let output = Path.Combine(directory, "Inventory.fs")

                    let dotnet =
                        Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"
                        |> Option.ofObj
                        |> Option.defaultValue "dotnet"

                    let start = ProcessStartInfo(dotnet)

                    for arg in
                        [
                            "--roll-forward"
                            "LatestMajor"
                            compatibleHost
                            "--inputfile"
                            path
                            "--outputfile"
                            output
                            "--plugin"
                            plugin
                            "--generator-filter"
                            "MorphirFSharpGenerator"
                            "--additionalparams"
                            "MorphirModule=Inventory"
                        ] do
                        start.ArgumentList.Add arg

                    // Gated, captured concurrently, and the tree is killed and awaited on timeout.
                    let result =
                        ChildProcess
                            .RunAsync(start, TimeSpan.FromSeconds 120.0)
                            .GetAwaiter()
                            .GetResult()

                    let diagnostics =
                        result.StandardOutput
                        + result.StandardError

                    Expect.equal result.ExitCode 0 diagnostics

                    Expect.stringContains
                        (File.ReadAllText output)
                        "dailyRate"
                        "host-produced module"
        ]

    [<EntryPoint>]
    let main argv = runTestsWithCLIArgs [] argv tests
