module Morphir.CodeGeneration.FSharp.Tests.ModelConformanceTests

open Expecto
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

let private generate model =
    match model.Distribution with
    | DistributionModel.Classic _ ->
        let tree =
            Morphir.CodeGeneration.FSharp.Transpiler.generateModel model (VFile.fromPath "model")

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
    | DistributionModel.V4 distribution ->
        try
            V4Emit.generate GenerationOptions.Default distribution
            |> List.map (fun file -> file.RelativePath, file.Source)
            |> Ok
        with GenerationFailure(code, _, _) ->
            Result.Error [ code ]

[<Tests>]
let tests =
    testList "Model conformance" [
        let required = [
            for version in [ "3"; "4.0.0" ] ->
                {
                    Version = version
                    Format = "typed"
                    Layout = "memory"
                    Optimize = true
                }
        ]

        let variants = [
            for row in required ->
                {
                    Representation = row
                    Input = Supported(fun () -> ModelConformanceInputs.typeStructure row.Version)
                }
        ]

        let case = GenerationCases.typeStructure required variants

        for row in variants do
            testCase $"{case.Id}/{row.Representation.Version}"
            <| fun () ->
                GenerationConformance.run generate case row
                |> ignore

            testCase $"visibility/{row.Representation.Version}"
            <| fun () ->
                let files =
                    ModelConformanceInputs.typeStructure row.Representation.Version
                    |> generate

                let sources =
                    ModelConformanceInputs.ok files
                    |> List.map snd
                    |> String.concat "\n"

                Expect.stringContains
                    sources
                    "internal Hidden"
                    "private definitions retain assembly visibility"

        for caseBuilder, inputs in
            [
                GenerationCases.expressionCore, ModelConformanceInputs.expressionCore
                GenerationCases.recordBilling, ModelConformanceInputs.recordBilling
                GenerationCases.lexicalCapture, ModelConformanceInputs.lexicalCapture
                GenerationCases.localRecursion, ModelConformanceInputs.localRecursion
                GenerationCases.recordOperations, ModelConformanceInputs.recordOperations
                GenerationCases.letRecOrder, ModelConformanceInputs.letRecOrder
                GenerationCases.sdkMappings, ModelConformanceInputs.sdkMappings
            ] do
            let required = [
                for version in [ "3"; "4.0.0" ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
            ]

            let variants = [
                for row in required ->
                    {
                        Representation = row
                        Input = Supported(fun () -> inputs row.Version)
                    }
            ]

            let case = caseBuilder required variants

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore

        let recursionRows = [
            for version in [ "3"; "4.0.0" ] ->
                {
                    Version = version
                    Format = "typed"
                    Layout = "memory"
                    Optimize = true
                }
        ]

        let recursionVariants = [
            for row in recursionRows ->
                {
                    Representation = row
                    Input =
                        Refused(
                            (fun () -> ModelConformanceInputs.letRecCycle row.Version),
                            "initialization-cycle"
                        )
                }
        ]

        let recursionCase = {
            Id = "scope.recursive-value-cycle"
            Consumer = ""
            ExpectedOutput = ""
            Required = recursionRows
            Variants = recursionVariants
        }

        for row in recursionVariants do
            testCase $"{recursionCase.Id}/{row.Representation.Version}"
            <| fun () ->
                GenerationConformance.run generate recursionCase row
                |> ignore

        for id, code, json in TypedValidationTests.failures do
            let required = [
                for version in [ "3"; "4.0.0" ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
            ]

            let variants = [
                for row in required ->
                    {
                        Representation = row
                        Input =
                            Refused(
                                (fun () ->
                                    ModelConformanceInputs.classic json
                                    |> ModelConformanceInputs.forVersion row.Version
                                ),
                                code
                            )
                    }
            ]

            let case = {
                Id =
                    "diagnostics."
                    + id
                Consumer = ""
                ExpectedOutput = ""
                Required = required
                Variants = variants
            }

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore

        for consistent in [ true; false ] do
            let required = [
                for version in [ "3"; "4.0.0" ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
            ]

            let variants = [
                for row in required do
                    let input =
                        if row.Version = "3" then
                            Unrepresentable
                                "Classic attributes do not define v4 inferred-type semantics"
                        elif consistent then
                            Supported(fun () -> ModelConformanceInputs.inferredLiteral true)
                        else
                            Refused(
                                (fun () -> ModelConformanceInputs.inferredLiteral false),
                                "type-mismatch"
                            )

                    yield { Representation = row; Input = input }
            ]

            let case = {
                Id =
                    (if consistent then
                         "inferred.consistent"
                     else
                         "inferred.contradiction")
                Consumer = "printfn \"%d\" Car.Rental.Literals.value"
                ExpectedOutput = "42"
                Required = required
                Variants = variants
            }

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore

        testCase "inferred-type diagnostic retains its source range"
        <| fun () ->
            match (ModelConformanceInputs.inferredLiteral false).Distribution with
            | DistributionModel.V4 distribution ->
                try
                    V4Emit.generate GenerationOptions.Default distribution
                    |> ignore

                    failtest "contradiction must fail without returning artifacts"
                with GenerationFailure(code, location, _) ->
                    Expect.equal code "type-mismatch" "common diagnostic category"
                    Expect.stringContains location "7:3" "v4 source context"
            | _ -> failtest "fixture must be v4"

        for declaredNamed in [ true; false ] do
            let required = [
                for version in [ "3"; "4.0.0" ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
            ]

            let variants = [
                for row in required ->
                    {
                        Representation = row
                        Input =
                            (if row.Version = "3" then
                                 Unrepresentable
                                     "Classic attributes do not define v4 inferred-type semantics"
                             else
                                 Supported(fun () ->
                                     ModelConformanceInputs.inferredRecord declaredNamed
                                 ))
                    }
            ]

            let case = {
                Id =
                    "inferred.record-context-"
                    + string declaredNamed
                Consumer = "printfn \"%d\" Car.Rental.Inferred.value.score"
                ExpectedOutput = "9"
                Required = required
                Variants = variants
            }

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore

        for scalar in GenerationScalars.cases do
            let required = [
                for version in [ "3"; "4.0.0" ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = true
                    }
            ]

            let variants = [
                for row in required do
                    let materialize () =
                        ModelConformanceInputs.scalar row.Version scalar.Input

                    let input =
                        match row.Version, scalar.ClassicUnavailable, scalar.Refusal with
                        | "3", Some reason, _ -> Unrepresentable reason
                        | _, _, Some code -> Refused(materialize, code)
                        | _ -> Supported materialize

                    yield { Representation = row; Input = input }
            ]

            let case = {
                Id = scalar.Id
                Consumer = scalar.Consumer
                ExpectedOutput = scalar.Expected
                Required = required
                Variants = variants
            }

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore
    ]
