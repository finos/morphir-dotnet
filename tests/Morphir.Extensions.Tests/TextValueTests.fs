module Morphir.Extensions.Tests.TextValueTests

open System
open Expecto
open FsCheck
open Morphir.IR
open Morphir.Extensions
open Morphir.Extensions.Tests.Generators

[<Tests>]
let tests =
    testList "Text values" [
        testList "ExtensionName" [
            test "accepts a display name" {
                Expect.equal (string (ExtensionName.Create "C# generator")) "C# generator" "kept"
            }
            test "accepts 128 characters" {
                Expect.isOk (ExtensionName.Parse(String('n', 128))) "accepted"
            }
            test "refuses empty" {
                Expect.equal
                    (ExtensionName.Parse "")
                    (Error(ParseFailure.Empty "extension name"))
                    "refused"
            }
            test "refuses 129 characters" {
                Expect.equal
                    (ExtensionName.Parse(String('n', 129)))
                    (Error(ParseFailure.TooLong("extension name", 128)))
                    "refused"
            }
            test "refuses leading white space" {
                Expect.equal
                    (ExtensionName.Parse " name")
                    (Error(ParseFailure.InvalidCharacter("extension name", 0)))
                    "refused"
            }
            test "refuses trailing white space" {
                Expect.equal
                    (ExtensionName.Parse "name ")
                    (Error(ParseFailure.InvalidCharacter("extension name", 4)))
                    "refused"
            }
            test "refuses a control character" {
                Expect.equal
                    (ExtensionName.Parse "na\tme")
                    (Error(ParseFailure.InvalidCharacter("extension name", 2)))
                    "refused"
            }
        ]

        testList "ExtensionDescription" [
            test "accepts 1024 characters" {
                Expect.isOk (ExtensionDescription.Parse(String('d', 1024))) "accepted"
            }
            test "refuses 1025 characters" {
                Expect.equal
                    (ExtensionDescription.Parse(String('d', 1025)))
                    (Error(ParseFailure.TooLong("extension description", 1024)))
                    "refused"
            }
            test "refuses empty" {
                Expect.equal
                    (ExtensionDescription.Parse "")
                    (Error(ParseFailure.Empty "extension description"))
                    "refused"
            }
        ]

        testList "ReleaseVersion" [
            testProperty "a canonical version parses and prints back unchanged"
            <| Prop.forAll
                (Arb.fromGen canonicalVersion)
                (fun text ->
                    match ReleaseVersion.Parse text with
                    | Ok version -> string version = text
                    | Error _ -> false
                )

            for text in [ "0.0.0-dev"; "0.4.0-rc.1"; "1.2.3+abc.1" ] do
                test $"accepts {text}" { Expect.isOk (ReleaseVersion.Parse text) "accepted" }

            test "refuses empty" {
                Expect.equal
                    (ReleaseVersion.Parse "")
                    (Error(ParseFailure.Empty "release version"))
                    "refused"
            }

            test "refuses two parts" {
                match ReleaseVersion.Parse "1.2" with
                | Error(ParseFailure.InvalidVersion("release version", _)) -> ()
                | other -> failtest $"unexpected {other}"
            }

            test "refuses a leading zero" {
                match ReleaseVersion.Parse "01.2.3" with
                | Error(ParseFailure.InvalidVersion("release version", _)) -> ()
                | other -> failtest $"unexpected {other}"
            }

            test "keeps the parsed SemVer value" {
                let version = ReleaseVersion.Create "0.4.0-rc.1"
                Expect.equal version.SemanticVersion.Minor 4 "minor"
                Expect.equal version.SemanticVersion.PreRelease (Some "rc.1") "pre-release"
            }
        ]
    ]
