namespace Morphir.Models.Tests.IR.DSL

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions
open Morphir.IR.DSL.Names

module NamesTests =
    // Use the builders directly from the module to avoid name resolution issues
    // The AutoOpen attribute on Names module makes 'name', 'path', etc. available

    [<Tests>]
    let tests =
        testList "DSL Names" [
            testList "NameBuilder" [
                testCase "Creates Name from single string (without yield)"
                <| fun _ ->
                    let result = name { "firstName" }
                    let expected = Name.fromString "firstName"
                    result |> Expect.equal expected

                testCase "Creates Name from single string (with yield)"
                <| fun _ ->
                    let result = name { yield "firstName" }
                    let expected = Name.fromString "firstName"
                    result |> Expect.equal expected

                testCase "Creates Name from list of strings (without yield)"
                <| fun _ ->
                    let result = name { ["add"; "function"] }
                    let expected = Name.fromList [ "add"; "function" ]
                    result |> Expect.equal expected

                testCase "Creates Name from list of strings (with yield)"
                <| fun _ ->
                    let result = name { yield ["add"; "function"] }
                    let expected = Name.fromList [ "add"; "function" ]
                    result |> Expect.equal expected
            ]

            testList "PathBuilder" [
                testCase "Creates Path from single string"
                <| fun _ ->
                    let result = path { "morphir" }
                    let expected = Path.fromList [ Name.fromString "morphir" ]
                    Expect.equal result expected

                testCase "Creates Path from list of strings"
                <| fun _ ->
                    let result = path { ["morphir"; "sdk"] }
                    let expected =
                        Path.fromList
                            [ Name.fromString "morphir"
                              Name.fromString "sdk" ]
                    Expect.equal result expected
            ]

            testList "FQNameBuilder" [
                testCase "Creates FQName with all components"
                <| fun _ ->
                    let result =
                        fqName {
                            packagePath ["morphir"; "sdk"]
                            modPath ["basics"]
                            localName ["int"]
                        }
                    let expected =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromString "morphir"; Name.fromString "sdk" ])
                            (Path.fromList [ Name.fromString "basics" ])
                            (Name.fromString "int")
                    Expect.equal result expected

                testCase "Creates FQName with package and module only"
                <| fun _ ->
                    let result =
                        fqName {
                            packagePath ["morphir"; "sdk"]
                            modPath ["basics"]
                        }
                    let expected =
                        FQName.fqNameFromPaths
                            (Path.fromList [ Name.fromString "morphir"; Name.fromString "sdk" ])
                            (Path.fromList [ Name.fromString "basics" ])
                            (Name.fromString "")
                    Expect.equal result expected
            ]

            testList "QNameBuilder" [
                testCase "Creates QName with module and local name"
                <| fun _ ->
                    let result =
                        qName {
                            modPath ["basics"]
                            localName ["int"]
                        }
                    let expected =
                        QName.qNameFromPath
                            (Path.fromList [ Name.fromString "basics" ])
                            (Name.fromString "int")
                    Expect.equal result expected
            ]

            testList "PackageNameBuilder" [
                testCase "Creates PackageName from list of strings"
                <| fun _ ->
                    let result = packageName { ["morphir"; "sdk"] }
                    let expected =
                        Path.fromList
                            [ Name.fromString "morphir"
                              Name.fromString "sdk" ]
                        |> PackageName.packageName
                    Expect.equal result expected
            ]

            testList "ModulePathBuilder" [
                testCase "Creates ModulePath from list of strings"
                <| fun _ ->
                    let result = modulePath { ["basics"] }
                    let expected =
                        Path.fromList [ Name.fromString "basics" ]
                        |> ModulePath.modulePath
                    Expect.equal result expected
            ]
        ]

