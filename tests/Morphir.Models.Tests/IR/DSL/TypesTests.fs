namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic.DSL.Types
open Morphir.IR.DSL.Names
open Morphir.IR.Classic.Type

module TypesTests =

    [<Tests>]
    let tests =
        testList "DSL Types" [
            testList "TypeBuilder" [
                testCase "Creates Reference type"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["basics"]
                            local ["int"]
                        }
                    let result = type' { reference fqName }
                    let expected = Type.reference () fqName []
                    result |> Expect.equal expected

                testCase "Creates Reference type with type arguments"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["list"]
                            local ["list"]
                        }
                    let intType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["basics"]
                                    local ["int"]
                                })
                        }
                    let result = type' { reference fqName [ intType ] }
                    let expected =
                        Type.reference () fqName [ intType ]
                    result |> Expect.equal expected

                testCase "Creates Tuple type"
                <| fun _ ->
                    let intType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["basics"]
                                    local ["int"]
                                })
                        }
                    let stringType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["string"]
                                    local ["string"]
                                })
                        }
                    let result = type' { tuple [ intType; stringType ] }
                    let expected = Type.tuple () [ intType; stringType ]
                    result |> Expect.equal expected

                testCase "Creates Record type"
                <| fun _ ->
                    let intType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["basics"]
                                    local ["int"]
                                })
                        }
                    let stringType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["string"]
                                    local ["string"]
                                })
                        }
                    let result =
                        type' {
                            record [
                                field "firstName" stringType
                                field "age" intType
                            ]
                        }
                    let expected =
                        Type.record ()
                            [ Type.field (Name.fromString "firstName") stringType
                              Type.field (Name.fromString "age") intType ]
                    result |> Expect.equal expected

                testCase "Creates Function type"
                <| fun _ ->
                    let intType =
                        type' {
                            reference (
                                fqName {
                                    package ["morphir"; "sdk"]
                                    module' ["basics"]
                                    local ["int"]
                                })
                        }
                    let result = type' { FunctionType intType intType }
                    let expected = Type.functionType () intType intType
                    result |> Expect.equal expected

                testCase "Creates Unit type"
                <| fun _ ->
                    let result = type' { unit () }
                    let expected = Type.unit ()
                    result |> Expect.equal expected

                testCase "Creates Variable type"
                <| fun _ ->
                    let result = type' { variable "a" }
                    let expected = Type.variable () (Name.fromString "a")
                    result |> Expect.equal expected

                testCase "irType alias works"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["basics"]
                            local ["int"]
                        }
                    let result = irType { reference fqName }
                    let expected = Type.reference () fqName []
                    result |> Expect.equal expected
            ]
        ]

