// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Frontend.FSharp.Tests.EvidenceTests

open System
open Expecto
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Frontend.FSharp.Analysis
open Morphir.Frontend.FSharp.Tests.AnalysisSupport

let private single lines =
    let text = program lines
    let evidence = passed (analyze [ pricing text ])
    text, evidence, List.exactlyOne evidence.Matches

let private patternsOf (found: MatchEvidence) =
    found.Clauses
    |> List.map (fun clause -> shapeOf clause.Pattern)

let private bodyOf (clause: ClauseEvidence) =
    let (SynMatchClause(resultExpr = body)) = clause.Clause
    body.Range

/// Analyzes with a length-preserving change to the analysis copy, and returns the refusal.
let private refusedBy (rewrite: string -> string -> string) lines =
    let text = program lines

    let diagnostics =
        failed (
            analyzeWith
                (fun prefix texts ->
                    texts
                    |> List.map (rewrite prefix)
                )
                EvidenceLimits.Default
                [ pricing text ]
        )

    mustNotLeak diagnostics
    text, List.exactlyOne diagnostics

let private sameLength (prefix: string) (from: string) (into: string) (text: string) =
    let from = from.Replace("{p}", prefix)
    let into = into.Replace("{p}", prefix)
    Expect.equal into.Length from.Length "the change keeps the text length"
    Expect.stringContains text from "the analysis copy holds the text to change"
    text.Replace(from, into)

let private expectRefusal
    (text: string)
    (diagnostic: Diagnostic)
    (expected: string)
    (matchText: string)
    =
    Expect.equal (codeOf diagnostic) "fsharp.unsupported_match_shape" "code"
    Expect.equal (reason diagnostic) expected "reason"
    Expect.equal diagnostic.LocationOrNull.Uri "Pricing.fs" "original document"

    let range = diagnostic.LocationOrNull.Range
    let lines = LineIndex text

    let start =
        lines.Offset(
            range.Start.Line
            + 1,
            range.Start.Character
        )

    let finish =
        lines.Offset(
            range.End.Line
            + 1,
            range.End.Character
        )

    Expect.equal
        (text.Substring(
            start,
            finish
            - start
        ))
        matchText
        "at the original match"

[<Tests>]
let tests =
    testList "F# match evidence" [
        test "a program with no match uses the original check" {
            let evidence = passed (analyze [ pricing (program [ "let one = 1" ]) ])
            Expect.isEmpty evidence.Matches "no matches"

            Expect.isTrue
                (obj.ReferenceEquals(evidence.Project, evidence.Original))
                "original project"
        }

        test "a reduced variable match keeps its source pattern and checked types" {
            let text, _, found = single [ "let get (x: int) = match x with | n -> n + 1" ]
            Expect.equal (typeName found.SubjectType) "System.Int32" "subject"
            Expect.equal (typeName found.ResultType) "System.Int32" "result"
            Expect.equal (patternsOf found) [ "variable:n" ] "pattern"

            Expect.equal
                (textAt text found.Expression.Range)
                "match x with | n -> n + 1"
                "original range"

            Expect.equal (textAt text (bodyOf found.Clauses.Head)) "n + 1" "original body range"
        }

        test "literal and wildcard clauses keep their order and types" {
            let _, _, found = single [ "let get (x: int) = match x with | 0 -> 1 | _ -> 2" ]
            Expect.equal (patternsOf found) [ "literal"; "wildcard" ] "order"

            Expect.equal
                (found.Clauses
                 |> List.map (fun clause -> typeName clause.Pattern.Type))
                [ "System.Int32"; "System.Int32" ]
                "types"
        }

        test "a tuple pattern gets each component type from the checked tuple" {
            let _, _, found =
                single [ "let get (x: int * string) = match x with | a, b -> b.Length + a" ]

            let pattern = found.Clauses.Head.Pattern
            Expect.equal (shapeOf pattern) "tuple" "tuple"

            Expect.equal
                (pattern.Children
                 |> List.map (fun child -> typeName child.Type))
                [ "System.Int32"; "System.String" ]
                "component types"
        }

        test "union clauses keep source order and their field types" {
            let _, _, found =
                single [
                    "type Status = Available | Rented of int"
                    "let get status = match status with | Rented 0 -> 0 | Rented n -> n + 1 | Available -> 10"
                ]

            Expect.equal
                (patternsOf found)
                [ "constructor:Rented"; "constructor:Rented"; "constructor:Available" ]
                "never reordered"

            Expect.equal
                (found.Clauses[0].Pattern.Children
                 |> List.map shapeOf)
                [ "literal" ]
                "literal field"

            Expect.equal
                (found.Clauses[1].Pattern.Children
                 |> List.map (fun child -> typeName child.Type))
                [ "System.Int32" ]
                "field type"
        }

        test "list patterns keep the element and tail types" {
            let _, _, found =
                single [
                    "let get (xs: int list) = match xs with | head :: tail -> List.length tail + head | [] -> 0"
                ]

            Expect.equal (patternsOf found) [ "cons"; "empty-list" ] "patterns"

            Expect.equal
                (found.Clauses.Head.Pattern.Children
                 |> List.map (fun child -> typeName child.Type))
                [ "System.Int32"; "Microsoft.FSharp.Collections.FSharpList`1" ]
                "element, then tail"
        }

        test "a generic union field is instantiated from the checked subject" {
            let _, _, found =
                single [
                    "type Box<'a> = Box of 'a"
                    "let get (box: Box<string>) = match box with | Box text -> text.Length"
                ]

            Expect.equal
                (found.Clauses.Head.Pattern.Children
                 |> List.map (fun child -> typeName child.Type))
                [ "System.String" ]
                "instantiated field"
        }

        test "constructor arity separates several fields from one tuple field" {
            let _, _, found =
                single [
                    "type Shape = Pair of int * string | Tupled of (int * string)"
                    "let get shape ="
                    "    match shape with"
                    "    | Pair (a, b) -> a + b.Length"
                    "    | Tupled (a, b) -> a + b.Length"
                ]

            Expect.equal
                (found.Clauses[0].Pattern.Children
                 |> List.map shapeOf)
                [ "variable:a"; "variable:b" ]
                "two fields"

            Expect.equal
                (found.Clauses[1].Pattern.Children
                 |> List.map shapeOf)
                [ "tuple" ]
                "one tuple field"
        }

        test "as, unit and parenthesized patterns are supported" {
            let _, _, found =
                single [
                    "let get (x: int, ()) = match x, () with | ((0 as zero), ()) -> zero | (_, ()) -> 1"
                ]

            Expect.equal
                (found.Clauses.Head.Pattern.Children
                 |> List.map shapeOf)
                [ "as"; "unit" ]
                "as, then unit"
        }

        test "a match lambda keeps its implicit argument" {
            let _, _, found =
                single [ "let describe = function | 0 -> \"zero\" | _ -> \"other\"" ]

            Expect.isTrue found.IsLambda "function"
            Expect.equal (typeName found.SubjectType) "System.Int32" "subject"
            Expect.equal (typeName found.ResultType) "System.String" "result"
        }

        test "a computed subject and shadowing keep their checked types" {
            let _, _, found =
                single [
                    "let get (x: int) ="
                    "    let x = string x"
                    "    match x.Length + 1 with"
                    "    | x -> x"
                ]

            Expect.equal (typeName found.SubjectType) "System.Int32" "computed subject"
            Expect.equal (patternsOf found) [ "variable:x" ] "shadowing variable"
        }

        test
            "nested matches keep source order, outer first, and co-terminal bodies close inside out" {
            let text =
                program [
                    "let pick (a: int) (b: int) ="
                    "    match a with"
                    "    | 0 -> \"z\""
                    "    | _ ->"
                    "        match b with"
                    "        | 0 -> \"a\""
                    "        | _ -> \"b\""
                ]

            let evidence = passed (analyze [ pricing text ])

            Expect.equal
                (evidence.Matches
                 |> List.map (fun found -> found.Ordinal))
                [ 0; 1 ]
                "two matches"

            let outer, inner = evidence.Matches[0], evidence.Matches[1]
            Expect.stringStarts (textAt text outer.Expression.Range) "match a with" "outer first"
            Expect.stringStarts (textAt text inner.Expression.Range) "match b with" "inner second"
            Expect.equal (outer.Expression.Range.End) (inner.Expression.Range.End) "co-terminal"

            Expect.equal
                (textAt text (bodyOf outer.Clauses[1]))
                (textAt text inner.Expression.Range)
                "full body span"
        }

        test "a polymorphic match result keeps its generic function type" {
            let text =
                program [
                    "let identity = match () with | () -> fun value -> value"
                    "let number = identity 1"
                    "let word = identity \"x\""
                ]

            let evidence = passed (analyze [ pricing text ])
            let found = List.exactlyOne evidence.Matches
            Expect.isTrue found.ResultType.IsFunctionType "function result"

            Expect.isTrue
                found.ResultType.GenericArguments[0].IsGenericParameter
                "generic, not one use type"
        }

        test "the analysis copy keeps generic constraints" {
            let text =
                program [ "let largest (a: 'T) (b: 'T) = match compare a b with | 1 -> a | _ -> b" ]

            let evidence = passed (analyze [ pricing text ])

            let signature (project: CheckedProject) =
                project.Uses
                |> Array.pick (fun symbolUse ->
                    match symbolUse.Symbol with
                    | :? FSharp.Compiler.Symbols.FSharpMemberOrFunctionOrValue as value when
                        symbolUse.IsFromDefinition
                        && value.DisplayName = "largest"
                        ->
                        Some(Evidence.fingerprint value.FullType)
                    | _ -> None
                )

            Expect.stringContains
                (signature evidence.Original)
                "comparison"
                "the original constraint"

            Expect.equal
                (signature evidence.Project)
                (signature evidence.Original)
                "the same signature"
        }

        test "generated names are fresh across all documents" {
            let other =
                document
                    "Notes.fs"
                    "namespace Acme.Demo\nmodule Notes =\n    let text = \"__morphir_evidence_0_\"\n"

            let text = program [ "let get (x: int) = match x with | n -> n" ]
            let evidence = passed (analyze [ other; pricing text ])
            Expect.hasLength evidence.Matches 1 "one match"

            match evidence.Matches.Head.Clauses.Head.Pattern.Shape with
            | PatternShape.Variable value ->
                Expect.equal value.DisplayName "n" "the original variable"
            | _ -> failtest "a variable"
        }

        test "CRLF documents give the same evidence and the same original ranges" {
            let lines = [
                "type Status = Available | Rented of int"
                "let get status ="
                "    match status with"
                "    | Rented n -> n"
                "    | Available -> 0"
            ]

            let lf = program lines
            let crlf = lf.Replace("\n", "\r\n")

            let fromLf =
                passed (analyze [ pricing lf ])
                |> fun evidence -> List.exactlyOne evidence.Matches

            let fromCrlf =
                passed (analyze [ pricing crlf ])
                |> fun evidence -> List.exactlyOne evidence.Matches

            Expect.equal (patternsOf fromCrlf) (patternsOf fromLf) "patterns"
            Expect.equal (fromCrlf.Expression.Range.Start) (fromLf.Expression.Range.Start) "start"
            Expect.equal (fromCrlf.Expression.Range.End) (fromLf.Expression.Range.End) "end"
            Expect.equal (textAt crlf (bodyOf fromCrlf.Clauses[0])) "n" "body"
        }

        test "a match after a non-BMP character maps through the analysis copy" {
            let _, _, found =
                single [
                    "let get (x: string) = match x with | \"😀\" -> 1 | other -> other.Length"
                ]

            Expect.equal (patternsOf found) [ "literal"; "variable:other" ] "patterns"
        }

        test
            "the insertion map keeps original text at mapped ranges, with CRLF and nested insertions" {
            let original = "ab\r\nc😀d\r\nef"

            let insertions = [|
                { At = 2; Text = "X" }
                { At = 4; Text = "((" }
                { At = 8; Text = ")" }
                { At = 8; Text = "))" }
            |]

            let analysisText = "abX\r\n((c😀d)))\r\nef"
            let map = InsertionMap(LineIndex original, LineIndex analysisText, insertions)
            let range = Range.mkRange "f" (Position.mkPos 2 0) (Position.mkPos 2 4)
            Expect.equal (textAt original range) "c😀d" "original"
            let mapped = map.ToAnalysis("f", range)
            Expect.equal (textAt analysisText mapped) "c😀d" "same text in the copy"

            Expect.equal
                (map.ToOriginal(analysisText.IndexOf 'e'))
                (original.IndexOf 'e')
                "back after insertions"

            Expect.equal
                (map.ToOriginal(analysisText.IndexOf '('))
                4
                "inserted text maps to its insertion point"
        }

        test "missing witness evidence is refused at the original match" {
            let text, diagnostic =
                refusedBy
                    (fun prefix text ->
                        let witness = $" as {prefix}0_0_subject)"

                        sameLength
                            prefix
                            witness
                            (")"
                             + String(
                                 ' ',
                                 witness.Length
                                 - 1
                             ))
                            text
                    )
                    [ "let get (x: int) = match x with | n -> n" ]

            expectRefusal text diagnostic "missing_witness" "match x with | n -> n"
        }

        test "ambiguous witness evidence is refused" {
            let text, diagnostic =
                refusedBy (fun prefix -> sameLength prefix "{p}0_1_subject" "{p}0_0_subject") [
                    "let get (x: int) = match x with | 0 -> 1 | n -> n"
                ]

            expectRefusal text diagnostic "ambiguous_witness" "match x with | 0 -> 1 | n -> n"
        }

        test "a changed checked signature is refused" {
            let text, diagnostic =
                refusedBy (fun prefix -> sameLength prefix "(value    )" "(value + 0)") [
                    "let identity = match () with | () -> fun value -> (value    )"
                    "let number = identity 1"
                ]

            expectRefusal
                text
                diagnostic
                "changed_signature"
                "match () with | () -> fun value -> (value    )"
        }

        test "a changed resolution is refused" {
            let text, diagnostic =
                refusedBy (fun prefix -> sameLength prefix "one) with" "uno) with") [
                    "let one = 1"
                    "let uno = 1"
                    "let get () = match () with | () -> one"
                ]

            expectRefusal text diagnostic "changed_resolution" "match () with | () -> one"
        }

        test "an analysis copy that does not type-check is refused" {
            let text, diagnostic =
                refusedBy (fun prefix -> sameLength prefix "\"zero\") with" "0     ) with") [
                    "let get (x: int) = match x with | 0 -> \"zero\" | _ -> \"other\""
                ]

            expectRefusal
                text
                diagnostic
                "analysis_rejected"
                "match x with | 0 -> \"zero\" | _ -> \"other\""
        }

        test
            "a pattern outside the supported set is refused by evidence when admission does not refuse it" {
            let text =
                program [ "let get (xs: int list) = match xs with | [ a ] -> a | _ -> 0" ]

            let diagnostics = failed (analyzeWithoutAdmission [ pricing text ])
            mustNotLeak diagnostics

            let refusal =
                diagnostics
                |> List.filter (fun diagnostic -> diagnostic.IsError)
                |> List.exactlyOne

            expectRefusal text refusal "unsupported_pattern" "match xs with | [ a ] -> a | _ -> 0"
        }

        test "the match and growth limits apply" {
            let input = [ pricing (program [ "let get (x: int) = match x with | n -> n" ]) ]

            for limits in
                [
                    { EvidenceLimits.Default with
                        MaxMatches = 0
                    }
                    { EvidenceLimits.Default with
                        MaxAddedCharacters = 1
                    }
                ] do
                let diagnostics = failed (analyzeWith unchanged limits input)

                Expect.equal
                    (diagnostics
                     |> List.map reason)
                    [ "limit_exceeded" ]
                    "limit"
        }

        test "no generated binding reaches the evidence" {
            let text =
                program [
                    "type Status = Available | Rented of int"
                    "let get status = match status with | Rented n -> n | Available -> 0"
                    "let pick = function | (a, _) :: _ -> a | [] -> 0"
                ]

            let evidence = passed (analyze [ pricing text ])

            let rec names (pattern: PatternEvidence) =
                (match pattern.Shape with
                 | PatternShape.Variable value -> [ value.DisplayName ]
                 | _ -> [])
                @ (pattern.Children
                   |> List.collect names)

            let all =
                evidence.Matches
                |> List.collect (fun found ->
                    found.Clauses
                    |> List.collect (fun clause -> names clause.Pattern)
                )

            Expect.equal all [ "n"; "a" ] "only original names"

            for found in evidence.Matches do
                Expect.isFalse
                    (found.Expression.Range.FileName.Contains "analysis")
                    "original syntax, never the analysis copy"
        }
    ]
    |> testLabel "PlatformSpecific"
