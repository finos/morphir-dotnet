// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices
open System.Text
open Morphir.Knowledge

/// An RDF blank node identity independent of its label in any concrete syntax.
type BlankNodeId =
    private
    | BlankNodeId of Guid

    /// Allocates a fresh blank node identity.
    static member New() = BlankNodeId(Guid.NewGuid())

    /// The opaque identity, for equality diagnostics. A serializer supplies its own label.
    member this.Value = let (BlankNodeId value) = this in value

    override this.ToString() = this.Value.ToString("D")

/// Why a literal could not be constructed.
[<RequireQualifiedAccess>]
type LiteralError =
    | InvalidLexicalForm
    | MissingDatatype
    | LanguageTagRequired
    | InvalidLanguageTag

    override this.ToString() =
        match this with
        | LiteralError.InvalidLexicalForm -> "The lexical form must be a Unicode string."
        | LiteralError.MissingDatatype -> "A typed literal requires a datatype IRI."
        | LiteralError.LanguageTagRequired -> "An rdf:langString literal requires a language tag."
        | LiteralError.InvalidLanguageTag -> "Expected a language tag."

type private LiteralKind =
    | Typed of Iri
    | LanguageTagged of string

module private LiteralVocabulary =
    let rdfLangString = Iri.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"
    let xsdString = Iri.Create "http://www.w3.org/2001/XMLSchema#string"

    // The fixed grandfathered tags in RFC 5646 are valid in addition to the regular grammar.
    let private grandfathered =
        set [
            "en-gb-oed"; "i-ami"; "i-bnn"; "i-default"; "i-enochian"; "i-hak"; "i-klingon"; "i-lux"
            "i-mingo"; "i-navajo"; "i-pwn"; "i-tao"; "i-tay"; "i-tsu"; "sgn-be-fr"; "sgn-be-nl"; "sgn-ch-de"
            "art-lojban"; "cel-gaulish"; "no-bok"; "no-nyn"; "zh-guoyu"; "zh-hakka"; "zh-min"; "zh-min-nan"; "zh-xiang"
        ]

    let private isAlpha (part: string) = part |> Seq.forall Char.IsAsciiLetter

    let isLanguageTag (tag: string) =
        let parts = tag.Split '-'
        if parts |> Array.exists (fun part -> part.Length = 0 || part.Length > 8) then
            false
        elif grandfathered.Contains tag || (parts[0] = "x" && parts.Length > 1) then
            true
        elif parts[0].Length < 2 || not (isAlpha parts[0]) then
            false
        else
            let mutable next = 1
            let consume predicate =
                if next < parts.Length && predicate parts[next] then
                    next <- next + 1
                    true
                else false

            if parts[0].Length <= 3 then
                let mutable extended = 0
                while extended < 3 && consume (fun part -> part.Length = 3 && isAlpha part) do
                    extended <- extended + 1

            consume (fun part -> part.Length = 4 && isAlpha part) |> ignore
            consume (fun part -> part.Length = 2 && isAlpha part || part.Length = 3 && Seq.forall Char.IsAsciiDigit part)
            |> ignore

            let variants = HashSet<string>(StringComparer.Ordinal)
            let mutable unique = true
            while consume (fun part -> part.Length >= 5 || part.Length = 4 && Char.IsAsciiDigit part[0]) do
                unique <- variants.Add(parts[next - 1]) && unique

            let extensions = HashSet<string>(StringComparer.Ordinal)
            while unique && consume (fun part -> part.Length = 1 && part <> "x") do
                let extension = parts[next - 1]
                let argumentsStart = next
                while consume (fun part -> part.Length >= 2) do ()
                unique <- next > argumentsStart && extensions.Add extension

            let complete = if consume ((=) "x") then next < parts.Length else next = parts.Length
            unique && complete

    let strictUtf8 = UTF8Encoding(false, true)

    let isUnicode (value: string | null) =
        match value with
        | null -> false
        | value ->
            try
                strictUtf8.GetByteCount value |> ignore
                true
            with :? EncoderFallbackException ->
                false

/// An RDF 1.1 literal. Equality uses the lexical form, datatype and normalized language tag.
type RdfLiteral =
    private
    | RdfLiteral of lexicalForm: string * kind: LiteralKind

    /// The lexical string, with escape sequences already resolved.
    member this.LexicalForm = let (RdfLiteral(lexical, _)) = this in lexical

    /// The datatype of the literal, including rdf:langString for a tagged string.
    member this.Datatype =
        match this with
        | RdfLiteral(_, Typed datatype) -> datatype
        | RdfLiteral(_, LanguageTagged _) -> LiteralVocabulary.rdfLangString

    /// The normalized BCP 47 tag if the literal is tagged, for F# callers.
    member this.LanguageTag =
        match this with
        | RdfLiteral(_, LanguageTagged language) -> Some language
        | RdfLiteral(_, Typed _) -> None

    /// The normalized BCP 47 tag, or null, for C# callers.
    member this.LanguageTagOrNull: string | null = Option.toObj this.LanguageTag

    /// Constructs a typed literal from a Unicode lexical value without coercing the value. For F# callers.
    static member Typed(lexicalForm: string | null, datatype: Iri) =
        match lexicalForm with
        | null -> Error LiteralError.InvalidLexicalForm
        | value when not (LiteralVocabulary.isUnicode value) -> Error LiteralError.InvalidLexicalForm
        | _ when obj.ReferenceEquals(datatype, null) -> Error LiteralError.MissingDatatype
        | _ when datatype = LiteralVocabulary.rdfLangString -> Error LiteralError.LanguageTagRequired
        | value -> Ok(RdfLiteral(value, LiteralKind.Typed datatype))

    /// Constructs a language-tagged literal from a Unicode lexical value. For F# callers.
    static member LanguageTagged(lexicalForm: string | null, language: string | null) =
        match lexicalForm, language with
        | null, _ -> Error LiteralError.InvalidLexicalForm
        | value, _ when not (LiteralVocabulary.isUnicode value) -> Error LiteralError.InvalidLexicalForm
        | _, null -> Error LiteralError.InvalidLanguageTag
        | _, tag when tag |> Seq.exists (fun ch -> not (Char.IsAsciiLetterOrDigit ch || ch = '-')) ->
            Error LiteralError.InvalidLanguageTag
        | value, tag ->
            let normalized = tag.ToLowerInvariant()
            if LiteralVocabulary.isLanguageTag normalized then Ok(RdfLiteral(value, LiteralKind.LanguageTagged normalized))
            else Error LiteralError.InvalidLanguageTag

    /// Constructs a typed literal from external text. For C# callers.
    static member TryCreateTyped(
        lexicalForm: string | null,
        datatype: Iri,
        [<Out; MaybeNullWhen(false)>] literal: byref<RdfLiteral>,
        [<Out; MaybeNullWhen(true)>] error: byref<LiteralError>
    ) =
        match RdfLiteral.Typed(lexicalForm, datatype) with
        | Ok value ->
            literal <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            literal <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Constructs a tagged literal from external text. For C# callers.
    static member TryCreateLanguageTagged(
        lexicalForm: string | null,
        language: string | null,
        [<Out; MaybeNullWhen(false)>] literal: byref<RdfLiteral>,
        [<Out; MaybeNullWhen(true)>] error: byref<LiteralError>
    ) =
        match RdfLiteral.LanguageTagged(lexicalForm, language) with
        | Ok value ->
            literal <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            literal <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Constructs an xsd:string literal from a Unicode lexical value. For F# callers.
    static member String(lexicalForm: string | null) =
        RdfLiteral.Typed(lexicalForm, LiteralVocabulary.xsdString)

    /// Constructs a typed literal from a known lexical value. Throws for an invalid input.
    static member CreateTyped(lexicalForm: string, datatype: Iri) =
        match RdfLiteral.Typed(lexicalForm, datatype) with
        | Ok result -> result
        | Error (LiteralError.InvalidLexicalForm as error) -> invalidArg (nameof lexicalForm) (error.ToString())
        | Error error -> invalidArg (nameof datatype) (error.ToString())

    /// Constructs a tagged literal from known values. Throws for an invalid input.
    static member CreateLanguageTagged(lexicalForm: string, language: string) =
        match RdfLiteral.LanguageTagged(lexicalForm, language) with
        | Ok result -> result
        | Error LiteralError.InvalidLexicalForm ->
            invalidArg (nameof lexicalForm) (LiteralError.InvalidLexicalForm.ToString())
        | Error error -> invalidArg (nameof language) (error.ToString())

    /// Constructs an xsd:string literal from a known lexical value. Throws for an invalid input.
    static member CreateString(lexicalForm: string) =
        RdfLiteral.CreateTyped(lexicalForm, LiteralVocabulary.xsdString)

    override this.ToString() =
        match this.LanguageTag with
        | Some tag -> $"\"{this.LexicalForm}\"@{tag}"
        | None -> $"\"{this.LexicalForm}\"^^<{this.Datatype.Value}>"

/// An RDF 1.1 subject.
[<RequireQualifiedAccess>]
type RdfSubject =
    | Iri of Iri
    | BlankNode of BlankNodeId

    /// Constructs an IRI subject. For C# callers.
    static member FromIri(value: Iri) = RdfSubject.Iri value

    /// Constructs a blank node subject. For C# callers.
    static member FromBlankNode(value: BlankNodeId) = RdfSubject.BlankNode value

    /// Visits either kind of subject. For C# callers.
    member this.Match(iri: Func<Iri, 'Result>, blankNode: Func<BlankNodeId, 'Result>) =
        match this with
        | RdfSubject.Iri value -> iri.Invoke value
        | RdfSubject.BlankNode value -> blankNode.Invoke value

    override this.ToString() =
        match this with
        | RdfSubject.Iri value -> $"<{value.Value}>"
        | RdfSubject.BlankNode value -> $"_:{value}"

/// An RDF 1.1 object.
[<RequireQualifiedAccess>]
type RdfObject =
    | Iri of Iri
    | BlankNode of BlankNodeId
    | Literal of RdfLiteral

    /// Constructs an IRI object. For C# callers.
    static member FromIri(value: Iri) = RdfObject.Iri value

    /// Constructs a blank node object. For C# callers.
    static member FromBlankNode(value: BlankNodeId) = RdfObject.BlankNode value

    /// Constructs a literal object. For C# callers.
    static member FromLiteral(value: RdfLiteral) = RdfObject.Literal value

    /// Visits each kind of object. For C# callers.
    member this.Match(iri: Func<Iri, 'Result>, blankNode: Func<BlankNodeId, 'Result>, literal: Func<RdfLiteral, 'Result>) =
        match this with
        | RdfObject.Iri value -> iri.Invoke value
        | RdfObject.BlankNode value -> blankNode.Invoke value
        | RdfObject.Literal value -> literal.Invoke value

    override this.ToString() =
        match this with
        | RdfObject.Iri value -> $"<{value.Value}>"
        | RdfObject.BlankNode value -> $"_:{value}"
        | RdfObject.Literal value -> value.ToString()

/// An RDF 1.1 graph label.
[<RequireQualifiedAccess>]
type GraphName =
    | IriName of Iri
    | BlankNodeName of BlankNodeId

    /// Constructs a graph name from an IRI. For C# callers.
    static member FromIri(value: Iri) = GraphName.IriName value

    /// Constructs a graph name from a blank node. For C# callers.
    static member FromBlankNode(value: BlankNodeId) = GraphName.BlankNodeName value

    /// Visits either kind of name. For C# callers.
    member this.Match(iri: Func<Iri, 'Result>, blankNode: Func<BlankNodeId, 'Result>) =
        match this with
        | GraphName.IriName value -> iri.Invoke value
        | GraphName.BlankNodeName value -> blankNode.Invoke value

    override this.ToString() =
        match this with
        | GraphName.IriName value -> $"<{value.Value}>"
        | GraphName.BlankNodeName value -> $"_:{value}"

/// A graph location within a dataset.
[<RequireQualifiedAccess>]
type GraphLocation =
    | DefaultGraph
    | NamedGraph of GraphName

    /// Selects the named graph. For C# callers.
    static member Named(name: GraphName) = GraphLocation.NamedGraph name

    /// Visits either graph location. For C# callers.
    member this.Match(defaultGraph: Func<'Result>, namedGraph: Func<GraphName, 'Result>) =
        match this with
        | GraphLocation.DefaultGraph -> defaultGraph.Invoke()
        | GraphLocation.NamedGraph name -> namedGraph.Invoke name

    override this.ToString() =
        match this with
        | GraphLocation.DefaultGraph -> "default graph"
        | GraphLocation.NamedGraph name -> name.ToString()

/// An RDF statement within a graph.
type Triple =
    { /// The subject, an IRI or a blank node.
      Subject: RdfSubject
      /// The predicate IRI.
      Predicate: Iri
      /// The object, an IRI, a blank node or a literal.
      Object: RdfObject }

    override this.ToString() = $"{this.Subject} <{this.Predicate.Value}> {this.Object}"

/// An RDF statement in its dataset context.
type Quad =
    { /// The subject, an IRI or a blank node.
      Subject: RdfSubject
      /// The predicate IRI.
      Predicate: Iri
      /// The object, an IRI, a blank node or a literal.
      Object: RdfObject
      /// The graph containing the statement.
      Graph: GraphLocation }

    /// Gets the statement without its graph membership.
    member this.ToTriple(): Triple =
        { Subject = this.Subject; Predicate = this.Predicate; Object = this.Object }

    /// Places a statement in a graph.
    static member FromTriple(graph: GraphLocation, triple: Triple) =
        { Subject = triple.Subject; Predicate = triple.Predicate; Object = triple.Object; Graph = graph }

    override this.ToString() = $"{this.ToTriple()} in {this.Graph}"

/// Conversions between triples and quads.
[<RequireQualifiedAccess>]
module Quad =
    /// Gets the statement without its graph membership.
    let toTriple (quad: Quad) = quad.ToTriple()

    /// Places a statement in a graph.
    let ofTriple graph triple = Quad.FromTriple(graph, triple)

/// An immutable RDF 1.1 graph with set membership.
[<Sealed>]
type RdfGraph private (triples: Set<Triple>) =
    /// The empty graph.
    static member Empty = RdfGraph Set.empty

    /// The graph's distinct triples.
    member _.Triples = ImmutableArray.CreateRange triples

    /// The number of distinct triples.
    member _.Count = triples.Count

    /// True if the graph has no statements.
    member _.IsEmpty = triples.IsEmpty

    /// Checks whether the graph contains a statement.
    member _.Contains(triple: Triple) = triples.Contains triple

    /// Adds a statement to the graph, retaining one copy of equivalent triples.
    member _.Add(triple: Triple) = RdfGraph(triples.Add triple)

    member internal _.Values = triples

    member internal _.Union(other: RdfGraph) = RdfGraph(Set.union triples other.Values)

    override _.ToString() = $"RDF graph ({triples.Count} triples)"

/// An immutable RDF 1.1 dataset with one default graph and uniquely named graphs.
[<Sealed>]
type RdfDataset private (defaultGraph: RdfGraph, namedGraphs: Map<GraphName, RdfGraph>) =
    /// A dataset with an empty default graph.
    static member Empty = RdfDataset(RdfGraph.Empty, Map.empty)

    /// The default graph, including when empty.
    member _.DefaultGraph = defaultGraph

    /// Named graph locations, including empty graphs.
    member _.Names = namedGraphs |> Seq.map (fun pair -> pair.Key) |> ImmutableArray.CreateRange

    /// All statements in the dataset, including their graph locations.
    member this.Quads = ImmutableArray.CreateRange this.EnumerateQuads

    /// Finds a graph. For F# callers.
    member _.FindGraph(location: GraphLocation) =
        match location with
        | GraphLocation.DefaultGraph -> Some defaultGraph
        | GraphLocation.NamedGraph name -> Map.tryFind name namedGraphs

    /// Finds a graph. For C# callers.
    member this.TryGetGraph(location: GraphLocation, [<Out; MaybeNullWhen(false)>] graph: byref<RdfGraph>) =
        match this.FindGraph location with
        | Some result ->
            graph <- result
            true
        | None ->
            graph <- Unchecked.defaultof<_>
            false

    /// Checks whether the dataset contains a quad at its specific graph location.
    member this.Contains(quad: Quad) =
        this.FindGraph quad.Graph |> Option.exists (fun graph -> graph.Contains(quad.ToTriple()))

    /// Creates or replaces a named graph, including an empty one.
    member _.WithNamedGraph(name: GraphName, graph: RdfGraph) =
        RdfDataset(defaultGraph, namedGraphs.Add(name, graph))

    /// Adds a quad to its graph, creating a missing named graph as needed.
    member this.Add(quad: Quad) =
        let triple = quad.ToTriple()
        match quad.Graph with
        | GraphLocation.DefaultGraph -> RdfDataset(defaultGraph.Add triple, namedGraphs)
        | GraphLocation.NamedGraph name ->
            let graph = Map.tryFind name namedGraphs |> Option.defaultValue RdfGraph.Empty
            this.WithNamedGraph(name, graph.Add triple)

    /// Combines graph membership while preserving existing blank node identities.
    member _.Merge(other: RdfDataset) =
        let mergeGraph names name (graph: RdfGraph) =
            let merged = Map.tryFind name names |> Option.map graph.Union |> Option.defaultValue graph
            Map.add name merged names
        RdfDataset(defaultGraph.Union other.DefaultGraph, Map.fold mergeGraph namedGraphs other.NamedGraphs)

    member internal _.NamedGraphs = namedGraphs

    member internal _.EnumerateQuads =
        seq {
            for triple in defaultGraph.Values do
                yield Quad.ofTriple GraphLocation.DefaultGraph triple
            for KeyValue(name, graph) in namedGraphs do
                for triple in graph.Values do
                    yield Quad.ofTriple (GraphLocation.NamedGraph name) triple
        }

    override _.ToString() = $"RDF dataset ({namedGraphs.Count} named graphs)"

/// The version of an RDF dataset used by a graph or statement address.
type DatasetVersion =
    { /// The dataset identity.
      Dataset: Iri
      /// The revision scoped to this dataset.
      Revision: RevisionId }

    override this.ToString() = $"{this.Dataset.Value} at {this.Revision}"

/// The identity of a graph within one dataset revision.
type GraphRef =
    { /// The original dataset revision.
      Dataset: DatasetVersion
      /// The graph location within that revision.
      Graph: GraphLocation }

    override this.ToString() = $"{this.Graph} of {this.Dataset}"

/// The address of a quad within one dataset revision.
type StatementRef =
    { /// The original dataset revision.
      Dataset: DatasetVersion
      /// The original statement and its graph.
      Quad: Quad }

    override this.ToString() = $"{this.Quad} of {this.Dataset}"

/// An identity issued for an individual authored claim.
type StatementId =
    private
    | StatementId of Iri

    /// The claim's IRI.
    member this.Iri = let (StatementId value) = this in value

    /// Wraps an already parsed IRI.
    static member Create(value: Iri) =
        ArgumentNullException.ThrowIfNull value
        StatementId value

    override this.ToString() = this.Iri.Value

/// One authored claim about a statement. Other claims may target the same quad.
type AuthoredClaim =
    { /// The identity of this authored claim.
      Id: StatementId
      /// The quad that the claim concerns.
      Statement: StatementRef
      /// The claim's author and creation information.
      Provenance: AnnotationProvenance }

    override this.ToString() = this.Id.ToString()

/// An immutable dataset together with the version against which references can be resolved.
[<Sealed>]
type DatasetSnapshot private (version: DatasetVersion, dataset: RdfDataset) =
    /// The version of the retained dataset.
    member _.Version = version

    /// The immutable dataset.
    member _.Dataset = dataset

    /// Associates an immutable dataset with its provider-issued version.
    static member Create(version: DatasetVersion, dataset: RdfDataset) = DatasetSnapshot(version, dataset)

    /// Finds a graph at this exact version. For F# callers.
    member _.ResolveGraph(reference: GraphRef) =
        if reference.Dataset = version then dataset.FindGraph reference.Graph else None

    /// Finds a graph at this exact version. For C# callers.
    member this.TryResolveGraph(reference: GraphRef, [<Out; MaybeNullWhen(false)>] graph: byref<RdfGraph>) =
        match this.ResolveGraph reference with
        | Some result ->
            graph <- result
            true
        | None ->
            graph <- Unchecked.defaultof<_>
            false

    /// Checks that a statement reference resolves in this exact version.
    member _.ContainsStatement(reference: StatementRef) =
        reference.Dataset = version && dataset.Contains reference.Quad

    override _.ToString() = version.ToString()
