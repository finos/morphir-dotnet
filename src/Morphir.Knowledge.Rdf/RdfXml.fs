// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Xml
open System.Xml.Linq
open Morphir.Knowledge

module internal RdfXmlNames =
    let private isSurrogatePair (text: string) index =
        index + 1 < text.Length && Char.IsHighSurrogate text[index] && Char.IsLowSurrogate text[index + 1]

    /// Splits an IRI at its longest suffix that is an XML NCName, as namespace and local name.
    let split (iri: string) =
        let mutable start = iri.Length

        let mutable scanning = true
        while scanning && start > 0 do
            if start >= 2 && Char.IsLowSurrogate iri[start - 1] && Char.IsHighSurrogate iri[start - 2] then
                start <- start - 2
            elif XmlConvert.IsNCNameChar iri[start - 1] then
                start <- start - 1
            else
                scanning <- false

        let mutable local = start
        while local < iri.Length && not (XmlConvert.IsStartNCNameChar iri[local] || isSurrogatePair iri local) do
            local <- local + (if isSurrogatePair iri local then 2 else 1)

        if local >= iri.Length || local = 0 then None
        else Some(iri.Substring(0, local), iri.Substring local)

    /// Whether XML 1.0 can hold every character of the text.
    let xmlText (text: string) =
        try
            XmlConvert.VerifyXmlChars text |> ignore
            true
        with :? XmlException ->
            false

module internal RdfXmlWriter =
    type Plan =
        { Triples: Triple list
          Prefixes: (string * string) list
          Label: BlankNodeId -> string }

    let private key label (triple: Triple) =
        let subject =
            match triple.Subject with
            | RdfSubject.Iri value -> "<" + value.Value
            | RdfSubject.BlankNode node -> "_:" + label node

        let objectKey =
            match triple.Object with
            | RdfObject.Iri value -> "<" + value.Value
            | RdfObject.BlankNode node -> "_:" + label node
            | RdfObject.Literal value -> "\"" + value.ToString()

        subject, triple.Predicate.Value, objectKey

    /// Checks everything the format cannot hold, so that an error emits nothing.
    let plan (dataset: RdfDataset) (label: BlankNodeId -> string) (handling: NamedGraphHandling) =
        match DefaultGraphOnly.split dataset handling with
        | Error error -> Error error
        | Ok(graph, omitted) ->
            let triples = graph.DefaultGraph.Triples |> Seq.toList

            let invalidPredicate =
                triples |> List.tryFind (fun triple -> (RdfXmlNames.split triple.Predicate.Value).IsNone)

            let invalidLiteral =
                triples
                |> List.tryPick (fun triple ->
                    match triple.Object with
                    | RdfObject.Literal value when not (RdfXmlNames.xmlText value.LexicalForm) -> Some value
                    | _ -> None)

            match invalidPredicate, invalidLiteral with
            | Some triple, _ -> Error(RdfWriteError.PredicateWithoutLocalName triple.Predicate)
            | None, Some literal -> Error(RdfWriteError.NotXmlCharacters literal)
            | None, None ->
                let ordered =
                    triples
                    |> List.sortWith (fun left right ->
                        let (ls, lp, lo), (rs, rp, ro) = key label left, key label right
                        let bySubject = CanonicalNQuads.compare ls rs
                        if bySubject <> 0 then bySubject
                        else
                            let byPredicate = CanonicalNQuads.compare lp rp
                            if byPredicate <> 0 then byPredicate else CanonicalNQuads.compare lo ro)

                let used =
                    ordered
                    |> List.map (fun triple -> fst (RdfXmlNames.split triple.Predicate.Value).Value)
                    |> List.distinct

                let wellKnown =
                    RdfNamespaces.WellKnown
                    |> List.filter (fun (prefix, ns) -> prefix = "rdf" || List.contains ns used)

                let generated =
                    used
                    |> List.filter (fun ns -> not (RdfNamespaces.WellKnown |> List.exists (fun (_, known) -> known = ns)))
                    |> List.sortWith CanonicalNQuads.compare
                    |> List.mapi (fun index ns -> "ns" + index.ToString(CultureInfo.InvariantCulture), ns)

                Ok({ Triples = ordered; Prefixes = wellKnown @ generated; Label = label }, omitted)

    /// Writes the rdf:RDF element of a plan into a caller's writer.
    let element (writer: XmlWriter) (plan: Plan) =
        let rdf = RdfNamespaces.Rdf
        let prefixOf ns = plan.Prefixes |> List.find (fun (_, known) -> known = ns) |> fst
        writer.WriteStartElement("rdf", "RDF", rdf)

        for prefix, ns in plan.Prefixes do
            writer.WriteAttributeString("xmlns", prefix, "http://www.w3.org/2000/xmlns/", ns)

        let subjectKey (triple: Triple) = triple.Subject

        for subject, triples in plan.Triples |> List.groupBy subjectKey do
            writer.WriteStartElement("rdf", "Description", rdf)

            match subject with
            | RdfSubject.Iri value -> writer.WriteAttributeString("rdf", "about", rdf, value.Value)
            | RdfSubject.BlankNode node -> writer.WriteAttributeString("rdf", "nodeID", rdf, plan.Label node)

            for triple in triples do
                let ns, local = (RdfXmlNames.split triple.Predicate.Value).Value
                writer.WriteStartElement(prefixOf ns, local, ns)

                match triple.Object with
                | RdfObject.Iri value -> writer.WriteAttributeString("rdf", "resource", rdf, value.Value)
                | RdfObject.BlankNode node -> writer.WriteAttributeString("rdf", "nodeID", rdf, plan.Label node)
                | RdfObject.Literal value ->
                    match value.LanguageTag with
                    | Some language -> writer.WriteAttributeString("xml", "lang", null, language)
                    | None when value.Datatype.Value <> RdfNamespaces.Xsd + "string" ->
                        writer.WriteAttributeString("rdf", "datatype", rdf, value.Datatype.Value)
                    | None -> ()

                    writer.WriteString value.LexicalForm

                writer.WriteEndElement()

            writer.WriteEndElement()

        writer.WriteEndElement()

    let document (plan: Plan) =
        let settings =
            XmlWriterSettings(
                Encoding = UTF8Encoding false,
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Entitize
            )

        use stream = new MemoryStream()

        do
            use writer = XmlWriter.Create(stream, settings)
            writer.WriteStartDocument()
            element writer plan
            writer.WriteEndDocument()

        stream.WriteByte(byte '\n')
        stream.ToArray()

    /// Labels blank nodes b0, b1 and so on in the graph's own order, which depends on their identities.
    /// For labels that do not, write a canonical dataset.
    let firstOccurrence (dataset: RdfDataset) =
        let labels = Dictionary<BlankNodeId, string>()

        for triple in dataset.DefaultGraph.Triples do
            for node in
                [ match triple.Subject with
                  | RdfSubject.BlankNode node -> yield node
                  | _ -> ()
                  match triple.Object with
                  | RdfObject.BlankNode node -> yield node
                  | _ -> () ] do
                if not (labels.ContainsKey node) then
                    labels.Add(node, "b" + labels.Count.ToString(CultureInfo.InvariantCulture))

        fun node -> labels[node]

/// An RDF 1.1 XML Syntax reader, and a writer for the default graph.
[<AbstractClass; Sealed>]
type RdfXml private () =
    static let readTree (baseIri: Iri option) (element: XElement) =
        try
            Ok(RdfXmlGrammar.read (baseIri |> Option.map (fun value -> value.Value)) element)
        with RdfXmlFailure error ->
            Error error

    static let readBytes (source: ReadOnlyMemory<byte>) (baseIri: Iri option) =
        try
            let document = RdfXmlInput.load source

            match document.Root with
            | null -> Error { Kind = RdfXmlReadErrorKind.MalformedXml; Message = "The input has no root element."; Location = ValueNone }
            | root -> readTree baseIri root
        with RdfXmlFailure error ->
            Error error

    static let tryResult (result: Result<RdfDataset, RdfXmlReadError>) (dataset: byref<RdfDataset>) (error: byref<RdfXmlReadError>) =
        match result with
        | Ok value ->
            dataset <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            dataset <- Unchecked.defaultof<_>
            error <- failure
            false

    static let output (plan: Result<RdfXmlWriter.Plan * ImmutableArray<GraphName>, RdfWriteError>) =
        plan |> Result.map (fun (plan, omitted) -> RdfOutput(RdfXmlWriter.document plan, omitted))

    static let into (writer: XmlWriter) (plan: Result<RdfXmlWriter.Plan * ImmutableArray<GraphName>, RdfWriteError>) =
        ArgumentNullException.ThrowIfNull writer

        plan
        |> Result.map (fun (plan, omitted) ->
            RdfXmlWriter.element writer plan
            omitted)

    static let checkedPlan (dataset: RdfDataset) label (handling: NamedGraphHandling) =
        ArgumentNullException.ThrowIfNull dataset
        ArgumentNullException.ThrowIfNull handling
        RdfXmlWriter.plan dataset label handling

    /// Reads UTF-8 RDF/XML into the default graph, resolving relative references against the base IRI
    /// and any xml:base. Document type declarations are refused, and no external resource is read.
    /// For F# callers.
    static member Read(source: ReadOnlyMemory<byte>, baseIri: Iri) : Result<RdfDataset, RdfXmlReadError> =
        ArgumentNullException.ThrowIfNull baseIri
        readBytes source (Some baseIri)

    /// Reads UTF-8 RDF/XML; relative references need an xml:base. For F# callers.
    static member Read(source: ReadOnlyMemory<byte>) : Result<RdfDataset, RdfXmlReadError> = readBytes source None

    /// Reads an rdf:RDF element, or a single node element, already parsed from a larger document.
    /// Namespace bindings, xml:base and xml:lang are inherited from its ancestors. The caller's
    /// parser settings decide how the tree was read, and errors have locations only when the tree
    /// has line information. For F# callers.
    static member ReadElement(element: XElement, baseIri: Iri) : Result<RdfDataset, RdfXmlReadError> =
        ArgumentNullException.ThrowIfNull element
        ArgumentNullException.ThrowIfNull baseIri
        readTree (Some baseIri) element

    /// Reads an rdf:RDF element from a larger document; relative references need an xml:base. For F# callers.
    static member ReadElement(element: XElement) : Result<RdfDataset, RdfXmlReadError> =
        ArgumentNullException.ThrowIfNull element
        readTree None element

    /// Reads UTF-8 RDF/XML with a base IRI. For C# callers.
    static member TryRead(
        source: ReadOnlyMemory<byte>,
        baseIri: Iri,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfXmlReadError>
    ) =
        tryResult (RdfXml.Read(source, baseIri)) &dataset &error

    /// Reads UTF-8 RDF/XML. For C# callers.
    static member TryRead(
        source: ReadOnlyMemory<byte>,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfXmlReadError>
    ) =
        tryResult (RdfXml.Read source) &dataset &error

    /// Reads an rdf:RDF element from a larger document with a base IRI. For C# callers.
    static member TryReadElement(
        element: XElement,
        baseIri: Iri,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfXmlReadError>
    ) =
        tryResult (RdfXml.ReadElement(element, baseIri)) &dataset &error

    /// Reads an rdf:RDF element from a larger document. For C# callers.
    static member TryReadElement(
        element: XElement,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfXmlReadError>
    ) =
        tryResult (RdfXml.ReadElement element) &dataset &error


    /// Writes the default graph as a UTF-8 RDF/XML document, one rdf:Description per subject.
    /// Named graphs follow the policy. Nothing is written when the result is an error. For F# callers.
    static member Write(dataset: RdfDataset, namedGraphs: NamedGraphHandling) : Result<RdfOutput, RdfWriteError> =
        checkedPlan dataset (RdfXmlWriter.firstOccurrence dataset) namedGraphs |> output

    /// Writes the default graph as RDF/XML, and fails if the dataset has named graphs. For F# callers.
    static member Write(dataset: RdfDataset) = RdfXml.Write(dataset, NamedGraphHandling.Reject)

    /// Writes the default graph as canonical RDF/XML: canonical rdf:nodeID labels and sorted
    /// statements, so that isomorphic graphs write identical bytes. For F# callers.
    static member Write(canonical: CanonicalDataset, namedGraphs: NamedGraphHandling) : Result<RdfOutput, RdfWriteError> =
        ArgumentNullException.ThrowIfNull canonical
        checkedPlan canonical.Dataset canonical.LabelOf namedGraphs |> output

    /// Writes the rdf:RDF element into a caller's writer, for embedding in another XML document, and
    /// returns the omitted graphs. Nothing is written when the result is an error. For F# callers.
    static member WriteElement(writer: XmlWriter, dataset: RdfDataset, namedGraphs: NamedGraphHandling) =
        checkedPlan dataset (RdfXmlWriter.firstOccurrence dataset) namedGraphs |> into writer

    /// Writes a canonical rdf:RDF element into a caller's writer. For F# callers.
    static member WriteElement(writer: XmlWriter, canonical: CanonicalDataset, namedGraphs: NamedGraphHandling) =
        ArgumentNullException.ThrowIfNull canonical
        checkedPlan canonical.Dataset canonical.LabelOf namedGraphs |> into writer

    /// Writes the default graph as an RDF/XML document under the named-graph policy. For C# callers.
    static member TryWrite(
        dataset: RdfDataset,
        namedGraphs: NamedGraphHandling,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match RdfXml.Write(dataset, namedGraphs) with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes the default graph as an RDF/XML document, and fails if the dataset has named graphs.
    /// For C# callers.
    static member TryWrite(
        dataset: RdfDataset,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        RdfXml.TryWrite(dataset, NamedGraphHandling.Reject, &output, &error)

    /// Writes canonical RDF/XML under the named-graph policy. For C# callers.
    static member TryWrite(
        canonical: CanonicalDataset,
        namedGraphs: NamedGraphHandling,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match RdfXml.Write(canonical, namedGraphs) with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes the rdf:RDF element into a caller's writer. For C# callers.
    static member TryWriteElement(
        writer: XmlWriter,
        dataset: RdfDataset,
        namedGraphs: NamedGraphHandling,
        [<Out>] omittedGraphs: byref<ImmutableArray<GraphName>>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match RdfXml.WriteElement(writer, dataset, namedGraphs) with
        | Ok omitted ->
            omittedGraphs <- omitted
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            omittedGraphs <- ImmutableArray.Empty
            error <- failure
            false

    /// Writes a canonical rdf:RDF element into a caller's writer. For C# callers.
    static member TryWriteElement(
        writer: XmlWriter,
        canonical: CanonicalDataset,
        namedGraphs: NamedGraphHandling,
        [<Out>] omittedGraphs: byref<ImmutableArray<GraphName>>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match RdfXml.WriteElement(writer, canonical, namedGraphs) with
        | Ok omitted ->
            omittedGraphs <- omitted
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            omittedGraphs <- ImmutableArray.Empty
            error <- failure
            false
