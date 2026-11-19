// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

/// Namespace IRIs that RDF/XML output binds to fixed prefixes.
[<AbstractClass; Sealed>]
type RdfNamespaces private () =
    /// RDF, bound to the prefix rdf.
    static member Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    /// RDF Schema, bound to the prefix rdfs.
    static member Rdfs = "http://www.w3.org/2000/01/rdf-schema#"

    /// XML Schema datatypes, bound to the prefix xsd.
    static member Xsd = "http://www.w3.org/2001/XMLSchema#"

    /// The W3C Web Annotation vocabulary, bound to the prefix oa.
    static member WebAnnotation = "http://www.w3.org/ns/oa#"

    /// DCMI Metadata Terms, bound to the prefix dcterms.
    static member DublinCoreTerms = "http://purl.org/dc/terms/"

    /// The Morphir diagram vocabulary, bound to the prefix md. Proposed; the project owners have
    /// not agreed the final IRI yet, so this is the only place that names it.
    static member MorphirDiagram = "https://morphir.finos.org/ns/diagram#"

    /// The fixed prefixes, in the order that RDF/XML output declares them.
    static member internal WellKnown =
        [ "rdf", RdfNamespaces.Rdf
          "rdfs", RdfNamespaces.Rdfs
          "xsd", RdfNamespaces.Xsd
          "oa", RdfNamespaces.WebAnnotation
          "dcterms", RdfNamespaces.DublinCoreTerms
          "md", RdfNamespaces.MorphirDiagram ]
