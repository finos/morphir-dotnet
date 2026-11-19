// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.Linq
open System.Runtime.InteropServices
open System.Text
open Morphir.Knowledge
open Morphir.Knowledge.Rdf

/// An endpoint of a diagram link: elements of a diagram, ranges of a text, or any resource.
/// Equality compares versions, elements and ranges.
[<RequireQualifiedAccess; CustomEquality; NoComparison>]
type LinkEndpoint =
    /// Elements of one diagram revision.
    | Diagram of DiagramSelection
    /// Ranges of one text revision, such as a code snippet.
    | Text of TextSelection
    /// A resource named by its IRI, such as a web page.
    | Resource of Iri

    /// Elements of one diagram revision. For C# callers.
    static member FromDiagram(selection: DiagramSelection) =
        ArgumentNullException.ThrowIfNull selection
        LinkEndpoint.Diagram selection

    /// Ranges of one text revision. For C# callers.
    static member FromText(selection: TextSelection) =
        ArgumentNullException.ThrowIfNull selection
        LinkEndpoint.Text selection

    /// A resource named by its IRI. For C# callers.
    static member FromResource(iri: Iri) =
        ArgumentNullException.ThrowIfNull iri
        LinkEndpoint.Resource iri

    /// For C# callers: every case needs a function.
    member this.Match(diagram: Func<DiagramSelection, 'T>, text: Func<TextSelection, 'T>, resource: Func<Iri, 'T>) =
        match this with
        | LinkEndpoint.Diagram selection -> diagram.Invoke selection
        | LinkEndpoint.Text selection -> text.Invoke selection
        | LinkEndpoint.Resource iri -> resource.Invoke iri

    override this.Equals(other: obj | null) =
        match this, other with
        | LinkEndpoint.Diagram left, (:? LinkEndpoint as LinkEndpoint.Diagram right) -> left.Equals right
        | LinkEndpoint.Text left, (:? LinkEndpoint as LinkEndpoint.Text right) ->
            left.Version = right.Version && left.Ranges.SequenceEqual right.Ranges
        | LinkEndpoint.Resource left, (:? LinkEndpoint as LinkEndpoint.Resource right) -> left.Equals right
        | _ -> false

    override this.GetHashCode() =
        match this with
        | LinkEndpoint.Diagram selection -> selection.GetHashCode()
        | LinkEndpoint.Text selection -> HashCode.Combine(selection.Version, selection.Ranges.Length)
        | LinkEndpoint.Resource iri -> iri.GetHashCode()

    override this.ToString() =
        match this with
        | LinkEndpoint.Diagram selection -> selection.ToString()
        | LinkEndpoint.Text selection -> selection.ToString()
        | LinkEndpoint.Resource iri -> iri.ToString()

/// Why comments and links could not be read from a dataset.
[<RequireQualifiedAccess>]
type DiagramAnnotationError =
    /// The dataset refers to a document revision that the caller did not resolve.
    | UnresolvedDocument of DocumentVersion
    /// The selected diagram elements are not valid for their revision.
    | InvalidSelection of DiagramSelectionError
    /// The selected text ranges are not valid for their revision.
    | InvalidTextSelection of SelectionError
    /// A comment or link node is incomplete or has a value of the wrong kind.
    | Malformed of node: string * reason: string
    /// Two comments or links have the same identity.
    | DuplicateId of Iri

    /// For C# callers: every case needs a function.
    member this.Match
        (
            unresolvedDocument: Func<DocumentVersion, 'T>,
            invalidSelection: Func<DiagramSelectionError, 'T>,
            invalidTextSelection: Func<SelectionError, 'T>,
            malformed: Func<string, string, 'T>,
            duplicateId: Func<Iri, 'T>
        ) =
        match this with
        | DiagramAnnotationError.UnresolvedDocument version -> unresolvedDocument.Invoke version
        | DiagramAnnotationError.InvalidSelection error -> invalidSelection.Invoke error
        | DiagramAnnotationError.InvalidTextSelection error -> invalidTextSelection.Invoke error
        | DiagramAnnotationError.Malformed(node, reason) -> malformed.Invoke(node, reason)
        | DiagramAnnotationError.DuplicateId iri -> duplicateId.Invoke iri

    override this.ToString() =
        match this with
        | DiagramAnnotationError.UnresolvedDocument version -> $"The document revision {version} was not resolved."
        | DiagramAnnotationError.InvalidSelection error -> error.ToString()
        | DiagramAnnotationError.InvalidTextSelection error -> error.ToString()
        | DiagramAnnotationError.Malformed(node, reason) -> $"{node}: {reason}"
        | DiagramAnnotationError.DuplicateId iri -> $"The identity <{iri.Value}> is used by more than one comment or link."

/// Comments on diagram elements, links between diagrams, texts and resources, and the statements
/// of a dataset that are neither. Comment and link identities are unique.
[<Sealed>]
type DiagramAnnotationSet
    private
    (
        comments: ImmutableArray<Annotation<DiagramSelection, string>>,
        links: ImmutableArray<KnowledgeLink<LinkEndpoint, LinkEndpoint>>,
        other: ImmutableArray<Quad>
    ) =
    static let validate (comments: ImmutableArray<Annotation<DiagramSelection, string>>) (links: ImmutableArray<KnowledgeLink<LinkEndpoint, LinkEndpoint>>) other =
        let duplicate =
            Seq.append (comments |> Seq.map (fun comment -> comment.Id.Iri)) (links |> Seq.map (fun link -> link.Id.Iri))
            |> Seq.countBy id
            |> Seq.tryFind (fun (_, count) -> count > 1)

        match duplicate with
        | Some(iri, _) -> Error(DiagramAnnotationError.DuplicateId iri)
        | None -> Ok(DiagramAnnotationSet(comments, links, other))

    /// Comments, whose body is plain text.
    member _.Comments = comments

    /// Links with typed endpoints and a predicate.
    member _.Links = links

    /// Statements that belong to no comment or link. A read fills it, and a write keeps it unchanged.
    member _.Other = other

    /// A copy with other comments. Throws when an identity repeats.
    member _.WithComments(replacement: IEnumerable<Annotation<DiagramSelection, string>>) =
        ArgumentNullException.ThrowIfNull replacement
        DiagramAnnotationSet.Create(replacement, links, other)

    /// A copy with other links. Throws when an identity repeats.
    member _.WithLinks(replacement: IEnumerable<KnowledgeLink<LinkEndpoint, LinkEndpoint>>) =
        ArgumentNullException.ThrowIfNull replacement
        DiagramAnnotationSet.Create(comments, replacement, other)

    override _.ToString() = $"{comments.Length} comment(s), {links.Length} link(s), {other.Length} other statement(s)"

    /// Comments, links and other statements, or why they cannot form a set. For F# callers.
    static member Validate
        (
            comments: IEnumerable<Annotation<DiagramSelection, string>>,
            links: IEnumerable<KnowledgeLink<LinkEndpoint, LinkEndpoint>>,
            other: IEnumerable<Quad>
        ) : Result<DiagramAnnotationSet, DiagramAnnotationError> =
        ArgumentNullException.ThrowIfNull comments
        ArgumentNullException.ThrowIfNull links
        ArgumentNullException.ThrowIfNull other
        validate (ImmutableArray.CreateRange comments) (ImmutableArray.CreateRange links) (ImmutableArray.CreateRange other)

    /// Comments, links and other statements. Throws when a comment or link identity repeats.
    static member Create
        (
            comments: IEnumerable<Annotation<DiagramSelection, string>>,
            links: IEnumerable<KnowledgeLink<LinkEndpoint, LinkEndpoint>>,
            other: IEnumerable<Quad>
        ) =
        match DiagramAnnotationSet.Validate(comments, links, other) with
        | Ok set -> set
        | Error error -> invalidArg (nameof comments) (error.ToString())

    /// Comments and links to write. Throws when a comment or link identity repeats.
    static member Create
        (comments: IEnumerable<Annotation<DiagramSelection, string>>, links: IEnumerable<KnowledgeLink<LinkEndpoint, LinkEndpoint>>)
        =
        DiagramAnnotationSet.Create(comments, links, Seq.empty)

    /// Comments, links and other statements. For C# callers.
    static member TryCreate
        (
            comments: IEnumerable<Annotation<DiagramSelection, string>>,
            links: IEnumerable<KnowledgeLink<LinkEndpoint, LinkEndpoint>>,
            other: IEnumerable<Quad>,
            [<Out; MaybeNullWhen(false)>] set: byref<DiagramAnnotationSet>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramAnnotationError>
        ) =
        match DiagramAnnotationSet.Validate(comments, links, other) with
        | Ok value ->
            set <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            set <- Unchecked.defaultof<_>
            error <- failure
            false

    static member internal FromRead(comments, links, other) = DiagramAnnotationSet(comments, links, other)

/// Carries a typed error out of the mapping's recursive helpers; never escapes the public members.
type private AnnotationFailure(error: DiagramAnnotationError) =
    inherit Exception(error.ToString())
    member _.Error = error

module internal AnnotationVocabulary =
    let rdf local = Iri.Create(RdfNamespaces.Rdf + local)
    let oa local = Iri.Create(RdfNamespaces.WebAnnotation + local)
    let dcterms local = Iri.Create(RdfNamespaces.DublinCoreTerms + local)
    let md local = Iri.Create(RdfNamespaces.MorphirDiagram + local)
    let xsd local = Iri.Create(RdfNamespaces.Xsd + local)

    let rdfType = rdf "type"
    let rdfValue = rdf "value"

    /// The number of code points before a UTF-16 offset.
    let codePoints (text: string) (offset: int) =
        let mutable count = 0
        let mutable index = 0

        while index < offset && index < text.Length do
            index <- index + (if Char.IsHighSurrogate text[index] && index + 1 < text.Length then 2 else 1)
            count <- count + 1

        count

    /// Whether a UTF-16 offset falls between the two halves of a surrogate pair.
    let splitsPair (text: string) (offset: int) =
        offset > 0 && offset < text.Length && Char.IsLowSurrogate text[offset] && Char.IsHighSurrogate text[offset - 1]

    /// The UTF-16 offset of a code point position, or None past the end of the text.
    let utf16 (text: string) (codePoint: int) =
        let mutable count = 0
        let mutable index = 0

        while count < codePoint && index < text.Length do
            index <- index + (if Char.IsHighSurrogate text[index] && index + 1 < text.Length then 2 else 1)
            count <- count + 1

        if count = codePoint then Some index else None

/// The mapping between diagram comments and links and RDF. Comments are W3C Web Annotations with
/// the commenting motivation. Selections are oa:SpecificResource nodes with the document as source,
/// its md:revision, and one oa:FragmentSelector per element or one oa:TextPositionSelector per text
/// range, counted in code points. Links are md:Link nodes; a link from one element to an endpoint
/// that resolves to an IRI also gets the direct statement, so that plain RDF consumers can query it.
[<AbstractClass; Sealed>]
type DiagramAnnotations private () =
    static let quad subject predicate object =
        { Subject = subject; Predicate = predicate; Object = object; Graph = GraphLocation.DefaultGraph }

    static let blank () = BlankNodeId.New()
    static let iriObject (iri: Iri) = RdfObject.Iri iri
    static let literal (text: string) =
        match RdfLiteral.String text with
        | Ok value -> RdfObject.Literal value
        | Error _ ->
            raise (AnnotationFailure(DiagramAnnotationError.Malformed(text, "The text is not valid Unicode, so RDF cannot hold it.")))

    static let integer (value: int) =
        RdfObject.Literal(RdfLiteral.CreateTyped(value.ToString(CultureInfo.InvariantCulture), AnnotationVocabulary.xsd "nonNegativeInteger"))

    static let elementIri (selection: DiagramSelection) (reference: DiagramElementRef) =
        Iri.Create(selection.Version.Document.Iri.Value + "#" + reference.ToString())

    /// The statements of a specific resource, and the node that stands for it.
    static let resource (texts: Func<DocumentVersion, TextSnapshot | null>) (endpoint: LinkEndpoint) (quads: ResizeArray<Quad>) : RdfObject =
        let specific (version: DocumentVersion) (selectors: (BlankNodeId -> unit) list) =
            let node = blank ()
            let subject = RdfSubject.BlankNode node
            quads.Add(quad subject AnnotationVocabulary.rdfType (iriObject (AnnotationVocabulary.oa "SpecificResource")))
            quads.Add(quad subject (AnnotationVocabulary.oa "hasSource") (iriObject version.Document.Iri))
            quads.Add(quad subject (AnnotationVocabulary.md "revision") (literal version.Revision.Value))

            for write in selectors do
                let selector = blank ()
                quads.Add(quad subject (AnnotationVocabulary.oa "hasSelector") (RdfObject.BlankNode selector))
                write selector

            RdfObject.BlankNode node

        match endpoint with
        | LinkEndpoint.Resource iri -> iriObject iri
        | LinkEndpoint.Diagram selection ->
            specific selection.Version [
                for reference in selection.Elements ->
                    fun selector ->
                        let subject = RdfSubject.BlankNode selector
                        quads.Add(quad subject AnnotationVocabulary.rdfType (iriObject (AnnotationVocabulary.oa "FragmentSelector")))
                        quads.Add(quad subject AnnotationVocabulary.rdfValue (literal (reference.ToString())))
            ]
        | LinkEndpoint.Text selection ->
            let text =
                match texts.Invoke selection.Version with
                | null -> raise (AnnotationFailure(DiagramAnnotationError.UnresolvedDocument selection.Version))
                | snapshot ->
                    match selection.TryRead snapshot with
                    | Error failure -> raise (AnnotationFailure(DiagramAnnotationError.InvalidTextSelection failure))
                    | Ok _ ->
                        let split =
                            selection.Ranges
                            |> Seq.tryFind (fun range -> AnnotationVocabulary.splitsPair snapshot.Text range.Start.Value || AnnotationVocabulary.splitsPair snapshot.Text range.EndExclusive.Value)

                        match split with
                        | Some range ->
                            raise (AnnotationFailure(DiagramAnnotationError.Malformed(range.ToString(), "The range splits a surrogate pair, which code points cannot express.")))
                        | None -> snapshot.Text

            specific selection.Version [
                for range in selection.Ranges ->
                    fun selector ->
                        let subject = RdfSubject.BlankNode selector
                        quads.Add(quad subject AnnotationVocabulary.rdfType (iriObject (AnnotationVocabulary.oa "TextPositionSelector")))
                        quads.Add(quad subject (AnnotationVocabulary.oa "start") (integer (AnnotationVocabulary.codePoints text range.Start.Value)))
                        quads.Add(quad subject (AnnotationVocabulary.oa "end") (integer (AnnotationVocabulary.codePoints text range.EndExclusive.Value)))
            ]

    /// The IRI an endpoint resolves to for the direct statement of a link, if any.
    static let resolvedIri (endpoint: LinkEndpoint) =
        match endpoint with
        | LinkEndpoint.Resource iri -> Some iri
        | LinkEndpoint.Diagram selection when selection.Elements.Length = 1 -> Some(elementIri selection selection.Elements[0])
        | LinkEndpoint.Diagram _ -> None
        | LinkEndpoint.Text selection -> Some selection.Version.Document.Iri

    static let writeAll (set: DiagramAnnotationSet) (texts: Func<DocumentVersion, TextSnapshot | null>) =
        let quads = ResizeArray<Quad>()

        for comment in set.Comments do
            let subject = RdfSubject.Iri comment.Id.Iri
            quads.Add(quad subject AnnotationVocabulary.rdfType (iriObject (AnnotationVocabulary.oa "Annotation")))
            quads.Add(quad subject (AnnotationVocabulary.oa "motivatedBy") (iriObject (AnnotationVocabulary.oa "commenting")))
            quads.Add(quad subject (AnnotationVocabulary.oa "bodyValue") (literal comment.Body))
            quads.Add(quad subject (AnnotationVocabulary.dcterms "creator") (iriObject comment.Provenance.Author))

            let created =
                comment.Provenance.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)

            quads.Add(
                quad subject (AnnotationVocabulary.dcterms "created") (RdfObject.Literal(RdfLiteral.CreateTyped(created, AnnotationVocabulary.xsd "dateTime")))
            )

            quads.Add(quad subject (AnnotationVocabulary.oa "hasTarget") (resource texts (LinkEndpoint.Diagram comment.Target) quads))

        for link in set.Links do
            let subject = RdfSubject.Iri link.Id.Iri
            quads.Add(quad subject AnnotationVocabulary.rdfType (iriObject (AnnotationVocabulary.md "Link")))
            quads.Add(quad subject (AnnotationVocabulary.md "linkSource") (resource texts link.Source quads))
            quads.Add(quad subject (AnnotationVocabulary.md "linkPredicate") (iriObject link.Predicate))
            quads.Add(quad subject (AnnotationVocabulary.md "linkTarget") (resource texts link.Target quads))

            match link.Source, resolvedIri link.Target with
            | LinkEndpoint.Diagram selection, Some target when selection.Elements.Length = 1 ->
                quads.Add(quad (RdfSubject.Iri(elementIri selection selection.Elements[0])) link.Predicate (iriObject target))
            | _ -> ()

        quads.AddRange set.Other
        quads |> Seq.fold (fun (dataset: RdfDataset) value -> dataset.Add value) RdfDataset.Empty

    static let readAll
        (dataset: RdfDataset)
        (diagrams: Func<DocumentVersion, DiagramSnapshot | null>)
        (texts: Func<DocumentVersion, TextSnapshot | null>)
        =
        let quads = dataset.Quads
        let consumed = HashSet<Quad>()
        // Only the default graph holds annotations, as writeAll writes them. A statement of a named graph is never
        // consumed, so it stays in Other with its graph, and statements of two graphs never form one annotation.
        let bySubject =
            quads
            |> Seq.filter (fun item -> item.Graph = GraphLocation.DefaultGraph)
            |> Seq.groupBy (fun item -> item.Subject)
            |> dict

        let statements subject =
            match bySubject.TryGetValue subject with
            | true, items -> items |> Seq.toList
            | false, _ -> []

        let fail node reason = raise (AnnotationFailure(DiagramAnnotationError.Malformed(node, reason)))

        let values subject (predicate: Iri) =
            statements subject |> List.filter (fun item -> item.Predicate = predicate)

        let single subject (predicate: Iri) =
            match values subject predicate with
            | [ item ] ->
                consumed.Add item |> ignore
                item.Object
            | [] -> fail (subject.ToString()) $"It has no <{predicate.Value}>."
            | _ -> fail (subject.ToString()) $"It has more than one <{predicate.Value}>."

        let hasType subject (kind: Iri) =
            values subject AnnotationVocabulary.rdfType |> List.exists (fun item -> item.Object = RdfObject.Iri kind)

        let consumeType subject (kind: Iri) =
            for item in values subject AnnotationVocabulary.rdfType do
                if item.Object = RdfObject.Iri kind then consumed.Add item |> ignore

        let text subject predicate =
            match single subject predicate with
            | RdfObject.Literal value -> value.LexicalForm
            | _ -> fail (subject.ToString()) $"<{(predicate: Iri).Value}> is not a literal."

        let iri subject predicate =
            match single subject predicate with
            | RdfObject.Iri value -> value
            | _ -> fail (subject.ToString()) $"<{(predicate: Iri).Value}> is not an IRI."

        let typedText subject (predicate: Iri) (datatype: Iri) =
            match single subject predicate with
            | RdfObject.Literal value when value.Datatype = datatype -> value.LexicalForm
            | _ -> fail (subject.ToString()) $"<{predicate.Value}> is not an <{datatype.Value}> literal."

        let number subject predicate =
            let lexical = typedText subject predicate (AnnotationVocabulary.xsd "nonNegativeInteger")
            let digits = if lexical.StartsWith '+' then lexical.Substring 1 else lexical

            if digits.Length = 0 || not (Seq.forall Char.IsAsciiDigit digits) then
                fail (subject.ToString()) $"<{(predicate: Iri).Value}> is not a non-negative integer."

            match Int32.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, value -> value
            | false, _ -> fail (subject.ToString()) $"<{predicate.Value}> is larger than a text offset can be."

        let endpoint (object: RdfObject) =
            let subject =
                match object with
                | RdfObject.Iri value -> RdfSubject.Iri value
                | RdfObject.BlankNode node -> RdfSubject.BlankNode node
                | RdfObject.Literal _ -> fail (object.ToString()) "A link endpoint cannot be a literal."

            if not (hasType subject (AnnotationVocabulary.oa "SpecificResource")) then
                match object with
                | RdfObject.Iri value -> LinkEndpoint.Resource value
                | _ -> fail (subject.ToString()) "A blank endpoint must be an oa:SpecificResource."
            else
                consumeType subject (AnnotationVocabulary.oa "SpecificResource")
                let version =
                    { Document = DocumentId.Create(iri subject (AnnotationVocabulary.oa "hasSource"))
                      Revision =
                        match RevisionId.Parse(text subject (AnnotationVocabulary.md "revision")) with
                        | Ok revision -> revision
                        | Error error -> fail (subject.ToString()) (error.ToString()) }

                let selectors =
                    values subject (AnnotationVocabulary.oa "hasSelector")
                    |> List.map (fun item ->
                        consumed.Add item |> ignore

                        match item.Object with
                        | RdfObject.BlankNode node -> RdfSubject.BlankNode node
                        | RdfObject.Iri value -> RdfSubject.Iri value
                        | RdfObject.Literal _ -> fail (subject.ToString()) "A selector cannot be a literal.")

                if selectors.IsEmpty then fail (subject.ToString()) "It has no oa:hasSelector."

                if selectors |> List.forall (fun selector -> hasType selector (AnnotationVocabulary.oa "FragmentSelector")) then
                    match diagrams.Invoke version with
                    | null -> raise (AnnotationFailure(DiagramAnnotationError.UnresolvedDocument version))
                    | snapshot ->
                        let references =
                            selectors
                            |> List.map (fun selector ->
                                consumeType selector (AnnotationVocabulary.oa "FragmentSelector")

                                match DiagramElementRef.Parse(text selector AnnotationVocabulary.rdfValue) with
                                | Ok reference -> reference
                                | Error error -> fail (selector.ToString()) (error.ToString()))

                        if snapshot.Version <> version then
                            raise (AnnotationFailure(DiagramAnnotationError.InvalidSelection DiagramSelectionError.VersionMismatch))

                        match DiagramSelection.TryCreate(snapshot, references) with
                        | Ok selection -> LinkEndpoint.Diagram selection
                        | Error error -> raise (AnnotationFailure(DiagramAnnotationError.InvalidSelection error))
                elif selectors |> List.forall (fun selector -> hasType selector (AnnotationVocabulary.oa "TextPositionSelector")) then
                    match texts.Invoke version with
                    | null -> raise (AnnotationFailure(DiagramAnnotationError.UnresolvedDocument version))
                    | snapshot ->
                        let position selector predicate =
                            match AnnotationVocabulary.utf16 snapshot.Text (number selector predicate) with
                            | Some offset -> TextOffset.Create offset
                            | None -> fail (selector.ToString()) "The position is beyond the end of the text."

                        let ranges =
                            selectors
                            |> List.map (fun selector ->
                                consumeType selector (AnnotationVocabulary.oa "TextPositionSelector")
                                let start = position selector (AnnotationVocabulary.oa "start")
                                let finish = position selector (AnnotationVocabulary.oa "end")

                                if finish.Value < start.Value then fail (selector.ToString()) "The end precedes the start."

                                SourceSpan.Create(start, finish))

                        if snapshot.Version <> version then
                            raise (AnnotationFailure(DiagramAnnotationError.InvalidTextSelection SelectionError.VersionMismatch))

                        match TextSelection.TryCreate(snapshot, ranges) with
                        | Ok selection -> LinkEndpoint.Text selection
                        | Error error -> raise (AnnotationFailure(DiagramAnnotationError.InvalidTextSelection error))
                else
                    fail (subject.ToString()) "The selectors must all be fragment selectors or all text position selectors."

        let comments = ResizeArray()
        let links = ResizeArray()

        // A node that does not follow this mapping, such as an annotation from another tool, stays in
        // Other with its statements; it does not fail the read.
        let attempt (body: unit -> unit) =
            let saved = HashSet consumed

            try
                body ()
            with :? AnnotationFailure as failure when (match failure.Error with DiagramAnnotationError.Malformed _ -> true | _ -> false) ->
                consumed.Clear()
                consumed.UnionWith saved

        for subject in bySubject.Keys |> Seq.toList do
          attempt <| fun () ->
            match subject with
            | RdfSubject.Iri id when
                hasType subject (AnnotationVocabulary.oa "Annotation")
                && values subject (AnnotationVocabulary.oa "motivatedBy")
                   |> List.exists (fun item -> item.Object = RdfObject.Iri(AnnotationVocabulary.oa "commenting"))
                ->
                consumeType subject (AnnotationVocabulary.oa "Annotation")
                let body = text subject (AnnotationVocabulary.oa "bodyValue")
                single subject (AnnotationVocabulary.oa "motivatedBy") |> ignore
                let author = iri subject (AnnotationVocabulary.dcterms "creator")

                let created =
                    let lexical = typedText subject (AnnotationVocabulary.dcterms "created") (AnnotationVocabulary.xsd "dateTime")

                    // xsd:dateTime; a value without a time zone is read as UTC, independent of the machine.
                    match DateTimeOffset.TryParseExact(lexical, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
                    | true, value -> value
                    | false, _ -> fail (subject.ToString()) "dcterms:created is not an xsd:dateTime."

                match endpoint (single subject (AnnotationVocabulary.oa "hasTarget")) with
                | LinkEndpoint.Diagram target ->
                    comments.Add
                        { Id = AnnotationId.Create id
                          Target = target
                          Body = body
                          Provenance = { Author = author; CreatedAt = created } }
                | _ -> fail (subject.ToString()) "A comment must target diagram elements."
            | RdfSubject.Iri id when hasType subject (AnnotationVocabulary.md "Link") ->
                consumeType subject (AnnotationVocabulary.md "Link")
                let source = endpoint (single subject (AnnotationVocabulary.md "linkSource"))
                let predicate = iri subject (AnnotationVocabulary.md "linkPredicate")
                let target = endpoint (single subject (AnnotationVocabulary.md "linkTarget"))

                match source, resolvedIri target with
                | LinkEndpoint.Diagram selection, Some resolved when selection.Elements.Length = 1 ->
                    consumed.Add(quad (RdfSubject.Iri(elementIri selection selection.Elements[0])) predicate (iriObject resolved)) |> ignore
                | _ -> ()

                links.Add { Id = LinkId.Create id; Source = source; Predicate = predicate; Target = target }
            | _ -> ()

        // The statements of selectors, specific resources, comments and links were consumed above;
        // a consumed direct statement only counts if it is in the dataset.
        let other = quads |> Seq.filter (fun item -> not (consumed.Contains item)) |> ImmutableArray.CreateRange
        DiagramAnnotationSet.FromRead(ImmutableArray.CreateRange comments, ImmutableArray.CreateRange links, other)

    /// Writes comments and links as statements in the default graph. Text ranges are written in code
    /// points, so the resolver must give the text revision of every text endpoint. For F# callers.
    static member Write
        (annotations: DiagramAnnotationSet, texts: Func<DocumentVersion, TextSnapshot | null>)
        : Result<RdfDataset, DiagramAnnotationError> =
        ArgumentNullException.ThrowIfNull annotations
        ArgumentNullException.ThrowIfNull texts

        try
            Ok(writeAll annotations texts)
        with :? AnnotationFailure as failure ->
            Error failure.Error

    /// Writes comments and links that have no text endpoint. For F# callers.
    static member Write(annotations: DiagramAnnotationSet) =
        DiagramAnnotations.Write(annotations, Func<DocumentVersion, TextSnapshot | null>(fun _ -> null))

    /// Writes comments and links. For C# callers.
    static member TryWrite
        (
            annotations: DiagramAnnotationSet,
            texts: Func<DocumentVersion, TextSnapshot | null>,
            [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramAnnotationError>
        ) =
        match DiagramAnnotations.Write(annotations, texts) with
        | Ok value ->
            dataset <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            dataset <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Reads comments and links. The resolvers give the diagram and text revisions that selections
    /// name; a revision they do not resolve is an error. Every other statement is kept in Other.
    /// For F# callers.
    static member Read
        (
            dataset: RdfDataset,
            diagrams: Func<DocumentVersion, DiagramSnapshot | null>,
            texts: Func<DocumentVersion, TextSnapshot | null>
        ) : Result<DiagramAnnotationSet, DiagramAnnotationError> =
        ArgumentNullException.ThrowIfNull dataset
        ArgumentNullException.ThrowIfNull diagrams
        ArgumentNullException.ThrowIfNull texts

        try
            Ok(readAll dataset diagrams texts)
        with :? AnnotationFailure as failure ->
            Error failure.Error

    /// Reads comments and links. For C# callers.
    static member TryRead
        (
            dataset: RdfDataset,
            diagrams: Func<DocumentVersion, DiagramSnapshot | null>,
            texts: Func<DocumentVersion, TextSnapshot | null>,
            [<Out; MaybeNullWhen(false)>] annotations: byref<DiagramAnnotationSet>,
            [<Out; MaybeNullWhen(true)>] error: byref<DiagramAnnotationError>
        ) =
        match DiagramAnnotations.Read(dataset, diagrams, texts) with
        | Ok value ->
            annotations <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            annotations <- Unchecked.defaultof<_>
            error <- failure
            false
