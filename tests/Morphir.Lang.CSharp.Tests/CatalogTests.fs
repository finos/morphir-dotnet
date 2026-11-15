// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Lang.CSharp.Tests.CatalogTests

open System.Collections.Immutable
open Expecto
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.Diagnostics
open Morphir.Compilation
open Morphir.Lang.CSharp
open Morphir.Lang.CSharp.Analyzers
open Morphir.Lang.CSharp.Tests.FrontendSupport

let private exampleText (rule: AdmissionRule) = $"namespace Acme.Trading.Domain;\n" + rule.Example + "\n"

/// The example of a rule as a document of the package. An example is a body of declarations.
let private asDocument (rule: AdmissionRule) = document "Example.cs" (exampleText rule)

/// The global options of a model project, as the props file of the package sets them.
type private ModelProjectOptions() =
    inherit AnalyzerConfigOptions()

    override _.TryGetValue(key: string, [<System.Runtime.InteropServices.Out>] value: byref<string>) : bool =
        if key = ModelProject.PropertyName then
            value <- "true"
            true
        else
            value <- null
            false

type private ModelProjectOptionsProvider() =
    inherit AnalyzerConfigOptionsProvider()
    override _.GlobalOptions = ModelProjectOptions() :> AnalyzerConfigOptions
    override _.GetOptions(_: SyntaxTree) = ModelProjectOptions() :> AnalyzerConfigOptions
    override _.GetOptions(_: AdditionalText) = ModelProjectOptions() :> AnalyzerConfigOptions

/// The diagnostics of the admission analyzer as the compiler of an IDE gives them: (message, start line, start column,
/// end line, end column), in the order of their position and id.
let private analyzerView (text: string) =
    let references =
        match FixedReferences.all () with
        | Ok references -> references
        | Error missing -> failtest $"{missing}"

    let tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions(LanguageVersion.CSharp15), path = "Example.cs")
    let compilation = CSharpCompilation.Create("model", [ tree ], references, CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
    let options = AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, ModelProjectOptionsProvider())

    compilation
        .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(AdmissionAnalyzer()), options)
        .GetAnalyzerDiagnosticsAsync()
        .GetAwaiter()
        .GetResult()
    |> Seq.map (fun d ->
        let span = d.Location.GetLineSpan()
        d.GetMessage(), span.StartLinePosition.Line, span.StartLinePosition.Character, span.EndLinePosition.Line, span.EndLinePosition.Character)
    |> Seq.sort
    |> List.ofSeq

/// The admission refusals of the frontend in the same form. Compiler warnings, such as a field that is never set, are not
/// diagnostics of the analyzer.
let private frontendView (result: Checked<CompiledPackage>) =
    Checked.diagnostics result
    |> List.filter (fun d -> Diagnostic.code d |> Option.exists (fun c -> c.Value.StartsWith "csharp.unsupported_"))
    |> List.map (fun d ->
        match Diagnostic.location d with
        | Some location ->
            let range = location.Range
            d.Message, range.Start.Line, range.Start.Character, range.End.Line, range.End.Character
        | None -> failtest $"A diagnostic without a location: {d.Message}")
    |> List.sort

// The rule that needs a second assembly cannot be shown to the frontend: its fixed references cannot bind the symbol,
// so the compiler reports it first. The analyzer tests cover that rule.
let private frontendRules = Rules.All |> Seq.filter (fun rule -> rule.Id <> Rules.OutsideReference.Id) |> List.ofSeq

[<Tests>]
let tests =
    testList
        "C# rule catalog"
        [ test "each rule example gives its own code and a range through the frontend" {
              for rule in frontendRules do
                  let result = compile [ asDocument rule ]
                  let diagnostics = Checked.diagnostics result |> List.filter (fun d -> d.IsError)

                  Expect.isNonEmpty diagnostics $"{rule.Id} gives a diagnostic"

                  let codes = diagnostics |> List.choose Diagnostic.code |> List.map (fun c -> c.Value) |> List.distinct
                  Expect.equal codes [ rule.Code ] $"{rule.Id}: only its own code"

                  let lines = (exampleText rule).Split('\n')

                  for diagnostic in diagnostics do
                      match Diagnostic.location diagnostic with
                      | Some location ->
                          let range = location.Range
                          Expect.equal location.Uri "Example.cs" $"{rule.Id}: the document"
                          Expect.isLessThan range.End.Line lines.Length $"{rule.Id}: the range is inside the document"
                          Expect.isLessThanOrEqual range.End.Character lines[range.End.Line].Length $"{rule.Id}: the end is on its line"
                          Expect.isTrue ((range.Start.Line, range.Start.Character) < (range.End.Line, range.End.Character)) $"{rule.Id}: the range is not empty"
                      | None -> failtest $"{rule.Id}: a diagnostic without a location"
          }

          test "the analyzer and the frontend give the same message and range for each rule example" {
              for rule in frontendRules do
                  let fromAnalyzer = analyzerView (exampleText rule)
                  Expect.isNonEmpty fromAnalyzer $"{rule.Id}: the analyzer reports the example"
                  Expect.equal (frontendView (compile [ asDocument rule ])) fromAnalyzer $"{rule.Id}: one message and one range everywhere"
          }

          test "the analyzer allows the assemblies of the fixed reference set of the frontend and no other" {
              let references =
                  match FixedReferences.all () with
                  | Ok references -> references
                  | Error missing -> failtest $"{missing}"

              let bound = CSharpCompilation.Create("names", [], references)

              let names =
                  [ for reference in references ->
                        match bound.GetAssemblyOrModuleSymbol reference with
                        | :? IAssemblySymbol as assembly -> assembly.Name
                        | other -> failtest $"{reference.Display} is not an assembly: {other}" ]
                  |> List.sort

              Expect.equal (FixedReferenceSet.AssemblyNames |> Seq.sort |> List.ofSeq) names "one reference set"
          }

          test "the frontend codes are csharp.unsupported_ and the slug of the rule" {
              for rule in Rules.All do
                  Expect.equal rule.Code ("csharp.unsupported_" + rule.Slug) rule.Id
                  Expect.isTrue (rule.Iri.EndsWith("unsupported_" + rule.Slug)) rule.Id
          }

          test "the code is a valid diagnostic code" {
              for rule in Rules.All do
                  match DiagnosticCode.Parse rule.Code with
                  | Ok _ -> ()
                  | Error failure -> failtest $"{rule.Id}: {failure}"
          } ]
