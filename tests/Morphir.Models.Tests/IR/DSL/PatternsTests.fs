namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.Testing.Assertions
open Morphir.IR
open Morphir.IR.Classic.DSL.Patterns
open Morphir.IR.DSL.Names

module PatternsTests =

    [<Tests>]
    let tests =
        testList "DSL Patterns" [
            testList "PatternBuilder" [
                testCase "Creates WildcardPattern"
                <| fun _ ->
                    let result = pattern { Wildcard }
                    let expected = Morphir.IR.Classic.Pattern.wildcard ()
                    result |> Expect.equal expected

                testCase "Creates VariablePattern"
                <| fun _ ->
                    let result = pattern { Variable "x" }
                    let expected =
                        Morphir.IR.Classic.Pattern.asPattern () (Morphir.IR.Classic.Pattern.wildcard ()) (Name.fromString "x")
                    result |> Expect.equal expected

                testCase "Creates TuplePattern"
                <| fun _ ->
                    let pattern1 = pattern { Variable "x" }
                    let pattern2 = pattern { Variable "y" }
                    let result = pattern { Tuple [ pattern1; pattern2 ] }
                    let expected = Morphir.IR.Classic.Pattern.tuple () [ pattern1; pattern2 ]
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
                    let expected = Morphir.IR.Classic.Pattern.constructor () fqName [ argPattern ]
                    result |> Expect.equal expected

                testCase "Creates LiteralPattern"
                <| fun _ ->
                    let result = pattern { Literal (Morphir.IR.Classic.Literal.BoolLiteral true) }
                    let expected = Morphir.IR.Classic.Pattern.literal () (Morphir.IR.Classic.Literal.BoolLiteral true)
                    result |> Expect.equal expected

                testCase "Creates UnitPattern with CustomOperation"
                <| fun _ ->
                    let result = pattern { Unit }
                    let expected = Morphir.IR.Classic.Pattern.unit ()
                    result |> Expect.equal expected

                testCase "Creates UnitPattern with tagless syntax"
                <| fun _ ->
                    let result = pattern { () }
                    let expected = Morphir.IR.Classic.Pattern.unit ()
                    result |> Expect.equal expected

                testCase "Creates AsPattern with fluent As method (string name)"
                <| fun _ ->
                    let nestedPattern = pattern { Variable "x" }
                    let result = nestedPattern.As("value")
                    let expected = Morphir.IR.Classic.Pattern.asPattern () nestedPattern (Name.fromString "value")
                    result |> Expect.equal expected

                testCase "Creates AsPattern with fluent As method (Name)"
                <| fun _ ->
                    let nestedPattern = pattern { Variable "y" }
                    let name = Name.fromString "item"
                    let result = nestedPattern.As(name)
                    let expected = Morphir.IR.Classic.Pattern.asPattern () nestedPattern name
                    result |> Expect.equal expected

                testCase "Creates AsPattern with fluent As method on tuple pattern"
                <| fun _ ->
                    let tuplePattern = pattern { Tuple [ pattern { Variable "x" }; pattern { Variable "y" } ] }
                    let result = tuplePattern.As("point")
                    let expected = Morphir.IR.Classic.Pattern.asPattern () tuplePattern (Name.fromString "point")
                    result |> Expect.equal expected

                testCase "Creates AsPattern with fluent As method (explicit attributes)"
                <| fun _ ->
                    let nestedPattern = pattern { Variable "z" }
                    let result = nestedPattern.As((), "coord")
                    let expected = Morphir.IR.Classic.Pattern.asPattern () nestedPattern (Name.fromString "coord")
                    result |> Expect.equal expected

                testCase "Creates TuplePattern from 2-tuple (tagless syntax)"
                <| fun _ ->
                    let p1 = pattern { Variable "x" }
                    let p2 = pattern { Variable "y" }
                    let tup = (p1, p2)
                    let result = pattern { tup }
                    let expected = Morphir.IR.Classic.Pattern.tuple () [ p1; p2 ]
                    result |> Expect.equal expected

                testCase "Creates TuplePattern from 3-tuple (tagless syntax)"
                <| fun _ ->
                    let p1 = pattern { Variable "x" }
                    let p2 = pattern { Variable "y" }
                    let p3 = pattern { Variable "z" }
                    let tup = (p1, p2, p3)
                    let result = pattern { tup }
                    let expected = Morphir.IR.Classic.Pattern.tuple () [ p1; p2; p3 ]
                    result |> Expect.equal expected

                testCase "Creates TuplePattern from 4-tuple (tagless syntax)"
                <| fun _ ->
                    let p1 = pattern { Wildcard }
                    let p2 = pattern { Variable "a" }
                    let p3 = pattern { Variable "b" }
                    let p4 = pattern { Wildcard }
                    let tup = (p1, p2, p3, p4)
                    let result = pattern { tup }
                    let expected = Morphir.IR.Classic.Pattern.tuple () [ p1; p2; p3; p4 ]
                    result |> Expect.equal expected

                testCase "Creates TuplePattern from nested tuple pattern"
                <| fun _ ->
                    let p1 = pattern { Variable "x" }
                    let p2 = pattern { Variable "y" }
                    let innerTup = (p1, p2)
                    let inner = pattern { innerTup }
                    let p3 = pattern { Variable "z" }
                    let outerTup = (inner, p3)
                    let result = pattern { outerTup }
                    let innerExpected = Morphir.IR.Classic.Pattern.tuple () [ p1; p2 ]
                    let expected = Morphir.IR.Classic.Pattern.tuple () [ innerExpected; p3 ]
                    result |> Expect.equal expected

                testCase "Creates HeadTailPattern with fluent Cons method"
                <| fun _ ->
                    let headPat = pattern { Variable "x" }
                    let tailPat = pattern { Variable "xs" }
                    let result = headPat.Cons(tailPat)
                    let expected = Morphir.IR.Classic.Pattern.headTail () headPat tailPat
                    result |> Expect.equal expected

                testCase "Creates HeadTailPattern with fluent Cons method (explicit attributes)"
                <| fun _ ->
                    let headPat = pattern { Variable "first" }
                    let tailPat = pattern { Variable "rest" }
                    let result = headPat.Cons((), tailPat)
                    let expected = Morphir.IR.Classic.Pattern.headTail () headPat tailPat
                    result |> Expect.equal expected

                testCase "Creates nested HeadTailPattern with chained Cons"
                <| fun _ ->
                    let p1 = pattern { Variable "a" }
                    let p2 = pattern { Variable "b" }
                    let p3 = pattern { Variable "rest" }
                    let result = p1.Cons(p2.Cons(p3))
                    let inner = Morphir.IR.Classic.Pattern.headTail () p2 p3
                    let expected = Morphir.IR.Classic.Pattern.headTail () p1 inner
                    result |> Expect.equal expected

                testCase "Creates HeadTailPattern with EmptyListPattern tail"
                <| fun _ ->
                    let headPat = pattern { Variable "single" }
                    let emptyTail = Morphir.IR.Classic.Pattern.emptyList ()
                    let result = headPat.Cons(emptyTail)
                    let expected = Morphir.IR.Classic.Pattern.headTail () headPat emptyTail
                    result |> Expect.equal expected
            ]
        ]

