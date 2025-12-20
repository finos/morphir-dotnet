module Morphir.IR.Classic.Tests.ModuleTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions
open System.Collections.Generic

[<Tests>]
let tests =
    testList "Module" [
        testList "ModuleSpecification" [
            testCase "Creates ModuleSpecification with empty maps"
            <| fun _ ->
                let spec = Module.moduleSpecification Map.empty Map.empty None

                spec.Types.Count |> Expect.equal 0
                spec.Values.Count |> Expect.equal 0
                spec.Doc |> Expect.equal None

            testCase "Creates ModuleSpecification with types and values"
            <| fun _ ->
                let typeName = Name.fromList [ "myType" ]
                let typeSpec = Type.typeAliasSpecification [] (Type.unit ())
                let documentedType = Documented.withoutDocumentation typeSpec
                let types = Map.empty |> Map.add typeName documentedType

                let valueName = Name.fromList [ "myValue" ]
                let valueSpec = Value.valueSpecification [] (Type.unit ())
                let documentedValue = Documented.withoutDocumentation valueSpec
                let values = Map.empty |> Map.add valueName documentedValue

                let spec = Module.moduleSpecification types values (Some "Module documentation")

                spec.Types.Count |> Expect.equal 1
                spec.Values.Count |> Expect.equal 1
                spec.Doc |> Expect.equal (Some "Module documentation")
        ]

        testList "ModuleDefinition" [
            testCase "Creates ModuleDefinition with empty maps"
            <| fun _ ->
                let def = Module.moduleDefinition Map.empty Map.empty None

                def.Types.Count |> Expect.equal 0
                def.Values.Count |> Expect.equal 0
                def.Doc |> Expect.equal None

            testCase "Creates ModuleDefinition with types and values"
            <| fun _ ->
                let typeName = Name.fromList [ "myType" ]
                let typeDef = Type.typeAliasDefinition [] (Type.unit ())
                let documentedType = Documented.withoutDocumentation typeDef
                let accessControlledType = AccessControlled.public' documentedType
                let types = Map.empty |> Map.add typeName accessControlledType

                let valueName = Name.fromList [ "myValue" ]
                let valueDef = Value.valueDefinition [] (Type.unit ()) (Value.unit ())
                let documentedValue = Documented.withoutDocumentation valueDef
                let accessControlledValue = AccessControlled.public' documentedValue
                let values = Map.empty |> Map.add valueName accessControlledValue

                let def = Module.moduleDefinition types values (Some "Module documentation")

                def.Types.Count |> Expect.equal 1
                def.Values.Count |> Expect.equal 1
                def.Doc |> Expect.equal (Some "Module documentation")

            testCase "Creates ModuleDefinition with private types and values"
            <| fun _ ->
                let typeName = Name.fromList [ "privateType" ]
                let typeDef = Type.typeAliasDefinition [] (Type.unit ())
                let documentedType = Documented.withoutDocumentation typeDef
                let accessControlledType = AccessControlled.private' documentedType
                let types = Map.empty |> Map.add typeName accessControlledType

                let valueName = Name.fromList [ "privateValue" ]
                let valueDef = Value.valueDefinition [] (Type.unit ()) (Value.unit ())
                let documentedValue = Documented.withoutDocumentation valueDef
                let accessControlledValue = AccessControlled.private' documentedValue
                let values = Map.empty |> Map.add valueName accessControlledValue

                let def = Module.moduleDefinition types values None

                def.Types.Count |> Expect.equal 1
                def.Values.Count |> Expect.equal 1
                let typeAccess = def.Types.[typeName]
                typeAccess.Access |> Expect.equal AccessControlled.Private
                let valueAccess = def.Values.[valueName]
                valueAccess.Access |> Expect.equal AccessControlled.Private
        ]
    ]

