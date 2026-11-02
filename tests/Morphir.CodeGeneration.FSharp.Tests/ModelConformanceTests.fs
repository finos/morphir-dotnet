module Morphir.CodeGeneration.FSharp.Tests.ModelConformanceTests

open Expecto
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.Testing
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

let private generateWith optimize model =
    let tree =
        Morphir.CodeGeneration.FSharp.Transpiler.generateModelWith
            { Optimize = optimize }
            model
            (VFile.fromPath "model")

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

let private generate = generateWith true

let private publishResult output result =
    let tree =
        match result with
        | Ok files ->
            files
            |> List.map (fun (path, source) -> path, VFile.ofText path source)
            |> Map.ofList
            |> VFileTree.fromFileMap
            |> VFileTree.set VFileTree.declaredOrder (List.map fst files)
        | Result.Error codes ->
            codes
            |> List.fold
                (fun tree code ->
                    tree
                    |> VFileTree.report
                        { VMessage.create MessageSeverity.Error "Generation refused" with
                            RuleId = Some code
                        }
                )
                VFileTree.empty

    VFileTree.publish
        {
            OutputRoot = output
            Manifest = ManifestMode.Manifest "test-ownership.json"
        }
        tree

let private preservesPublication materialize =
    let directory =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "morphir-refusal-"
            + System.Guid.NewGuid().ToString("N")
        )

    try
        publishResult directory (Ok [ "prior.fs", "module Prior\nlet value = 1" ])
        |> ModelConformanceInputs.ok
        |> ignore

        let snapshot () =
            System.IO.Directory.GetFiles(directory, "*", System.IO.SearchOption.AllDirectories)
            |> Array.map (fun file ->
                System.IO.Path.GetRelativePath(directory, file),
                System.Convert.ToBase64String(System.IO.File.ReadAllBytes file)
            )
            |> Array.sort

        let before = snapshot ()

        match
            generate (materialize ())
            |> publishResult directory
        with
        | Ok _ -> failtest "A refused model must never publish artifacts"
        | Result.Error _ ->
            Expect.equal (snapshot ()) before "common publication contract preserves earlier output"
    finally
        if System.IO.Directory.Exists directory then
            System.IO.Directory.Delete(directory, true)

[<Tests>]
let tests =
    testList "Model conformance" [
        let rentalRows: Representation list = [
            for version in [ "1"; "2"; "3"; "4.0.0" ] do
                for optimize in [ false; true ] ->
                    {
                        Version = version
                        Format = "typed"
                        Layout = "memory"
                        Optimize = optimize
                    }
        ]

        let rentalInputs = [
            for row in rentalRows ->
                {
                    Representation = row
                    Input =
                        Supported(fun () ->
                            let version = if row.Version = "4.0.0" then "3" else row.Version
                            let json = ModelResources.read $"car-rental-v{version}.json"
                            let model = ModelConformanceInputs.classic json

                            if row.Version = "4.0.0" then
                                ClassicToV4TestAdapter.promote model
                            else
                                model
                        )
                }
        ]

        let rental = GenerationCases.rentalTotal rentalRows rentalInputs

        for row in rentalInputs do
            testCase
                $"public pricing/{row.Representation.Version}/optimize={row.Representation.Optimize}"
            <| fun () ->
                GenerationConformance.run (generateWith row.Representation.Optimize) rental row
                |> ignore

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
                for version in [ "3"; "4.0.0" ] do
                    for layout in [ "memory"; "nested-binding" ] ->
                        {
                            Version = version
                            Format = "typed"
                            Layout = layout
                            Optimize = true
                        }
            ]

            let variants = [
                for row in required ->
                    {
                        Representation = row
                        Input =
                            Supported(fun () ->
                                if row.Layout = "nested-binding" then
                                    inputs "3"
                                    |> ModelConformanceInputs.nestedBindings row.Version
                                else
                                    inputs row.Version
                            )
                    }
            ]

            let case = caseBuilder required variants

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}/{row.Representation.Layout}"
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

        let dependencyRows: Representation list = [
            for version in [ "3"; "4.0.0" ] ->
                {
                    Version = version
                    Format = "typed"
                    Layout = "derived-dependency"
                    Optimize = true
                }
        ]

        let dependencyVariants = [
            for row in dependencyRows ->
                {
                    Representation = row
                    Input =
                        if row.Version = "3" then
                            Unrepresentable "Classic has no DerivedTypeSpecification"
                        else
                            Supported(fun () ->
                                ModelRefusalInputs.dependencySignature "derived" false
                            )
                }
        ]

        let dependencyCase =
            GenerationCases.derivedDependency dependencyRows dependencyVariants

        for row in dependencyVariants do
            testCase $"{dependencyCase.Id}/{row.Representation.Version}"
            <| fun () ->
                GenerationConformance.run generate dependencyCase row
                |> ignore

        for id, code, inputs in ModelRefusalInputs.cases do
            let variants = [
                for version, input in inputs ->
                    {
                        Representation = {
                            Version = version
                            Format = "typed"
                            Layout = "memory"
                            Optimize = true
                        }
                        Input = input
                    }
            ]

            let case = {
                Id =
                    "refusal."
                    + id
                Consumer = ""
                ExpectedOutput = ""
                Required =
                    variants
                    |> List.map (fun row -> row.Representation)
                Variants = variants
            }

            for row in variants do
                testCase $"{case.Id}/{row.Representation.Version}"
                <| fun () ->
                    GenerationConformance.run generate case row
                    |> ignore

                match row.Input with
                | Refused(materialize, _) ->
                    testCase $"publication/{case.Id}/{row.Representation.Version}"
                    <| fun () -> preservesPublication materialize
                | _ -> ()

        testCase "opaque optional metadata is retained without becoming code"
        <| fun () ->
            let row = {
                Representation = {
                    Version = "4.0.0"
                    Format = "typed"
                    Layout = "memory"
                    Optimize = true
                }
                Input = Supported ModelRefusalInputs.opaqueMetadata
            }

            let case = {
                Id = "metadata.opaque"
                Consumer = "printfn \"%d\" Car.Rental.Refusal.value"
                ExpectedOutput = "42"
                Required = [ row.Representation ]
                Variants = [ row ]
            }

            GenerationConformance.run generate case row
            |> ignore

        for arithmetic in GenerationArithmetic.cases do
            let required = [
                for version in [ "3"; "4.0.0" ] do
                    for optimize in [ true; false ] ->
                        {
                            Version = version
                            Format = "typed"
                            Layout = "memory"
                            Optimize = optimize
                        }
            ]

            let variants = [
                for row in required ->
                    {
                        Representation = row
                        Input =
                            Supported(fun () ->
                                ModelConformanceInputs.arithmetic row.Version arithmetic.Kind
                            )
                    }
            ]

            let case = {
                Id = arithmetic.Id
                Consumer = arithmetic.Consumer
                ExpectedOutput = arithmetic.Expected
                Required = required
                Variants = variants
            }

            for row in variants do
                testCase
                    $"{case.Id}/{row.Representation.Version}/optimize={row.Representation.Optimize}"
                <| fun () ->
                    GenerationConformance.run (generateWith row.Representation.Optimize) case row
                    |> ignore

                    if
                        arithmetic.Kind
                        <> "user"
                    then
                        let source =
                            ModelConformanceInputs.arithmetic
                                row.Representation.Version
                                arithmetic.Kind
                            |> generateWith row.Representation.Optimize
                            |> ModelConformanceInputs.ok
                            |> List.map snd
                            |> String.concat "\n"

                        Expect.equal
                            (source.Contains "+")
                            (not (
                                row.Representation.Optimize
                                && arithmetic.Folds
                            ))
                            "only proven exact folds replace operations"

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
