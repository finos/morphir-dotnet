// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
/// The samples of src/Morphir.Knowledge.Rdf/README.md. The C# blocks are compiled from the README text.
/// The F# blocks are repeated here, each test naming its README section.
module Morphir.Knowledge.Rdf.Tests.ReadmeTests

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open Expecto
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Morphir.Knowledge.Rdf

let private readme =
    lazy
        use stream =
            Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Morphir.Knowledge.Rdf.README.md")
            |> Option.ofObj
            |> Option.defaultWith (fun () -> failwith "The README is not embedded.")

        use reader = new StreamReader(stream, Encoding.UTF8)
        reader.ReadToEnd()

/// The fenced blocks of one language, with the heading of the section they belong to.
let blocks (language: string) =
    let text = readme.Value.Replace("\r\n", "\n")
    let mutable section = ""

    [ for part in Regex.Split(text, $"(?m)^(?=## |```{language}\\n)") do
        if part.StartsWith "## " then
            section <- part.Substring(3, part.IndexOf '\n' - 3)
        elif part.StartsWith $"```{language}\n" then
            let body = part.Substring(language.Length + 4)
            yield section, body.Substring(0, body.IndexOf "```") ]

let private references =
    lazy
        (AppContext.GetData "TRUSTED_PLATFORM_ASSEMBLIES" :?> string).Split Path.PathSeparator
        |> Array.map (fun path -> MetadataReference.CreateFromFile path :> MetadataReference)

/// Compiles a C# sample as a program with nullable analysis and returns its errors and warnings.
let compile (source: string) =
    let options =
        CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions = NullableContextOptions.Enable)

    let tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions(LanguageVersion.Latest))

    CSharpCompilation.Create("ReadmeSample", [ tree ], references.Value, options).GetDiagnostics()
    |> Seq.filter (fun diagnostic -> diagnostic.Severity >= DiagnosticSeverity.Warning)
    |> Seq.map string
    |> Seq.toList

[<Tests>]
let tests =
    testList "RDF README" [
        let samples = blocks "csharp"

        testCase "the README has a C# sample for each codec" <| fun () ->
            Expect.containsAll (samples |> List.map fst) [ "Read and write"; "N-Triples"; "RDF/XML"; "Canonical output" ] "every codec section has a C# sample"

        for section, source in samples do
            testCase $"the C# sample of '{section}' compiles" <| fun () ->
                Expect.isEmpty (compile source) "the sample compiles without warnings"

        testCase "the F# sample of 'RDF/XML' runs" <| fun () ->
            let dataset =
                match NTriples.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes "<urn:item> <http://purl.org/dc/terms/title> \"Order\"@en .\n")) with
                | Ok dataset -> dataset
                | Error error -> failwith (error.ToString())

            match RdfXml.Write dataset with
            | Ok output ->
                Expect.stringContains (Encoding.UTF8.GetString output.Bytes.Span) """<dcterms:title xml:lang="en">Order</dcterms:title>""" "the title is a dcterms property"
            | Error error -> failwith (error.ToString())

        testCase "the F# sample of 'Canonical output' runs" <| fun () ->
            let dataset =
                match NQuads.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes "_:x <urn:p> _:y .\n_:y <urn:p> _:x .\n")) with
                | Ok dataset -> dataset
                | Error error -> failwith (error.ToString())

            match RdfCanonicalization.Canonicalize(dataset, CanonicalizationOptions.Default) with
            | Ok canonical ->
                match NQuads.Write canonical with
                | Ok output ->
                    Expect.equal (Encoding.UTF8.GetString output.Bytes) "_:c14n0 <urn:p> _:c14n1 .\n_:c14n1 <urn:p> _:c14n0 .\n" "the cycle has canonical labels"
                | Error error -> failwith (error.ToString())
            | Error error -> failwith (error.ToString())

        testCase "the F# sample of 'N-Triples' runs" <| fun () ->
            let bytes = Encoding.UTF8.GetBytes("<urn:item> <urn:status> \"approved\" .\n")
            let dataset =
                match NTriples.Read(ReadOnlyMemory bytes) with
                | Ok dataset -> dataset
                | Error error -> failwith (error.ToString())

            match NTriples.Write(dataset, NamedGraphHandling.DropNamedGraphs) with
            | Ok output ->
                Expect.isEmpty output.OmittedGraphs "the sample has no named graphs"
                Expect.equal (Encoding.UTF8.GetString output.Bytes.Span) "<urn:item> <urn:status> \"approved\" .\n" "the sample writes its triple"
            | Error error -> failwith (error.ToString())
    ]
