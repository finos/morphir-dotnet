namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic.DSL
open Morphir.IR.Classic.DSL.Types
open Morphir.IR.DSL.Names
open Morphir.IR.Classic
open Morphir.Testing.Assertions

module TypesTests =

    // The ^-> operator is now available from AutoOpen TypeExtensions module

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
                    let result = type'.reference(fqName)
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
                    let intType = type'.reference (fqnSimple ["morphir"; "sdk"] ["basics"] "int")
                    let result = type'.reference(fqName, [ intType ])
                    let expected =
                        Type.reference () fqName [ intType ]
                    result |> Expect.equal expected

                testCase "Creates Tuple type"
                <| fun _ ->
                    let intType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["basics"]
                                local ["int"]
                            })
                    let stringType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["string"]
                                local ["string"]
                            })
                    let result = type' { tuple [ intType; stringType ] }
                    let expected = Type.tuple () [ intType; stringType ]
                    result |> Expect.equal expected

                testCase "Creates Record type"
                <| fun _ ->
                    let intType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["basics"]
                                local ["int"]
                            })
                    let stringType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["string"]
                                local ["string"]
                            })
                    let result =
                        type' {
                            record [
                                type'.field("firstName", stringType)
                                type'.field("age", intType)
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
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["basics"]
                                local ["int"]
                            })
                    let result = type'.functionType(intType, intType)
                    let expected = Type.functionType () intType intType
                    result |> Expect.equal expected

                testCase "Creates Unit type"
                <| fun _ ->
                    let result = type' { () }
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

                testCase "Tagless unit syntax: type' { () }"
                <| fun _ ->
                    let result = type' { () }
                    let expected = Type.unit ()
                    result |> Expect.equal expected

                testCase "Func method with list syntax"
                <| fun _ ->
                    let intType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["basics"]
                                local ["int"]
                            })
                    let charType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["basics"]
                                local ["char"]
                            })
                    let stringType =
                        type'.reference (
                            fqName {
                                package ["morphir"; "sdk"]
                                module' ["string"]
                                local ["string"]
                            })

                    // func [Int; Char] String should create Int -> Char -> String
                    let result = type' { func [ intType; charType ] stringType }

                    // Verify it's a function type with correct structure
                    match result with
                    | Type.Function(_, arg1, innerFunc) ->
                        arg1 |> Expect.equal intType
                        match innerFunc with
                        | Type.Function(_, arg2, ret) ->
                            arg2 |> Expect.equal charType
                            ret |> Expect.equal stringType
                        | _ -> failwith "Expected nested Function type"
                    | _ -> failwith "Expected Function type"

                testCase "Lowercase CustomOperation methods work"
                <| fun _ ->
                    let result = type' { variable "a" }
                    let expected = Type.variable () (Name.fromString "a")
                    result |> Expect.equal expected
            ]

            testList "^-> Operator" [
                testCase "Creates function type with ^-> operator"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let stringType = Type.variable () (Name.fromString "String")
                    let funcType = intType ^-> stringType

                    match funcType with
                    | Type.Function(_, arg, ret) ->
                        arg |> Expect.equal intType
                        ret |> Expect.equal stringType
                    | _ -> failwith "Expected Function type"

                testCase "^-> operator is right-associative"
                <| fun _ ->
                    let intType = Type.variable () (Name.fromString "Int")
                    let charType = Type.variable () (Name.fromString "Char")
                    let stringType = Type.variable () (Name.fromString "String")

                    // intType ^-> charType ^-> stringType creates Int -> (Char -> String)
                    let chained = intType ^-> charType ^-> stringType
                    let rightAssoc = intType ^-> (charType ^-> stringType)

                    chained |> Expect.equal rightAssoc
            ]

            testList "Helpers Module" [
                testCase "typeVar helper creates Variable type"
                <| fun _ ->
                    let result = typeVar "a"
                    let expected = Type.Variable((), Name.fromString "a")
                    result |> Expect.equal expected

                testCase "typeRef helper creates Reference type"
                <| fun _ ->
                    let fqName =
                        fqName {
                            package ["morphir"; "sdk"]
                            module' ["basics"]
                            local ["int"]
                        }
                    let result = typeRef fqName
                    let expected = Type.Reference((), fqName, [])
                    result |> Expect.equal expected

                testCase "typeFuncList creates curried function"
                <| fun _ ->
                    let intType = typeVar "Int"
                    let charType = typeVar "Char"
                    let stringType = typeVar "String"

                    // typeFuncList [Int; Char] String = Int -> Char -> String
                    let result = typeFuncList [ intType; charType ] stringType

                    match result with
                    | Type.Function(_, arg1, innerFunc) ->
                        arg1 |> Expect.equal intType
                        match innerFunc with
                        | Type.Function(_, arg2, ret) ->
                            arg2 |> Expect.equal charType
                            ret |> Expect.equal stringType
                        | _ -> failwith "Expected nested Function type"
                    | _ -> failwith "Expected Function type"

                testCase "typeField creates field"
                <| fun _ ->
                    let stringType = typeVar "String"
                    let result = typeField "name" stringType
                    let expected = Type.field (Name.fromString "name") stringType
                    result |> Expect.equal expected

                testCase "fqnSimple creates FQName with simple local name"
                <| fun _ ->
                    let result = fqnSimple ["morphir"; "sdk"] ["basics"] "int"
                    result.LocalName |> Expect.equal (Name.fromString "int")
            ]
        ]

