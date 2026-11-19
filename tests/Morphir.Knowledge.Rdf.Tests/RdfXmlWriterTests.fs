// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.RdfXmlWriterTests

open System
open System.IO
open System.Text
open System.Xml
open Expecto
open Morphir.Knowledge
open Morphir.Knowledge.Rdf

module RdfXmlMother =
    let iri (value: string) = Iri.Create value
    let vocab local = iri ("https://example.org/vocab/" + local)
    let order = RdfSubject.Iri(iri "https://example.org/order/7")

    /// An order whose line is a blank node with typed, language-tagged and plain literals.
    let orderStatements () =
        let line = BlankNodeId.New()

        [ { Subject = order; Predicate = vocab "line"; Object = RdfObject.BlankNode line }
          { Subject = order
            Predicate = iri "http://purl.org/dc/terms/created"
            Object = RdfObject.Literal(RdfLiteral.CreateTyped("2026-11-18", iri "http://www.w3.org/2001/XMLSchema#date")) }
          { Subject = RdfSubject.BlankNode line
            Predicate = vocab "quantity"
            Object = RdfObject.Literal(RdfLiteral.CreateTyped("02", iri "http://www.w3.org/2001/XMLSchema#integer")) }
          { Subject = RdfSubject.BlankNode line
            Predicate = vocab "note"
            Object = RdfObject.Literal(RdfLiteral.CreateLanguageTagged("Livraison <rapide> & sûre", "fr-CA")) }
          { Subject = RdfSubject.BlankNode line; Predicate = vocab "sku"; Object = RdfObject.Literal(RdfLiteral.CreateString "A1") } ]

    let dataset (triples: Triple list) =
        triples |> List.fold (fun (result: RdfDataset) triple -> result.Add(Quad.FromTriple(GraphLocation.DefaultGraph, triple))) RdfDataset.Empty

    let expectedOrder =
        String.concat
            "\n"
            [ """<?xml version="1.0" encoding="utf-8"?>"""
              """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:ns0="https://example.org/vocab/">"""
              """  <rdf:Description rdf:about="https://example.org/order/7">"""
              """    <dcterms:created rdf:datatype="http://www.w3.org/2001/XMLSchema#date">2026-11-18</dcterms:created>"""
              """    <ns0:line rdf:nodeID="c14n0" />"""
              """  </rdf:Description>"""
              """  <rdf:Description rdf:nodeID="c14n0">"""
              """    <ns0:note xml:lang="fr-ca">Livraison &lt;rapide&gt; &amp; sûre</ns0:note>"""
              """    <ns0:quantity rdf:datatype="http://www.w3.org/2001/XMLSchema#integer">02</ns0:quantity>"""
              """    <ns0:sku>A1</ns0:sku>"""
              """  </rdf:Description>"""
              """</rdf:RDF>"""
              "" ]

let private canonical dataset =
    match RdfCanonicalization.Canonicalize dataset with
    | Ok value -> value
    | Error error -> failtest (error.ToString())

let private text (output: RdfOutput) = Encoding.UTF8.GetString output.Bytes.Span

[<Tests>]
let tests =
    testList "RDF/XML writer" [
        testCase "writes the striped form with datatypes, language tags and node identifiers" <| fun () ->
            match RdfXml.Write(canonical (RdfXmlMother.dataset (RdfXmlMother.orderStatements ())), NamedGraphHandling.Reject) with
            | Ok output -> Expect.equal (text output) RdfXmlMother.expectedOrder "one rdf:Description per subject"
            | Error error -> failtest (error.ToString())

        testCase "the output is the same for statements added in another order" <| fun () ->
            let statements = RdfXmlMother.orderStatements ()
            let write triples =
                match RdfXml.Write(RdfXmlMother.dataset triples) with
                | Ok output -> text output
                | Error error -> failtest (error.ToString())

            Expect.equal (write (List.rev statements)) (write statements) "insertion order does not matter"

        testCase "canonical output does not depend on blank-node identities" <| fun () ->
            let write () =
                match RdfXml.Write(canonical (RdfXmlMother.dataset (RdfXmlMother.orderStatements ())), NamedGraphHandling.Reject) with
                | Ok output -> text output
                | Error error -> failtest (error.ToString())

            Expect.equal (write ()) (write ()) "fresh identities write the same bytes"

        testCase "a predicate without an XML local name is a typed failure that names it" <| fun () ->
            let predicate = RdfXmlMother.iri "https://example.org/p/1"
            let dataset = RdfXmlMother.dataset [ { Subject = RdfXmlMother.order; Predicate = predicate; Object = RdfObject.Literal(RdfLiteral.CreateString "x") } ]
            match RdfXml.Write dataset with
            | Error(RdfWriteError.PredicateWithoutLocalName named) -> Expect.equal named predicate "the predicate is named"
            | other -> failtest $"expected a predicate failure, got {other}"

        testCase "a predicate that RDF/XML reserves for its syntax is a typed failure that names it" <| fun () ->
            for name in [ "li"; "about"; "Description" ] do
                let predicate = RdfXmlMother.iri (RdfNamespaces.Rdf + name)
                let dataset = RdfXmlMother.dataset [ { Subject = RdfXmlMother.order; Predicate = predicate; Object = RdfObject.Literal(RdfLiteral.CreateString "x") } ]
                match RdfXml.Write dataset with
                | Error(RdfWriteError.ReservedPredicate named) -> Expect.equal named predicate $"rdf:{name} is named"
                | other -> failtest $"expected a reserved-predicate failure for rdf:{name}, got {other}"

        testCase "a literal with a character XML cannot hold is a typed failure" <| fun () ->
            let literal = RdfLiteral.CreateString "bell\u0007"
            let dataset = RdfXmlMother.dataset [ { Subject = RdfXmlMother.order; Predicate = RdfXmlMother.vocab "sound"; Object = RdfObject.Literal literal } ]
            match RdfXml.Write dataset with
            | Error(RdfWriteError.NotXmlCharacters named) -> Expect.equal named literal "the literal is named"
            | other -> failtest $"expected a character failure, got {other}"

        testCase "named graphs are rejected by name or dropped explicitly" <| fun () ->
            let review = GraphName.IriName(RdfXmlMother.iri "https://example.org/review")
            let dataset =
                (RdfXmlMother.dataset (RdfXmlMother.orderStatements ()))
                    .Add(Quad.FromTriple(GraphLocation.NamedGraph review, { Subject = RdfXmlMother.order; Predicate = RdfXmlMother.vocab "status"; Object = RdfObject.Literal(RdfLiteral.CreateString "approved") }))

            match RdfXml.Write dataset with
            | Error(RdfWriteError.NamedGraphs graphs) -> Expect.equal (List.ofSeq graphs) [ review ] "the review graph is named"
            | other -> failtest $"expected a named-graph failure, got {other}"

            match RdfXml.Write(canonical dataset, NamedGraphHandling.DropNamedGraphs) with
            | Ok output ->
                Expect.equal (List.ofSeq output.OmittedGraphs) [ review ] "the result names the dropped graph"
                Expect.isFalse ((text output).Contains "approved") "the review statement is not written"
            | Error error -> failtest (error.ToString())

        testCase "the element can be embedded in a caller's XML document" <| fun () ->
            use buffer = new StringWriter()
            do
                use writer = XmlWriter.Create(buffer, XmlWriterSettings(OmitXmlDeclaration = true))
                writer.WriteStartElement("svg", "http://www.w3.org/2000/svg")
                writer.WriteStartElement("metadata", "http://www.w3.org/2000/svg")
                match RdfXml.WriteElement(writer, canonical (RdfXmlMother.dataset (RdfXmlMother.orderStatements ())), NamedGraphHandling.Reject) with
                | Ok omitted -> Expect.isEmpty omitted "nothing is omitted"
                | Error error -> failtest (error.ToString())
                writer.WriteEndElement()
                writer.WriteEndElement()

            let document = XmlDocument()
            document.LoadXml(buffer.ToString())
            let rdf = document.DocumentElement.FirstChild.FirstChild
            Expect.equal (rdf.NamespaceURI, rdf.LocalName) (RdfNamespaces.Rdf, "RDF") "the metadata holds rdf:RDF"
            Expect.equal rdf.ChildNodes.Count 2 "the element has both descriptions"
    ]
