module Morphir.IR.Classic.Tests.LiteralTests

open Expecto
open Morphir.IR.Classic
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Literal" [
        testList "boolLiteral" [
            testCase "Creates BoolLiteral with true"
            <| fun _ ->
                let lit = Literal.boolLiteral true

                match lit with
                | Literal.BoolLiteral value ->
                    value |> Expect.equal true
                | _ -> failwith "Expected BoolLiteral"

            testCase "Creates BoolLiteral with false"
            <| fun _ ->
                let lit = Literal.boolLiteral false

                match lit with
                | Literal.BoolLiteral value ->
                    value |> Expect.equal false
                | _ -> failwith "Expected BoolLiteral"
        ]

        testList "charLiteral" [
            testCase "Creates CharLiteral"
            <| fun _ ->
                let lit = Literal.charLiteral 'a'

                match lit with
                | Literal.CharLiteral value ->
                    value |> Expect.equal 'a'
                | _ -> failwith "Expected CharLiteral"
        ]

        testList "stringLiteral" [
            testCase "Creates StringLiteral"
            <| fun _ ->
                let lit = Literal.stringLiteral "hello"

                match lit with
                | Literal.StringLiteral value ->
                    value |> Expect.equal "hello"
                | _ -> failwith "Expected StringLiteral"
        ]

        testList "wholeNumberLiteral" [
            testCase "Creates WholeNumberLiteral with positive number"
            <| fun _ ->
                let lit = Literal.wholeNumberLiteral 42L

                match lit with
                | Literal.WholeNumberLiteral value ->
                    value |> Expect.equal 42L
                | _ -> failwith "Expected WholeNumberLiteral"

            testCase "Creates WholeNumberLiteral with negative number"
            <| fun _ ->
                let lit = Literal.wholeNumberLiteral -17L

                match lit with
                | Literal.WholeNumberLiteral value ->
                    value |> Expect.equal -17L
                | _ -> failwith "Expected WholeNumberLiteral"
        ]

        testList "floatLiteral" [
            testCase "Creates FloatLiteral with positive number"
            <| fun _ ->
                let lit = Literal.floatLiteral 3.14

                match lit with
                | Literal.FloatLiteral value ->
                    value |> Expect.equal 3.14
                | _ -> failwith "Expected FloatLiteral"

            testCase "Creates FloatLiteral with negative number"
            <| fun _ ->
                let lit = Literal.floatLiteral -0.5

                match lit with
                | Literal.FloatLiteral value ->
                    value |> Expect.equal -0.5
                | _ -> failwith "Expected FloatLiteral"
        ]

        testList "decimalLiteral" [
            testCase "Creates DecimalLiteral"
            <| fun _ ->
                let lit = Literal.decimalLiteral "123.456"

                match lit with
                | Literal.DecimalLiteral value ->
                    value |> Expect.equal "123.456"
                | _ -> failwith "Expected DecimalLiteral"

            testCase "Creates DecimalLiteral with negative number"
            <| fun _ ->
                let lit = Literal.decimalLiteral "-123.456"

                match lit with
                | Literal.DecimalLiteral value ->
                    value |> Expect.equal "-123.456"
                | _ -> failwith "Expected DecimalLiteral"
        ]
    ]

