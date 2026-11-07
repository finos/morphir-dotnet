module Morphir.Compilation.Tests.DiagnosticTests

open System
open System.Text.Json
open Expecto
open Morphir.Compilation

let private position line character = Position.Create(line, character)

[<Tests>]
let tests =
    testList "Diagnostics" [
        testList "DiagnosticCode" [
            for literal in
                [
                    "fsharp.unsupported_mutable"
                    "compile.unsupported_ir_version"
                    "elm.type-mismatch"
                    "msbuild.unavailable"
                    "a"
                ] do
                test $"accepts {literal}" {
                    let code = DiagnosticCode.Create literal
                    Expect.equal code.Value literal "value"
                    Expect.equal (string code) literal "text"
                }

            test "refuses null" {
                Expect.equal
                    (DiagnosticCode.Parse null)
                    (Error DiagnosticCodeFailure.Empty)
                    "failure"
            }

            for text, failure in
                [
                    "", DiagnosticCodeFailure.Empty
                    "Fsharp.x", DiagnosticCodeFailure.InvalidSegment 0
                    "fsharp..x", DiagnosticCodeFailure.InvalidSegment 1
                    "fsharp.1x", DiagnosticCodeFailure.InvalidSegment 1
                    "fsharp.has space", DiagnosticCodeFailure.InvalidSegment 1
                ] do
                test $"refuses '{text}'" {
                    Expect.equal (DiagnosticCode.Parse text) (Error failure) "failure"
                }

            test "TryParse sets one of the outputs" {
                let mutable code = Unchecked.defaultof<_>
                let mutable failure = Unchecked.defaultof<_>
                Expect.isTrue (DiagnosticCode.TryParse("fsharp.compiler", &code, &failure)) "parsed"
                Expect.equal code.Value "fsharp.compiler" "code"
                Expect.isFalse (DiagnosticCode.TryParse("", &code, &failure)) "refused"
                Expect.equal failure DiagnosticCodeFailure.Empty "failure"
            }
        ]

        testList "Position" [
            test "counts UTF-16 code units, so a non-BMP character is two" {
                let text = "let s = \"😀\" + mutable"
                let offset = text.IndexOf "mutable"

                Expect.equal
                    (Position.FromOffset(text, offset))
                    (position 0 15)
                    "the emoji counts twice"
            }

            test "lines are zero-based and end at \\n, \\r\\n and \\r" {
                let text = "a\nb\r\nc\rd"
                Expect.equal (Position.FromOffset(text, 0)) (position 0 0) "first"
                Expect.equal (Position.FromOffset(text, 2)) (position 1 0) "after \\n"
                Expect.equal (Position.FromOffset(text, 5)) (position 2 0) "after \\r\\n"
                Expect.equal (Position.FromOffset(text, 7)) (position 3 0) "after \\r"
                Expect.equal (Position.FromOffset(text, text.Length)) (position 3 1) "end of text"
            }

            test "refuses negative values and offsets outside the text" {
                Expect.throwsT<ArgumentOutOfRangeException>
                    (fun () ->
                        Position.Create(-1, 0)
                        |> ignore
                    )
                    "line"

                Expect.throwsT<ArgumentOutOfRangeException>
                    (fun () ->
                        Position.Create(0, -1)
                        |> ignore
                    )
                    "character"

                Expect.throwsT<ArgumentOutOfRangeException>
                    (fun () ->
                        Position.FromOffset("ab", 3)
                        |> ignore
                    )
                    "offset"
            }
        ]

        test "a range end is never before its start" {
            Expect.throwsT<ArgumentException>
                (fun () ->
                    Range.Create(position 1 0, position 0 5)
                    |> ignore
                )
                "ordered"

            let range = Range.Create(position 2 8, position 2 15)
            Expect.equal (range.Start, range.End) (position 2 8, position 2 15) "kept"
        }

        test "a diagnostic keeps its code, location, related locations and data" {
            let location =
                Location.Create("Pricing.fs", Range.Create(position 2 8, position 2 15))

            use document = JsonDocument.Parse """{"errorNumber":25}"""

            let diagnostic =
                Diagnostic.warning
                    (DiagnosticCode.Create "fsharp.compiler")
                    "Incomplete pattern matches."
                |> Diagnostic.at location
                |> Diagnostic.withRelated
                    {
                        Location = location
                        Message = "Declared here."
                    }
                |> Diagnostic.withData document.RootElement

            Expect.equal diagnostic.Severity DiagnosticSeverity.Warning "severity"
            Expect.isFalse diagnostic.IsError "not an error"

            Expect.equal
                (diagnostic.CodeOrNull
                 |> Option.ofObj
                 |> Option.map (fun x -> x.Value))
                (Some "fsharp.compiler")
                "code"

            Expect.equal (Diagnostic.location diagnostic) (Some location) "location"
            Expect.equal diagnostic.Related.Count 1 "related"
            Expect.equal (diagnostic.Data.Value.GetProperty("errorNumber").GetInt32()) 25 "data"
            Expect.equal diagnostic.Severity.WireValue "warning" "wire value"
        }

        test "a diagnostic needs a message" {
            Expect.throwsT<ArgumentException>
                (fun () ->
                    Diagnostic.Create(DiagnosticSeverity.Error, null, "", null)
                    |> ignore
                )
                "message"
        }

        test "diagnostics with the same data are equal" {
            use first = JsonDocument.Parse """{"errorNumber":25}"""
            use second = JsonDocument.Parse """{"errorNumber":25}"""

            let make (data: JsonElement) =
                Diagnostic.warning (DiagnosticCode.Create "fsharp.compiler") "Incomplete."
                |> Diagnostic.withData data

            Expect.equal (make first.RootElement) (make second.RootElement) "content equality"
        }

        test "C# With members agree with the F# module" {
            let location = Location.Create("A.fs", Range.Create(position 0 0, position 0 1))
            let diagnostic = Diagnostic.Create(DiagnosticSeverity.Info, null, "note", null)

            Expect.equal
                (diagnostic.WithLocation location)
                (Diagnostic.at location diagnostic)
                "location"

            Expect.equal
                (diagnostic.Severity.Match(
                    (fun () -> "e"),
                    (fun () -> "w"),
                    (fun () -> "i"),
                    (fun () -> "h")
                ))
                "i"
                "match"
        }

        test "a location needs a URI" {
            Expect.throwsT<ArgumentException>
                (fun () ->
                    Location.Create(null, Range.Create(position 0 0, position 0 0))
                    |> ignore
                )
                "null"
        }
    ]
