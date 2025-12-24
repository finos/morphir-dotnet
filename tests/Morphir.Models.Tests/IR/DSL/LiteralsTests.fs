namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR.Classic.DSL.Literals
open Morphir.IR.Classic.Literal

module LiteralsTests =

    [<Tests>]
    let tests =
        testList "DSL Literals" [
            testList "LiteralBuilder" [
                testCase "Creates BoolLiteral"
                <| fun _ ->
                    let result = literal { bool true }
                    let expected = BoolLiteral true
                    result |> Expect.equal expected

                testCase "Creates StringLiteral"
                <| fun _ ->
                    let result = literal { string "hello" }
                    let expected = StringLiteral "hello"
                    result |> Expect.equal expected

                testCase "Creates WholeNumberLiteral"
                <| fun _ ->
                    let result = literal { int 42L }
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Creates FloatLiteral"
                <| fun _ ->
                    let result = literal { float 3.14 }
                    let expected = FloatLiteral 3.14
                    result |> Expect.equal expected

                testCase "Creates CharLiteral"
                <| fun _ ->
                    let result = literal { char 'a' }
                    let expected = CharLiteral 'a'
                    result |> Expect.equal expected

                testCase "Creates DecimalLiteral"
                <| fun _ ->
                    let result = literal { decimal "123.45" }
                    let expected = DecimalLiteral "123.45"
                    result |> Expect.equal expected
            ]

            testList "Helpers" [
                testCase "boolLiteral helper works"
                <| fun _ ->
                    let result = Helpers.boolLiteral true
                    let expected = BoolLiteral true
                    result |> Expect.equal expected

                testCase "stringLiteral helper works"
                <| fun _ ->
                    let result = Helpers.stringLiteral "test"
                    let expected = StringLiteral "test"
                    result |> Expect.equal expected

                testCase "wholeNumberLiteral helper works"
                <| fun _ ->
                    let result = Helpers.wholeNumberLiteral 42L
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected
            ]
        ]

