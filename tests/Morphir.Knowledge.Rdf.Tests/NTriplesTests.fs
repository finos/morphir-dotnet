// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.NTriplesTests

open System
open System.Text
open Expecto
open Morphir.Knowledge
open Morphir.Knowledge.Rdf
open Morphir.Knowledge.Rdf.Tests.Isomorphism

module NTriplesMother =
    let iri suffix = Iri.Create("https://example.org/" + suffix)

    /// A default graph with typed and language-tagged literals and a blank node used three times.
    let order =
        let line = RdfSubject.BlankNode(BlankNodeId.New())
        let lineObject =
            match line with
            | RdfSubject.BlankNode node -> RdfObject.BlankNode node
            | _ -> failwith "unreachable"

        let triple subject predicate value =
            Quad.FromTriple(GraphLocation.DefaultGraph, { Subject = subject; Predicate = iri predicate; Object = value })

        RdfDataset.Empty
            .Add(triple (RdfSubject.Iri(iri "order/7")) "line" lineObject)
            .Add(triple line "quantity" (RdfObject.Literal(RdfLiteral.CreateTyped("02", Iri.Create "http://www.w3.org/2001/XMLSchema#integer"))))
            .Add(triple line "note" (RdfObject.Literal(RdfLiteral.CreateLanguageTagged("Livraison \"rapide\"\n", "fr-CA"))))
            .Add(triple line "sku" (RdfObject.Literal(RdfLiteral.CreateString "A\\B")))

    let review = GraphName.IriName(iri "review")

    /// The order in the default graph plus one statement in a named review graph.
    let orderWithReview =
        order.Add(
            Quad.FromTriple(
                GraphLocation.NamedGraph review,
                { Subject = RdfSubject.Iri(iri "order/7"); Predicate = iri "status"; Object = RdfObject.Literal(RdfLiteral.CreateString "approved") }
            )
        )

    let graphLabel = "<urn:s> <urn:p> <urn:o> <urn:g> .\n"
    let sharedLabel = "_:x <urn:p> _:x .\n_:x <urn:q> \"v\" .\n"

let read (text: string) =
    match NTriples.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes text)) with
    | Ok dataset -> dataset
    | Error error -> failtest (error.ToString())

let write dataset =
    match NTriples.Write dataset with
    | Ok output -> output
    | Error error -> failtest (error.ToString())

[<Tests>]
let tests =
    testList "N-Triples" [
        testCase "a default graph round-trips up to blank-node renaming" <| fun () ->
            let output = write NTriplesMother.order
            Expect.isEmpty output.OmittedGraphs "a default graph omits nothing"
            let back = read (Encoding.UTF8.GetString output.Bytes.Span)
            Expect.isTrue (isomorphic NTriplesMother.order back) "the literals and the shared blank node survive"

        testCase "a fourth term is a syntax error at that term" <| fun () ->
            match NTriples.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes NTriplesMother.graphLabel)) with
            | Ok _ -> failtest "N-Triples has no graph label"
            | Error error ->
                Expect.equal error.Kind NQuadsReadErrorKind.GraphLabelNotAllowed "the error names the graph label"
                Expect.equal error.Kind.Code "ntriples.graph-label" "the code is stable"
                match error.Span with
                | ValueSome span -> Expect.equal span.Start.Value 24 "the span starts at the fourth term"
                | ValueNone -> failtest "a syntax error has a span"

        testCase "a named graph is a typed failure that names the graph" <| fun () ->
            match NTriples.Write(NTriplesMother.orderWithReview, NamedGraphHandling.Reject) with
            | Ok _ -> failtest "N-Triples cannot hold the review graph"
            | Error(RdfWriteError.NamedGraphs graphs) -> Expect.equal (List.ofSeq graphs) [ NTriplesMother.review ] "the review graph is named"
            | Error other -> failtest (other.ToString())

            match NTriples.Write NTriplesMother.orderWithReview with
            | Error(RdfWriteError.NamedGraphs _) -> ()
            | _ -> failtest "the default policy rejects named graphs"

        testCase "explicit omission writes the default graph and names the omitted graph" <| fun () ->
            match NTriples.Write(NTriplesMother.orderWithReview, NamedGraphHandling.DropNamedGraphs) with
            | Error error -> failtest (error.ToString())
            | Ok output ->
                Expect.equal (List.ofSeq output.OmittedGraphs) [ NTriplesMother.review ] "the review graph is named"
                let back = read (Encoding.UTF8.GetString output.Bytes.Span)
                Expect.isTrue (isomorphic NTriplesMother.order back) "only the default graph is written"

        testCase "a label denotes one node within an input and a fresh node in another input" <| fun () ->
            let first = read NTriplesMother.sharedLabel
            let subjects = first.Quads |> Seq.map (fun quad -> quad.Subject) |> Seq.distinct |> Seq.toList
            Expect.hasLength subjects 1 "both statements share the subject"
            let second = read NTriplesMother.sharedLabel
            let merged = first.Merge second
            Expect.equal merged.Quads.Length 4 "the second input has its own blank node"
            Expect.isTrue (first.Quads |> Seq.forall (fun quad -> quad.Graph = GraphLocation.DefaultGraph)) "every triple is in the default graph"
    ]
