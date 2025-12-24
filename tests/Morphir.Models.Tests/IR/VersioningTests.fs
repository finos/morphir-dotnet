module Morphir.IR.Tests.VersioningTests

open Expecto
open Morphir.IR
open Morphir.Testing.Assertions

[<Tests>]
let tests =
    testList "Versioning" [
        testList "SemanticVersion" [
            testList "parse" [
                testCase "Parses basic version \"1.0.0\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0.0" with
                    | Ok sv ->
                        sv.Major |> Expect.equal 1
                        sv.Minor |> Expect.equal 0
                        sv.Patch |> Expect.equal 0
                        sv.PreRelease |> Expect.equal None
                        sv.BuildMetadata |> Expect.equal None
                    | Error _ -> failwith "Expected successful parse"

                testCase "Parses version with pre-release \"1.0.0-alpha\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0.0-alpha" with
                    | Ok sv ->
                        sv.Major |> Expect.equal 1
                        sv.Minor |> Expect.equal 0
                        sv.Patch |> Expect.equal 0
                        sv.PreRelease |> Expect.equal (Some "alpha")
                        sv.BuildMetadata |> Expect.equal None
                    | Error _ -> failwith "Expected successful parse"

                testCase "Parses version with build metadata \"1.0.0+20130313144700\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0.0+20130313144700" with
                    | Ok sv ->
                        sv.Major |> Expect.equal 1
                        sv.Minor |> Expect.equal 0
                        sv.Patch |> Expect.equal 0
                        sv.PreRelease |> Expect.equal None
                        sv.BuildMetadata |> Expect.equal (Some "20130313144700")
                    | Error _ -> failwith "Expected successful parse"

                testCase "Parses version with pre-release and build \"1.0.0-beta+exp.sha.5114f85\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0.0-beta+exp.sha.5114f85" with
                    | Ok sv ->
                        sv.Major |> Expect.equal 1
                        sv.Minor |> Expect.equal 0
                        sv.Patch |> Expect.equal 0
                        sv.PreRelease |> Expect.equal (Some "beta")
                        sv.BuildMetadata |> Expect.equal (Some "exp.sha.5114f85")
                    | Error _ -> failwith "Expected successful parse"

                testCase "Parses complex pre-release \"1.0.0-alpha.1\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0.0-alpha.1" with
                    | Ok sv ->
                        sv.PreRelease |> Expect.equal (Some "alpha.1")
                    | Error _ -> failwith "Expected successful parse"

                testCase "Fails to parse empty string"
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "" with
                    | Ok _ -> failwith "Expected parse error"
                    | Error _ -> ()

                testCase "Fails to parse invalid format \"1.0\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "1.0" with
                    | Ok _ -> failwith "Expected parse error"
                    | Error _ -> ()

                testCase "Fails to parse non-numeric version \"a.b.c\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "a.b.c" with
                    | Ok _ -> failwith "Expected parse error"
                    | Error _ -> ()

                testCase "Fails to parse negative version \"-1.0.0\""
                <| fun _ ->
                    match Versioning.SemanticVersion.parse "-1.0.0" with
                    | Ok _ -> failwith "Expected parse error"
                    | Error _ -> ()
            ]

            testList "toString" [
                testCase "Formats basic version"
                <| fun _ ->
                    let sv = { Major = 1; Minor = 2; Patch = 3; PreRelease = None; BuildMetadata = None }
                    Versioning.SemanticVersion.toString sv
                    |> Expect.equal "1.2.3"

                testCase "Formats version with pre-release"
                <| fun _ ->
                    let sv = { Major = 1; Minor = 0; Patch = 0; PreRelease = Some "alpha"; BuildMetadata = None }
                    Versioning.SemanticVersion.toString sv
                    |> Expect.equal "1.0.0-alpha"

                testCase "Formats version with build metadata"
                <| fun _ ->
                    let sv = { Major = 1; Minor = 0; Patch = 0; PreRelease = None; BuildMetadata = Some "build.123" }
                    Versioning.SemanticVersion.toString sv
                    |> Expect.equal "1.0.0+build.123"

                testCase "Formats version with both pre-release and build"
                <| fun _ ->
                    let sv = { Major = 1; Minor = 0; Patch = 0; PreRelease = Some "beta"; BuildMetadata = Some "exp.sha.5114f85" }
                    Versioning.SemanticVersion.toString sv
                    |> Expect.equal "1.0.0-beta+exp.sha.5114f85"
            ]
        ]

        testList "FormatVersion" [
            testCase "Classic is a valid FormatVersion"
            <| fun _ ->
                let fv = Versioning.FormatVersion.Classic 2
                match fv with
                | Versioning.FormatVersion.Classic 2 -> ()
                | _ -> failwith "Expected Classic 2"

            testCase "SemVer is a valid FormatVersion"
            <| fun _ ->
                let sv = { Major = 3; Minor = 0; Patch = 0; PreRelease = Some "Experimental"; BuildMetadata = None }
                let fv = Versioning.FormatVersion.SemVer sv
                match fv with
                | Versioning.FormatVersion.SemVer v ->
                    v.Major |> Expect.equal 3
                    v.PreRelease |> Expect.equal (Some "Experimental")
                | _ -> failwith "Expected SemVer"
        ]

        testList "version" [
            testCase "Classic returns version string"
            <| fun _ ->
                Versioning.version (Versioning.FormatVersion.Classic 2)
                |> Expect.equal "2"

            testCase "SemVer returns semantic version string"
            <| fun _ ->
                let sv = { Major = 3; Minor = 0; Patch = 0; PreRelease = Some "Experimental"; BuildMetadata = None }
                Versioning.version (Versioning.FormatVersion.SemVer sv)
                |> Expect.equal "3.0.0-Experimental"
        ]

        testList "isClassic" [
            testCase "Classic is classic"
            <| fun _ ->
                Versioning.isClassic (Versioning.FormatVersion.Classic 2)
                |> Expect.equal true

            testCase "SemVer is not classic"
            <| fun _ ->
                let sv = { Major = 3; Minor = 0; Patch = 0; PreRelease = None; BuildMetadata = None }
                Versioning.isClassic (Versioning.FormatVersion.SemVer sv)
                |> Expect.equal false
        ]

        testList "parse" [
            testCase "Parses \"2\" as Classic 2"
            <| fun _ ->
                match Versioning.parse "2" with
                | Ok (Versioning.FormatVersion.Classic 2) -> ()
                | Ok _ -> failwith "Expected Classic 2"
                | Error _ -> failwith "Expected successful parse"

            testCase "Parses \"1\" as Classic 1"
            <| fun _ ->
                match Versioning.parse "1" with
                | Ok (Versioning.FormatVersion.Classic 1) -> ()
                | Ok _ -> failwith "Expected Classic 1"
                | Error _ -> failwith "Expected successful parse"

            testCase "Parses \"3.0.0\" as SemVer"
            <| fun _ ->
                match Versioning.parse "3.0.0" with
                | Ok (Versioning.FormatVersion.SemVer sv) ->
                    sv.Major |> Expect.equal 3
                    sv.Minor |> Expect.equal 0
                    sv.Patch |> Expect.equal 0
                | Ok _ -> failwith "Expected SemVer"
                | Error _ -> failwith "Expected successful parse"

            testCase "Parses \"3.0.0-Experimental\" as SemVer"
            <| fun _ ->
                match Versioning.parse "3.0.0-Experimental" with
                | Ok (Versioning.FormatVersion.SemVer sv) ->
                    sv.PreRelease |> Expect.equal (Some "Experimental")
                | Ok _ -> failwith "Expected SemVer"
                | Error _ -> failwith "Expected successful parse"

            testCase "Fails to parse invalid version"
            <| fun _ ->
                match Versioning.parse "invalid" with
                | Ok _ -> failwith "Expected parse error"
                | Error _ -> ()
        ]
    ]

