module Morphir.IR.Tests.NameTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Name" [
        testList "fromString" [
            testCase "From string with camelCase and underscores"
            <| fun _ ->
                Name.fromString "fooBar_baz 123"
                |> Expect.equal (Name.fromList [ "foo"; "bar"; "baz"; "123" ])

            testCase "From string with camelCase"
            <| fun _ ->
                Name.fromString "valueInUSD"
                |> Expect.equal (Name.fromList [ "value"; "in"; "u"; "s"; "d" ])

            testCase "From string with PascalCase"
            <| fun _ ->
                Name.fromString "ValueInUSD"
                |> Expect.equal (Name.fromList [ "value"; "in"; "u"; "s"; "d" ])

            testCase "From string with snake_case"
            <| fun _ ->
                Name.fromString "value_in_USD"
                |> Expect.equal (Name.fromList [ "value"; "in"; "u"; "s"; "d" ])

            testCase "From string with only separators"
            <| fun _ ->
                Name.fromString "_-% "
                |> Expect.equal Name.empty
        ]

        testList "toTitleCase" [
            testCase "Title case conversion"
            <| fun _ ->
                Name.fromList [ "foo"; "bar"; "baz"; "123" ]
                |> Name.toTitleCase
                |> Expect.equal "FooBarBaz123"

            testCase "Title case with abbreviations"
            <| fun _ ->
                Name.fromList [ "value"; "in"; "u"; "s"; "d" ]
                |> Name.toTitleCase
                |> Expect.equal "ValueInUSD"
        ]

        testList "toCamelCase" [
            testCase "Camel case conversion"
            <| fun _ ->
                Name.fromList [ "foo"; "bar"; "baz"; "123" ]
                |> Name.toCamelCase
                |> Expect.equal "fooBarBaz123"

            testCase "Camel case with abbreviations"
            <| fun _ ->
                Name.fromList [ "value"; "in"; "u"; "s"; "d" ]
                |> Name.toCamelCase
                |> Expect.equal "valueInUSD"
        ]

        testList "toSnakeCase" [
            testCase "Snake case conversion"
            <| fun _ ->
                Name.fromList [ "foo"; "bar"; "baz"; "123" ]
                |> Name.toSnakeCase
                |> Expect.equal "foo_bar_baz_123"

            testCase "Snake case with abbreviations"
            <| fun _ ->
                Name.fromList [ "value"; "in"; "u"; "s"; "d" ]
                |> Name.toSnakeCase
                |> Expect.equal "value_in_USD"
        ]

        testList "toHumanWords" [
            testCase "Human words conversion"
            <| fun _ ->
                Name.fromList [ "foo"; "bar"; "baz"; "123" ]
                |> Name.toHumanWords
                |> Expect.equal [ "foo"; "bar"; "baz"; "123" ]

            testCase "Human words with abbreviations"
            <| fun _ ->
                Name.fromList [ "value"; "in"; "u"; "s"; "d" ]
                |> Name.toHumanWords
                |> Expect.equal [ "value"; "in"; "USD" ]
        ]

        testList "isEmpty" [
            testCase "Empty name is empty"
            <| fun _ ->
                Name.isEmpty Name.empty
                |> Expect.isTrue

            testCase "Non-empty name is not empty"
            <| fun _ ->
                Name.isEmpty (Name.fromList [ "foo" ])
                |> Expect.isFalse
        ]

        testList "toList" [
            testCase "Converts name to list"
            <| fun _ ->
                Name.fromList [ "foo"; "bar"; "baz" ]
                |> Name.toList
                |> Expect.equal [ "foo"; "bar"; "baz" ]
        ]
    ]

