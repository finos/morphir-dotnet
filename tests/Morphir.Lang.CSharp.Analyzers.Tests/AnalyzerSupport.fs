// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Analyzers.Tests.AnalyzerSupport

open System.Collections.Immutable
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Diagnostics
open Morphir.Lang.CSharp.Analyzers

/// The global options of a project: the MSBuild property that marks a model project.
type private Options(modelProject: bool) =
    inherit AnalyzerConfigOptions()

    override _.TryGetValue(key: string, [<System.Runtime.InteropServices.Out>] value: byref<string>) : bool =
        if modelProject && key = ModelProject.PropertyName then
            value <- "true"
            true
        else
            value <- null
            false

type private OptionsProvider(modelProject: bool) =
    inherit AnalyzerConfigOptionsProvider()
    override _.GlobalOptions = Options(modelProject) :> AnalyzerConfigOptions
    override _.GetOptions(_: SyntaxTree) = Options(false) :> AnalyzerConfigOptions
    override _.GetOptions(_: AdditionalText) = Options(false) :> AnalyzerConfigOptions

let references: MetadataReference list =
    Basic.Reference.Assemblies.Net100.References.All
    |> Seq.map (fun reference -> reference :> MetadataReference)
    |> List.ofSeq

/// The polyfill file that a net10.0 model project has: C# 15 unions need runtime types that net10.0 lacks.
let private polyfill = CSharpSyntaxTree.ParseText(Morphir.Polyfill.Source, CSharpParseOptions(LanguageVersion.Preview))

let private compile (source: string) (extra: MetadataReference list) =
    let tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions(LanguageVersion.Preview))

    CSharpCompilation.Create(
        "model",
        [ tree; polyfill ],
        Seq.append references extra,
        CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions = NullableContextOptions.Enable)
    )

/// The analyzer diagnostics of a C# source, ordered by position, as (id, line, message).
let diagnoseWith (modelProject: bool) (compilation: CSharpCompilation) =
    let options = AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, OptionsProvider(modelProject))

    let withAnalyzers =
        compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(AdmissionAnalyzer()), options)

    withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
    |> Seq.sortBy (fun d -> d.Location.SourceSpan.Start)
    |> Seq.map (fun d -> d.Id, d.Location.GetLineSpan().StartLinePosition.Line + 1, d.GetMessage())
    |> List.ofSeq

let diagnose (source: string) = diagnoseWith true (compile source [])

let diagnoseOutsideModelProject (source: string) = diagnoseWith false (compile source [])

/// The compile errors of a source: a test source must be valid C#, or its rule results mean nothing.
let compileErrors (source: string) =
    (compile source []).GetDiagnostics()
    |> Seq.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
    |> Seq.map (fun d -> d.ToString())
    |> List.ofSeq

let diagnoseWithSuppression (source: string) (id: string) =
    let compilation =
        (compile source [])
            .WithOptions(
                CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions = NullableContextOptions.Enable,
                    specificDiagnosticOptions = [ System.Collections.Generic.KeyValuePair(id, ReportDiagnostic.Suppress) ]
                )
            )

    diagnoseWith true compilation

/// The attribute assembly of the package, as a model project references it.
let attributes: MetadataReference =
    MetadataReference.CreateFromFile(typeof<Morphir.MorphirModelAttribute>.Assembly.Location) :> MetadataReference

/// The analyzer diagnostics of a model project source that uses the attributes of the package.
let diagnoseWithAttributes (source: string) = diagnoseWith true (compile source [ attributes ])

/// The compile errors of a source that uses the attributes of the package.
let compileErrorsWithAttributes (source: string) =
    (compile source [ attributes ]).GetDiagnostics()
    |> Seq.filter (fun d -> d.Severity = DiagnosticSeverity.Error)
    |> Seq.map (fun d -> d.ToString())
    |> List.ofSeq

/// The analyzer diagnostics of a source with the attributes of the package, in a project that is not a model project.
let diagnoseOutsideModelProjectWithAttributes (source: string) = diagnoseWith false (compile source [ attributes ])
