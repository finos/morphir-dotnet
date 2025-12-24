module Morphir.IR.Tests.QNameTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "QName" [
        testList "qName" [
            testCase "Creates QName from components"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Morphir.IR.Name.fromList [ "my" ]; Morphir.IR.Name.fromList [ "module" ] ]
                let localName = Morphir.IR.Name.fromList [ "value" ]

                let qName = QName.qName modulePath localName

                QName.modulePath qName
                |> Expect.equal modulePath

                QName.localName qName
                |> Expect.equal localName
        ]

        testList "qNameFromPath" [
            testCase "Creates QName from Path and Name"
            <| fun _ ->
                let modulePath = Path.fromList [ Morphir.IR.Name.fromList [ "my" ]; Morphir.IR.Name.fromList [ "module" ] ]
                let localName = Morphir.IR.Name.fromList [ "value" ]

                let qName = QName.qNameFromPath modulePath localName

                QName.modulePath qName
                |> ModulePath.modulePathToPath
                |> Expect.equal modulePath

                QName.localName qName
                |> Expect.equal localName
        ]

        testList "toString" [
            testCase "Formats QName as ModulePath:LocalName"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Morphir.IR.Name.fromList [ "my" ]; Morphir.IR.Name.fromList [ "module" ] ]
                let localName = Morphir.IR.Name.fromList [ "value" ]
                let qName = QName.qName modulePath localName

                QName.toString qName
                |> Expect.equal "My.Module:value"

            testCase "Formats QName with multi-word local name"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Morphir.IR.Name.fromList [ "basics" ] ]
                let localName = Morphir.IR.Name.fromList [ "add"; "two" ]
                let qName = QName.qName modulePath localName

                QName.toString qName
                |> Expect.equal "Basics:addTwo"
        ]

        testList "toHumanString" [
            testCase "Formats QName same as toString (no package to omit)"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Morphir.IR.Name.fromList [ "my" ]; Morphir.IR.Name.fromList [ "module" ] ]
                let localName = Morphir.IR.Name.fromList [ "value" ]
                let qName = QName.qName modulePath localName

                QName.toHumanString qName
                |> Expect.equal "My.Module:value"
        ]

        testList "toDebugString" [
            testCase "Formats QName with explicit components"
            <| fun _ ->
                let modulePath = ModulePath.modulePathFromList [ Morphir.IR.Name.fromList [ "my" ]; Morphir.IR.Name.fromList [ "module" ] ]
                let localName = Morphir.IR.Name.fromList [ "value" ]
                let qName = QName.qName modulePath localName

                QName.toDebugString qName
                |> Expect.equal "QName(My.Module, value)"
        ]
    ]

