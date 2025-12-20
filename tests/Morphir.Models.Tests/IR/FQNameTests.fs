module Morphir.IR.Tests.FQNameTests

open Expecto
open Morphir.IR
open Morphir.Models.Tests.TestHelpers

[<Tests>]
let tests =
    testList "FQName" [
        testList "fqName" [
            testCase "Creates FQName from components"
            <| fun _ ->
                let packagePath = FQName.packageNameFromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let modulePath = FQName.modulePathFromList [ Name.fromList [ "string" ] ]
                let localName = Name.fromList [ "to"; "upper" ]

                let fqName = FQName.fqName packagePath modulePath localName

                FQName.packagePath fqName
                |> Expect.equal packagePath

                FQName.modulePathFromFQName fqName
                |> Expect.equal modulePath

                FQName.localName fqName
                |> Expect.equal localName
        ]

        testList "packageName" [
            testCase "Creates PackageName from Path"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let packageName = FQName.packageName path

                FQName.packageNameToPath packageName
                |> Expect.equal path

            testCase "Creates PackageName from list"
            <| fun _ ->
                let names = [ Name.fromList [ "morphir" ]; Name.fromList [ "sdk" ] ]
                let packageName = FQName.packageNameFromList names

                FQName.packageNameToPath packageName
                |> Path.toList
                |> Expect.equal names

            testCase "Creates PackageName from string"
            <| fun _ ->
                FQName.packageNameFromString "morphir.sdk"
                |> FQName.packageNameToPath
                |> Path.toCanonicalString
                |> Expect.equal "morphir/sdk"
        ]

        testList "modulePath" [
            testCase "Creates ModulePath from Path"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "string" ] ]
                let modulePath = FQName.modulePath path

                FQName.modulePathToPath modulePath
                |> Expect.equal path

            testCase "Creates ModulePath from list"
            <| fun _ ->
                let names = [ Name.fromList [ "string" ] ]
                let modulePath = FQName.modulePathFromList names

                FQName.modulePathToPath modulePath
                |> Path.toList
                |> Expect.equal names

            testCase "Creates ModulePath from string"
            <| fun _ ->
                FQName.modulePathFromString "string.utils"
                |> FQName.modulePathToPath
                |> Path.toCanonicalString
                |> Expect.equal "string/utils"
        ]

        testList "fqNameFromPaths" [
            testCase "Creates FQName from Path values"
            <| fun _ ->
                let packagePath = Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let modulePath = Path.fromList [ Name.fromList [ "string" ] ]
                let localName = Name.fromList [ "to"; "upper" ]

                let fqName = FQName.fqNameFromPaths packagePath modulePath localName

                FQName.packagePath fqName
                |> FQName.packageNameToPath
                |> Expect.equal packagePath

                FQName.modulePathFromFQName fqName
                |> FQName.modulePathToPath
                |> Expect.equal modulePath

                FQName.localName fqName
                |> Expect.equal localName
        ]
    ]
