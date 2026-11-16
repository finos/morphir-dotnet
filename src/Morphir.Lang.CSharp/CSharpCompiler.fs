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

/// <summary>
/// Every document of a request is code of the user. A header such as <c>// &lt;auto-generated/&gt;</c> or a name such as
/// <c>.g.cs</c> does not switch off the admission analyzer, because the frontend lowers the document all the same.
/// </summary>
type private UserCodeOptionsProvider() =
    inherit SyntaxTreeOptionsProvider()
    override _.IsGenerated(_: SyntaxTree, _: CancellationToken) = GeneratedKind.NotGenerated

    override _.TryGetDiagnosticValue(_: SyntaxTree, _: string, _: CancellationToken, severity: byref<ReportDiagnostic>) =
        severity <- ReportDiagnostic.Default
        false

    override _.TryGetGlobalDiagnosticValue(_: string, _: CancellationToken, severity: byref<ReportDiagnostic>) =
        severity <- ReportDiagnostic.Default
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

    /// <summary>The path of the tree of the implicit usings. Diagnostics of this tree are not the user's.</summary>
    let usingsPath = "morphir:usings/GlobalUsings.g.cs"

    /// <summary>The implicit usings of an SDK project (<c>ImplicitUsings</c> enabled), the same as the model project gets.</summary>
    let implicitUsings =
        [ "System"
          "System.Collections.Generic"
          "System.IO"
          "System.Linq"
          "System.Net.Http"
          "System.Threading"
          "System.Threading.Tasks" ]

    let private usingsSource =
        implicitUsings |> List.map (fun name -> $"global using global::{name};") |> String.concat "\n"

    let private parseOptions = CSharpParseOptions(LanguageVersion.CSharp15)

    /// <summary>
    /// Warnings that decide whether a model is complete. A <c>switch</c> that misses a case is an error here: Morphir has
    /// no way to write a partial match. CS8524 is not one of them: it says that a value outside the named members of an
    /// enum is not handled, and a Morphir custom type has no such value.
    /// </summary>
    let private escalated = [ "CS8509"; "CS8510" ]

    let private options =
        CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions = NullableContextOptions.Enable,
            specificDiagnosticOptions = [ for id in escalated -> KeyValuePair(id, ReportDiagnostic.Error) ],
            // A pragma or an attribute that suppresses a diagnostic does not change the model, so the frontend sees it.
            reportSuppressedDiagnostics = true
        )
            .WithSyntaxTreeOptionsProvider(UserCodeOptionsProvider())

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

            let usings = CSharpSyntaxTree.ParseText(usingsSource, parseOptions, path = usingsPath)

            let trees = [ for source in sources -> source.Tree ] @ [ polyfill; usings ]

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

    /// <summary>The location of a node, or None for the polyfill tree.</summary>
    let locationOfNode (compiled: Compiled) (node: SyntaxNode) : Morphir.Compilation.Location option =
        uriOf compiled node.SyntaxTree
        |> Option.map (fun uri -> locate uri (node.GetLocation().GetLineSpan()))

    /// <summary>The location of a token, or None for the polyfill tree.</summary>
    let locationOfToken (compiled: Compiled) (token: SyntaxToken) : Morphir.Compilation.Location option =
        uriOf compiled token.SyntaxTree
        |> Option.map (fun uri -> locate uri (token.GetLocation().GetLineSpan()))

    /// The data member <c>{"id": id}</c>. The writer needs no reflection, so the Native AOT host can call it.
    let private dataOf (id: string) =
        use buffer = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(buffer)
        writer.WriteStartObject()
        writer.WriteString("id", id)
        writer.WriteEndObject()
        writer.Flush()
        use document = JsonDocument.Parse(buffer.ToArray())
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
        | tree -> tree.FilePath <> polyfillPath && tree.FilePath <> usingsPath

    /// <summary>
    /// Whether a diagnostic counts. A suppressed compiler diagnostic counts only when it decides whether the model is
    /// complete; a suppressed admission diagnostic always counts, because the frontend cannot lower the construct.
    /// </summary>
    let private counts (admission: bool) (diagnostic: Microsoft.CodeAnalysis.Diagnostic) =
        not diagnostic.IsSuppressed || admission || List.contains diagnostic.Id escalated

    /// <summary>
    /// Puts diagnostics in document order: the document, the start and the end of the span. The id and the message
    /// then decide between two diagnostics at one span, because the analyzers run at the same time and report in any order.
    /// </summary>
    let private ordered (compiled: Compiled) (items: (Morphir.Compilation.Diagnostic * Microsoft.CodeAnalysis.Diagnostic) list) =
        let rank (diagnostic: Microsoft.CodeAnalysis.Diagnostic) =
            let index, span =
                match diagnostic.Location.SourceTree with
                | null -> -1, (0, 0)
                | tree ->
                    let index =
                        compiled.Sources
                        |> List.tryFindIndex (fun source -> obj.ReferenceEquals(source.Tree, tree))
                        |> Option.defaultValue Int32.MaxValue

                    index, (diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.End)

            index, span, diagnostic.Id, diagnostic.GetMessage()

        items
        |> List.sortWith (fun (_, a) (_, b) ->
            let (indexA, spanA, idA, messageA), (indexB, spanB, idB, messageB) = rank a, rank b

            match compare indexA indexB with
            | 0 ->
                match compare spanA spanB with
                | 0 ->
                    match String.CompareOrdinal(idA, idB) with
                    | 0 -> String.CompareOrdinal(messageA, messageB)
                    | order -> order
                | order -> order
            | order -> order)
        |> List.map fst

    /// <summary>The C# compiler diagnostics (code <c>csharp.compiler</c>), in document order. Hidden ones are left out.</summary>
    let compilerDiagnostics (compiled: Compiled) (cancellationToken: CancellationToken) : Morphir.Compilation.Diagnostic list =
        compiled.Compilation.GetDiagnostics cancellationToken
        |> Seq.filter (fun diagnostic -> isUserLocation diagnostic && counts false diagnostic)
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

    /// <summary>
    /// The diagnostics of the given analyzers, in document order. A diagnostic of an admission rule has the code of the
    /// rule. Any other diagnostic, for example <c>AD0001</c> when an analyzer fails, is a <c>csharp.compiler</c> error
    /// with its id in the data: the analyzer did not finish, so the frontend does not know whether the source is admitted.
    /// </summary>
    let analyzerDiagnostics
        (analyzers: DiagnosticAnalyzer list)
        (compiled: Compiled)
        (cancellationToken: CancellationToken)
        : Morphir.Compilation.Diagnostic list =
        let analyzerOptions = AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, ModelOptionsProvider())

        let withAnalyzers =
            compiled.Compilation.WithAnalyzers(
                ImmutableArray.CreateRange<DiagnosticAnalyzer>(analyzers),
                CompilationWithAnalyzersOptions(analyzerOptions, null, true, false, true)
            )

        withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).GetAwaiter().GetResult()
        |> Seq.filter (fun diagnostic -> isUserLocation diagnostic && counts true diagnostic)
        |> Seq.map (fun diagnostic ->
            let made =
                match Rules.TryGetById diagnostic.Id with
                | true, rule -> Morphir.Compilation.Diagnostic.error (DiagnosticCode.Create rule.Code) (diagnostic.GetMessage())
                | _ ->
                    Morphir.Compilation.Diagnostic.error CSharpCodes.compiler (diagnostic.GetMessage())
                    |> Morphir.Compilation.Diagnostic.withData (dataOf diagnostic.Id)

            let located =
                match place compiled diagnostic with
                | Some location -> Morphir.Compilation.Diagnostic.at location made
                | None -> made

            located, diagnostic
        )
        |> List.ofSeq
        |> ordered compiled

    /// <summary>The refusals of the admission analyzer, with the code of each rule, in document order.</summary>
    let admissionDiagnostics (compiled: Compiled) (cancellationToken: CancellationToken) : Morphir.Compilation.Diagnostic list =
        analyzerDiagnostics [ AdmissionAnalyzer() ] compiled cancellationToken
