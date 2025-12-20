module Morphir.IR.Classic.Tests.ValueTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions
open System.Collections.Generic

[<Tests>]
let tests =
    testList "Value" [
        testList "Value Expressions" [
            testList "literal" [
                testCase "Creates Literal value with BoolLiteral"
                <| fun _ ->
                    let lit = Literal.boolLiteral true
                    let value = Value.literal () lit

                    match value with
                    | Value.Literal(attrs, literal) ->
                        match literal with
                        | Literal.BoolLiteral b -> b |> Expect.equal true
                        | _ -> failwith "Expected BoolLiteral"
                    | _ -> failwith "Expected Literal value"
            ]

            testList "constructor" [
                testCase "Creates Constructor value"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "maybe" ] ])
                            (Name.fromList [ "just" ])
                    let value = Value.constructor () fqName

                    match value with
                    | Value.Constructor(attrs, fn) ->
                        fn |> Expect.equal fqName
                    | _ -> failwith "Expected Constructor value"
            ]

            testList "tuple" [
                testCase "Creates Tuple value with multiple elements"
                <| fun _ ->
                    let element1 = Value.literal () (Literal.boolLiteral true)
                    let element2 = Value.literal () (Literal.stringLiteral "test")
                    let value = Value.tuple () [ element1; element2 ]

                    match value with
                    | Value.Tuple(attrs, elements) ->
                        elements.Length |> Expect.equal 2
                    | _ -> failwith "Expected Tuple value"
            ]

            testList "list" [
                testCase "Creates List value"
                <| fun _ ->
                    let elements = [
                        Value.literal () (Literal.wholeNumberLiteral 1L)
                        Value.literal () (Literal.wholeNumberLiteral 2L)
                        Value.literal () (Literal.wholeNumberLiteral 3L)
                    ]
                    let value = Value.list () elements

                    match value with
                    | Value.List(attrs, items) ->
                        items.Length |> Expect.equal 3
                    | _ -> failwith "Expected List value"
            ]

            testList "record" [
                testCase "Creates Record value"
                <| fun _ ->
                    let fields =
                        Map.empty
                        |> Map.add (Name.fromList [ "firstName" ]) (Value.literal () (Literal.stringLiteral "John"))
                        |> Map.add (Name.fromList [ "age" ]) (Value.literal () (Literal.wholeNumberLiteral 30L))
                    let value = Value.record () fields

                    match value with
                    | Value.Record(attrs, fs) ->
                        fs.Count |> Expect.equal 2
                    | _ -> failwith "Expected Record value"
            ]

            testList "variable" [
                testCase "Creates Variable value"
                <| fun _ ->
                    let name = Name.fromList [ "x" ]
                    let value = Value.variable () name

                    match value with
                    | Value.Variable(attrs, n) ->
                        n |> Expect.equal name
                    | _ -> failwith "Expected Variable value"
            ]

            testList "reference" [
                testCase "Creates Reference value"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ])
                            (Path.fromList [ Name.fromList [ "basics" ] ])
                            (Name.fromList [ "add" ])
                    let value = Value.reference () fqName

                    match value with
                    | Value.Reference(attrs, fn) ->
                        fn |> Expect.equal fqName
                    | _ -> failwith "Expected Reference value"
            ]

            testList "field" [
                testCase "Creates Field value"
                <| fun _ ->
                    let recordValue = Value.variable () (Name.fromList [ "user" ])
                    let fieldName = Name.fromList [ "firstName" ]
                    let value = Value.field () recordValue fieldName

                    match value with
                    | Value.Field(attrs, record, field) ->
                        field |> Expect.equal fieldName
                    | _ -> failwith "Expected Field value"
            ]

            testList "fieldFunction" [
                testCase "Creates FieldFunction value"
                <| fun _ ->
                    let fieldName = Name.fromList [ "firstName" ]
                    let value = Value.fieldFunction () fieldName

                    match value with
                    | Value.FieldFunction(attrs, field) ->
                        field |> Expect.equal fieldName
                    | _ -> failwith "Expected FieldFunction value"
            ]

            testList "apply" [
                testCase "Creates Apply value"
                <| fun _ ->
                    let functionValue = Value.reference () (FQName.fqNameFromPaths (Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ]) (Path.fromList [ Name.fromList [ "basics" ] ]) (Name.fromList [ "add" ]))
                    let argumentValue = Value.literal () (Literal.wholeNumberLiteral 5L)
                    let value = Value.apply () functionValue argumentValue

                    match value with
                    | Value.Apply(attrs, func, arg) -> ()
                    | _ -> failwith "Expected Apply value"
            ]

            testList "lambda" [
                testCase "Creates Lambda value"
                <| fun _ ->
                    let pattern = Pattern.wildcardPattern ()
                    let body = Value.variable () (Name.fromList [ "x" ])
                    let value = Value.lambda () pattern body

                    match value with
                    | Value.Lambda(attrs, pat, b) -> ()
                    | _ -> failwith "Expected Lambda value"
            ]

            testList "letDefinition" [
                testCase "Creates LetDefinition value"
                <| fun _ ->
                    let bindingName = Name.fromList [ "x" ]
                    let definition = Value.valueDefinition [] (Type.unit ()) (Value.literal () (Literal.wholeNumberLiteral 5L))
                    let inExpr = Value.variable () bindingName
                    let value = Value.letDefinition () bindingName definition inExpr

                    match value with
                    | Value.LetDefinition(attrs, name, def, expr) ->
                        name |> Expect.equal bindingName
                    | _ -> failwith "Expected LetDefinition value"
            ]

            testList "letRecursion" [
                testCase "Creates LetRecursion value"
                <| fun _ ->
                    let bindings =
                        Map.empty
                        |> Map.add (Name.fromList [ "f" ]) (Value.valueDefinition [] (Type.unit ()) (Value.unit ()))
                    let inExpr = Value.unit ()
                    let value = Value.letRecursion () bindings inExpr

                    match value with
                    | Value.LetRecursion(attrs, bindings', expr) ->
                        bindings'.Count |> Expect.equal 1
                    | _ -> failwith "Expected LetRecursion value"
            ]

            testList "destructure" [
                testCase "Creates Destructure value"
                <| fun _ ->
                    let pattern = Pattern.tuplePattern () [ Pattern.wildcardPattern (); Pattern.wildcardPattern () ]
                    let valueToDestructure = Value.tuple () [ Value.unit (); Value.unit () ]
                    let inExpr = Value.unit ()
                    let value = Value.destructure () pattern valueToDestructure inExpr

                    match value with
                    | Value.Destructure(attrs, pat, valToDest, expr) -> ()
                    | _ -> failwith "Expected Destructure value"
            ]

            testList "ifThenElse" [
                testCase "Creates IfThenElse value"
                <| fun _ ->
                    let condition = Value.literal () (Literal.boolLiteral true)
                    let thenBranch = Value.literal () (Literal.stringLiteral "yes")
                    let elseBranch = Value.literal () (Literal.stringLiteral "no")
                    let value = Value.ifThenElse () condition thenBranch elseBranch

                    match value with
                    | Value.IfThenElse(attrs, cond, then', else') -> ()
                    | _ -> failwith "Expected IfThenElse value"
            ]

            testList "patternMatch" [
                testCase "Creates PatternMatch value"
                <| fun _ ->
                    let valueToMatch = Value.variable () (Name.fromList [ "x" ])
                    let cases = [
                        (Pattern.wildcardPattern (), Value.unit ())
                    ]
                    let value = Value.patternMatch () valueToMatch cases

                    match value with
                    | Value.PatternMatch(attrs, valToMatch, cases') ->
                        cases'.Length |> Expect.equal 1
                    | _ -> failwith "Expected PatternMatch value"
            ]

            testList "updateRecord" [
                testCase "Creates UpdateRecord value"
                <| fun _ ->
                    let recordToUpdate = Value.variable () (Name.fromList [ "user" ])
                    let fieldsToUpdate =
                        Map.empty
                        |> Map.add (Name.fromList [ "age" ]) (Value.literal () (Literal.wholeNumberLiteral 31L))
                    let value = Value.updateRecord () recordToUpdate fieldsToUpdate

                    match value with
                    | Value.UpdateRecord(attrs, record, fields) ->
                        fields.Count |> Expect.equal 1
                    | _ -> failwith "Expected UpdateRecord value"
            ]

            testList "unit" [
                testCase "Creates Unit value"
                <| fun _ ->
                    let value = Value.unit ()

                    match value with
                    | Value.Unit attrs -> ()
                    | _ -> failwith "Expected Unit value"
            ]
        ]

        testList "ValueSpecification" [
            testCase "Creates ValueSpecification"
            <| fun _ ->
                let inputs = [
                    (Name.fromList [ "a" ], Type.variable () (Name.fromList [ "int" ]))
                    (Name.fromList [ "b" ], Type.variable () (Name.fromList [ "int" ]))
                ]
                let output = Type.variable () (Name.fromList [ "int" ])
                let spec = Value.valueSpecification inputs output

                spec.Inputs.Length |> Expect.equal 2
                spec.Output |> Expect.equal output
        ]

        testList "ValueDefinition" [
            testCase "Creates ValueDefinition"
            <| fun _ ->
                let inputTypes = [
                    (Name.fromList [ "x" ], (), Type.variable () (Name.fromList [ "int" ]))
                ]
                let outputType = Type.variable () (Name.fromList [ "int" ])
                let body = Value.variable () (Name.fromList [ "x" ])
                let def = Value.valueDefinition inputTypes outputType body

                def.InputTypes.Length |> Expect.equal 1
                def.OutputType |> Expect.equal outputType
                match def.Body with
                | Value.Variable(_, name) -> name |> Expect.equal (Name.fromList [ "x" ])
                | _ -> failwith "Expected Variable in body"
        ]
    ]

