module Morphir.CodeGeneration.FSharp.Tests.GenerationOptionsTests

open System.Text.Json
open Expecto
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

let private ok =
    function
    | Ok value -> value
    | Result.Error error -> failtestf "%A" error

let private typed version json =
    use document = JsonDocument.Parse(json: string)

    let distribution =
        (Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
         |> ok)
            .Distribution

    {
        SourceVersion =
            SourceFormatVersion.parse version
            |> ok
        Distribution = DistributionModel.Classic distribution
    }

let private generate (row: Representation) =
    let options = {
        GenerationOptions.Optimize = row.Optimize
    }

    let json = ModelResources.read $"car-rental-v{row.Version}.json"

    let tree =
        if row.Layout = "memory" then
            Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                options
                (typed row.Version json)
                (VFile.fromPath "model.json")
        else
            Morphir.CodeGeneration.FSharp.Transpiler.generateWithOptions
                options
                (VFile.ofText "model.json" json)

    if VFileTree.hasErrors tree then
        Result.Error(
            diagnosticsOf tree
            |> List.map (fun diagnostic -> diagnostic.Category)
        )
    else
        Ok(
            modulesOf tree
            |> List.map (fun file -> file.RelativePath, file.Source)
        )

[<Tests>]
let tests =
    testList "Generation options" [
        let required = [
            for version in [ "1"; "2"; "3" ] do
                for layout in [ "memory"; "file" ] do
                    for optimize in [ true; false ] ->
                        {
                            Version = version
                            Format = "json"
                            Layout = layout
                            Optimize = optimize
                        }
        ]

        let variants = [
            for row in required ->
                {
                    Representation = row
                    Input = Supported(fun () -> row)
                }
        ]

        let case = GenerationCases.rentalTotal required variants

        for row in variants do
            testCase
                $"{case.Id}/{row.Representation.Version}/{row.Representation.Layout}/optimize={row.Representation.Optimize}"
            <| fun () ->
                GenerationConformance.run generate case row
                |> ignore

        testCase "defaults retain constant folding"
        <| fun () -> Expect.isTrue GenerationOptions.Default.Optimize "existing behavior"

        testCase "typed false omits the folding stage while retaining source behavior"
        <| fun () ->
            let json =
                Morphir.CodeGeneration.FSharp.Tests.Fixtures.model [
                    "math",
                    [],
                    [
                        Morphir.CodeGeneration.FSharp.Tests.Fixtures.binding
                            "sum"
                            []
                            Morphir.CodeGeneration.FSharp.Tests.Fixtures.intType
                            (Morphir.CodeGeneration.FSharp.Tests.Fixtures.op
                                "add"
                                (Morphir.CodeGeneration.FSharp.Tests.Fixtures.integer 20)
                                (Morphir.CodeGeneration.FSharp.Tests.Fixtures.integer 22))
                    ]
                ]

            let plain =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                    { Optimize = false }
                    (typed "3" json)
                    (VFile.fromPath "model.json")

            let folded =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
                    GenerationOptions.Default
                    (typed "3" json)
                    (VFile.fromPath "model.json")

            Expect.stringContains (modulesOf plain).Head.Source "+" "explicit false keeps addition"
            Expect.stringContains (modulesOf folded).Head.Source "42" "default folds exact addition"
    ]
