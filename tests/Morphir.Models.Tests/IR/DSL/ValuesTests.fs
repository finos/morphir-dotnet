namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR.Classic.DSL.Values
open Morphir.IR.DSL.Names
open Morphir.IR.Classic.DSL.Literals
open Morphir.IR.Classic.Value
open Morphir.IR.Classic.Literal
open Morphir.IR.Name

module ValuesTests =

    [<Tests>]
    let tests =
        testList "DSL Values" [
            testList "ValueBuilder" [
                testCase "Creates Literal value"
                <| fun _ ->
                    let result = value { literal (boolLiteral true) }
                    let expected = Value.literal () (BoolLiteral true)
                    result |> Expect.equal expected

                testCase "Creates Variable value"
                <| fun _ ->
                    let result = value { variable "x" }
                    let expected = Value.variable () (Name.fromString "x")
                    result |> Expect.equal expected

                testCase "Creates Tuple value"
                <| fun _ ->
                    let element1 = value { literal (wholeNumberLiteral 1L) }
                    let element2 = value { literal (stringLiteral "test") }
                    let result = value { tuple [ element1; element2 ] }
                    let expected = Value.tuple () [ element1; element2 ]
                    result |> Expect.equal expected

                testCase "Creates List value"
                <| fun _ ->
                    let elements =
                        [ value { literal (wholeNumberLiteral 1L) }
                          value { literal (wholeNumberLiteral 2L) } ]
                    let result = value { list elements }
                    let expected = Value.list () elements
                    result |> Expect.equal expected

                testCase "Creates Record value"
                <| fun _ ->
                    let fields =
                        Map.empty
                        |> Map.add (Name.fromString "name") (value { literal (stringLiteral "John") })
                        |> Map.add (Name.fromString "age") (value { literal (wholeNumberLiteral 30L) })
                    let result = value { record fields }
                    let expected = Value.record () fields
                    result |> Expect.equal expected

                testCase "Creates Record value from list"
                <| fun _ ->
                    let result =
                        value {
                            record [
                                ("name", value { literal (stringLiteral "John") })
                                ("age", value { literal (wholeNumberLiteral 30L) })
                            ]
                        }
                    let expected =
                        Map.empty
                        |> Map.add (Name.fromString "name") (value { literal (stringLiteral "John") })
                        |> Map.add (Name.fromString "age") (value { literal (wholeNumberLiteral 30L) })
                        |> Value.record ()
                    result |> Expect.equal expected

                testCase "Creates Reference value"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["maybe"]
                            local ["just"]
                        }
                    let result = value { reference fqName }
                    let expected = Value.reference () fqName
                    result |> Expect.equal expected

                testCase "Creates Apply value"
                <| fun _ ->
                    let func = value { variable "add" }
                    let arg = value { literal (wholeNumberLiteral 1L) }
                    let result = value { apply func arg }
                    let expected = Value.apply () func arg
                    result |> Expect.equal expected

                testCase "Creates Unit value"
                <| fun _ ->
                    let result = value { unit () }
                    let expected = Value.unit ()
                    result |> Expect.equal expected
            ]
        ]

