module Morphir.CodeGeneration.FSharp.Tests.GenerationConformanceTests

open Expecto
open Morphir.Testing
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

let private representation version : Representation = {
    Version = version
    Format = "json"
    Layout = "file"
}

let private rentalCase =
    let required = [ for version in [ "1"; "2"; "3" ] -> representation version ]

    let variants = [
        for version in [ "1"; "2"; "3" ] ->
            {
                Representation = representation version
                Input =
                    Supported(fun () ->
                        $"car-rental-v{version}.json",
                        ModelResources.read $"car-rental-v{version}.json"
                    )
            }
    ]

    GenerationCases.rentalTotal required variants

let private generate (path, json) =
    Transpiler.Generate(path, json)
    |> Result.map (List.map (fun file -> file.RelativePath, file.Source))
    |> Result.mapError (List.map (fun error -> error.Category))

[<Tests>]
let tests =
    testList "Generation conformance" [
        for variant in rentalCase.Variants do
            testCase $"{rentalCase.Id}/{variant.Representation.Version}/json"
            <| fun _ ->
                let result = GenerationConformance.run generate rentalCase variant
                Expect.equal result (GenerationConformance.Executed "15000") "independent behavior"

        testCase "matrix rejects missing required representations"
        <| fun _ ->
            let incomplete =
                { rentalCase with
                    Variants = List.tail rentalCase.Variants
                }

            Expect.isNonEmpty (GenerationConformance.validate incomplete) "missing row is an error"

        testCase "matrix rejects duplicate representations"
        <| fun _ ->
            let duplicate =
                { rentalCase with
                    Variants =
                        rentalCase.Variants.Head
                        :: rentalCase.Variants
                }

            Expect.isNonEmpty (GenerationConformance.validate duplicate) "duplicate row is an error"

        testCase "unrepresentable encoding retains a reason without an ignored test"
        <| fun _ ->
            let row = {
                Representation = {
                    Version = "1"
                    Format = "ion"
                    Layout = "file"
                }
                Input = GenerationConformance.Unrepresentable "Ion IR does not define a v1 encoding"
            }

            let case =
                { rentalCase with
                    Required = [ row.Representation ]
                    Variants = [ row ]
                }

            Expect.isEmpty (GenerationConformance.validate case) "explicit incompatibility"

            Expect.equal
                (GenerationConformance.run generate case row)
                (GenerationConformance.NotRepresentable "Ion IR does not define a v1 encoding")
                "reason is a visible result"

        testCase "unrepresentable encoding requires an explanation"
        <| fun _ ->
            let row =
                { rentalCase.Variants.Head with
                    Input = GenerationConformance.Unrepresentable ""
                }

            let case =
                { rentalCase with
                    Required = [ row.Representation ]
                    Variants = [ row ]
                }

            Expect.isNonEmpty
                (GenerationConformance.validate case)
                "unexplained omission is an error"

        testCase "expected refusal uses the same runner"
        <| fun _ ->
            let row =
                { rentalCase.Variants.Head with
                    Input =
                        GenerationConformance.Refused(
                            (fun () -> "bad.json", "{}"),
                            "unsupported-version"
                        )
                }

            let case =
                { rentalCase with
                    Required = [ row.Representation ]
                    Variants = [ row ]
                }

            Expect.equal
                (GenerationConformance.run generate case row)
                (GenerationConformance.RefusedAsExpected "unsupported-version")
                "diagnostic expectation"
    ]
