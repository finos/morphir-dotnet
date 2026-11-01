namespace Morphir.Myriad.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open Expecto
open Myriad.Core
open Morphir.Myriad

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
            Directory.Delete(directory, true)

    let context path selector =
        let parameters = Dictionary<string, string>()

        selector
        |> Option.iter (fun value -> parameters.Add("MorphirModule", value))

        GeneratorContext.Create(None, (fun _ -> Seq.empty), path, None, parameters)

    [<Tests>]
    let tests =
        testList "Morphir Myriad" [
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
                    let packages =
                        match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
                        | null ->
                            Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                ".nuget",
                                "packages"
                            )
                        | value -> value

                    let host =
                        Path.Combine(
                            packages,
                            "myriad",
                            "0.85.0",
                            "tools",
                            "net9.0",
                            "any",
                            "Myriad.dll"
                        )

                    let plugin = typeof<MorphirFSharpGenerator>.Assembly.Location
                    let compatibleDirectory = Path.Combine(directory, "host")

                    Directory.CreateDirectory compatibleDirectory
                    |> ignore

                    for file in Directory.GetFiles(Path.GetDirectoryName host) do
                        File.Copy(file, Path.Combine(compatibleDirectory, Path.GetFileName file))

                    File.Copy(
                        Path.Combine(Path.GetDirectoryName plugin, "FSharp.Core.dll"),
                        Path.Combine(compatibleDirectory, "FSharp.Core.dll"),
                        true
                    )

                    let compatibleHost = Path.Combine(compatibleDirectory, "Myriad.dll")
                    let output = Path.Combine(directory, "Inventory.fs")

                    let dotnet =
                        Environment.GetEnvironmentVariable "DOTNET_HOST_PATH"
                        |> Option.ofObj
                        |> Option.defaultValue "dotnet"

                    let start = ProcessStartInfo(dotnet)
                    start.UseShellExecute <- false
                    start.RedirectStandardOutput <- true
                    start.RedirectStandardError <- true

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

                    use child = Process.Start start
                    let stdout = child.StandardOutput.ReadToEndAsync()
                    let stderr = child.StandardError.ReadToEndAsync()

                    if not (child.WaitForExit 120000) then
                        child.Kill true
                        failtest "Myriad host exceeded its timeout"

                    let diagnostics =
                        stdout.Result
                        + stderr.Result

                    Expect.equal child.ExitCode 0 diagnostics

                    Expect.stringContains
                        (File.ReadAllText output)
                        "dailyRate"
                        "host-produced module"
        ]

    [<EntryPoint>]
    let main argv = runTestsWithCLIArgs [] argv tests
