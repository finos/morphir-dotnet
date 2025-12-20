module Morphir.IR.Classic.Tests.PackageTests

open Expecto
open Morphir.IR
open Morphir.IR.Classic
open Morphir.Testing.Assertions
open System.Collections.Generic

[<Tests>]
let tests =
    testList "Package" [
        testList "PackageSpecification" [
            testCase "Creates PackageSpecification with empty modules"
            <| fun _ ->
                let spec = Package.packageSpecification Map.empty

                spec.Modules.Count |> Expect.equal 0

            testCase "Creates PackageSpecification with modules"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "myModule" ] ]
                let moduleSpec = Module.moduleSpecification Map.empty Map.empty None
                let modules = Map.empty |> Map.add modulePath moduleSpec

                let spec = Package.packageSpecification modules

                spec.Modules.Count |> Expect.equal 1
                spec.Modules.ContainsKey(modulePath) |> Expect.isTrue
        ]

        testList "PackageDefinition" [
            testCase "Creates PackageDefinition with empty modules"
            <| fun _ ->
                let def = Package.packageDefinition Map.empty

                def.Modules.Count |> Expect.equal 0

            testCase "Creates PackageDefinition with public module"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "myModule" ] ]
                let moduleDef = Module.moduleDefinition Map.empty Map.empty None
                let accessControlledModule = AccessControlled.public' moduleDef
                let modules = Map.empty |> Map.add modulePath accessControlledModule

                let def = Package.packageDefinition modules

                def.Modules.Count |> Expect.equal 1
                def.Modules.ContainsKey(modulePath) |> Expect.isTrue
                let moduleAccess = def.Modules.[modulePath]
                moduleAccess.Access |> Expect.equal AccessControlled.Public

            testCase "Creates PackageDefinition with private module"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "privateModule" ] ]
                let moduleDef = Module.moduleDefinition Map.empty Map.empty None
                let accessControlledModule = AccessControlled.private' moduleDef
                let modules = Map.empty |> Map.add modulePath accessControlledModule

                let def = Package.packageDefinition modules

                def.Modules.Count |> Expect.equal 1
                let moduleAccess = def.Modules.[modulePath]
                moduleAccess.Access |> Expect.equal AccessControlled.Private

            testCase "Creates PackageDefinition with multiple modules"
            <| fun _ ->
                let modulePath1 = ModulePath.modulePathFromList [ Name.fromList [ "module1" ] ]
                let modulePath2 = ModulePath.modulePathFromList [ Name.fromList [ "module2" ] ]
                let moduleDef = Module.moduleDefinition Map.empty Map.empty None
                let modules =
                    Map.empty
                    |> Map.add modulePath1 (AccessControlled.public' moduleDef)
                    |> Map.add modulePath2 (AccessControlled.private' moduleDef)

                let def = Package.packageDefinition modules

                def.Modules.Count |> Expect.equal 2
                def.Modules.ContainsKey(modulePath1) |> Expect.isTrue
                def.Modules.ContainsKey(modulePath2) |> Expect.isTrue
        ]
    ]

