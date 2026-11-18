// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.DatasetTests

open System
open Expecto
open Morphir.Knowledge
open Morphir.Knowledge.Rdf

module DatasetMother =
    let iri suffix = Iri.Create("https://example.org/" + suffix)

    let approved = GraphName.IriName(iri "approved")
    let draft = GraphName.IriName(iri "draft")
    let asOf revision: DatasetVersion = { Dataset = iri "finance"; Revision = RevisionId.Create revision }

    let netRevenue: Triple =
        { Subject = RdfSubject.Iri(iri "net-revenue")
          Predicate = iri "code"
          Object = RdfObject.Literal(RdfLiteral.CreateTyped("01", Iri.Create("http://www.w3.org/2001/XMLSchema#integer"))) }

    let bothStatuses =
        RdfDataset.Empty
            .Add(Quad.ofTriple (GraphLocation.NamedGraph approved) netRevenue)
            .Add(Quad.ofTriple (GraphLocation.NamedGraph draft) netRevenue)

    let approvedStatement: StatementRef =
        { Dataset = asOf "1"; Quad = Quad.ofTriple (GraphLocation.NamedGraph approved) netRevenue }

    let emptyDraft = RdfDataset.Empty.WithNamedGraph(draft, RdfGraph.Empty)

[<Tests>]
let tests =
    let m = DatasetMother.netRevenue
    let draft = DatasetMother.draft

    testList "RDF datasets" [
        testCase "flat quads retain the term in each position" <| fun () ->
            let graph = GraphLocation.NamedGraph DatasetMother.approved
            let quad = Quad.ofTriple graph m
            Expect.equal (quad.Subject, quad.Predicate, quad.Object, quad.Graph)
                (m.Subject, m.Predicate, m.Object, graph) "all four fields are directly accessible"
            Expect.equal (Quad.toTriple quad) m "a quad yields its original triple"
            match quad.Object with
            | RdfObject.Literal literal -> Expect.equal literal.LexicalForm "01" "the original integer lexical form is retained"
            | _ -> failtest "the object must be a literal"

        testCase "membership is deduplicated within each graph" <| fun () ->
            let named = Quad.ofTriple (GraphLocation.NamedGraph draft) m
            let defaultQuad = Quad.ofTriple GraphLocation.DefaultGraph m
            let dataset = RdfDataset.Empty.Add(defaultQuad).Add(defaultQuad).Add(named)
            Expect.equal (dataset.Quads |> Seq.toList) [ defaultQuad; named ] "the same triple occupies two graph addresses"

        testCase "language tags preserve normalized well-formed BCP 47 tags" <| fun () ->
            for tag in [ "EN-Latn-US"; "i-klingon"; "x-private"; "en-u-ca-gregory" ] do
                match RdfLiteral.LanguageTagged("note", tag) with
                | Ok value -> Expect.equal value.LanguageTag (Some(tag.ToLowerInvariant())) "the tag has lowercase RDF value"
                | Error _ -> failtest $"expected a valid language tag: {tag}"

        testCase "A language tag has valid subtags and no duplicate extensions or variants" <| fun () ->
            for tag in [ "en-a"; "x"; "en-ab-cd"; "en-u-ca-gregory-u-nu-latn"; "sl-rozaj-rozaj"; "Kn" ] do
                Expect.equal (RdfLiteral.LanguageTagged("note", tag)) (Error LiteralError.InvalidLanguageTag)
                    $"expected an invalid language tag: {tag}"

        testCase "targets resolve only against their version and graph" <| fun () ->
            let expected = DatasetMother.approvedStatement
            let snapshot = DatasetSnapshot.Create(DatasetMother.asOf "1", DatasetMother.bothStatuses)
            let changed = DatasetSnapshot.Create(DatasetMother.asOf "2", DatasetMother.bothStatuses)
            let source = TextSnapshot.Create(
                { Document = DocumentId.Create(DatasetMother.iri "definition"); Revision = RevisionId.Create "1" },
                "Gross revenue less discounts")
            let range = SourceSpan.Create(TextOffset.Create 0, TextOffset.Create 13)
            let selection =
                match TextSelection.TryCreate(source, [ range ]) with
                | Ok value -> value
                | Error _ -> failtest "the selected phrase is in bounds"
            let evidence: KnowledgeLink<TextSelection, StatementRef> =
                { Id = LinkId.Create(DatasetMother.iri "evidence")
                  Source = selection
                  Predicate = DatasetMother.iri "supports"
                  Target = expected }
            Expect.isTrue (snapshot.ContainsStatement evidence.Target) "evidence resolves to the approved quad"
            Expect.isFalse (changed.ContainsStatement evidence.Target) "a newer revision cannot resolve an old target"

        testCase "an empty graph remains independently addressable" <| fun () ->
            let version = DatasetMother.asOf "1"
            let graphRef: GraphRef = { Dataset = version; Graph = GraphLocation.NamedGraph draft }
            let snapshot = DatasetSnapshot.Create(version, DatasetMother.emptyDraft)
            let review: Annotation<GraphRef, string> =
                { Id = AnnotationId.Create(DatasetMother.iri "review")
                  Target = graphRef
                  Body = "Review this empty graph"
                  Provenance = { Author = DatasetMother.iri "reviewer"; CreatedAt = DateTimeOffset.UnixEpoch } }
            match snapshot.ResolveGraph review.Target with
            | Some graph -> Expect.isTrue graph.IsEmpty "an empty named graph has a graph target"
            | None -> failtest "the graph should exist"

        testCase "independent authored claims can target the same statement" <| fun () ->
            let authored claim author: AuthoredClaim =
                { Id = StatementId.Create(DatasetMother.iri claim)
                  Statement = DatasetMother.approvedStatement
                  Provenance = { Author = DatasetMother.iri author; CreatedAt = DateTimeOffset.UnixEpoch } }
            let first, second = authored "claim-one" "alice", authored "claim-two" "bob"
            Expect.equal first.Statement second.Statement "both claims are about the same quad address"
            Expect.notEqual first.Id second.Id "the authored claims can be identified independently"
            Expect.notEqual first.Provenance second.Provenance "they retain different provenance"
    ]
