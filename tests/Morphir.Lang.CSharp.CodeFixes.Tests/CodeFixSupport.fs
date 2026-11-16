// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// Runs the admission analyzer and a code fix on one document of an ad hoc workspace, as an IDE does.
module Morphir.Lang.CSharp.CodeFixes.Tests.CodeFixSupport

open System.Collections.Immutable
open System.Threading
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CodeActions
open Microsoft.CodeAnalysis.CodeFixes
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Diagnostics
open Microsoft.CodeAnalysis.Text
open Morphir.Lang.CSharp.Analyzers

/// The global options of a model project: the MSBuild property that the props file of the package sets.
type private Options() =
    inherit AnalyzerConfigOptions()

    override _.TryGetValue(key: string, [<System.Runtime.InteropServices.Out>] value: byref<string>) : bool =
        if key = ModelProject.PropertyName then
            value <- "true"
            true
        else
            value <- null
            false

type private OptionsProvider() =
    inherit AnalyzerConfigOptionsProvider()
    override _.GlobalOptions = Options() :> AnalyzerConfigOptions
    override _.GetOptions(_: SyntaxTree) = Options() :> AnalyzerConfigOptions
    override _.GetOptions(_: AdditionalText) = Options() :> AnalyzerConfigOptions

/// A value that a Roslyn API can give as null, which a test needs.
let private required (what: string) (value: 'T | null) : 'T =
    match value with
    | null -> failwith $"No {what}."
    | found -> found

let private compilationOf (document: Document) =
    document.Project.GetCompilationAsync().GetAwaiter().GetResult() |> required "compilation"

let private references: MetadataReference list =
    Basic.Reference.Assemblies.Net100.References.All |> Seq.map (fun r -> r :> MetadataReference) |> List.ofSeq

/// A document of a C# 15 model project, with the polyfill file that net10.0 needs for unions.
let document (source: string) : Document =
    let workspace = new AdhocWorkspace()

    let project =
        workspace
            .AddProject("Model", LanguageNames.CSharp)
            .WithParseOptions(CSharpParseOptions(LanguageVersion.Preview))
            .WithCompilationOptions(CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions = NullableContextOptions.Enable))
            .WithMetadataReferences(references)

    let project = project.AddDocument("Polyfill.cs", SourceText.From Morphir.Polyfill.Source).Project
    project.AddDocument("Model.cs", SourceText.From source)

/// The compile errors of a document.
let compileErrors (document: Document) =
    (compilationOf document).GetDiagnostics()
    |> Seq.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
    |> Seq.map string
    |> List.ofSeq

/// The diagnostics of the admission analyzer in the document.
let diagnostics (document: Document) : Diagnostic list =
    let options = AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, OptionsProvider())

    (compilationOf document)
        .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(AdmissionAnalyzer()), options)
        .GetAnalyzerDiagnosticsAsync()
        .GetAwaiter()
        .GetResult()
    |> Seq.filter (fun d ->
        match d.Location.SourceTree with
        | null -> false
        | tree -> tree.FilePath = document.Name || tree.FilePath = (document.FilePath |> Option.ofObj |> Option.defaultValue ""))
    |> List.ofSeq

/// The code actions that a fix provider registers for each diagnostic that it can fix.
let actions (fixer: CodeFixProvider) (document: Document) : CodeAction list =
    let found = ResizeArray<CodeAction>()

    for diagnostic in diagnostics document do
        if fixer.FixableDiagnosticIds.Contains diagnostic.Id then
            let context =
                CodeFixContext(document, diagnostic, (fun action _ -> found.Add action), CancellationToken.None)

            fixer.RegisterCodeFixesAsync(context).GetAwaiter().GetResult()

    List.ofSeq found

/// The document after one code action.
let apply (document: Document) (action: CodeAction) : Document =
    let operations = action.GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult()

    let solution =
        operations
        |> Seq.pick (function
            | :? ApplyChangesOperation as change -> Some change.ChangedSolution
            | _ -> None)

    solution.GetDocument(document.Id) |> required "fixed document"

let text (document: Document) = document.GetTextAsync().GetAwaiter().GetResult().ToString()
