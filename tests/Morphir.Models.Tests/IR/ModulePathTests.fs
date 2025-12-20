module Morphir.IR.Tests.ModulePathTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "ModulePath" [
        testList "modulePath" [
            testCase "Creates ModulePath from Path"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "string" ] ]
                let modulePath = ModulePath.modulePath path

                ModulePath.modulePathToPath modulePath
                |> Expect.equal path

            testCase "Creates ModulePath from list"
            <| fun _ ->
                let names = [ Name.fromList [ "string" ] ]
                let modulePath = ModulePath.modulePathFromList names

                ModulePath.modulePathToPath modulePath
                |> Path.toList
                |> Expect.equal names

            testCase "Creates ModulePath from string"
            <| fun _ ->
                ModulePath.modulePathFromString "string.utils"
                |> ModulePath.modulePathToPath
                |> Path.toCanonicalString
                |> Expect.equal "string/utils"
        ]
    ]

