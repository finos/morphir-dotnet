// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.AdmissionTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Morphir.Compilation
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport

let private documentsOf (example: (string * string) list) =
    example
    |> List.map (fun (uri, text) -> document uri text)

/// The source text at the zero-based range of a diagnostic.
let private flagged (documents: SourceDocument list) (diagnostic: Diagnostic) =
    let location = diagnostic.LocationOrNull

    let text =
        (documents
         |> List.find (fun document -> document.Uri = location.Uri))
            .Text

    let lines = LineIndex text

    let start =
        lines.Offset(
            location.Range.Start.Line
            + 1,
            location.Range.Start.Character
        )

    let finish =
        lines.Offset(
            location.Range.End.Line
            + 1,
            location.Range.End.Character
        )

    text.Substring(
        start,
        finish
        - start
    )

let private reasonOrConstruct (diagnostic: Diagnostic) =
    match Diagnostic.data diagnostic with
    | Some element -> element.GetProperty("construct").GetString()
    | None -> null

let private codes diagnostics =
    diagnostics
    |> List.filter (fun (diagnostic: Diagnostic) -> diagnostic.IsError)
    |> List.map codeOf

let private ruleTest (rule: AdmissionRule) =
    test $"{rule.Code.Value}: the example is refused at its construct" {
        let documents = documentsOf rule.Example
        let diagnostics = failed (analyze documents)
        mustNotLeak diagnostics

        let errors =
            diagnostics
            |> List.filter (fun diagnostic -> diagnostic.IsError)

        Expect.isNonEmpty errors "refused"

        Expect.equal
            (codes diagnostics
             |> List.distinct)
            [ rule.Code.Value ]
            "only this rule's code"

        let first = errors.Head
        Expect.equal (flagged documents first) rule.Flagged "at the construct"
        Expect.stringStarts first.Message rule.Message "the message names the construct"
        Expect.equal (reasonOrConstruct first) rule.Construct "the data names the construct"
    }

[<Tests>]
let tests =
    testList "F# admission" [
        testList "catalog examples" [
            for rule in Admission.rules do
                ruleTest rule
        ]

        testList "other forms of refused families" [
            for name, lines, expected in
                [
                    "a function parameter pattern",
                    [ "let first [ a ] = a" ],
                    [ "fsharp.unsupported_list_pattern" ]
                    "a lambda parameter pattern",
                    [ "let first = fun [ a ] -> a" ],
                    [ "fsharp.unsupported_list_pattern" ]
                    "a let pattern",
                    [ "let pick pair = let { contents = c } = pair in c" ],
                    [ "fsharp.unsupported_record_pattern" ]
                    "an active pattern definition and its use",
                    [
                        "let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd"
                        "let half n = match n with | Even -> n / 2 | Odd -> n"
                    ],
                    [
                        "fsharp.unsupported_active_pattern"
                        "fsharp.unsupported_active_pattern"
                        "fsharp.unsupported_active_pattern"
                    ]
                    "a measure definition and annotation",
                    [ "[<Measure>] type kg"; "let weight (value: float<kg>) = value" ],
                    [ "fsharp.unsupported_unit_of_measure"; "fsharp.unsupported_unit_of_measure" ]
                    "a mutable record field",
                    [ "type Box = { mutable Value: int }" ],
                    [ "fsharp.unsupported_mutable" ]
                    "an array type",
                    [ "let size (values: int[]) = 0" ],
                    [ "fsharp.unsupported_array" ]
                    "a struct record",
                    [ "[<Struct>]"; "type Point = { X: int }" ],
                    [ "fsharp.unsupported_struct" ]
                    "an interpolated string",
                    [ "let text n = $\"{n}\"" ],
                    [ "fsharp.unsupported_expression" ]
                    "a try ... with",
                    [ "let safe f = try f () with _ -> 0" ],
                    [ "fsharp.unsupported_exception" ]
                    "an if without else",
                    [ "let check b = if b then ()" ],
                    [ "fsharp.unsupported_expression" ]
                    "a while loop",
                    [ "let spin () = while false do ()" ],
                    [ "fsharp.unsupported_loop" ]
                    "a range list",
                    [ "let some = [ 1 .. 3 ]" ],
                    [ "fsharp.unsupported_comprehension" ]
                    "a downcast",
                    [ "let text (value: obj) = value :?> string" ],
                    [ "fsharp.unsupported_cast" ]
                    "a byte literal pattern",
                    [ "let isZero (b: byte) = match b with | 0uy -> true | _ -> false" ],
                    [ "fsharp.unsupported_literal" ]
                ] do
                test name {
                    let diagnostics = failed (analyze [ pricing (program lines) ])
                    mustNotLeak diagnostics
                    Expect.equal (codes diagnostics) expected "codes"
                }
        ]

        test "catalog codes are unique and in the ecosystem form" {
            let all =
                Admission.rules
                |> List.map (fun rule -> rule.Code.Value)

            Expect.equal (List.distinct all) all "unique"

            for code in all do
                Expect.isMatch
                    code
                    "^fsharp\\.unsupported_[a-z_]+$"
                    "fsharp.unsupported_<snake_case>"
        }

        test "every refused family of the spec has a catalog code" {
            let all =
                Admission.rules
                |> List.map (fun rule -> rule.Code.Value)
                |> Set.ofList

            for family in
                [
                    "mutable"
                    "assignment"
                    "loop"
                    "class"
                    "interface"
                    "member"
                    "object_expression"
                    "computation_expression"
                    "sequence_expression"
                    "comprehension"
                    "array"
                    "exception"
                    "active_pattern"
                    "unit_of_measure"
                    "quotation"
                    "anonymous_record"
                    "struct"
                    "inline"
                    "attribute"
                    "type_test"
                    "cast"
                    "conditional_compilation"
                    "signature_file"
                    "pattern_guard"
                    "or_pattern"
                    "record_pattern"
                ] do
                Expect.isTrue (all.Contains $"fsharp.unsupported_{family}") family
        }

        test "every refusal code in the conformance corpus is a catalog code" {
            let suites =
                Path.GetFullPath(
                    Path.Combine(
                        __SOURCE_DIRECTORY__,
                        "..",
                        "..",
                        "conformance",
                        "suites",
                        "frontend-fsharp"
                    )
                )

            let used =
                Directory.GetFiles(suites, "*.feature")
                |> Seq.collect (fun file ->
                    Regex.Matches(File.ReadAllText file, "fsharp\\.unsupported_[a-z_]+")
                )
                |> Seq.map (fun found -> found.Value)
                |> Set.ofSeq

            let all =
                Admission.rules
                |> List.map (fun rule -> rule.Code.Value)
                |> Set.ofList

            Expect.isNonEmpty used "the corpus has refusals"
            Expect.isEmpty (Set.difference used all) "every corpus code is in the catalog"
        }

        test "several problems in one document are reported together, each at its own range" {
            let text =
                program [ "let mutable x = 0"; "type Counter() ="; "    member _.Value = 1" ]

            let diagnostics = failed (analyze [ pricing text ])

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic ->
                     codeOf diagnostic, diagnostic.LocationOrNull.Range.Start.Line
                 ))
                [ "fsharp.unsupported_mutable", 2; "fsharp.unsupported_class", 3 ]
                "mutable, then class"
        }

        test "conditional compilation is refused at the directive" {
            let text =
                "namespace Acme.Demo\nmodule Pricing =\n#if DEBUG\n    let mode = 1\n#endif\n    let other = 2\n"

            let diagnostics = failed (analyze [ pricing text ])
            let refusal = List.exactlyOne diagnostics
            Expect.equal (codeOf refusal) "fsharp.unsupported_conditional_compilation" "code"
            Expect.equal (flagged [ pricing text ] refusal) "#if DEBUG" "at the directive"
        }

        test "a refusal inside a refused construct is not reported again" {
            let text =
                program [
                    "type Counter() ="
                    "    let mutable count = 0"
                    "    member _.Next() = count <- count + 1"
                    "let work = async { let mutable n = 0 in return n }"
                ]

            let diagnostics = failed (analyze [ pricing text ])

            Expect.equal
                (codes diagnostics)
                [ "fsharp.unsupported_class"; "fsharp.unsupported_computation_expression" ]
                "one refusal per construct"
        }

        test "refusals in several documents are in document order" {
            let first =
                document
                    "A.fs"
                    "namespace Acme.Demo\nmodule A =\n    let inline twice (x: int) = x * 2\n"

            let second =
                document "B.fs" "namespace Acme.Demo\nmodule B =\n    let mutable total = 0\n"

            let diagnostics = failed (analyze [ first; second ])

            Expect.equal
                (diagnostics
                 |> List.map (fun diagnostic -> diagnostic.LocationOrNull.Uri, codeOf diagnostic))
                [ "A.fs", "fsharp.unsupported_inline"; "B.fs", "fsharp.unsupported_mutable" ]
                "document order"
        }

        test "a compiler error stops before admission" {
            let text = program [ "let mutable total: int = \"a\"" ]
            let diagnostics = failed (analyze [ pricing text ])

            Expect.equal
                (codes diagnostics)
                [ "fsharp.compiler" ]
                "no refusal for code the compiler rejected"
        }

        test "the supported subset passes admission" {
            let text =
                program [
                    "type Plate = string"
                    "type Car = { Plate: Plate; Seats: int }"
                    "type Status ="
                    "    | Available"
                    "    | Rented of days: int"
                    "type Pair<'a> = Pair of 'a * 'a"
                    "[<RequireQualifiedAccess>]"
                    "type Size = Small | Large"
                    "let rate = 42.5"
                    "let price: decimal = 10m"
                    "let total (rate: int) (days: int) ="
                    "    let basis = rate * days"
                    "    basis + 10"
                    "let describe status ="
                    "    match status with"
                    "    | Rented 0 -> \"today\""
                    "    | Rented n -> string n"
                    "    | Available -> \"free\""
                    "let rec length xs ="
                    "    match xs with"
                    "    | [] -> 0"
                    "    | _ :: rest -> 1 + length rest"
                    "let rec isEven n = if n = 0 then true else isOdd (n - 1)"
                    "and isOdd n = if n = 0 then false else isEven (n - 1)"
                    "let grow car = { car with Seats = car.Seats + 1 }"
                    "let make () = { Plate = \"A\"; Seats = 4 }"
                    "let numbers = [ 1; 2; 3 ]"
                    "let empty: int list = []"
                    "let doubled = numbers |> List.map (fun x -> x * 2)"
                    "let first (a, _) = a"
                    "let swap = fun (a, b) -> (b, a)"
                    "let sign = function | 0 -> 0 | _ -> 1"
                    "let maybe: Result<int option list, string> = Ok [ Some 1 ]"
                    "let named ((Pair(a, _)) as whole) = (a, whole)"
                    "let unit () = ()"
                    "let letter = 'a'"
                    "let big = 1L"
                ]

            let result = analyze [ pricing text ]

            passed result
            |> ignore

            Expect.isEmpty
                (Checked.diagnostics result
                 |> List.filter (fun diagnostic -> diagnostic.IsError))
                "no refusal"
        }
    ]
