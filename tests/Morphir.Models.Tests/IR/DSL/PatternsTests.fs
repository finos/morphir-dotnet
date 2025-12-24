namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR.Classic.DSL.Patterns
open Morphir.IR.DSL.Names
open Morphir.IR.Classic.Pattern
open Morphir.IR.Name

module PatternsTests =

    [<Tests>]
    let tests =
        testList "DSL Patterns" [
            testList "PatternBuilder" [
                testCase "Creates WildcardPattern"
                <| fun _ ->
                    let result = pattern { Wildcard () }
                    let expected = Morphir.IR.Classic.Pattern.wildcardPattern ()
                    result |> Expect.equal expected

                testCase "Creates VariablePattern"
                <| fun _ ->
                    let result = pattern { Variable "x" }
                    let expected =
                        Morphir.IR.Classic.Pattern.asPattern () (Morphir.IR.Classic.Pattern.wildcardPattern ()) (Morphir.IR.Name.fromString "x")
                    result |> Expect.equal expected

                testCase "Creates TuplePattern"
                <| fun _ ->
                    let pattern1 = pattern { Variable "x" }
                    let pattern2 = pattern { Variable "y" }
                    let result = pattern { Tuple [ pattern1; pattern2 ] }
                    let expected = Morphir.IR.Classic.Pattern.tuplePattern () [ pattern1; pattern2 ]
                    result |> Expect.equal expected

                testCase "Creates ConstructorPattern"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["maybe"]
                            local ["just"]
                        }
                    let argPattern = pattern { Variable "value" }
                    let result = pattern { Constructor fqName [ argPattern ] }
                    let expected = Morphir.IR.Classic.Pattern.constructorPattern () fqName [ argPattern ]
                    result |> Expect.equal expected

                testCase "Creates LiteralPattern"
                <| fun _ ->
                    let result = pattern { Literal (Morphir.IR.Classic.Literal.BoolLiteral true) }
                    let expected = Morphir.IR.Classic.Pattern.literalPattern () (Morphir.IR.Classic.Literal.BoolLiteral true)
                    result |> Expect.equal expected

                testCase "Creates UnitPattern"
                <| fun _ ->
                    let result = pattern { Unit () }
                    let expected = Morphir.IR.Classic.Pattern.unitPattern ()
                    result |> Expect.equal expected
            ]
        ]

