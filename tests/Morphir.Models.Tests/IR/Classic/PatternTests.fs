module Morphir.IR.Classic.Tests.PatternTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Pattern" [
        testList "wildcardPattern" [
            testCase "Creates WildcardPattern"
            <| fun _ ->
                let pattern = Pattern.wildcardPattern ()

                match pattern with
                | Pattern.WildcardPattern attrs -> ()
                | _ -> failwith "Expected WildcardPattern"
        ]

        testList "asPattern" [
            testCase "Creates AsPattern with WildcardPattern nested"
            <| fun _ ->
                let name = Name.fromList [ "x" ]
                let nested = Pattern.wildcardPattern ()
                let pattern = Pattern.asPattern () nested name

                match pattern with
                | Pattern.AsPattern(attrs, nestedPattern, varName) ->
                    varName |> Expect.equal name
                    match nestedPattern with
                    | Pattern.WildcardPattern _ -> ()
                    | _ -> failwith "Expected nested WildcardPattern"
                | _ -> failwith "Expected AsPattern"
        ]

        testList "tuplePattern" [
            testCase "Creates TuplePattern with multiple elements"
            <| fun _ ->
                let element1 = Pattern.wildcardPattern ()
                let element2 = Pattern.wildcardPattern ()
                let pattern = Pattern.tuplePattern () [ element1; element2 ]

                match pattern with
                | Pattern.TuplePattern(attrs, elements) ->
                    elements.Length |> Expect.equal 2
                | _ -> failwith "Expected TuplePattern"
        ]

        testList "constructorPattern" [
            testCase "Creates ConstructorPattern without arguments"
            <| fun _ ->
                let fqName =
                    FQName.fqNameFromPaths
                        (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                        (Path.fromList [ Name.fromList [ "maybe" ] ])
                        (Name.fromList [ "nothing" ])
                let pattern = Pattern.constructorPattern () fqName []

                match pattern with
                | Pattern.ConstructorPattern(attrs, fn, args) ->
                    fn |> Expect.equal fqName
                    args |> Expect.equal []
                | _ -> failwith "Expected ConstructorPattern"

            testCase "Creates ConstructorPattern with arguments"
            <| fun _ ->
                let fqName =
                    FQName.fqNameFromPaths
                        (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                        (Path.fromList [ Name.fromList [ "maybe" ] ])
                        (Name.fromList [ "just" ])
                let argPattern = Pattern.wildcardPattern ()
                let pattern = Pattern.constructorPattern () fqName [ argPattern ]

                match pattern with
                | Pattern.ConstructorPattern(attrs, fn, args) ->
                    fn |> Expect.equal fqName
                    args.Length |> Expect.equal 1
                | _ -> failwith "Expected ConstructorPattern"
        ]

        testList "emptyListPattern" [
            testCase "Creates EmptyListPattern"
            <| fun _ ->
                let pattern = Pattern.emptyListPattern ()

                match pattern with
                | Pattern.EmptyListPattern attrs -> ()
                | _ -> failwith "Expected EmptyListPattern"
        ]

        testList "headTailPattern" [
            testCase "Creates HeadTailPattern"
            <| fun _ ->
                let headPattern = Pattern.wildcardPattern ()
                let tailPattern = Pattern.wildcardPattern ()
                let pattern = Pattern.headTailPattern () headPattern tailPattern

                match pattern with
                | Pattern.HeadTailPattern(attrs, head, tail) ->
                    match head with
                    | Pattern.WildcardPattern _ -> ()
                    | _ -> failwith "Expected head to be WildcardPattern"
                    match tail with
                    | Pattern.WildcardPattern _ -> ()
                    | _ -> failwith "Expected tail to be WildcardPattern"
                | _ -> failwith "Expected HeadTailPattern"
        ]

        testList "literalPattern" [
            testCase "Creates LiteralPattern with BoolLiteral"
            <| fun _ ->
                let lit = Literal.boolLiteral true
                let pattern = Pattern.literalPattern () lit

                match pattern with
                | Pattern.LiteralPattern(attrs, literal) ->
                    match literal with
                    | Literal.BoolLiteral value ->
                        value |> Expect.equal true
                    | _ -> failwith "Expected BoolLiteral"
                | _ -> failwith "Expected LiteralPattern"

            testCase "Creates LiteralPattern with StringLiteral"
            <| fun _ ->
                let lit = Literal.stringLiteral "test"
                let pattern = Pattern.literalPattern () lit

                match pattern with
                | Pattern.LiteralPattern(attrs, literal) ->
                    match literal with
                    | Literal.StringLiteral value ->
                        value |> Expect.equal "test"
                    | _ -> failwith "Expected StringLiteral"
                | _ -> failwith "Expected LiteralPattern"
        ]

        testList "unitPattern" [
            testCase "Creates UnitPattern"
            <| fun _ ->
                let pattern = Pattern.unitPattern ()

                match pattern with
                | Pattern.UnitPattern attrs -> ()
                | _ -> failwith "Expected UnitPattern"
        ]

        testList "toString" [
            testCase "WildcardPattern formats as underscore"
            <| fun _ ->
                let pattern = Pattern.wildcardPattern ()
                Pattern.toString pattern
                |> Expect.equal "_"

            testCase "AsPattern formats as nested pattern as name"
            <| fun _ ->
                let nested = Pattern.wildcardPattern ()
                let name = Name.fromList [ "x" ]
                let pattern = Pattern.asPattern () nested name
                Pattern.toString pattern
                |> Expect.equal "_ as x"

            testCase "TuplePattern formats as comma-separated patterns in parentheses"
            <| fun _ ->
                let element1 = Pattern.wildcardPattern ()
                let element2 = Pattern.wildcardPattern ()
                let pattern = Pattern.tuplePattern () [ element1; element2 ]
                Pattern.toString pattern
                |> Expect.equal "(_ , _)"

            testCase "ConstructorPattern without arguments formats as FQName"
            <| fun _ ->
                let fqName =
                    FQName.fqNameFromPaths
                        (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                        (Path.fromList [ Name.fromList [ "maybe" ] ])
                        (Name.fromList [ "nothing" ])
                let pattern = Pattern.constructorPattern () fqName []
                Pattern.toString pattern
                |> Expect.equal "Morphir.SDK.Maybe.Nothing"

            testCase "ConstructorPattern with arguments formats as FQName followed by patterns"
            <| fun _ ->
                let fqName =
                    FQName.fqNameFromPaths
                        (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                        (Path.fromList [ Name.fromList [ "maybe" ] ])
                        (Name.fromList [ "just" ])
                let argPattern = Pattern.wildcardPattern ()
                let pattern = Pattern.constructorPattern () fqName [ argPattern ]
                Pattern.toString pattern
                |> Expect.equal "Morphir.SDK.Maybe.Just _"

            testCase "EmptyListPattern formats as empty brackets"
            <| fun _ ->
                let pattern = Pattern.emptyListPattern ()
                Pattern.toString pattern
                |> Expect.equal "[]"

            testCase "HeadTailPattern formats as head :: tail"
            <| fun _ ->
                let headPattern = Pattern.wildcardPattern ()
                let tailPattern = Pattern.wildcardPattern ()
                let pattern = Pattern.headTailPattern () headPattern tailPattern
                Pattern.toString pattern
                |> Expect.equal "_ :: _"

            testCase "LiteralPattern formats using Literal.toString"
            <| fun _ ->
                let lit = Literal.boolLiteral true
                let pattern = Pattern.literalPattern () lit
                Pattern.toString pattern
                |> Expect.equal "True"

            testCase "UnitPattern formats as ()"
            <| fun _ ->
                let pattern = Pattern.unitPattern ()
                Pattern.toString pattern
                |> Expect.equal "()"
        ]
    ]

