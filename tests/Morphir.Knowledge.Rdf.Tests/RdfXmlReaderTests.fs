// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
module Morphir.Knowledge.Rdf.Tests.RdfXmlReaderTests

open System
open System.Text
open System.Text.Json
open System.Xml.Linq
open Expecto
open FsCheck
open Morphir.Knowledge
open Morphir.Knowledge.Rdf
open Morphir.TestSupport.Fixtures

module RdfXmlReaderMother =
    /// A document type with an external entity: the reader must refuse it before resolving anything.
    let externalEntity =
        String.concat
            "\n"
            [ """<?xml version="1.0"?>"""
              """<!DOCTYPE rdf:RDF [ <!ENTITY secret SYSTEM "file:///etc/passwd"> <!ENTITY remote SYSTEM "https://example.org/entity"> ]>"""
              """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:ex="https://example.org/">"""
              """  <rdf:Description rdf:about="https://example.org/s"><ex:p>&secret;&remote;</ex:p></rdf:Description>"""
              """</rdf:RDF>""" ]

    /// The rdf:Description on line 3 is never closed.
    let unclosed =
        String.concat
            "\n"
            [ """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:ex="https://example.org/">"""
              """  <rdf:Description rdf:about="https://example.org/s">"""
              """    <ex:p>value</ex:p>"""
              """</rdf:RDF>""" ]

    /// rdf:li is not allowed as an attribute; the offending element is on line 2.
    let liAttribute =
        String.concat
            "\n"
            [ """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">"""
              """  <rdf:Description rdf:about="https://example.org/s" rdf:li="x"/>"""
              """</rdf:RDF>""" ]

    /// An SVG whose root binds the prefixes that the embedded rdf:RDF element uses.
    let svg =
        """<svg xmlns="http://www.w3.org/2000/svg" xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:dcterms="http://purl.org/dc/terms/"><metadata><rdf:RDF><rdf:Description rdf:about="https://example.org/diagrams/checkout"><dcterms:title xml:lang="en">Checkout</dcterms:title></rdf:Description></rdf:RDF></metadata></svg>"""

let private canonicalText (dataset: RdfDataset) =
    match RdfCanonicalization.Canonicalize dataset with
    | Error error -> failtest (error.ToString())
    | Ok canonical ->
        match NQuads.Write canonical with
        | Ok output -> Encoding.UTF8.GetString output.Bytes
        | Error error -> failtest (error.ToString())

let private bytes (text: string) = ReadOnlyMemory(Encoding.UTF8.GetBytes text)

[<Tests>]
let w3cTests =
    use document = JsonDocument.Parse(ReadOnlyMemory(FixtureCatalog.ReadBytes FixtureCatalog.RdfXml.Corpus))
    let baseIri = document.RootElement.GetProperty("base").GetString()

    testList "W3C RDF/XML" [
        for test in document.RootElement.GetProperty("tests").EnumerateArray() do
            let name = test.GetProperty("name").GetString()
            let input = test.GetProperty("actionUtf8Base64").GetBytesFromBase64()
            let documentBase = Iri.Create(baseIri + test.GetProperty("action").GetString())

            match test.GetProperty("type").GetString() with
            | "eval" ->
                let expected = test.GetProperty("resultUtf8Base64").GetBytesFromBase64()
                testCase name <| fun () ->
                    let expectedDataset =
                        match NTriples.Read(ReadOnlyMemory expected) with
                        | Ok dataset -> dataset
                        | Error error -> failtest $"the expected N-Triples do not read: {error}"

                    match RdfXml.Read(ReadOnlyMemory input, documentBase) with
                    | Ok actual -> Expect.equal (canonicalText actual) (canonicalText expectedDataset) "the graph matches the expected N-Triples"
                    | Error error -> failtest (error.ToString())
            | _ ->
                testCase name <| fun () ->
                    match RdfXml.Read(ReadOnlyMemory input, documentBase) with
                    | Error _ -> ()
                    | Ok dataset -> failtest $"accepted a negative syntax test as {dataset.Quads.Length} statement(s)"
    ]

[<Tests>]
let tests =
    testList "RDF/XML reader" [
        testCase "a document type declaration is refused without resolving its entities" <| fun () ->
            match RdfXml.Read(bytes RdfXmlReaderMother.externalEntity) with
            | Error error ->
                Expect.equal error.Kind RdfXmlReadErrorKind.DocumentTypeDeclaration "the error names the document type"
                Expect.equal error.Kind.Code "rdfxml.doctype" "the code is stable"
                match error.Location with
                | ValueSome location -> Expect.equal location.Line 2 "the declaration is on line 2"
                | ValueNone -> failtest "the error has a location"
            | Ok _ -> failtest "a document type must be refused"

        testCase "a document type in UTF-16 input is refused as a document type" <| fun () ->
            let text = RdfXmlReaderMother.externalEntity.Replace("<?xml version=\"1.0\"?>", "<?xml version=\"1.0\" encoding=\"utf-16\"?>")
            let bytes = Array.append (UnicodeEncoding(false, true).GetPreamble()) (Encoding.Unicode.GetBytes text)
            match RdfXml.Read(ReadOnlyMemory bytes) with
            | Error error -> Expect.equal error.Kind RdfXmlReadErrorKind.DocumentTypeDeclaration "the document type is named"
            | Ok _ -> failtest "a document type must be refused"

        testCase "document type text inside content is not a document type" <| fun () ->
            let text = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><!-- <!DOCTYPE x> -->\n<rdf:Description></rdf:RDF>"
            match RdfXml.Read(bytes text) with
            | Error error -> Expect.equal error.Kind RdfXmlReadErrorKind.MalformedXml "the XML is malformed after the root starts"
            | Ok _ -> failtest "an unclosed element must be refused"

        testCase "malformed XML is an error with a line and a column" <| fun () ->
            match RdfXml.Read(bytes RdfXmlReaderMother.unclosed) with
            | Error error ->
                Expect.equal error.Kind RdfXmlReadErrorKind.MalformedXml "the XML is malformed"
                match error.Location with
                | ValueSome location ->
                    Expect.equal location.Line 4 "the mismatched end tag is on line 4"
                    Expect.isGreaterThan location.Column 0 "the column is known"
                | ValueNone -> failtest "the error has a location"
            | Ok _ -> failtest "an unclosed element must be refused"

        testCase "a grammar error names the element's line" <| fun () ->
            match RdfXml.Read(bytes RdfXmlReaderMother.liAttribute) with
            | Error error ->
                Expect.equal error.Kind RdfXmlReadErrorKind.InvalidSyntax "rdf:li cannot be an attribute"
                Expect.equal (error.Location |> ValueOption.map (fun location -> location.Line)) (ValueSome 2) "the element is on line 2"
            | Ok _ -> failtest "rdf:li as an attribute must be refused"

        testCase "an embedded element uses the namespace bindings of the enclosing document" <| fun () ->
            let svg = XDocument.Parse RdfXmlReaderMother.svg
            let rdf = svg.Root.Element(XName.Get("metadata", "http://www.w3.org/2000/svg")).Element(XName.Get("RDF", RdfNamespaces.Rdf))
            match RdfXml.ReadElement rdf with
            | Ok dataset ->
                let quad = dataset.Quads |> Seq.exactlyOne
                Expect.equal quad.Predicate (Iri.Create "http://purl.org/dc/terms/title") "the title predicate uses the inherited dcterms binding"
                match quad.Object with
                | RdfObject.Literal literal -> Expect.equal (literal.LexicalForm, literal.LanguageTag) ("Checkout", Some "en") "the literal keeps its language"
                | other -> failtest $"expected a literal, got {other}"
            | Error error -> failtest (error.ToString())

        testCase "deep nesting is refused instead of exhausting the stack" <| fun () ->
            let depth = 10_000
            let builder = StringBuilder("""<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns:p="https://example.org/">""")
            for _ in 1..depth do builder.Append("<rdf:Description><p:p>") |> ignore
            for _ in 1..depth do builder.Append("</p:p></rdf:Description>") |> ignore
            builder.Append "</rdf:RDF>" |> ignore

            match RdfXml.Read(bytes (builder.ToString())) with
            | Error error -> Expect.equal error.Kind RdfXmlReadErrorKind.InvalidSyntax "the nesting is refused"
            | Ok _ -> failtest "the nesting is too deep"

        testCase "empty rdf:ID and rdf:nodeID values are syntax errors" <| fun () ->
            for attribute in [ "rdf:ID"; "rdf:nodeID" ] do
                let text = $"""<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description {attribute}=""/></rdf:RDF>"""
                match RdfXml.Read(bytes text, Iri.Create "https://example.org/doc") with
                | Error error -> Expect.equal error.Kind RdfXmlReadErrorKind.InvalidSyntax $"an empty {attribute} is refused"
                | Ok _ -> failtest $"an empty {attribute} must be refused"

        testCase "a relative IRI without a base is an error" <| fun () ->
            let text = """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description rdf:about="relative"/></rdf:RDF>"""
            match RdfXml.Read(bytes text) with
            | Error error -> Expect.equal error.Kind RdfXmlReadErrorKind.InvalidIri "the reference cannot be resolved"
            | Ok _ -> failtest "a relative reference needs a base"
    ]

/// Generated default graphs: shared blank nodes, IRIs in several namespaces, and literals whose text
/// has markup, carriage returns and tabs.
module RdfXmlGenerators =
    let private iris =
        [ "https://example.org/vocab/name"; "http://purl.org/dc/terms/title"; "urn:x:item"; "https://example.org/a-1"
          "http://www.w3.org/ns/oa#hasTarget"; "https://example.org/diagrams/checkout#node=Billing" ]
        |> List.map Iri.Create

    let private predicates =
        [ "https://example.org/vocab/name"; "http://purl.org/dc/terms/created"; "http://www.w3.org/ns/oa#bodyValue"
          "https://morphir.finos.org/ns/diagram#revision"; "urn:x:ns:value" ]
        |> List.map Iri.Create

    let private text = Gen.elements [ ""; "plain"; "a < b & c > d"; "]]>"; "line\r\nnext\ttab"; "  spaced  "; "sûr 🌃" ]

    let private literal =
        Gen.oneof [
            text |> Gen.map RdfLiteral.CreateString
            Gen.map2 (fun value language -> RdfLiteral.CreateLanguageTagged(value, language)) text (Gen.elements [ "en"; "fr-ca" ])
            Gen.map2
                (fun value datatype -> RdfLiteral.CreateTyped(value, Iri.Create datatype))
                text
                (Gen.elements [ "http://www.w3.org/2001/XMLSchema#date"; "https://example.org/types/code" ])
        ]

    let graph =
        gen {
            let blanks = List.init 4 (fun _ -> BlankNodeId.New())
            let subject = Gen.oneof [ Gen.elements iris |> Gen.map RdfSubject.Iri; Gen.elements blanks |> Gen.map RdfSubject.BlankNode ]

            let object =
                Gen.oneof [
                    Gen.elements iris |> Gen.map RdfObject.Iri
                    Gen.elements blanks |> Gen.map RdfObject.BlankNode
                    literal |> Gen.map RdfObject.Literal
                ]

            let! triples =
                Gen.listOf (Gen.map3 (fun s p o -> { Subject = s; Predicate = p; Object = o }) subject (Gen.elements predicates) object)

            return triples |> List.fold (fun (dataset: RdfDataset) triple -> dataset.Add(Quad.FromTriple(GraphLocation.DefaultGraph, triple))) RdfDataset.Empty
        }

[<Tests>]
let roundTrip =
    testList "RDF/XML round trip" [
        testProperty "a written default graph reads back isomorphic"
        <| Prop.forAll (Arb.fromGen RdfXmlGenerators.graph) (fun dataset ->
            match RdfXml.Write dataset with
            | Error error -> failtest (error.ToString())
            | Ok output ->
                match RdfXml.Read output.Bytes with
                | Ok back -> canonicalText back = canonicalText dataset
                | Error error -> failtest (error.ToString()))
    ]
