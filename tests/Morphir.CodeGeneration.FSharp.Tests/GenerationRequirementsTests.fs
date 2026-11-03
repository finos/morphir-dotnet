module Morphir.CodeGeneration.FSharp.Tests.GenerationRequirementsTests

open Expecto
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.CodeGeneration.FSharp
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

[<Tests>]
let tests =
    testList "Generation requirements" [
        for version in [ "3"; "4.0.0" ] do
            testCase $"unknown required semantics fail for {version}"
            <| fun () ->
                let model = ModelConformanceInputs.typeStructure version

                let context =
                    VFile.fromPath "model"
                    |> VFile.set
                        Morphir.CodeGeneration.FSharp.Transpiler.requiredSemantics
                        (Set.singleton "example.numeric-profile")

                let tree = Morphir.CodeGeneration.FSharp.Transpiler.generateModel model context

                Expect.equal
                    (diagnosticsOf tree
                     |> List.map (fun diagnostic -> diagnostic.Category))
                    [ "unsupported-required-semantics" ]
                    "explicit requirement"

                Expect.equal (VFileTree.fileCount tree) 0 "no artifacts"
        testCase "legacy entry point also honors required semantics metadata"
        <| fun () ->
            let context =
                VFile.ofText "model.json" (ModelResources.read "daily-rate-v3.json")
                |> VFile.set
                    Morphir.CodeGeneration.FSharp.Transpiler.requiredSemantics
                    (Set.singleton "unknown")

            let tree = Morphir.CodeGeneration.FSharp.Transpiler.generate context

            Expect.equal
                (diagnosticsOf tree
                 |> List.map (fun diagnostic -> diagnostic.Category))
                [ "unsupported-required-semantics" ]
                "no silent fallback"
    ]
