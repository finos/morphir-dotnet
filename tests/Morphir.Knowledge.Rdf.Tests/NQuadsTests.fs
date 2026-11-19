// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.NQuadsTests

open System
open System.Text
open System.Text.Json
open Expecto
open Morphir.Knowledge.Rdf
open Morphir.Knowledge.Rdf.Tests.DatasetTests
open Morphir.TestSupport.Fixtures

module NQuadsMother =
    let crossGraphBlankNode =
        "_:b0 <urn:label> \"Revenu\"@FR .\r\n_:b0 <urn:amount> \"01\"^^<urn:integer> <urn:approved> ."

    let literalGraphLabel = "<urn:s> <urn:p> <urn:o> \"bad\" ."
    let sharedGraphLabel = "_:b0 <urn:p> <urn:o> _:b0 .\n"
    let tightTokens = """<urn:s><urn:p>"#\n\u263A"@en<urn:g>.#tail"""
    let invalidScalar = """<urn:s> <urn:p> "\UFFFFFFFF" ."""

let read text =
    match NQuads.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes(text: string))) with
    | Ok dataset -> dataset
    | Error error -> failtest (error.ToString())

[<Tests>]
let w3cSyntaxTests =
    use document = JsonDocument.Parse(ReadOnlyMemory(FixtureCatalog.ReadBytes FixtureCatalog.RdfNQuads.Corpus))
    testList "W3C N-Quads syntax" [
        for example in document.RootElement.GetProperty("tests").EnumerateArray() do
            let name = example.GetProperty("name").GetString()
            let valid = example.GetProperty("valid").GetBoolean()
            let bytes = example.GetProperty("utf8Base64").GetBytesFromBase64()
            testCase name <| fun () ->
                match NQuads.Read(ReadOnlyMemory bytes), valid with
                | Ok _, true
                | Error _, false -> ()
                | Error error, true -> failtest (error.ToString())
                | Ok _, false -> failtest "accepted a negative syntax case"
    ]

[<Tests>]
let tests =
    testList "N-Quads" [
        testCase "round-trips lexical forms and shared blank nodes across graphs" <| fun () ->
            let dataset = read NQuadsMother.crossGraphBlankNode
            let written =
                match NQuads.Write dataset with
                | Ok result -> result
                | Error error -> failtest (error.ToString())
            let actual = read (Encoding.UTF8.GetString written.Bytes)
            Expect.equal actual.Quads.Length 2 "both graphs have a statement"
            let defaultQuad = actual.Quads |> Seq.find (fun quad -> quad.Graph = GraphLocation.DefaultGraph)
            let namedQuad = actual.Quads |> Seq.find (fun quad -> quad.Graph <> GraphLocation.DefaultGraph)
            Expect.equal defaultQuad.Subject namedQuad.Subject "the same blank node occurs in both graphs"
            Expect.equal defaultQuad.Predicate.Value "urn:label" "the labeling predicate remains in the default graph"
            Expect.equal namedQuad.Predicate.Value "urn:amount" "the amount predicate remains in the named graph"
            match namedQuad.Graph with
            | GraphLocation.NamedGraph(GraphName.IriName graph) ->
                Expect.equal graph.Value "urn:approved" "the graph IRI round-trips"
            | _ -> failtest "expected the approved graph"
            match defaultQuad.Object, namedQuad.Object with
            | RdfObject.Literal label, RdfObject.Literal amount ->
                Expect.equal (label.LexicalForm, label.LanguageTagOrNull) ("Revenu", "fr") "language-tagged literal"
                Expect.equal (amount.LexicalForm, amount.Datatype.Value) ("01", "urn:integer") "typed literal"
            | _ -> failtest "both objects must be literals"

        testCase "independent imports allocate independent blank nodes" <| fun () ->
            let first = read NQuadsMother.sharedGraphLabel
            let second = read NQuadsMother.sharedGraphLabel
            let both = first.Merge second
            Expect.equal both.Quads.Length 2 "the identical source label denotes two nodes across imports"
            for quad in both.Quads do
                match quad.Subject, quad.Graph with
                | RdfSubject.BlankNode subject, GraphLocation.NamedGraph(GraphName.BlankNodeName graph) ->
                    Expect.equal graph subject "a blank graph name and subject share the document's label map"
                | _ -> failtest "both positions must be blank nodes"

        testCase "a literal graph label is rejected at the term" <| fun () ->
            let bytes = Encoding.UTF8.GetBytes NQuadsMother.literalGraphLabel
            match NQuads.Read(ReadOnlyMemory bytes) with
            | Error error ->
                Expect.equal error.Kind NQuadsReadErrorKind.InvalidGraph "graph label diagnostic"
                match error.Span with
                | ValueSome span -> Expect.equal span.Start.Value 24 "the diagnostic identifies the opening quote"
                | ValueNone -> failtest "the offending term must have a span"
            | Ok _ -> failtest "expected an invalid graph label"

        testCase "recognizes token boundaries, escapes and comments" <| fun () ->
            let quad = (read NQuadsMother.tightTokens).Quads |> Seq.exactlyOne
            match quad.Object with
            | RdfObject.Literal value -> Expect.equal value.LexicalForm "#\n☺" "the escape and in-literal comment marker survive"
            | _ -> failtest "the object must be a literal"

        testCase "reports malformed UTF-8 and out-of-range Unicode escapes" <| fun () ->
            let cases =
                [ [| 0xFFuy |], NQuadsReadErrorKind.InvalidUtf8
                  Encoding.UTF8.GetBytes NQuadsMother.invalidScalar, NQuadsReadErrorKind.InvalidEscape ]
            for bytes, expected in cases do
                match NQuads.Read(ReadOnlyMemory bytes) with
                | Error error -> Expect.equal error.Kind expected "typed Unicode failure"
                | Ok _ -> failtest "expected Unicode failure"

        testCase "empty named graphs require explicit omission" <| fun () ->
            let dataset = DatasetMother.emptyDraft
            match NQuads.Write dataset with
            | Error(NQuadsWriteError.EmptyNamedGraphs graphs) ->
                Expect.sequenceEqual graphs [ DatasetMother.draft ] "the writer returns the affected graph"
            | Ok _ -> failtest "the strict writer cannot encode the empty graph"

            match NQuads.Write(dataset, EmptyGraphHandling.Omit) with
            | Ok result ->
                Expect.isEmpty result.Bytes "the empty named graph has no quad"
                Expect.sequenceEqual result.OmittedGraphs [ DatasetMother.draft ] "omitted graph is reported"
            | Error error -> failtest (error.ToString())
    ]
