module Morphir.CodeGeneration.FSharp.Tests.TypedGenerationTests

open System.Text.Json
open Expecto
open Morphir.IR
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

let private load version json =
    use document = JsonDocument.Parse(json: string)

    let decoded =
        Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement
        |> ok

    {
        SourceVersion =
            SourceFormatVersion.parse version
            |> ok
        Distribution = DistributionModel.Classic decoded.Distribution
    }

let private generate (version, path, json) =
    let tree =
        Morphir.CodeGeneration.FSharp.Transpiler.generateModel
            (load version json)
            (VFile.fromPath path)

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
    testList "Typed model generation" [
        testCase "documented public typed-model consumer compiles"
        <| fun () ->
            let references = [
                typeof<VersionedModel>.Assembly.Location
                typeof<VFile>.Assembly.Location
                typeof<GenerationOptions>.Assembly.Location
            ]

            let actual =
                GenerationCompiler.executeWithReferences
                    references
                    []
                    """
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.CodeGeneration.FSharp
let generateLibrary (model: VersionedModel) =
    Transpiler.generateModel model (VFile.fromPath "retained/model.ion")
let generateWithoutFolding (model: VersionedModel) =
    Transpiler.generateModelWith { GenerationOptions.Default with Optimize = false } model (VFile.fromPath "retained/model.ion")
printfn "%b" GenerationOptions.Default.Optimize
"""

            Expect.equal actual "true" "public usage compiles without internal adapters"
        |> testLabel "PlatformSpecific"

        let required = [
            for version in [ "1"; "2"; "3" ] ->
                {
                    Version = version
                    Format = "json"
                    Layout = "typed"
                    Optimize = true
                }
        ]

        let variants = [
            for row in required ->
                {
                    Representation = row
                    Input =
                        Supported(fun () ->
                            row.Version,
                            "retained/model.json",
                            ModelResources.read $"car-rental-v{row.Version}.json"
                        )
                }
        ]

        let case = GenerationCases.rentalTotal required variants

        for row in variants do
            testCase $"{case.Id}/{row.Representation.Version}/typed"
            <| fun () ->
                GenerationConformance.run generate case row
                |> ignore
            |> testLabel "PlatformSpecific"

            testCase $"legacy and typed source and order agree for {row.Representation.Version}"
            <| fun () ->
                let version = row.Representation.Version
                let json = ModelResources.read $"car-rental-v{version}.json"

                let typed =
                    Morphir.CodeGeneration.FSharp.Transpiler.generateModel
                        (load version json)
                        (VFile.fromPath "model.json")

                let legacy =
                    Morphir.CodeGeneration.FSharp.Transpiler.generate (
                        VFile.ofText "model.json" json
                    )

                Expect.equal (modulesOf typed) (modulesOf legacy) "same source in declared order"

                Expect.equal
                    (VFileTree.tryGet Morphir.CodeGeneration.FSharp.Transpiler.manifest typed)
                    (VFileTree.tryGet Morphir.CodeGeneration.FSharp.Transpiler.manifest legacy)
                    "same manifest"

        testCase "classic semantic release context reaches the typed path"
        <| fun () ->
            let json = ModelResources.read "car-rental-v3.json"

            for version in [ "3.0.0"; "3.1.0" ] do
                let row = {
                    Representation = {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
                    Input = Supported(fun () -> version, "model.ion", json)
                }

                let case = GenerationCases.rentalTotal [ row.Representation ] [ row ]

                GenerationConformance.run generate case row
                |> ignore
        |> testLabel "PlatformSpecific"

        testCase "typed Specs remain an unsupported distribution"
        <| fun () ->
            let model = {
                SourceVersion =
                    SourceFormatVersion.parse "3.1.0"
                    |> ok
                Distribution =
                    DistributionModel.Classic(
                        Classic.Distribution.Specs(
                            PackageName(Path[Name["example"]]),
                            Map.empty,
                            { Modules = Map.empty }
                        )
                    )
            }

            let tree =
                Morphir.CodeGeneration.FSharp.Transpiler.generateModel
                    model
                    (VFile.fromPath "specs.ion")

            Expect.equal
                (diagnosticsOf tree
                 |> List.map (fun diagnostic -> diagnostic.Category))
                [ "unsupported-distribution" ]
                "specific refusal"

            Expect.equal (VFileTree.fileCount tree) 0 "no successful files"
    ]
