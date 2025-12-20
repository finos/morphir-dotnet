module Morphir.IR.Tests.FQNameTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "FQName" [
        testList "fqName" [
            testCase "Creates FQName from components"
            <| fun _ ->
                let packagePath = PackageName.packageNameFromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "string" ] ]
                let localName = Name.fromList [ "to"; "upper" ]

                let fqName = FQName.fqName packagePath modulePath localName

                FQName.packagePath fqName
                |> Expect.equal packagePath

                FQName.modulePathFromFQName fqName
                |> Expect.equal modulePath

                FQName.localName fqName
                |> Expect.equal localName
        ]

        testList "fqNameFromPaths" [
            testCase "Creates FQName from Path values"
            <| fun _ ->
                let packagePath = Path.fromList [ Name.fromList [ "morphir"; "sdk" ] ]
                let modulePath = Path.fromList [ Name.fromList [ "string" ] ]
                let localName = Name.fromList [ "to"; "upper" ]

                let fqName = FQName.fqNameFromPaths packagePath modulePath localName

                FQName.packagePath fqName
                |> PackageName.packageNameToPath
                |> Expect.equal packagePath

                FQName.modulePathFromFQName fqName
                |> ModulePath.modulePathToPath
                |> Expect.equal modulePath

                FQName.localName fqName
                |> Expect.equal localName
        ]
    ]
