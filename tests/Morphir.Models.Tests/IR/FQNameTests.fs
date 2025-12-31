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

        testList "fromString" [
            testCase "Parses well-formed FQName string with colon separator"
            <| fun _ ->
                let fqName = FQName.fromString "Morphir.SDK:Basics:int" ":"

                FQName.packagePath fqName
                |> PackageName.packageNameToPath
                |> Path.toString Name.toTitleCase "."
                |> Expect.equal "Morphir.SDK"

                FQName.modulePathFromFQName fqName
                |> ModulePath.modulePathToPath
                |> Path.toString Name.toTitleCase "."
                |> Expect.equal "Basics"

                FQName.localName fqName
                |> Name.toTitleCase
                |> Expect.equal "Int"

            testCase "Parses well-formed FQName string with dot separator"
            <| fun _ ->
                let fqName = FQName.fromString "MyPackage.MyModule.myValue" "."

                FQName.packagePath fqName
                |> PackageName.packageNameToPath
                |> Path.toString Name.toTitleCase "."
                |> Expect.equal "MyPackage"

                FQName.modulePathFromFQName fqName
                |> ModulePath.modulePathToPath
                |> Path.toString Name.toTitleCase "."
                |> Expect.equal "MyModule"

                FQName.localName fqName
                |> Name.toTitleCase
                |> Expect.equal "MyValue"

            testCase "Returns empty FQName for malformed string with too few parts"
            <| fun _ ->
                let fqName = FQName.fromString "Package:Module" ":"

                FQName.packagePath fqName
                |> Expect.equal PackageName.emptyPackageName

                FQName.modulePathFromFQName fqName
                |> Expect.equal ModulePath.emptyModulePath

                FQName.localName fqName
                |> Expect.equal Name.empty

            testCase "Returns empty FQName for malformed string with too many parts"
            <| fun _ ->
                let fqName = FQName.fromString "Package:Module:Local:Extra" ":"

                FQName.packagePath fqName
                |> Expect.equal PackageName.emptyPackageName

                FQName.modulePathFromFQName fqName
                |> Expect.equal ModulePath.emptyModulePath

                FQName.localName fqName
                |> Expect.equal Name.empty

            testCase "Returns empty FQName for empty string"
            <| fun _ ->
                let fqName = FQName.fromString "" ":"

                FQName.packagePath fqName
                |> Expect.equal PackageName.emptyPackageName

                FQName.modulePathFromFQName fqName
                |> Expect.equal ModulePath.emptyModulePath

                FQName.localName fqName
                |> Expect.equal Name.empty

            testCase "Handles empty package path in string"
            <| fun _ ->
                let fqName = FQName.fromString ":MyModule:myValue" ":"

                FQName.packagePath fqName
                |> PackageName.packageNameToPath
                |> Path.isEmpty
                |> Expect.isTrue

                FQName.modulePathFromFQName fqName
                |> ModulePath.modulePathToPath
                |> Path.toString Name.toTitleCase "."
                |> Expect.equal "MyModule"

                FQName.localName fqName
                |> Name.toTitleCase
                |> Expect.equal "MyValue"
        ]

        testList "fromStringStrict" [
            testCase "Successfully parses well-formed FQName string"
            <| fun _ ->
                match FQName.fromStringStrict "Morphir.SDK:Basics:int" ":" with
                | Ok fqName ->
                    FQName.packagePath fqName
                    |> PackageName.packageNameToPath
                    |> Path.toString Name.toTitleCase "."
                    |> Expect.equal "Morphir.SDK"

                    FQName.modulePathFromFQName fqName
                    |> ModulePath.modulePathToPath
                    |> Path.toString Name.toTitleCase "."
                    |> Expect.equal "Basics"

                    FQName.localName fqName
                    |> Name.toTitleCase
                    |> Expect.equal "Int"
                | Error msg ->
                    failtest $"Expected Ok, got Error: {msg}"

            testCase "Fails with descriptive error for too few parts"
            <| fun _ ->
                match FQName.fromStringStrict "Package:Module" ":" with
                | Ok _ ->
                    failtest "Expected Error, got Ok"
                | Error msg ->
                    Expect.stringContains msg "needs to have 3 parts" "Error message should mention 3 parts"
                    Expect.stringContains msg "found 2 parts" "Error message should mention found 2 parts"
                    Expect.stringContains msg "Package:Module" "Error message should include input string"
                    Expect.stringContains msg ":" "Error message should include separator"

            testCase "Fails with descriptive error for too many parts"
            <| fun _ ->
                match FQName.fromStringStrict "Package:Module:Local:Extra" ":" with
                | Ok _ ->
                    failtest "Expected Error, got Ok"
                | Error msg ->
                    Expect.stringContains msg "needs to have 3 parts" "Error message should mention 3 parts"
                    Expect.stringContains msg "found 4 parts" "Error message should mention found 4 parts"

            testCase "Fails with descriptive error for empty string"
            <| fun _ ->
                match FQName.fromStringStrict "" ":" with
                | Ok _ ->
                    failtest "Expected Error, got Ok"
                | Error msg ->
                    Expect.stringContains msg "needs to have 3 parts" "Error message should mention 3 parts"
                    Expect.stringContains msg "found 1 parts" "Error message should mention found 1 parts"

            testCase "Successfully parses with different separator"
            <| fun _ ->
                match FQName.fromStringStrict "MyPackage.MyModule.myValue" "." with
                | Ok fqName ->
                    FQName.packagePath fqName
                    |> PackageName.packageNameToPath
                    |> Path.toString Name.toTitleCase "."
                    |> Expect.equal "MyPackage"

                    FQName.localName fqName
                    |> Name.toTitleCase
                    |> Expect.equal "MyValue"
                | Error msg ->
                    failtest $"Expected Ok, got Error: {msg}"

            testCase "Handles empty package path in strict mode"
            <| fun _ ->
                match FQName.fromStringStrict ":MyModule:myValue" ":" with
                | Ok fqName ->
                    FQName.packagePath fqName
                    |> PackageName.packageNameToPath
                    |> Path.isEmpty
                    |> Expect.isTrue

                    FQName.modulePathFromFQName fqName
                    |> ModulePath.modulePathToPath
                    |> Path.toString Name.toTitleCase "."
                    |> Expect.equal "MyModule"
                | Error msg ->
                    failtest $"Expected Ok, got Error: {msg}"
        ]
    ]
