namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.Testing.Assertions
open Morphir.IR.Classic.DSL.Literals
open Morphir.IR.Classic  // Literal type and its cases (BoolLiteral, etc.) are at namespace level

module LiteralsTests =

    [<Tests>]
    let tests =
        testList "DSL Literals" [
            testList "Tagged Syntax (CustomOperations)" [
                testCase "Creates BoolLiteral with tagged syntax"
                <| fun _ ->
                    let result = literal { Bool true }
                    let expected = BoolLiteral true
                    result |> Expect.equal expected

                testCase "Creates StringLiteral with tagged syntax"
                <| fun _ ->
                    let result = literal { String "hello" }
                    let expected = StringLiteral "hello"
                    result |> Expect.equal expected

                testCase "Creates WholeNumberLiteral with tagged syntax (int64)"
                <| fun _ ->
                    let result = literal { Int 42L }
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Creates WholeNumberLiteral with tagged syntax (int32)"
                <| fun _ ->
                    let result = literal { Int 42 }
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Creates FloatLiteral with tagged syntax"
                <| fun _ ->
                    let result = literal { Float 3.14 }
                    let expected = FloatLiteral 3.14
                    result |> Expect.equal expected

                testCase "Creates CharLiteral with tagged syntax"
                <| fun _ ->
                    let result = literal { Char 'a' }
                    let expected = CharLiteral 'a'
                    result |> Expect.equal expected

                testCase "Creates DecimalLiteral with tagged syntax"
                <| fun _ ->
                    let result = literal { Decimal 123.45m }
                    let expected = DecimalLiteral 123.45m
                    result |> Expect.equal expected
            ]

            testList "Tagless Syntax (Yield overloads)" [
                testCase "Creates BoolLiteral with tagless syntax"
                <| fun _ ->
                    let result = literal { true }
                    let expected = BoolLiteral true
                    result |> Expect.equal expected

                testCase "Creates StringLiteral with tagless syntax"
                <| fun _ ->
                    let result = literal { "hello" }
                    let expected = StringLiteral "hello"
                    result |> Expect.equal expected

                testCase "Creates WholeNumberLiteral with tagless syntax (int64)"
                <| fun _ ->
                    let result = literal { 42L }
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Creates WholeNumberLiteral with tagless syntax (int32)"
                <| fun _ ->
                    let result = literal { 42 }
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Creates FloatLiteral with tagless syntax"
                <| fun _ ->
                    let result = literal { 3.14 }
                    let expected = FloatLiteral 3.14
                    result |> Expect.equal expected

                testCase "Creates CharLiteral with tagless syntax"
                <| fun _ ->
                    let result = literal { 'a' }
                    let expected = CharLiteral 'a'
                    result |> Expect.equal expected

                testCase "Creates DecimalLiteral with tagless syntax"
                <| fun _ ->
                    let result = literal { 123.45m }
                    let expected = DecimalLiteral 123.45m
                    result |> Expect.equal expected
            ]

            testList "Direct constructors (from Literal module)" [
                testCase "Literal.boolLiteral function works"
                <| fun _ ->
                    let result = Literal.boolLiteral true
                    let expected = BoolLiteral true
                    result |> Expect.equal expected

                testCase "Literal.stringLiteral function works"
                <| fun _ ->
                    let result = Literal.stringLiteral "test"
                    let expected = StringLiteral "test"
                    result |> Expect.equal expected

                testCase "Literal.wholeNumberLiteral function works"
                <| fun _ ->
                    let result = Literal.wholeNumberLiteral 42L
                    let expected = WholeNumberLiteral 42L
                    result |> Expect.equal expected

                testCase "Literal.decimalLiteral function works"
                <| fun _ ->
                    let result = Literal.decimalLiteral 123.45m
                    let expected = DecimalLiteral 123.45m
                    result |> Expect.equal expected
            ]

            testList "Zero case" [
                testCase "Empty literal block returns empty string"
                <| fun _ ->
                    let result = literal { () }
                    let expected = StringLiteral ""
                    result |> Expect.equal expected
            ]
        ]

