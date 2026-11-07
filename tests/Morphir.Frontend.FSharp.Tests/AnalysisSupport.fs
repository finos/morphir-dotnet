// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module internal Morphir.Frontend.FSharp.Tests.AnalysisSupport

open System
open System.Threading
open Expecto
open FSharp.Compiler.Symbols
open Morphir.Compilation
open Morphir.Frontend.FSharp.Analysis

let document uri text : SourceDocument = { Uri = uri; Text = text }

let pricing text = document "Pricing.fs" text

/// A module body in the namespace Acme.Demo. Each line of the body is indented by four spaces.
let program (lines: string list) =
    [ "namespace Acme.Demo"; "module Pricing =" ]
    @ (lines
       |> List.map (fun line -> if line.Length = 0 then line else $"    {line}"))
    |> String.concat "\n"
    |> fun text ->
        text
        + "\n"

let unchanged (_: string) (texts: string list) = texts

let analyzeWith rewrite limits documents =
    (FSharpAnalysis.analyzeWith rewrite limits documents CancellationToken.None)
        .GetAwaiter()
        .GetResult()

let analyze documents =
    analyzeWith unchanged EvidenceLimits.Default documents

let data (diagnostic: Diagnostic) =
    match Diagnostic.data diagnostic with
    | Some element -> element.GetRawText()
    | None -> ""

let describe (diagnostic: Diagnostic) =
    let code =
        match diagnostic.CodeOrNull with
        | null -> "-"
        | code -> code.Value

    let location =
        match diagnostic.LocationOrNull with
        | null -> "-"
        | location ->
            let range = location.Range
            $"{location.Uri}:{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}"

    $"{diagnostic.Severity.WireValue} {code} {location} {diagnostic.Message} {data diagnostic}"

let describeAll diagnostics =
    diagnostics
    |> List.map describe
    |> String.concat "\n"

let passed (result: Checked<'T>) =
    match Checked.tryValue result with
    | Some value -> value
    | None ->
        failtestf "Expected a passed result, got:\n%s" (describeAll (Checked.diagnostics result))

let failed (result: Checked<'T>) =
    Expect.isFalse
        result.IsPassed
        $"Expected a failed result, got:\n{describeAll (Checked.diagnostics result)}"

    Checked.diagnostics result

let codeOf (diagnostic: Diagnostic) =
    match diagnostic.CodeOrNull with
    | null -> ""
    | code -> code.Value

let errorNumber (diagnostic: Diagnostic) =
    match Diagnostic.data diagnostic with
    | Some element -> element.GetProperty("errorNumber").GetInt32()
    | None -> -1

let reason (diagnostic: Diagnostic) =
    match Diagnostic.data diagnostic with
    | Some element -> element.GetProperty("reason").GetString()
    | None -> null

let typeName (typ: FSharpType) =
    let typ = typ.StripAbbreviations()

    if typ.HasTypeDefinition then
        match typ.TypeDefinition.TryFullName with
        | Some name -> name
        | None -> typ.Format(FSharpDisplayContext.Empty)
    else
        typ.Format(FSharpDisplayContext.Empty)

let rec shapeOf (pattern: PatternEvidence) =
    match pattern.Shape with
    | PatternShape.Wildcard -> "wildcard"
    | PatternShape.Variable value -> $"variable:{value.DisplayName}"
    | PatternShape.Literal _ -> "literal"
    | PatternShape.Unit -> "unit"
    | PatternShape.Tuple -> "tuple"
    | PatternShape.Constructor case -> $"constructor:{case.Name}"
    | PatternShape.EmptyList -> "empty-list"
    | PatternShape.Cons -> "cons"
    | PatternShape.As -> "as"

/// The original text of an FCS range in a text.
let textAt (text: string) (range: FSharp.Compiler.Text.range) =
    let lines = LineIndex text
    let start = lines.Offset(range.StartLine, range.StartColumn)
    let finish = lines.Offset(range.EndLine, range.EndColumn)

    text.Substring(
        start,
        finish
        - start
    )

let mustNotLeak (diagnostics: Diagnostic list) =
    for diagnostic in diagnostics do
        let text = describe diagnostic
        Expect.isFalse (text.Contains "morphir-virtual") $"No virtual path in: {text}"
        Expect.isFalse (text.Contains "__morphir_evidence") $"No generated name in: {text}"

        match diagnostic.LocationOrNull with
        | null -> ()
        | location ->
            Expect.isFalse (location.Uri.Contains "morphir-virtual") "The URI is the caller's"
