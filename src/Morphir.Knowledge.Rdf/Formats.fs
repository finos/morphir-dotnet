// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Collections.Immutable
open System.Linq
open Morphir.Knowledge

/// How a writer for a format without named graphs handles a dataset that has them.
[<RequireQualifiedAccess>]
type NamedGraphHandling =
    /// Fail and name every named graph. Nothing is written.
    | Reject
    /// Write the default graph only, and name the omitted graphs in the output.
    | DropNamedGraphs

    override this.ToString() =
        match this with
        | NamedGraphHandling.Reject -> "reject named graphs"
        | NamedGraphHandling.DropNamedGraphs -> "drop named graphs"

/// Dataset content that a format cannot hold, reported before any data is written.
/// Equality compares the listed graphs element by element.
[<RequireQualifiedAccess; CustomEquality; NoComparison>]
type RdfWriteError =
    /// The format holds only the default graph, and the dataset has these named graphs.
    | NamedGraphs of ImmutableArray<GraphName>
    /// RDF/XML cannot split the predicate into a namespace and an XML local name.
    | PredicateWithoutLocalName of Iri
    /// RDF/XML cannot hold the literal, because it has a character that XML 1.0 forbids.
    | NotXmlCharacters of RdfLiteral

    /// For C# callers: every case needs a function.
    member this.Match
        (
            namedGraphs: Func<ImmutableArray<GraphName>, 'Result>,
            predicateWithoutLocalName: Func<Iri, 'Result>,
            notXmlCharacters: Func<RdfLiteral, 'Result>
        ) =
        match this with
        | RdfWriteError.NamedGraphs graphs -> namedGraphs.Invoke graphs
        | RdfWriteError.PredicateWithoutLocalName predicate -> predicateWithoutLocalName.Invoke predicate
        | RdfWriteError.NotXmlCharacters literal -> notXmlCharacters.Invoke literal

    override this.ToString() =
        match this with
        | RdfWriteError.NamedGraphs graphs ->
            let names = graphs |> Seq.map (fun graph -> graph.ToString()) |> String.concat ", "
            $"The format holds only the default graph; the dataset has {graphs.Length} named graph(s): {names}."
        | RdfWriteError.PredicateWithoutLocalName predicate ->
            $"RDF/XML cannot write the predicate <{predicate.Value}>: no suffix of it is an XML local name."
        | RdfWriteError.NotXmlCharacters literal ->
            $"RDF/XML cannot write the literal {literal}: it has a character that XML 1.0 forbids."

    override this.Equals(other: obj | null) =
        match this, other with
        | RdfWriteError.NamedGraphs left, (:? RdfWriteError as RdfWriteError.NamedGraphs right) -> left.SequenceEqual right
        | RdfWriteError.PredicateWithoutLocalName left, (:? RdfWriteError as RdfWriteError.PredicateWithoutLocalName right) ->
            left.Equals right
        | RdfWriteError.NotXmlCharacters left, (:? RdfWriteError as RdfWriteError.NotXmlCharacters right) -> left.Equals right
        | _ -> false

    override this.GetHashCode() =
        match this with
        | RdfWriteError.NamedGraphs graphs -> graphs |> Seq.fold (fun hash graph -> HashCode.Combine(hash, graph)) 17
        | RdfWriteError.PredicateWithoutLocalName predicate -> predicate.GetHashCode()
        | RdfWriteError.NotXmlCharacters literal -> literal.GetHashCode()

/// Serialized UTF-8 bytes and the named graphs that an explicit policy omitted. Only writers create it.
/// Equality compares the bytes and the graphs element by element.
[<Sealed>]
type RdfOutput internal (bytes: byte array, omittedGraphs: ImmutableArray<GraphName>) =
    /// The serialized UTF-8 bytes.
    member _.Bytes = ReadOnlyMemory bytes

    /// Named graphs omitted under the selected policy, empty when nothing was omitted.
    member _.OmittedGraphs = omittedGraphs

    override _.ToString() = $"RDF document ({bytes.Length} bytes)"

    override _.Equals(other: obj | null) =
        match other with
        | :? RdfOutput as output -> output.Bytes.Span.SequenceEqual(ReadOnlySpan bytes) && output.OmittedGraphs.SequenceEqual omittedGraphs
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(bytes.Length, omittedGraphs.Length)

module internal DefaultGraphOnly =
    /// Splits a dataset into its default graph and the names of its named graphs.
    let split (dataset: RdfDataset) (handling: NamedGraphHandling) =
        let names = dataset.Names

        if names.IsEmpty then
            Ok(dataset, names)
        else
            match handling with
            | NamedGraphHandling.Reject -> Error(RdfWriteError.NamedGraphs names)
            | NamedGraphHandling.DropNamedGraphs ->
                let defaultOnly =
                    dataset.DefaultGraph.Triples
                    |> Seq.fold (fun (result: RdfDataset) triple -> result.Add(Quad.FromTriple(GraphLocation.DefaultGraph, triple))) RdfDataset.Empty

                Ok(defaultOnly, names)
