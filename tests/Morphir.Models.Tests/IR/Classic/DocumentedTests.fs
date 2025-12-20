module Morphir.IR.Classic.Tests.DocumentedTests

open Expecto
open Morphir.IR.Classic
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Documented" [
        testList "withDocumentation" [
            testCase "Creates Documented with documentation"
            <| fun _ ->
                let documented = Documented.withDocumentation "This is a test" 42

                match documented with
                | Documented.WithDocumentation(doc, value) ->
                    doc |> Expect.equal "This is a test"
                    value |> Expect.equal 42
                | _ -> failwith "Expected WithDocumentation"
        ]

        testList "withoutDocumentation" [
            testCase "Creates Documented without documentation"
            <| fun _ ->
                let documented = Documented.withoutDocumentation 42

                match documented with
                | Documented.WithoutDocumentation value ->
                    value |> Expect.equal 42
                | _ -> failwith "Expected WithoutDocumentation"
        ]

        testList "value" [
            testCase "Gets value from WithDocumentation"
            <| fun _ ->
                let documented = Documented.withDocumentation "doc" 42
                let value = Documented.value documented

                value |> Expect.equal 42

            testCase "Gets value from WithoutDocumentation"
            <| fun _ ->
                let documented = Documented.withoutDocumentation 42
                let value = Documented.value documented

                value |> Expect.equal 42
        ]

        testList "doc" [
            testCase "Gets documentation from WithDocumentation"
            <| fun _ ->
                let documented = Documented.withDocumentation "test doc" 42
                let doc = Documented.doc documented

                doc |> Expect.equal (Some "test doc")

            testCase "Gets None from WithoutDocumentation"
            <| fun _ ->
                let documented = Documented.withoutDocumentation 42
                let doc = Documented.doc documented

                doc |> Expect.equal None
        ]

        testList "map" [
            testCase "Maps value in WithDocumentation while preserving doc"
            <| fun _ ->
                let documented = Documented.withDocumentation "doc" 5
                let mapped = Documented.map (fun x -> x * 2) documented

                match mapped with
                | Documented.WithDocumentation(doc, value) ->
                    doc |> Expect.equal "doc"
                    value |> Expect.equal 10
                | _ -> failwith "Expected WithDocumentation"

            testCase "Maps value in WithoutDocumentation"
            <| fun _ ->
                let documented = Documented.withoutDocumentation 5
                let mapped = Documented.map (fun x -> x * 2) documented

                match mapped with
                | Documented.WithoutDocumentation value ->
                    value |> Expect.equal 10
                | _ -> failwith "Expected WithoutDocumentation"
        ]
    ]

