module Morphir.IR.Tests.PackageNameTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "PackageName" [
        testList "packageName" [
            testCase "Creates PackageName from Path"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let packageName = PackageName.packageName path

                PackageName.packageNameToPath packageName
                |> Expect.equal path

            testCase "Creates PackageName from list"
            <| fun _ ->
                let names = [ Name.fromList [ "morphir" ]; Name.fromList [ "sdk" ] ]
                let packageName = PackageName.packageNameFromList names

                PackageName.packageNameToPath packageName
                |> Path.toList
                |> Expect.equal names

            testCase "Creates PackageName from string"
            <| fun _ ->
                PackageName.packageNameFromString "morphir.sdk"
                |> PackageName.packageNameToPath
                |> Path.toCanonicalString
                |> Expect.equal "morphir/sdk"
        ]
    ]

