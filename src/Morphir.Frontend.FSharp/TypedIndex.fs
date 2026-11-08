// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System.Collections.Generic
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

/// <summary>
/// The checked expressions of the evidence project, by range. Lowering reads the compiler's instantiated
/// type of an original expression here (FSF-2): it never solves F# types itself.
/// </summary>
[<Sealed>]
type internal TypedIndex(evidence: CheckedEvidence) =
    let index =
        Dictionary<struct (string * int * int * int * int), ResizeArray<FSharpExpr>>()

    let key (range: range) =
        struct (range.FileName, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn)

    let rec visit (expression: FSharpExpr) =
        let at = key expression.Range

        match index.TryGetValue at with
        | true, found -> found.Add expression
        | _ -> index[at] <- ResizeArray [ expression ]

        for child in expression.ImmediateSubExpressions do
            visit child

    let rec declarations (items: FSharpImplementationFileDeclaration list) =
        for item in items do
            match item with
            | FSharpImplementationFileDeclaration.Entity(_, children) -> declarations children
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(_, _, body) -> visit body
            | FSharpImplementationFileDeclaration.InitAction body -> visit body

    do
        for file in evidence.Project.Results.AssemblyContents.ImplementationFiles do
            declarations file.Declarations

    /// <summary>The checked expressions at an original range of an original document.</summary>
    member _.At(document: int, range: range) : FSharpExpr list =
        let target = evidence.Project.Documents[document]
        let mapped = evidence.Maps[document].ToAnalysis(target.Path, range)

        match index.TryGetValue(key mapped) with
        | true, found -> List.ofSeq found
        | _ -> []

    /// <summary>
    /// The one checked type at an original range: present when every checked expression there has the same type.
    /// </summary>
    member this.TypeAt(document: int, range: range) : FSharpType option =
        match this.At(document, range) with
        | [] -> None
        | first :: rest ->
            let same (other: FSharpExpr) =
                other.Type.StripAbbreviations().Equals(first.Type.StripAbbreviations())

            if List.forall same rest then Some first.Type else None
