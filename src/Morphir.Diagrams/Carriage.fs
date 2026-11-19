// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text
open System.Xml
open System.Xml.Linq
open Morphir.Knowledge
open Morphir.Knowledge.Rdf
open Morphir.Svg

/// One comment, link or other statement about a diagram element, as the player shows it.
type internal AnnotationItem =
    { Kind: string
      Id: string
      Author: string
      Time: string
      Body: string
      Predicate: string
      Target: string
      Value: string
      IsWeb: bool }

/// Builds the metadata element of a rendered diagram from the caller's dataset.
module internal Carriage =
    let private error code message =
        Diagnostic(code, DiagnosticSeverity.Error, message, None)

    let private warning code message =
        Diagnostic(code, DiagnosticSeverity.Warning, message, None)

    let private datasetName = SvgName.Qualified("md", "dataset", RdfNamespaces.MorphirDiagram)

    /// The element IRIs that the dataset names under the document: IRIs in statements, and fragment
    /// selectors of specific resources whose source is the document.
    let private namedElements (document: Iri) (dataset: RdfDataset) =
        let prefix = document.Value + "#"
        let found = SortedSet<string>(StringComparer.Ordinal)
        let quads = dataset.Quads

        let add (iri: Iri) =
            if iri.Value.StartsWith(prefix, StringComparison.Ordinal) then found.Add iri.Value |> ignore

        for quad in quads do
            match quad.Subject with
            | RdfSubject.Iri iri -> add iri
            | _ -> ()

            match quad.Object with
            | RdfObject.Iri iri -> add iri
            | _ -> ()

        let hasSource = Iri.Create(RdfNamespaces.WebAnnotation + "hasSource")
        let hasSelector = Iri.Create(RdfNamespaces.WebAnnotation + "hasSelector")
        let value = Iri.Create(RdfNamespaces.Rdf + "value")

        let bySubject = quads |> Seq.groupBy (fun quad -> quad.Subject) |> dict

        let objects subject (predicate: Iri) =
            match bySubject.TryGetValue subject with
            | true, statements -> statements |> Seq.filter (fun quad -> quad.Predicate = predicate) |> Seq.map (fun quad -> quad.Object)
            | false, _ -> Seq.empty

        for quad in quads do
            if quad.Predicate = hasSource && quad.Object = RdfObject.Iri document then
                for selector in objects quad.Subject hasSelector do
                    let selectorSubject =
                        match selector with
                        | RdfObject.BlankNode node -> Some(RdfSubject.BlankNode node)
                        | RdfObject.Iri iri -> Some(RdfSubject.Iri iri)
                        | RdfObject.Literal _ -> None

                    for subject in Option.toList selectorSubject do
                        for fragment in objects subject value do
                            match fragment with
                            | RdfObject.Literal literal -> found.Add(prefix + literal.LexicalForm) |> ignore
                            | _ -> ()

        found

    /// Converts an element written by an XML writer into scene nodes with qualified names.
    let rec private node (element: XElement) : SvgNode =
        let qualified (name: XName) =
            if name.Namespace = XNamespace.Xml then
                SvgName.Qualified("xml", name.LocalName, name.NamespaceName)
            else
                match element.GetPrefixOfNamespace name.Namespace with
                | null -> invalidOp $"The namespace {name.NamespaceName} has no prefix."
                | prefix -> SvgName.Qualified(prefix, name.LocalName, name.NamespaceName)

        let attributes =
            element.Attributes()
            |> Seq.filter (fun attribute -> not attribute.IsNamespaceDeclaration)
            |> Seq.map (fun attribute ->
                if attribute.Name.Namespace = XNamespace.None then
                    SvgAttribute.Create(attribute.Name.LocalName, attribute.Value)
                else
                    SvgAttribute.Create(qualified attribute.Name, attribute.Value))
            |> Seq.toArray

        let children =
            element.Nodes()
            |> Seq.choose (fun child ->
                match child with
                | :? XElement as inner -> Some(node inner)
                | :? XText as text -> Some(SvgNode.CreateText text.Value)
                | _ -> None)
            |> Seq.toArray

        SvgNode.CreateElement(qualified element.Name, attributes, children)

    let private notEmbeddable (embedding: MetadataEmbedding) (reason: string) =
        Error [ error "metadata/not-embeddable" $"The dataset cannot be embedded as {embedding.WireValue}: {reason}" ]

    /// Statements as text in an md:dataset element, when XML can hold every character.
    let private textDataset embedding (mediaType: string) (bytes: ReadOnlyMemory<byte>) =
        let text = Encoding.UTF8.GetString bytes.Span

        try
            XmlConvert.VerifyXmlChars text |> ignore
            Ok [ SvgNode.CreateElement(datasetName, [| SvgAttribute.Create("media-type", mediaType) |], [| SvgNode.CreateText text |]) ]
        with :? XmlException ->
            notEmbeddable embedding "a literal has a character that XML 1.0 forbids."

    /// The metadata content and its warnings, or the errors that prevent rendering.
    let prepare (diagram: Diagram) (options: SvgRenderOptions) : Result<SvgNode list option * Diagnostic list, Diagnostic list> =
        match options.MetadataRequest, Option.ofObj options.Document with
        | None, _ -> Ok(None, [])
        | Some _, None ->
            Error [ error "metadata/missing-document" "Embedding a dataset needs the diagram's document identity; call WithDocument." ]
        | Some(dataset, embedding, handling), Some document ->
            let iri = document.Iri

            if iri.Value.Contains '#' then
                Error [ error "metadata/document-fragment" $"The document IRI <{iri.Value}> has a fragment; element references must own it." ]
            else
                let holdsNamedGraphs = embedding = MetadataEmbedding.NQuads

                // N-Quads holds named graphs but not empty ones, so those are the names to report.
                let names =
                    if holdsNamedGraphs then
                        dataset.Names |> Seq.filter (fun name -> (dataset.FindGraph(GraphLocation.NamedGraph name) |> Option.exists (fun graph -> graph.IsEmpty))) |> ImmutableArray.CreateRange
                    else
                        dataset.Names

                let rebuilt () =
                    dataset.Quads |> Seq.fold (fun (result: RdfDataset) quad -> result.Add quad) RdfDataset.Empty

                let selected =
                    if names.IsEmpty then Ok(dataset, [])
                    elif holdsNamedGraphs && handling = NamedGraphHandling.Reject then
                        let listed = names |> Seq.map (fun name -> name.ToString()) |> String.concat ", "
                        notEmbeddable embedding $"N-Quads cannot hold the empty named graph(s) {listed}."
                    elif holdsNamedGraphs then
                        Ok(rebuilt (), [ for name in names -> warning "metadata/dropped-graph" $"The empty named graph {name} was dropped from the N-Quads metadata." ])
                    elif handling = NamedGraphHandling.Reject then
                        let listed = names |> Seq.map (fun name -> name.ToString()) |> String.concat ", "
                        Error [ error "metadata/named-graphs" $"{embedding.WireValue} embedding holds only the default graph; the dataset has the named graph(s) {listed}." ]
                    else
                        let defaultOnly =
                            dataset.DefaultGraph.Triples
                            |> Seq.fold (fun (result: RdfDataset) triple -> result.Add(Quad.FromTriple(GraphLocation.DefaultGraph, triple))) RdfDataset.Empty

                        Ok(defaultOnly, [ for name in names -> warning "metadata/dropped-graph" $"The named graph {name} was dropped from the {embedding.WireValue} metadata." ])

                match selected with
                | Error errors -> Error errors
                | Ok(embedded, dropped) ->
                    match RdfCanonicalization.Canonicalize(embedded, CanonicalizationOptions.Default) with
                    | Error failure -> Error [ error "metadata/canonicalization" (failure.ToString()) ]
                    | Ok canonical ->
                        let elements = DiagramElementRef.Enumerate diagram |> HashSet

                        let unknown =
                            [ for named in namedElements iri embedded do
                                  match Iri.Parse named |> Result.toOption |> Option.map (fun value -> DiagramElementRef.ParseIri(iri, value)) with
                                  | Some(Ok reference) when elements.Contains reference -> ()
                                  | _ -> yield warning "metadata/unknown-element" $"The metadata refers to <{named}>, which is not an element of this diagram." ]

                        let content =
                            match embedding with
                            | MetadataEmbedding.RdfXml ->
                                let document = XDocument()

                                let written =
                                    use writer = document.CreateWriter()
                                    RdfXml.WriteElement(writer, canonical, NamedGraphHandling.Reject)

                                match written with
                                | Error failure -> notEmbeddable embedding (failure.ToString())
                                | Ok _ ->
                                    match document.Root with
                                    | null -> Ok []
                                    | root -> Ok [ node root ]
                            | MetadataEmbedding.NQuads ->
                                match NQuads.Write canonical with
                                | Ok output -> textDataset embedding "application/n-quads" (ReadOnlyMemory output.Bytes)
                                | Error failure -> notEmbeddable embedding (failure.ToString())
                            | MetadataEmbedding.NTriples ->
                                match NTriples.Write(canonical, NamedGraphHandling.Reject) with
                                | Ok output -> textDataset embedding "application/n-triples" output.Bytes
                                | Error failure -> notEmbeddable embedding (failure.ToString())

                        content |> Result.map (fun nodes -> Some nodes, dropped @ unknown)

    let private isWeb (value: string) =
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

    /// The player's view of the dataset: for each element, in enumeration order, its comments, links
    /// and other statements. JavaScript never sees RDF, only these plain values.
    let project (diagram: Diagram) (options: SvgRenderOptions) : (string * AnnotationItem list) list =
        match options.MetadataRequest, Option.ofObj options.Document with
        | Some(dataset, _, _), Some document when not (document.Iri.Value.Contains '#') ->
            let documentIri = document.Iri
            let quads = dataset.Quads
            let bySubject = quads |> Seq.groupBy (fun quad -> quad.Subject) |> dict
            let elements = DiagramElementRef.Enumerate diagram
            let known = HashSet elements
            let iri local (ns: string) = Iri.Create(ns + local)
            let oa local = iri local RdfNamespaces.WebAnnotation
            let md local = iri local RdfNamespaces.MorphirDiagram

            let objects subject (predicate: Iri) =
                match bySubject.TryGetValue subject with
                | true, statements -> statements |> Seq.filter (fun quad -> quad.Predicate = predicate) |> Seq.map (fun quad -> quad.Object) |> Seq.toList
                | false, _ -> []

            let asSubject object =
                match object with
                | RdfObject.Iri value -> Some(RdfSubject.Iri value)
                | RdfObject.BlankNode node -> Some(RdfSubject.BlankNode node)
                | RdfObject.Literal _ -> None

            let text object =
                match object with
                | RdfObject.Literal literal -> literal.LexicalForm
                | RdfObject.Iri value -> value.Value
                | RdfObject.BlankNode _ -> ""

            let first subject predicate = objects subject predicate |> List.tryHead |> Option.map text |> Option.defaultValue ""

            /// The elements of this diagram that a specific resource selects.
            let selected object =
                match asSubject object with
                | Some resource when objects resource (oa "hasSource") |> List.contains (RdfObject.Iri documentIri) ->
                    [ for selector in objects resource (oa "hasSelector") do
                          for subject in Option.toList (asSubject selector) do
                              for value in objects subject (iri "value" RdfNamespaces.Rdf) do
                                  match DiagramElementRef.Parse(text value) with
                                  | Ok reference when known.Contains reference -> yield reference
                                  | _ -> () ]
                | _ -> []

            /// The IRI a link target resolves to: an IRI itself, or the source of a specific resource.
            let targetIri object =
                match asSubject object with
                | Some(RdfSubject.Iri value) when objects (RdfSubject.Iri value) (oa "hasSource") |> List.isEmpty -> value.Value
                | Some resource ->
                    let source = first resource (oa "hasSource")
                    let fragments = objects resource (oa "hasSelector") |> List.collect (fun selector -> asSubject selector |> Option.toList |> List.collect (fun subject -> objects subject (iri "value" RdfNamespaces.Rdf)))

                    match objects resource (oa "hasSelector") |> List.length, fragments with
                    | 1, [ fragment ] when not (source.Contains '#') && (DiagramElementRef.Parse(text fragment) |> Result.isOk) -> source + "#" + text fragment
                    | _ -> source
                | None -> ""

            let items = ResizeArray<DiagramElementRef * AnnotationItem>()
            let direct = HashSet<string * string * string>()
            let blank = { Kind = ""; Id = ""; Author = ""; Time = ""; Body = ""; Predicate = ""; Target = ""; Value = ""; IsWeb = false }

            for KeyValue(subject, _) in bySubject do
                let id =
                    match subject with
                    | RdfSubject.Iri value -> value.Value
                    | RdfSubject.BlankNode _ -> ""

                match objects subject (oa "bodyValue"), objects subject (oa "hasTarget") with
                | body :: _, targets when not targets.IsEmpty ->
                    for target in targets do
                        for reference in selected target do
                            items.Add(
                                reference,
                                { blank with
                                    Kind = "comment"
                                    Id = id
                                    Author = first subject (iri "creator" RdfNamespaces.DublinCoreTerms)
                                    Time = first subject (iri "created" RdfNamespaces.DublinCoreTerms)
                                    Body = text body }
                            )
                | _ -> ()

                match objects subject (md "linkSource"), objects subject (md "linkPredicate"), objects subject (md "linkTarget") with
                | source :: _, (RdfObject.Iri predicate) :: _, target :: _ ->
                    let resolved = targetIri target

                    for reference in selected source do
                        let elementIri = documentIri.Value + "#" + reference.ToString()
                        direct.Add((elementIri, predicate.Value, resolved)) |> ignore

                        items.Add(
                            reference,
                            { blank with Kind = "link"; Id = id; Predicate = predicate.Value; Target = resolved; IsWeb = isWeb resolved }
                        )
                | _ -> ()

            let prefix = documentIri.Value + "#"

            for quad in quads do
                match quad.Subject with
                | RdfSubject.Iri subject when subject.Value.StartsWith(prefix, StringComparison.Ordinal) ->
                    match DiagramElementRef.ParseIri(documentIri, subject) with
                    | Ok reference when known.Contains reference ->
                        let value = text quad.Object
                        let isIri = match quad.Object with RdfObject.Iri _ -> true | _ -> false

                        if not (direct.Contains((subject.Value, quad.Predicate.Value, value))) then
                            items.Add(
                                reference,
                                { blank with Kind = "property"; Predicate = quad.Predicate.Value; Value = value; IsWeb = isIri && isWeb value }
                            )
                    | _ -> ()
                | _ -> ()

            let kindOrder kind = match kind with "comment" -> 0 | "link" -> 1 | _ -> 2

            [ for reference in elements do
                  let own =
                      items
                      |> Seq.filter (fun (owner, _) -> owner = reference)
                      |> Seq.map snd
                      |> Seq.distinct
                      |> Seq.sortBy (fun item -> kindOrder item.Kind, item.Id, item.Predicate, item.Target, item.Value, item.Time, item.Body)
                      |> Seq.toList

                  if not own.IsEmpty then yield reference.ToString(), own ]
        | _ -> []

/// Why metadata could not be read from an SVG.
[<RequireQualifiedAccess>]
type DiagramMetadataError =
    /// The SVG itself could not be read.
    | Svg of SvgReadError
    /// The embedded RDF/XML is not valid.
    | RdfXml of RdfXmlReadError
    /// The embedded N-Quads or N-Triples text is not valid.
    | Statements of NQuadsReadError
    /// An md:dataset element names a media type that the reader does not know.
    | UnsupportedMediaType of mediaType: string

    /// For C# callers: every case needs a function.
    member this.Match
        (
            svg: Func<SvgReadError, 'T>,
            rdfXml: Func<RdfXmlReadError, 'T>,
            statements: Func<NQuadsReadError, 'T>,
            unsupportedMediaType: Func<string, 'T>
        ) =
        match this with
        | DiagramMetadataError.Svg error -> svg.Invoke error
        | DiagramMetadataError.RdfXml error -> rdfXml.Invoke error
        | DiagramMetadataError.Statements error -> statements.Invoke error
        | DiagramMetadataError.UnsupportedMediaType mediaType -> unsupportedMediaType.Invoke mediaType

    override this.ToString() =
        match this with
        | DiagramMetadataError.Svg error -> error.ToString()
        | DiagramMetadataError.RdfXml error -> error.ToString()
        | DiagramMetadataError.Statements error -> error.ToString()
        | DiagramMetadataError.UnsupportedMediaType mediaType -> $"The metadata media type '{mediaType}' is not supported."

/// Reads the dataset that a rendered diagram carries in its SVG metadata.
[<AbstractClass; Sealed>]
type DiagramMetadata private () =
    /// Reads the embedded dataset: RDF/XML in rdf:RDF elements, and N-Quads or N-Triples in md:dataset
    /// elements. An SVG without metadata gives an empty dataset. The SVG is read with DTDs prohibited
    /// and no resolver. For F# callers.
    static member Read(svg: ReadOnlyMemory<byte>) : Result<RdfDataset, DiagramMetadataError> =
        match SvgMetadataReader.Read svg with
        | Error error -> Error(DiagramMetadataError.Svg error)
        | Ok SvgMetadataResult.NotFound -> Ok RdfDataset.Empty
        | Ok(SvgMetadataResult.Found content) ->
            let read (element: XElement) =
                if element.Name = XName.Get("RDF", RdfNamespaces.Rdf) then
                    RdfXml.ReadElement element |> Result.mapError DiagramMetadataError.RdfXml |> Some
                elif element.Name = XName.Get("dataset", RdfNamespaces.MorphirDiagram) then
                    let bytes = ReadOnlyMemory(Encoding.UTF8.GetBytes element.Value)

                    match element.Attribute(XName.Get "media-type") |> Option.ofObj |> Option.map (fun attribute -> attribute.Value) with
                    | Some "application/n-quads" -> NQuads.Read bytes |> Result.mapError DiagramMetadataError.Statements |> Some
                    | Some "application/n-triples" -> NTriples.Read bytes |> Result.mapError DiagramMetadataError.Statements |> Some
                    | other -> Some(Error(DiagramMetadataError.UnsupportedMediaType(defaultArg other "")))
                else
                    None

            content.Elements
            |> Seq.choose read
            |> Seq.fold
                (fun (result: Result<RdfDataset, DiagramMetadataError>) next ->
                    match result, next with
                    | Ok dataset, Ok more -> Ok(dataset.Merge more)
                    | Error error, _
                    | _, Error error -> Error error)
                (Ok RdfDataset.Empty)

    /// Reads the embedded dataset. For C# callers.
    static member TryRead
        (
            svg: ReadOnlyMemory<byte>,
            [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramMetadataError>
        ) =
        match DiagramMetadata.Read svg with
        | Ok value ->
            dataset <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            dataset <- Unchecked.defaultof<_>
            error <- failure
            false
