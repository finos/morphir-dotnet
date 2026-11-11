module Morphir.Extensions.Tests.ExtensionIdTests

open System
open Expecto
open FsCheck
open Morphir.Extensions
open Morphir.Extensions.Tests.Generators

let private field = "extension id"

let private label (text: string | null) =
    match text with
    | null -> "null"
    | value when value.Length > 20 -> $"'{value.Substring(0, 8)}...' ({value.Length} characters)"
    | value -> $"'{value}'"

let private refused (text: string | null) (expected: ParseFailure) =
    test $"refuses {label text}" {
        Expect.equal (ExtensionId.Parse text) (Error expected) "refused with the rule"
    }

[<Tests>]
let tests =
    testList "ExtensionId" [
        testProperty "a generated valid id parses and prints back unchanged"
        <| Prop.forAll
            (Arb.fromGen dottedId)
            (fun text ->
                match ExtensionId.Parse text with
                | Ok id ->
                    id.Value = text
                    && string id = text
                | Error _ -> false
            )

        for text in [ "morphir-elm"; "a"; "morphir.csharp"; "acme.tools.lint-2"; String('a', 238) ] do
            test $"accepts {label text}" { Expect.isOk (ExtensionId.Parse text) "accepted" }

        refused null (ParseFailure.Empty field)
        refused "" (ParseFailure.Empty field)
        refused "." (ParseFailure.InvalidSegment(field, 0))
        refused ".." (ParseFailure.InvalidSegment(field, 0))
        refused "a..b" (ParseFailure.InvalidSegment(field, 1))
        refused ".a" (ParseFailure.InvalidSegment(field, 0))
        refused "a." (ParseFailure.InvalidSegment(field, 1))
        refused "A" (ParseFailure.InvalidSegment(field, 0))
        refused "1a" (ParseFailure.InvalidSegment(field, 0))
        refused "a-" (ParseFailure.InvalidSegment(field, 0))
        refused "a_b" (ParseFailure.InvalidSegment(field, 0))
        refused (String('a', 239)) (ParseFailure.TooLong(field, 238))

        test "ReservedForBuiltIns: every id that starts with morphir. is reserved; org.finos.morphir ids are not" {
            Expect.isTrue (ExtensionId.Create "morphir.fsharp").ReservedForBuiltIns "morphir.fsharp"
            Expect.isTrue (ExtensionId.Create "morphir.not-yet-shipped").ReservedForBuiltIns "a future built-in"
            Expect.isFalse (ExtensionId.Create "org.finos.morphir.fsharp-backend").ReservedForBuiltIns "the F# backend guest"
            Expect.isFalse (ExtensionId.Create "morphir-dotnet.fsharp").ReservedForBuiltIns "another first token"
        }

        test "TryParse gives the id or the failure" {
            let ok, id, failure = ExtensionId.TryParse "morphir.csharp"
            Expect.isTrue ok "parsed"
            Expect.equal (string id) "morphir.csharp" "id"
            Expect.isTrue (obj.ReferenceEquals(failure, null)) "no failure"
            let ok, id, failure = ExtensionId.TryParse "A"
            Expect.isFalse ok "refused"
            Expect.isTrue (obj.ReferenceEquals(id, null)) "no id"
            Expect.equal failure (ParseFailure.InvalidSegment(field, 0)) "failure"
        }

        test "Create refuses an invalid literal and names it" {
            try
                ExtensionId.Create "Bad.Id"
                |> ignore

                failtest "Create accepted an invalid literal"
            with :? ArgumentException as error ->
                Expect.stringContains error.Message "Bad.Id" "names the literal"
        }

        test "equality and order are ordinal" {
            Expect.equal
                (ExtensionId.Create "morphir.csharp")
                (ExtensionId.Create "morphir.csharp")
                "equal"

            Expect.isLessThan
                (compare (ExtensionId.Create "a-b") (ExtensionId.Create "a.b"))
                0
                "'-' sorts before '.'"

            Expect.isLessThan
                (compare (ExtensionId.Create "morphir.csharp") (ExtensionId.Create "morphir.fsharp"))
                0
                "csharp first"
        }

        test "a target id uses the same grammar" {
            Expect.equal (string (TargetId.Create "csharp")) "csharp" "accepted"

            Expect.equal
                (TargetId.Parse "C#")
                (Error(ParseFailure.InvalidSegment("target id", 0)))
                "refused"
        }
    ]
