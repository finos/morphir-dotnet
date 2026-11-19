// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge.Rdf

open System
open System.Diagnostics.CodeAnalysis
open System.Runtime.InteropServices

/// A UTF-8 RDF 1.1 N-Triples reader and writer. N-Triples holds the default graph only.
[<AbstractClass; Sealed>]
type NTriples private () =
    /// Reads UTF-8 bytes into the default graph, with fresh blank-node identities for this input.
    /// A statement with a graph label is a syntax error. For F# callers.
    static member Read(source: ReadOnlyMemory<byte>) = NQuadsReading.read false source

    /// Reads UTF-8 bytes into the default graph. For C# callers.
    static member TryRead(
        source: ReadOnlyMemory<byte>,
        [<Out; MaybeNullWhen(false)>] dataset: byref<RdfDataset>,
        [<Out; MaybeNullWhen(true)>] error: byref<NQuadsReadError>
    ) =
        match NTriples.Read source with
        | Ok value ->
            dataset <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            dataset <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes the default graph. A dataset with named graphs fails and names them, unless the
    /// caller drops them explicitly; the output then names the omitted graphs. For F# callers.
    static member Write(dataset: RdfDataset, namedGraphs: NamedGraphHandling) : Result<RdfOutput, RdfWriteError> =
        ArgumentNullException.ThrowIfNull dataset
        ArgumentNullException.ThrowIfNull namedGraphs

        DefaultGraphOnly.split dataset namedGraphs
        |> Result.map (fun (graph, omitted) -> RdfOutput(NQuadsWriter.write graph, omitted))

    /// Writes the default graph, and fails with every named graph's name if the dataset has any.
    /// For F# callers.
    static member Write(dataset: RdfDataset) = NTriples.Write(dataset, NamedGraphHandling.Reject)

    /// Writes the default graph as canonical N-Triples: canonical labels and escaping, statements in
    /// code point order. Isomorphic datasets write identical bytes. Named graphs follow the policy.
    /// For F# callers.
    static member Write(canonical: CanonicalDataset, namedGraphs: NamedGraphHandling) : Result<RdfOutput, RdfWriteError> =
        ArgumentNullException.ThrowIfNull canonical
        ArgumentNullException.ThrowIfNull namedGraphs

        DefaultGraphOnly.split canonical.Dataset namedGraphs
        |> Result.map (fun (graph, omitted) ->
            let text =
                graph.Quads
                |> Seq.map (CanonicalNQuads.line canonical.LabelOf)
                |> Seq.sortWith CanonicalNQuads.compare
                |> String.concat ""

            RdfOutput(NQuadsGrammar.strictUtf8.GetBytes text, omitted))

    /// Writes the default graph as canonical N-Triples under the named-graph policy. For C# callers.
    static member TryWrite(
        canonical: CanonicalDataset,
        namedGraphs: NamedGraphHandling,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match NTriples.Write(canonical, namedGraphs) with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes the default graph under the caller's named-graph policy. For C# callers.
    static member TryWrite(
        dataset: RdfDataset,
        namedGraphs: NamedGraphHandling,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match NTriples.Write(dataset, namedGraphs) with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false

    /// Writes the default graph, and fails if the dataset has named graphs. For C# callers.
    static member TryWrite(
        dataset: RdfDataset,
        [<Out; MaybeNullWhen(false)>] output: byref<RdfOutput>,
        [<Out; MaybeNullWhen(true)>] error: byref<RdfWriteError>
    ) =
        match NTriples.Write dataset with
        | Ok value ->
            output <- value
            error <- Unchecked.defaultof<_>
            true
        | Error failure ->
            output <- Unchecked.defaultof<_>
            error <- failure
            false
