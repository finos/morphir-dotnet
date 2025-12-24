namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR.DSL.Names

module NamesTests =
    // Use aliases that match actual DSL usage (with apostrophe to avoid conflicts)
    // Import the builders with apostrophe suffix to match actual usage pattern
    // Use fully qualified names to avoid any potential conflicts
    let name' = Morphir.IR.DSL.Names.name
    let path' = Morphir.IR.DSL.Names.path
    let packageName' = Morphir.IR.DSL.Names.packageName
    let modulePath' = Morphir.IR.DSL.Names.modulePath
    let fqName' = Morphir.IR.DSL.Names.fqName
    let qName' = Morphir.IR.DSL.Names.qName

    [<Tests>]
    let tests =
        testList "DSL Names" [
            testList "NameBuilder" [
                testCase "Creates Name from single string (without yield)"
                <| fun _ ->
                    let result = name' { "firstName" }
                    let expected = Morphir.IR.Name.fromString "firstName"
                    result |> Expect.equal expected

                testCase "Creates Name from single string (with yield)"
                <| fun _ ->
                    let result = name' { yield "firstName" }
                    let expected = Morphir.IR.Name.fromString "firstName"
                    result |> Expect.equal expected

                testCase "Creates Name from list of strings (without yield)"
                <| fun _ ->
                    let result = name' { ["add"; "function"] }
                    let expected = Morphir.IR.Name.fromList [ "add"; "function" ]
                    result |> Expect.equal expected

                testCase "Creates Name from list of strings (with yield)"
                <| fun _ ->
                    let result = name' { yield ["add"; "function"] }
                    let expected = Morphir.IR.Name.fromList [ "add"; "function" ]
                    result |> Expect.equal expected
            ]

            testList "PathBuilder" [
                testCase "Creates Path from single string"
                <| fun _ ->
                    let result = path' { "morphir" }
                    let expected = Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "morphir" ]
                    result |> Expect.equal expected

                testCase "Creates Path from list of strings"
                <| fun _ ->
                    let result = path' { ["morphir"; "sdk"] }
                    let expected =
                        Morphir.IR.Path.fromList
                            [ Morphir.IR.Name.fromString "morphir"
                              Morphir.IR.Name.fromString "sdk" ]
                    result |> Expect.equal expected
            ]

            testList "FQNameBuilder" [
                testCase "Creates FQName with all components"
                <| fun _ ->
                    let result =
                        fqName' {
                            packagePath ["morphir"; "sdk"]
                            module' ["basics"]
                            localName ["int"]
                        }
                    let expected =
                        Morphir.IR.FQName.fqNameFromPaths
                            (Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "morphir"; Morphir.IR.Name.fromString "sdk" ])
                            (Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "basics" ])
                            (Morphir.IR.Name.fromString "int")
                    result |> Expect.equal expected

                testCase "Creates FQName with package and module only"
                <| fun _ ->
                    let result =
                        fqName' {
                            packagePath ["morphir"; "sdk"]
                            module' ["basics"]
                        }
                    let expected =
                        Morphir.IR.FQName.fqNameFromPaths
                            (Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "morphir"; Morphir.IR.Name.fromString "sdk" ])
                            (Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "basics" ])
                            (Morphir.IR.Name.fromString "")
                    result |> Expect.equal expected
            ]

            testList "QNameBuilder" [
                testCase "Creates QName with module and local name"
                <| fun _ ->
                    let result =
                        qName' {
                            module' ["basics"]
                            localName ["int"]
                        }
                    let expected =
                        Morphir.IR.QName.qNameFromPath
                            (Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "basics" ])
                            (Morphir.IR.Name.fromString "int")
                    result |> Expect.equal expected
            ]

            testList "PackageNameBuilder" [
                testCase "Creates PackageName from list of strings"
                <| fun _ ->
                    let result = packageName' { ["morphir"; "sdk"] }
                    let expected =
                        Morphir.IR.Path.fromList
                            [ Morphir.IR.Name.fromString "morphir"
                              Morphir.IR.Name.fromString "sdk" ]
                        |> Morphir.IR.PackageName.packageName
                    result |> Expect.equal expected
            ]

            testList "ModulePathBuilder" [
                testCase "Creates ModulePath from list of strings"
                <| fun _ ->
                    let result = modulePath' { ["basics"] }
                    let expected =
                        Morphir.IR.Path.fromList [ Morphir.IR.Name.fromString "basics" ]
                        |> Morphir.IR.ModulePath.modulePath
                    result |> Expect.equal expected
            ]
        ]

