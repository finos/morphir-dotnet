module Morphir.IR.Tests.VersioningTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Versioning" [
        testList "FormatVersion" [
            testCase "Version2 is a valid FormatVersion"
            <| fun _ ->
                let fv = Versioning.FormatVersion.Version2
                match fv with
                | Versioning.FormatVersion.Version2 -> ()
                | _ -> failwith "Expected Version2"

            testCase "Experimental is a valid FormatVersion"
            <| fun _ ->
                let fv = Versioning.FormatVersion.Experimental
                match fv with
                | Versioning.FormatVersion.Experimental -> ()
                | _ -> failwith "Expected Experimental"
        ]

        testList "version" [
            testCase "Version2 returns \"2\""
            <| fun _ ->
                Versioning.version Versioning.FormatVersion.Version2
                |> Expect.equal "2"

            testCase "Experimental returns \"3.0-Experimental\""
            <| fun _ ->
                Versioning.version Versioning.FormatVersion.Experimental
                |> Expect.equal "3.0-Experimental"
        ]

        testList "isClassic" [
            testCase "Version2 is classic"
            <| fun _ ->
                Versioning.isClassic Versioning.FormatVersion.Version2
                |> Expect.equal true

            testCase "Experimental is not classic"
            <| fun _ ->
                Versioning.isClassic Versioning.FormatVersion.Experimental
                |> Expect.equal false
        ]

        testList "parse" [
            testCase "Parses \"2\" as Version2"
            <| fun _ ->
                Versioning.parse "2" Versioning.FormatVersion.Experimental
                |> Expect.equal Versioning.FormatVersion.Version2

            testCase "Parses \"3.0-Experimental\" as Experimental"
            <| fun _ ->
                Versioning.parse "3.0-Experimental" Versioning.FormatVersion.Version2
                |> Expect.equal Versioning.FormatVersion.Experimental

            testCase "Returns fallback for unknown version"
            <| fun _ ->
                Versioning.parse "unknown" Versioning.FormatVersion.Version2
                |> Expect.equal Versioning.FormatVersion.Version2

            testCase "Returns fallback for empty string"
            <| fun _ ->
                Versioning.parse "" Versioning.FormatVersion.Experimental
                |> Expect.equal Versioning.FormatVersion.Experimental
        ]

        testList "v2" [
            testCase "v2 returns Version2"
            <| fun _ ->
                Versioning.v2
                |> Expect.equal Versioning.FormatVersion.Version2
        ]
    ]

