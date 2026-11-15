// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Text.Json
open System.Threading
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Diagnostics
open Morphir.Compilation
open Morphir.Lang.CSharp.Analyzers

/// <summary>One C# document and its syntax tree.</summary>
type internal Source = {
    Document: SourceDocument
    Tree: SyntaxTree
}

/// <summary>A bound compilation of the documents of a request, with the polyfill file added.</summary>
type internal Compiled = {
    Compilation: CSharpCompilation
    Sources: Source list
}

/// <summary>The global options of the frontend: every compilation is a Morphir model project.</summary>
type private ModelOptions() =
    inherit AnalyzerConfigOptions()

    override _.TryGetValue(key: string, [<System.Runtime.InteropServices.Out>] value: byref<string>) : bool =
        if key = ModelProject.PropertyName then
            value <- "true"
            true
        else
            value <- null
            false

type private ModelOptionsProvider() =
    inherit AnalyzerConfigOptionsProvider()
    static let none = ModelOptions() :> AnalyzerConfigOptions
    override _.GlobalOptions = ModelOptions() :> AnalyzerConfigOptions
    override _.GetOptions(_: SyntaxTree) = none
    override _.GetOptions(_: AdditionalText) = none

/// <summary>Compiles the documents of a request with fixed options and fixed references.</summary>
module internal CSharpCompiler =

    /// <summary>The path of the polyfill tree. Diagnostics of this tree are not the user's.</summary>
    let polyfillPath = "morphir:polyfill/" + Morphir.Polyfill.FileName

    let private parseOptions = CSharpParseOptions(LanguageVersion.CSharp15)

    /// <summary>
    /// Warnings that decide whether a model is complete. A <c>switch</c> that misses a case is an error here: Morphir has
    /// no way to write a partial match.
    /// </summary>
    let private escalated = [ "CS8509"; "CS8510"; "CS8524" ]

    let private options =
        CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions = NullableContextOptions.Enable,
            specificDiagnosticOptions = [ for id in escalated -> KeyValuePair(id, ReportDiagnostic.Error) ]
        )

    /// <summary>The compilation of the documents, or the diagnostic that says why it cannot start.</summary>
    let compile (documents: SourceDocument list) : Result<Compiled, Diagnostic> =
        match FixedReferences.all () with
        | Error missing -> Error(FixedReferences.unavailable missing)
        | Ok references ->
            let sources =
                documents
                |> List.map (fun document ->
                    {
                        Document = document
                        Tree = CSharpSyntaxTree.ParseText(document.Text, parseOptions, path = document.Uri)
                    }
                )

            let polyfill =
                CSharpSyntaxTree.ParseText(Morphir.Polyfill.Source, parseOptions, path = polyfillPath)

            let trees = [ for source in sources -> source.Tree ] @ [ polyfill ]

            Ok
                {
                    Compilation = CSharpCompilation.Create("model", trees, references, options)
                    Sources = sources
                }

    /// <summary>The location of a Roslyn span in a document, with the URI of the document.</summary>
    let locate (uri: string) (span: FileLinePositionSpan) : Morphir.Compilation.Location =
        let start = span.StartLinePosition
        let finish = span.EndLinePosition

        Morphir.Compilation.Location.Create(
            uri,
            Range.Create(Position.Create(start.Line, start.Character), Position.Create(finish.Line, finish.Character))
        )

    let private uriOf (compiled: Compiled) (tree: SyntaxTree) =
        compiled.Sources
        |> List.tryFind (fun source -> obj.ReferenceEquals(source.Tree, tree))
        |> Option.map (fun source -> source.Document.Uri)

    /// <summary>The location of a node or token, or None for the polyfill tree.</summary>
    let locationOfNode (compiled: Compiled) (node: SyntaxNode) : Morphir.Compilation.Location option =
        uriOf compiled node.SyntaxTree
        |> Option.map (fun uri -> locate uri (node.GetLocation().GetLineSpan()))

    let private dataOf (id: string) =
        use document = JsonDocument.Parse(JsonSerializer.Serialize(dict [ "id", id ]))
        document.RootElement.Clone()

    let private severityOf (severity: Microsoft.CodeAnalysis.DiagnosticSeverity) =
        match severity with
        | Microsoft.CodeAnalysis.DiagnosticSeverity.Error -> Some Morphir.Compilation.DiagnosticSeverity.Error
        | Microsoft.CodeAnalysis.DiagnosticSeverity.Warning -> Some Morphir.Compilation.DiagnosticSeverity.Warning
        | Microsoft.CodeAnalysis.DiagnosticSeverity.Info -> Some Morphir.Compilation.DiagnosticSeverity.Info
        | _ -> None

    let private place (compiled: Compiled) (diagnostic: Microsoft.CodeAnalysis.Diagnostic) =
        match diagnostic.Location.SourceTree with
        | null -> None
        | tree ->
            uriOf compiled tree
            |> Option.map (fun uri -> locate uri (diagnostic.Location.GetLineSpan()))

    let private isUserLocation (diagnostic: Microsoft.CodeAnalysis.Diagnostic) =
        match diagnostic.Location.SourceTree with
        | null -> true
        | tree -> tree.FilePath <> polyfillPath

    let private ordered (compiled: Compiled) (items: (Morphir.Compilation.Diagnostic * Microsoft.CodeAnalysis.Diagnostic) list) =
        let rank (diagnostic: Microsoft.CodeAnalysis.Diagnostic) =
            match diagnostic.Location.SourceTree with
            | null -> -1, 0
            | tree ->
                let index =
                    compiled.Sources
                    |> List.tryFindIndex (fun source -> obj.ReferenceEquals(source.Tree, tree))
                    |> Option.defaultValue Int32.MaxValue

                index, diagnostic.Location.SourceSpan.Start

        items
        |> List.sortBy (fun (_, original) -> rank original)
        |> List.map fst

    /// <summary>The C# compiler diagnostics (code <c>csharp.compiler</c>), in document order. Hidden ones are left out.</summary>
    let compilerDiagnostics (compiled: Compiled) (cancellationToken: CancellationToken) : Morphir.Compilation.Diagnostic list =
        compiled.Compilation.GetDiagnostics cancellationToken
        |> Seq.filter isUserLocation
        |> Seq.choose (fun diagnostic ->
            severityOf diagnostic.Severity
            |> Option.map (fun severity ->
                let made =
                    Morphir.Compilation.Diagnostic.make severity CSharpCodes.compiler (diagnostic.GetMessage())
                    |> Morphir.Compilation.Diagnostic.withData (dataOf diagnostic.Id)

                let located =
                    match place compiled diagnostic with
                    | Some location -> Morphir.Compilation.Diagnostic.at location made
                    | None -> made

                located, diagnostic
            )
        )
        |> List.ofSeq
        |> ordered compiled

    /// <summary>The refusals of the admission analyzer, with the code of each rule, in document order.</summary>
    let admissionDiagnostics (compiled: Compiled) (cancellationToken: CancellationToken) : Morphir.Compilation.Diagnostic list =
        let analyzerOptions = AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, ModelOptionsProvider())

        let withAnalyzers =
            compiled.Compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(AdmissionAnalyzer()),
                analyzerOptions
            )

        withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).GetAwaiter().GetResult()
        |> Seq.filter isUserLocation
        |> Seq.choose (fun diagnostic ->
            match Rules.TryGetById diagnostic.Id with
            | true, rule ->
                let made =
                    Morphir.Compilation.Diagnostic.error (DiagnosticCode.Create rule.Code) (diagnostic.GetMessage())

                let located =
                    match place compiled diagnostic with
                    | Some location -> Morphir.Compilation.Diagnostic.at location made
                    | None -> made

                Some(located, diagnostic)
            | _ -> None
        )
        |> List.ofSeq
        |> ordered compiled
