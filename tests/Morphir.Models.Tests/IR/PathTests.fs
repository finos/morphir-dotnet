module Morphir.IR.Tests.PathTests

open Expecto
open Morphir.IR
open Morphir.Models.Tests.TestHelpers

[<Tests>]
let tests =
    testList "Path" [
        testList "fromString" [
            testCase "Works on Camel Humps and '.'"
            <| fun _ ->
                Path.fromString "fooBar.Baz"
                |> Expect.equal (Path.fromList [ Name.fromList [ "foo"; "bar" ]; Name.fromList [ "baz" ] ])

            testCase "Works on space and '/'"
            <| fun _ ->
                Path.fromString "foo bar/Baz"
                |> Expect.equal (Path.fromList [ Name.fromList [ "foo"; "bar" ]; Name.fromList [ "baz" ] ])
        ]

        testList "toString" [
            testCase "Using TitleCase"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ]; Name.fromList [ "baz" ] ]
                Path.toString Name.toTitleCase "." path
                |> Expect.equal "FooBar.Baz"

            testCase "Using SnakeCase"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ]; Name.fromList [ "baz" ] ]
                Path.toString Name.toSnakeCase "/" path
                |> Expect.equal "foo_bar/baz"
        ]

        testList "isPrefixOf" [
            testCase "Empty path is prefix of any path"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ] ]
                Path.isPrefixOf Path.empty path
                |> Expect.isTrue

            testCase "Path is prefix of itself"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ] ]
                Path.isPrefixOf path path
                |> Expect.isTrue

            testCase "Prefix path is prefix of longer path"
            <| fun _ ->
                let prefix = Path.fromList [ Name.fromList [ "foo" ] ]
                let path = Path.fromList [ Name.fromList [ "foo" ]; Name.fromList [ "bar" ] ]
                Path.isPrefixOf prefix path
                |> Expect.isTrue

            testCase "Non-prefix path is not prefix"
            <| fun _ ->
                let prefix = Path.fromList [ Name.fromList [ "bar" ] ]
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ] ]
                Path.isPrefixOf prefix path
                |> Expect.isFalse
        ]

        testList "toCanonicalString" [
            testCase "Converts to canonical string"
            <| fun _ ->
                let path = Path.fromList [ Name.fromList [ "foo"; "bar" ]; Name.fromList [ "baz" ] ]
                Path.toCanonicalString path
                |> Expect.equal "foo-bar/baz"
        ]

        testList "isEmpty" [
            testCase "Empty path is empty"
            <| fun _ ->
                Path.isEmpty Path.empty
                |> Expect.isTrue

            testCase "Non-empty path is not empty"
            <| fun _ ->
                Path.isEmpty (Path.fromList [ Name.fromList [ "foo" ] ])
                |> Expect.isFalse
        ]
    ]
