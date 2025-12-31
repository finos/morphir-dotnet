namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.IR.Classic.Module
open Morphir.IR.Classic.AccessControlled
open Morphir.IR.Classic.DSL.Modules

module ModulesTests =

    [<Tests>]
    let tests =
        testList "DSL Modules" [
            testList "Basic Module Definition Creation" [
                testCase "Creates empty module definition" <| fun _ ->
                    let result = mod' {
                        ()
                    }

                    Expect.equal result.Types Map.empty "Empty module should have no types"
                    Expect.equal result.Values Map.empty "Empty module should have no values"
                    Expect.equal result.Doc None "Empty module should have no documentation"

                testCase "Adds public type definition using typedef" <| fun _ ->
                    let typeDef = Type.typeAliasDefinition [] (Type.unit ())
                    let result = mod' {
                        typedef "MyType" typeDef
                    }

                    Expect.equal (Map.count result.Types) 1 "Should have one type"
                    let typeName = Name.fromString "MyType"
                    Expect.isTrue (Map.containsKey typeName result.Types) "Should contain MyType"

                    let accessControlled = Map.find typeName result.Types
                    Expect.equal accessControlled.Access Public "Type should be public by default"

                testCase "Adds public value definition using valuedef" <| fun _ ->
                    let valueDef = {
                        InputTypes = []
                        OutputType = Type.unit ()
                        Body = Value.Unit ()
                    }
                    let result = mod' {
                        valuedef "myValue" valueDef
                    }

                    Expect.equal (Map.count result.Values) 1 "Should have one value"
                    let valueName = Name.fromString "myValue"
                    Expect.isTrue (Map.containsKey valueName result.Values) "Should contain myValue"

                    let accessControlled = Map.find valueName result.Values
                    Expect.equal accessControlled.Access Public "Value should be public by default"

                testCase "Sets module documentation using doc" <| fun _ ->
                    let result = mod' {
                        doc "This is my module"
                    }

                    Expect.equal result.Doc (Some "This is my module") "Should set module documentation"
            ]

            testList "Access Control" [
                testCase "Adds private type using privateType" <| fun _ ->
                    let typeDef = Type.typeAliasDefinition [] (Type.unit ())
                    let result = mod' {
                        privateType "InternalType" typeDef
                    }

                    let typeName = Name.fromString "InternalType"
                    let accessControlled = Map.find typeName result.Types
                    Expect.equal accessControlled.Access Private "Type should be private"

                testCase "Adds public type using publicType" <| fun _ ->
                    let typeDef = Type.typeAliasDefinition [] (Type.unit ())
                    let result = mod' {
                        publicType "PublicType" typeDef
                    }

                    let typeName = Name.fromString "PublicType"
                    let accessControlled = Map.find typeName result.Types
                    Expect.equal accessControlled.Access Public "Type should be public"

                testCase "Adds private value using privateValue" <| fun _ ->
                    let valueDef = {
                        InputTypes = []
                        OutputType = Type.unit ()
                        Body = Value.Unit ()
                    }
                    let result = mod' {
                        privateValue "internalHelper" valueDef
                    }

                    let valueName = Name.fromString "internalHelper"
                    let accessControlled = Map.find valueName result.Values
                    Expect.equal accessControlled.Access Private "Value should be private"

                testCase "Adds public value using publicValue" <| fun _ ->
                    let valueDef = {
                        InputTypes = []
                        OutputType = Type.unit ()
                        Body = Value.Unit ()
                    }
                    let result = mod' {
                        publicValue "publicAPI" valueDef
                    }

                    let valueName = Name.fromString "publicAPI"
                    let accessControlled = Map.find valueName result.Values
                    Expect.equal accessControlled.Access Public "Value should be public"
            ]
        ]
