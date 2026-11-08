// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Frontend.FSharp.Analysis

open System.Collections.Generic
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Morphir.Compilation
open Morphir.Frontend.FSharp

/// <summary>
/// Admission (FSF-1, FSF-3): checks the original syntax against the rule catalog and reports every refusal
/// before any lowering. It runs only when the original documents have no F# error.
/// </summary>
[<RequireQualifiedAccess>]
module internal Admission =

    /// <summary>The rule catalog.</summary>
    let rules = AdmissionCatalog.rules

    /// <summary>The catalog's view of a checked document.</summary>
    let documentOf (document: CheckedDocument) : AdmissionDocument = {
        Uri = document.Uri
        Path = document.Path
        Text = document.Text
        Lines = document.Lines
        Syntax = document.Syntax
    }


    let private refusal (document: CheckedDocument) (rule: AdmissionRule) (range: range) =
        let message =
            match rule.Suggestion with
            | Some suggestion -> $"{rule.Message} {suggestion}"
            | None -> rule.Message

        use data =
            JsonDocument.Parse(JsonSerializer.Serialize(dict [ "construct", rule.Construct ]))

        Diagnostic.error (DiagnosticCode.Create rule.Code) message
        |> Diagnostic.at (Location.Create(document.Uri, CompilerEdge.rangeOf range))
        |> Diagnostic.withData (data.RootElement.Clone())

    let private symbolIndex (project: CheckedProject) =
        let index =
            Dictionary<struct (string * int * int * int * int), FSharpSymbolUse list>()

        for symbolUse in project.Uses do
            let range = symbolUse.Range

            let key =
                struct (range.FileName,
                        range.StartLine,
                        range.StartColumn,
                        range.EndLine,
                        range.EndColumn)

            match index.TryGetValue key with
            | true, existing ->
                index[key] <-
                    existing
                    @ [ symbolUse ]
            | _ -> index[key] <- [ symbolUse ]

        fun (range: range) ->
            match
                index.TryGetValue(
                    struct (range.FileName,
                            range.StartLine,
                            range.StartColumn,
                            range.EndLine,
                            range.EndColumn)
                )
            with
            | true, uses -> uses
            | _ -> []

    /// <summary>
    /// Checks every original document against the catalog and reports every refusal together, in document and
    /// position order. Passes with no diagnostic when the program is in the supported subset.
    /// </summary>
    let admitWith (catalog: AdmissionRule list) (project: CheckedProject) : Checked<unit> =
        let symbolsAt = symbolIndex project

        let diagnostics =
            project.Documents
            |> List.collect (fun document ->
                AdmissionCatalog.findings catalog symbolsAt (documentOf document)
                |> AdmissionCatalog.outermost
                |> List.map (fun (rule, found) -> refusal document rule found.Report)
            )
            |> CompilerEdge.order (
                project.Documents
                |> List.map (fun document -> {
                    Uri = document.Uri
                    Text = document.Text
                })
            )

        Checked.ofDiagnostics () diagnostics

    /// <summary>Checks the original documents against the whole catalog.</summary>
    let admit project = admitWith rules project
