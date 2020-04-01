module Morphir.IR.Tests.PathTests

open Morphir.SDK.Testing
open Morphir.SDK
open Morphir.IR

[<Tests>]
let tests =
    let fromStringTests =
        describe "fromString" [
            testCase "Works on Camel Humps and '.'"
                <| fun _ -> Path.fromString "fooBar.Baz"
                            |> Expect.equal
                                (Path.fromList [Name.fromList["foo"; "bar"]; Name.fromList["baz"]])

            testCase "Works on space and '/'"
                <| fun _ -> Path.fromString "foo bar/Baz"
                            |> Expect.equal
                                (Path.fromList [Name.fromList["foo"; "bar"]; Name.fromList["baz"]])
        ]

    let toStringTests =
        describe "toString" [
            let path =
                Path.fromList [
                    Name.fromList ["foo"; "bar"]
                    Name.fromList ["baz"]
                ]

            testCase "Using TitleCase"
                <| fun _ -> Path.toString Name.toTitleCase "." path
                            |> Expect.equal "FooBar.Baz"

            testCase "Using SnakeCase"
                <| fun _ -> Path.toString Name.toSnakeCase "/" path
                            |> Expect.equal "foo_bar/baz"
        ]

    let isPrefixOfTests =
        describe "isPrefixOf" [
            let toModuleName = Path.toString Name.toTitleCase "."
            let isPrefixOf prefix path expectedResult =
                testCase ("isPrefixOf " + toModuleName prefix + " " + toModuleName path + " == " + Bool.toString expectedResult)
                    <| fun _ -> Path.isPrefixOf prefix path |> Expect.equal expectedResult

            isPrefixOf [ [ "foo" ]; [ "bar" ] ] [ [ "foo" ] ] true
            isPrefixOf [ [ "foo" ] ] [ [ "foo" ]; [ "bar" ] ] false
            isPrefixOf [ [ "foo" ]; [ "bar" ] ] [ [ "foo" ]; [ "bar" ] ] true
        ]

    describe "PathTests" [
        fromStringTests
        toStringTests
        isPrefixOfTests
    ]
