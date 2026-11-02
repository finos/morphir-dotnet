module Morphir.CodeGeneration.FSharp.Tests.TypedValidationTests

open System.Text.Json
open Expecto
open Morphir.IR.Model
open Morphir.IR.Pipeline
open Morphir.Testing.GenerationConformance
open Morphir.CodeGeneration.FSharp.Tests.TranspilerAdapter

module F = Morphir.CodeGeneration.FSharp.Tests.Fixtures

let private ok =
    function
    | Ok value -> value
    | Result.Error error -> failtestf "%A" error

let private generate json =
    use document = JsonDocument.Parse(json: string)
    let decoded = Morphir.Json.Classic.ClassicJson.readDistribution document.RootElement

    let model = {
        SourceVersion =
            (SourceFormatVersion.parse "3"
             |> ok)
        Distribution =
            DistributionModel.Classic
                (decoded
                 |> ok)
                    .Distribution
    }

    Morphir.CodeGeneration.FSharp.Transpiler.generateModel
        model
        (VFile.fromPath "retained/source.ion")

let failures = [
    "unbound",
    "unbound-variable",
    F.model [ "domain", [], [ F.binding "value" [] F.intType (F.variable "missing") ] ]
    "SDK",
    "unsupported-sdk",
    F.model [
        "domain", [], [ F.binding "value" [] F.intType (F.sdkReference "unknown" "operation") ]
    ]
    "cycles",
    "module-cycle",
    F.model [
        "left", [], [ F.binding "a" [] F.intType (F.reference "right" "b") ]
        "right", [], [ F.binding "b" [] F.intType (F.reference "left" "a") ]
    ]
    "names",
    "invalid-name",
    F.model [ "domain", [], [ F.binding "1invalid" [] F.intType (F.integer 3) ] ]
    "local-name",
    "invalid-name",
    F.model [
        "domain", [], [ F.binding "identity" [ "arg_1", F.intType ] F.intType (F.variable "arg_1") ]
    ]
    "arity",
    "type-arity",
    F.model [
        "domain",
        [ F.typeAlias "box" [ "a" ] (F.tVar "a") ],
        [ F.binding "value" [] (F.tRef (F.own "domain" "box") []) (F.integer 3) ]
    ]
    "type",
    "type-mismatch",
    F.model [ "domain", [], [ F.binding "value" [] F.intType (F.str "wrong") ] ]
]

[<Tests>]
let tests =
    testList "Typed validation" [
        for id, code, json in failures do
            testCase id
            <| fun () ->
                let representation = {
                    Version = "3"
                    Format = "typed"
                    Layout = "memory"
                    Optimize = true
                }

                let row = {
                    Representation = representation
                    Input = Refused((fun () -> json), code)
                }

                let case = {
                    Id =
                        "diagnostics."
                        + id
                    Consumer = ""
                    ExpectedOutput = ""
                    Required = [ representation ]
                    Variants = [ row ]
                }

                let adapter json =
                    let tree = generate json
                    let diagnostics = diagnosticsOf tree

                    if VFileTree.hasErrors tree then
                        Expect.equal (VFileTree.fileCount tree) 0 "no partial successful files"

                        for diagnostic in diagnostics do
                            Expect.equal
                                diagnostic.InputPath
                                "retained/source.ion"
                                "retained input context"

                        Result.Error(
                            diagnostics
                            |> List.map (fun diagnostic -> diagnostic.Category)
                        )
                    else
                        Ok []

                Morphir.Testing.GenerationConformance.run adapter case row
                |> ignore
    ]
