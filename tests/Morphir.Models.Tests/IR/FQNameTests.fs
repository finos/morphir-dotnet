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
                let packagePath = PackageName.packageNameFromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]
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
                let packagePath = Path.fromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]
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

        testList "toString" [
            testCase "Formats FQName with all components"
            <| fun _ ->
                // SDK should be parsed as ["s", "d", "k"] to match morphir-elm behavior
                let packagePath = PackageName.packageNameFromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "basics" ] ]
                let localName = Name.fromList [ "int" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toString fqName
                |> Expect.equal "Morphir.SDK.Basics.Int"

            testCase "Formats FQName with empty package path"
            <| fun _ ->
                let packagePath = PackageName.emptyPackageName
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "my" ]; Name.fromList [ "module" ] ]
                let localName = Name.fromList [ "value" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toString fqName
                |> Expect.equal "My.Module.Value"
        ]

        testList "toHumanString" [
            testCase "Formats FQName omitting empty package path"
            <| fun _ ->
                let packagePath = PackageName.emptyPackageName
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "my" ]; Name.fromList [ "module" ] ]
                let localName = Name.fromList [ "value" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toHumanString fqName
                |> Expect.equal "My.Module.Value"

            testCase "Formats FQName with package path as Module.Name"
            <| fun _ ->
                let packagePath = PackageName.packageNameFromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "basics" ] ]
                let localName = Name.fromList [ "int" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toHumanString fqName
                |> Expect.equal "Basics.Int"
        ]

        testList "toDebugString" [
            testCase "Formats FQName with all components in debug format"
            <| fun _ ->
                let packagePath = PackageName.packageNameFromList [ Name.fromList [ "morphir" ]; Name.fromList [ "s"; "d"; "k" ] ]
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "basics" ] ]
                let localName = Name.fromList [ "int" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toDebugString fqName
                |> Expect.equal "FQName(Morphir.SDK, Basics, Int)"

            testCase "Formats FQName with empty package path in debug format"
            <| fun _ ->
                let packagePath = PackageName.emptyPackageName
                let modulePath = ModulePath.modulePathFromList [ Name.fromList [ "my" ]; Name.fromList [ "module" ] ]
                let localName = Name.fromList [ "value" ]
                let fqName = FQName.fqName packagePath modulePath localName

                FQName.toDebugString fqName
                |> Expect.equal "FQName(, My.Module, Value)"
        ]
    ]
