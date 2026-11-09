module Morphir.Compilation.Tests.TextParsingTests

open Expecto
open Morphir.Compilation
open Morphir.IR

[<Tests>]
let tests =
    testList "Text parsing for C# callers" [
        test "A package name parses with the canonical path parser" {
            let parsed, package, failure = ParsedPackageName.TryParse "acme/car-rental"
            Expect.isTrue parsed $"{failure}"
            let (PackageName path) = package.Name
            Expect.equal package.Text "acme/car-rental" "text"
            Expect.equal (CanonicalNames.parsePath "acme/car-rental") (Ok path) "path"
        }

        test "An empty package name is refused with a reason" {
            let parsed, _, failure = ParsedPackageName.TryParse ""
            Expect.isFalse parsed "empty"
            Expect.isFalse (System.String.IsNullOrEmpty failure) "reason"
        }

        test "A format version keeps its exact spelling" {
            for text in [ "4"; "4.0.0"; "4.1.0-draft.1"; "3" ] do
                let parsed, version, failure = SourceFormatVersions.TryParse text
                Expect.isTrue parsed $"{text}: {failure}"
                Expect.equal version.Text text "spelling"
        }

        test "Text that is not a format version is refused" {
            for text in [ ""; "four"; "-1"; "4.x" ] do
                let parsed, _, failure = SourceFormatVersions.TryParse text
                Expect.isFalse parsed text
                Expect.isFalse (System.String.IsNullOrEmpty failure) "reason"
        }
    ]
