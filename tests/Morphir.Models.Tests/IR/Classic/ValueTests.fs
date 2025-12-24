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
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
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
                    let functionValue = Value.reference () (FQName.fqNameFromPaths (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]) (Path.fromList [ Name.fromList [ "basics" ] ]) (Name.fromList [ "add" ]))
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

        testList "toString" [
            testList "Value.toString" [
                testCase "Constructor formats as FQName"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "maybe" ] ])
                            (Name.fromList [ "just" ])
                    let value = Value.constructor () fqName
                    Value.toString value
                    |> Expect.equal "Morphir.SDK.Maybe.Just"

                testCase "Tuple formats as comma-separated values in parentheses"
                <| fun _ ->
                    let element1 = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let element2 = Value.literal () (Literal.stringLiteral "test")
                    let value = Value.tuple () [ element1; element2 ]
                    Value.toString value
                    |> Expect.equal "(1, \"test\")"

                testCase "List formats as comma-separated values in brackets"
                <| fun _ ->
                    let elements = [
                        Value.literal () (Literal.wholeNumberLiteral 1L)
                        Value.literal () (Literal.wholeNumberLiteral 2L)
                    ]
                    let value = Value.list () elements
                    Value.toString value
                    |> Expect.equal "[ 1, 2 ]"

                testCase "Empty List formats as empty brackets"
                <| fun _ ->
                    let value = Value.list () []
                    Value.toString value
                    |> Expect.equal "[]"

                testCase "Record formats as fields in curly braces"
                <| fun _ ->
                    let fields =
                        Map.empty
                        |> Map.add (Name.fromList [ "name" ]) (Value.literal () (Literal.stringLiteral "John"))
                        |> Map.add (Name.fromList [ "age" ]) (Value.literal () (Literal.wholeNumberLiteral 30L))
                    let value = Value.record () fields
                    Value.toString value
                    |> Expect.equal "{ name = \"John\", age = 30 }"

                testCase "Reference formats as FQName in camelCase"
                <| fun _ ->
                    let fqName =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                            (Path.fromList [ Name.fromList [ "basics" ] ])
                            (Name.fromList [ "add" ])
                    let value = Value.reference () fqName
                    Value.toString value
                    |> Expect.equal "Morphir.SDK.Basics.Add"

                testCase "Unit formats as ()"
                <| fun _ ->
                    let value = Value.unit ()
                    Value.toString value
                    |> Expect.equal "()"

                testCase "Field formats as record.field"
                <| fun _ ->
                    let recordValue =
                        Value.record ()
                            (Map.empty
                             |> Map.add (Name.fromList [ "name" ]) (Value.literal () (Literal.stringLiteral "John")))
                    let fieldName = Name.fromList [ "name" ]
                    let value = Value.field () recordValue fieldName
                    Value.toString value
                    |> Expect.equal "{ name = \"John\" }.name"

                testCase "FieldFunction formats as .fieldName"
                <| fun _ ->
                    let fieldName = Name.fromList [ "age" ]
                    let value = Value.fieldFunction () fieldName
                    Value.toString value
                    |> Expect.equal ".age"

                testCase "Apply formats as function argument"
                <| fun _ ->
                    let func =
                        Value.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "add" ]))
                    let arg = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let value = Value.apply () func arg
                    Value.toString value
                    |> Expect.equal "Morphir.SDK.Basics.Add 1"

                testCase "Apply with complex arguments adds parentheses"
                <| fun _ ->
                    let func =
                        Value.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "add" ]))
                    let arg = Value.tuple () [ Value.literal () (Literal.wholeNumberLiteral 1L); Value.literal () (Literal.wholeNumberLiteral 2L) ]
                    let value = Value.apply () func arg
                    Value.toString value
                    |> Expect.equal "Morphir.SDK.Basics.Add (1, 2)"

                testCase "IfThenElse formats as if condition then thenBranch else elseBranch"
                <| fun _ ->
                    let condition = Value.literal () (Literal.boolLiteral true)
                    let thenBranch = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let elseBranch = Value.literal () (Literal.wholeNumberLiteral 2L)
                    let value = Value.ifThenElse () condition thenBranch elseBranch
                    Value.toString value
                    |> Expect.equal "if True then 1 else 2"

                testCase "UpdateRecord formats as { record | field = value }"
                <| fun _ ->
                    let recordValue =
                        Value.record ()
                            (Map.empty
                             |> Map.add (Name.fromList [ "name" ]) (Value.literal () (Literal.stringLiteral "John")))
                    let fieldsToUpdate =
                        Map.empty
                        |> Map.add (Name.fromList [ "age" ]) (Value.literal () (Literal.wholeNumberLiteral 30L))
                    let value = Value.updateRecord () recordValue fieldsToUpdate
                    Value.toString value
                    |> Expect.equal "{ { name = \"John\" } | age = 30 }"

                testCase "Lambda formats as \\pattern -> body"
                <| fun _ ->
                    let pattern = Pattern.wildcardPattern ()
                    let body = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let value = Value.lambda () pattern body
                    Value.toString value
                    |> Expect.equal "\\_ -> 1"

                testCase "Lambda with AsPattern formats correctly"
                <| fun _ ->
                    let pattern = Pattern.asPattern () (Pattern.wildcardPattern ()) (Name.fromList [ "x" ])
                    let body = Value.variable () (Name.fromList [ "x" ])
                    let value = Value.lambda () pattern body
                    Value.toString value
                    |> Expect.equal "\\_ as x -> x"

                testCase "Destructure formats as let pattern = value in body"
                <| fun _ ->
                    let pattern = Pattern.wildcardPattern ()
                    let valueToDestructure = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let inValue = Value.variable () (Name.fromList [ "x" ])
                    let value = Value.destructure () pattern valueToDestructure inValue
                    Value.toString value
                    |> Expect.equal "let _ = 1 in x"

                testCase "PatternMatch formats as case value of pattern -> body"
                <| fun _ ->
                    let valueToMatch = Value.literal () (Literal.wholeNumberLiteral 1L)
                    let pattern = Pattern.wildcardPattern ()
                    let body = Value.literal () (Literal.wholeNumberLiteral 2L)
                    let value = Value.patternMatch () valueToMatch [ (pattern, body) ]
                    Value.toString value
                    |> Expect.equal "case 1 of _ -> 2"

                testCase "PatternMatch with multiple cases formats correctly"
                <| fun _ ->
                    let valueToMatch = Value.literal () (Literal.boolLiteral true)
                    let case1 = (Pattern.literalPattern () (Literal.boolLiteral true), Value.literal () (Literal.wholeNumberLiteral 1L))
                    let case2 = (Pattern.literalPattern () (Literal.boolLiteral false), Value.literal () (Literal.wholeNumberLiteral 2L))
                    let value = Value.patternMatch () valueToMatch [ case1; case2 ]
                    Value.toString value
                    |> Expect.equal "case True of True -> 1; False -> 2"

                testCase "LetDefinition formats as let name args = body in inValue"
                <| fun _ ->
                    let name = Name.fromList [ "x" ]
                    let definition = Value.valueDefinition [] (Type.unit ()) (Value.literal () (Literal.wholeNumberLiteral 1L))
                    let inValue = Value.variable () name
                    let value = Value.letDefinition () name definition inValue
                    Value.toString value
                    |> Expect.equal "let x = 1 in x"

                testCase "LetRecursion formats as let name1 = body1; name2 = body2 in inValue"
                <| fun _ ->
                    let name1 = Name.fromList [ "x" ]
                    let name2 = Name.fromList [ "y" ]
                    let def1 = Value.valueDefinition [] (Type.unit ()) (Value.literal () (Literal.wholeNumberLiteral 1L))
                    let def2 = Value.valueDefinition [] (Type.unit ()) (Value.literal () (Literal.wholeNumberLiteral 2L))
                    let bindings =
                        Map.empty
                        |> Map.add name1 def1
                        |> Map.add name2 def2
                    let inValue = Value.variable () name1
                    let value = Value.letRecursion () bindings inValue
                    Value.toString value
                    |> Expect.equal "let x = 1; y = 2 in x"
            ]
        ]

        testList "ValueSpecification" [
            testList "toString" [
                testCase "ValueSpecification with no inputs formats as output type"
                <| fun _ ->
                    let output =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let spec = Value.valueSpecification [] output
                    Value.ValueSpecification.toString spec
                    |> Expect.equal "Morphir.SDK.Basics.Int"

                testCase "ValueSpecification with inputs formats as inputs -> output"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let stringType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "string" ] ])
                                (Name.fromList [ "string" ]))
                            []
                    let inputs = [
                        (Name.fromList [ "x" ], intType)
                        (Name.fromList [ "y" ], stringType)
                    ]
                    let spec = Value.valueSpecification inputs intType
                    Value.ValueSpecification.toString spec
                    |> Expect.equal "(x : Morphir.SDK.Basics.Int, y : Morphir.SDK.String.String) -> Morphir.SDK.Basics.Int"
            ]

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
            testList "toString" [
                testCase "ValueDefinition with no inputs formats as name = body"
                <| fun _ ->
                    let outputType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let body = Value.literal () (Literal.wholeNumberLiteral 42)
                    let def = Value.valueDefinition [] outputType body
                    Value.ValueDefinition.toString (Name.fromList [ "answer" ]) def
                    |> Expect.equal "answer = 42"

                testCase "ValueDefinition with inputs formats as name args = body"
                <| fun _ ->
                    let intType =
                        Type.reference ()
                            (FQName.fqNameFromPaths
                                (Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ])
                                (Path.fromList [ Name.fromList [ "basics" ] ])
                                (Name.fromList [ "int" ]))
                            []
                    let inputTypes = [
                        (Name.fromList [ "x" ], (), intType)
                        (Name.fromList [ "y" ], (), intType)
                    ]
                    let outputType = intType
                    let body = Value.variable () (Name.fromList [ "x" ])
                    let def = Value.valueDefinition inputTypes outputType body
                    Value.ValueDefinition.toString (Name.fromList [ "add" ]) def
                    |> Expect.equal "add x y = x"
            ]

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

