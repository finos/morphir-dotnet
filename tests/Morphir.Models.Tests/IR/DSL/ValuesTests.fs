namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.Value
open Morphir.IR.Classic.Literal
open Morphir.IR.Classic.DSL.Values
open Morphir.IR.Classic.DSL.Helpers

module ValuesTests =

    [<Tests>]
    let tests =
        testList "DSL Values" [
            testList "Tagless Literal Syntax" [
                testCase "Creates Literal value from int (tagless)" <| fun _ ->
                    let result = value { 42 }
                    let expected = Literal((), WholeNumberLiteral 42L)
                    Expect.equal result expected "Should create WholeNumberLiteral from int"

                testCase "Creates Literal value from int64 (tagless)" <| fun _ ->
                    let result = value { 100L }
                    let expected = Literal((), WholeNumberLiteral 100L)
                    Expect.equal result expected "Should create WholeNumberLiteral from int64"

                testCase "Creates Literal value from string (tagless)" <| fun _ ->
                    let result = value { "hello" }
                    let expected = Literal((), StringLiteral "hello")
                    Expect.equal result expected "Should create StringLiteral from string"

                testCase "Creates Literal value from bool (tagless)" <| fun _ ->
                    let result = value { true }
                    let expected = Literal((), BoolLiteral true)
                    Expect.equal result expected "Should create BoolLiteral from bool"

                testCase "Creates Literal value from float (tagless)" <| fun _ ->
                    let result = value { 3.14 }
                    let expected = Literal((), FloatLiteral 3.14)
                    Expect.equal result expected "Should create FloatLiteral from float"

                testCase "Creates Literal value from decimal (tagless)" <| fun _ ->
                    let result = value { 99.99m }
                    let expected = Literal((), DecimalLiteral 99.99m)
                    Expect.equal result expected "Should create DecimalLiteral from decimal"

                testCase "Creates Literal value from char (tagless)" <| fun _ ->
                    let result = value { 'x' }
                    let expected = Literal((), CharLiteral 'x')
                    Expect.equal result expected "Should create CharLiteral from char"
            ]

            testList "Tagless Unit Syntax" [
                testCase "Creates Unit value with tagless syntax" <| fun _ ->
                    let result = value { () }
                    let expected = Unit ()
                    Expect.equal result expected "value { () } should create Unit value"
            ]

            testList "CustomOperations" [
                testCase "CustomOperation 'variable' works with Name" <| fun _ ->
                    let result = value { variable (Name.fromString "x") }
                    let expected = Variable((), Name.fromString "x")
                    Expect.equal result expected "Should create Variable from Name"

                testCase "CustomOperation 'variable' works with string" <| fun _ ->
                    let result = value { variable "myVar" }
                    let expected = Variable((), Name.fromString "myVar")
                    Expect.equal result expected "Should create Variable from string"

                testCase "CustomOperation 'literal' works" <| fun _ ->
                    let result = value { literal (BoolLiteral true) }
                    let expected = Literal((), BoolLiteral true)
                    Expect.equal result expected "Should create Literal"

                testCase "CustomOperation 'tuple' works" <| fun _ ->
                    let elem1 = value { 1 }
                    let elem2 = value { 2 }
                    let result = value { tuple [elem1; elem2] }
                    let expected = Value.Tuple((), [Literal((), WholeNumberLiteral 1L); Literal((), WholeNumberLiteral 2L)])
                    Expect.equal result expected "Should create Tuple"

                testCase "CustomOperation 'list' works" <| fun _ ->
                    let elem1 = value { "a" }
                    let elem2 = value { "b" }
                    let result = value { list [elem1; elem2] }
                    let expected = Value.List((), [Literal((), StringLiteral "a"); Literal((), StringLiteral "b")])
                    Expect.equal result expected "Should create List"

                testCase "CustomOperation 'record' works with Map" <| fun _ ->
                    let fields = Map.ofList [(Name.fromString "x", value { 10 })]
                    let result = value { record fields }
                    let expected = Value.Record((), Map.ofList [(Name.fromString "x", Literal((), WholeNumberLiteral 10L))])
                    Expect.equal result expected "Should create Record from Map"

                testCase "CustomOperation 'record' works with Name list" <| fun _ ->
                    let result = value { record [(Name.fromString "x", value { 10 })] }
                    let expected = Value.Record((), Map.ofList [(Name.fromString "x", Literal((), WholeNumberLiteral 10L))])
                    Expect.equal result expected "Should create Record from Name list"

                testCase "CustomOperation 'record' works with string list" <| fun _ ->
                    let result = value { record [("name", value { "Alice" })] }
                    let expected = Value.Record((), Map.ofList [(Name.fromString "name", Literal((), StringLiteral "Alice"))])
                    Expect.equal result expected "Should create Record from string list"

                testCase "CustomOperation 'reference' works" <| fun _ ->
                    let fqn = fqnSimple ["pkg"] ["mod"] "func"
                    let result = value { reference fqn }
                    let expected = Value.Reference((), fqn)
                    Expect.equal result expected "Should create Reference"

                testCase "CustomOperation 'apply' works" <| fun _ ->
                    let func = value.Reference(fqnSimple ["std"] ["math"] "add")
                    let arg = value { 5 }
                    let result = value { apply func arg }
                    let expected = Apply((), func, arg)
                    Expect.equal result expected "Should create Apply"
            ]

            testList "Pascal-case Methods (Direct Usage)" [
                testCase "Creates Variable value" <| fun _ ->
                    let result = value.Variable("x")
                    let expected = Variable((), Name.fromString "x")
                    Expect.equal result expected "Should create Variable"

                testCase "Creates Literal value" <| fun _ ->
                    let result = value.Literal(StringLiteral "test")
                    let expected = Literal((), StringLiteral "test")
                    Expect.equal result expected "Should create Literal"

                testCase "Creates Tuple value" <| fun _ ->
                    let elem1 = value { 1 }
                    let elem2 = value { 2 }
                    let result = value.Tuple([elem1; elem2])
                    let expected = Value.Tuple((), [Literal((), WholeNumberLiteral 1L); Literal((), WholeNumberLiteral 2L)])
                    Expect.equal result expected "Should create Tuple"

                testCase "Creates List value" <| fun _ ->
                    let elem1 = value { true }
                    let elem2 = value { false }
                    let result = value.List([elem1; elem2])
                    let expected = Value.List((), [Literal((), BoolLiteral true); Literal((), BoolLiteral false)])
                    Expect.equal result expected "Should create List"

                testCase "Creates Record value" <| fun _ ->
                    let fields = Map.ofList [(Name.fromString "age", value { 30 })]
                    let result = value.Record(fields)
                    let expected = Value.Record((), Map.ofList [(Name.fromString "age", Literal((), WholeNumberLiteral 30L))])
                    Expect.equal result expected "Should create Record"

                testCase "Creates Reference value" <| fun _ ->
                    let fqn = fqnSimple ["lib"] ["util"] "helper"
                    let result = value.Reference(fqn)
                    let expected = Value.Reference((), fqn)
                    Expect.equal result expected "Should create Reference"

                testCase "Creates Apply value" <| fun _ ->
                    let func = value.Reference(fqnSimple ["std"] ["list"] "map")
                    let arg = value { 10 }
                    let result = value.Apply(func, arg)
                    let expected = Apply((), func, arg)
                    Expect.equal result expected "Should create Apply"

                testCase "Creates Unit value" <| fun _ ->
                    let result = value.Unit()
                    let expected = Unit ()
                    Expect.equal result expected "Should create Unit"
            ]
        ]

