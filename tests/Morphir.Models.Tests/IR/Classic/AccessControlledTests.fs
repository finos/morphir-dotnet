module Morphir.IR.Classic.Tests.AccessControlledTests

open Expecto
open Morphir.IR.Classic
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "AccessControlled" [
        testList "public'" [
            testCase "Creates public access controlled value"
            <| fun _ ->
                let value = 42
                let accessControlled = AccessControlled.public' value

                accessControlled.Access
                |> Expect.equal AccessControlled.Public

                AccessControlled.withPrivateAccess accessControlled
                |> Expect.equal value

            testCase "Public value has Public access"
            <| fun _ ->
                let accessControlled = AccessControlled.public' "test"
                (accessControlled.Access = AccessControlled.Public)
                |> Expect.isTrue
        ]

        testList "private'" [
            testCase "Creates private access controlled value"
            <| fun _ ->
                let value = "secret"
                let accessControlled = AccessControlled.private' value

                accessControlled.Access
                |> Expect.equal AccessControlled.Private

                AccessControlled.withPrivateAccess accessControlled
                |> Expect.equal value

            testCase "Private value has Private access"
            <| fun _ ->
                let accessControlled = AccessControlled.private' 123
                (accessControlled.Access = AccessControlled.Private)
                |> Expect.isTrue
        ]

        testList "map" [
            testCase "Maps value while preserving Public access"
            <| fun _ ->
                let original = AccessControlled.public' 5
                let mapped = AccessControlled.map (fun x -> x * 2) original

                mapped.Access
                |> Expect.equal AccessControlled.Public

                AccessControlled.withPrivateAccess mapped
                |> Expect.equal 10

            testCase "Maps value while preserving Private access"
            <| fun _ ->
                let original = AccessControlled.private' "hello"
                let mapped = AccessControlled.map (fun s -> s + " world") original

                mapped.Access
                |> Expect.equal AccessControlled.Private

                AccessControlled.withPrivateAccess mapped
                |> Expect.equal "hello world"
        ]

        testList "withPrivateAccess" [
            testCase "Always returns value regardless of access level"
            <| fun _ ->
                let publicValue = AccessControlled.public' 42
                let privateValue = AccessControlled.private' 42

                AccessControlled.withPrivateAccess publicValue
                |> Expect.equal 42

                AccessControlled.withPrivateAccess privateValue
                |> Expect.equal 42
        ]
    ]

