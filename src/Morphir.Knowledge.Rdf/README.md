# Morphir.Knowledge.Rdf

`Morphir.Knowledge.Rdf` models RDF 1.1 terms, graphs and datasets and provides
UTF-8 N-Quads interchange. It uses the shared identity, annotation and link types
from `Morphir.Knowledge`.

A dataset always has a default graph. Named graphs admit either an IRI or a
blank node as their name and can be explicitly empty. Triples have set membership
within each graph. A quad exposes the subject, predicate, object and graph as
individual fields, so the same triple can have a distinct quad in each graph.

A `GraphRef` identifies a graph in a specific `DatasetVersion`, including an
empty graph. A `StatementRef` identifies a quad in a specific version.
`DatasetSnapshot` resolves references only for the version it represents.
An `AuthoredClaim` adds a claim IRI and provenance to a statement reference so
independent claims about the same quad can carry their own annotations.

## Read and write

An N-Quads read allocates fresh blank-node identities for that input. Repeated
labels share an identity across its graphs. The same label in another input
gets a new identity. On write, the codec allocates its own labels and preserves
the dataset up to blank-node renaming.

Keep the original imported dataset or its blank-node identity mapping when
persisting a snapshot that has annotation targets involving blank nodes.

The reader reports malformed UTF-8 as an encoding error. Syntax errors carry a
half-open UTF-16 span into the decoded source. The writer returns UTF-8 bytes.
Empty named graphs are valid in the dataset model. N-Quads cannot represent
them, so the default write returns a typed error with the affected graph names.
To omit those graphs explicitly, pass `EmptyGraphHandling.Omit` and read the
names from `NQuadsOutput.OmittedGraphs`.

### F#

```fsharp
open System
open System.Text
open Morphir.Knowledge.Rdf

let bytes = Encoding.UTF8.GetBytes("<urn:item> <urn:status> \"approved\" <urn:review> .\n")
let dataset =
    match NQuads.Read(ReadOnlyMemory bytes) with
    | Ok dataset -> dataset
    | Error error -> failwith (error.ToString())

let quad = dataset.Quads[0]
let triple = Quad.toTriple quad

match NQuads.Write dataset with
| Ok output -> printf "%s" (Encoding.UTF8.GetString output.Bytes)
| Error error -> failwith (error.ToString())
```

### C#

```csharp
using System;
using System.Text;
using Morphir.Knowledge.Rdf;

var bytes = Encoding.UTF8.GetBytes("<urn:item> <urn:status> \"approved\" <urn:review> .\n");
if (!NQuads.TryRead(bytes, out var dataset, out var readError))
    throw new InvalidOperationException(readError.ToString());

var quad = dataset.Quads[0];
var triple = quad.ToTriple();

if (!NQuads.TryWrite(dataset, EmptyGraphHandling.Reject, out var output, out var writeError))
    throw new InvalidOperationException(writeError.ToString());

Console.Write(Encoding.UTF8.GetString(output.Bytes));
```

## N-Triples

N-Triples holds the default graph only. `NTriples.Read` puts every triple in
the default graph, with the same blank-node identity and diagnostics as an
N-Quads read. A statement with a fourth term is a syntax error with the code
`ntriples.graph-label`, and its span starts at that term.

A format without named graphs reports them before it writes anything. By
default, writing a dataset with named graphs as N-Triples fails with
`RdfWriteError.NamedGraphs`, which names every named graph. To write the
default graph alone, pass `NamedGraphHandling.DropNamedGraphs` and read the
names of the omitted graphs from `RdfOutput.OmittedGraphs`. The same policy
applies to RDF/XML.

### F#

```fsharp
open System
open System.Text
open Morphir.Knowledge.Rdf

let bytes = Encoding.UTF8.GetBytes("<urn:item> <urn:status> \"approved\" .\n")
let dataset =
    match NTriples.Read(ReadOnlyMemory bytes) with
    | Ok dataset -> dataset
    | Error error -> failwith (error.ToString())

match NTriples.Write(dataset, NamedGraphHandling.DropNamedGraphs) with
| Ok output ->
    for graph in output.OmittedGraphs do
        eprintfn "Omitted %O" graph
    printf "%s" (Encoding.UTF8.GetString output.Bytes.Span)
| Error error -> failwith (error.ToString())
```

### C#

```csharp
using System;
using System.Text;
using Morphir.Knowledge.Rdf;

var bytes = Encoding.UTF8.GetBytes("<urn:item> <urn:status> \"approved\" .\n");
if (!NTriples.TryRead(bytes, out var dataset, out var readError))
    throw new InvalidOperationException(readError.ToString());

if (!NTriples.TryWrite(dataset, NamedGraphHandling.Reject, out var output, out var writeError))
{
    var graphs = writeError.Match(
        namedGraphs: names => names.Length,
        predicateWithoutLocalName: _ => 0);
    throw new InvalidOperationException($"{graphs} named graph(s): {writeError}");
}

Console.Write(Encoding.UTF8.GetString(output.Bytes.Span));
```

## Canonical output

Every read gives blank nodes fresh identities, so two reads of one file write
different labels. `RdfCanonicalization.Canonicalize` implements
[RDF Dataset Canonicalization (RDFC-1.0)](https://www.w3.org/TR/rdf-canon/): it
labels every blank node `c14n0`, `c14n1` and so on, so that isomorphic datasets
get the same labels. Pass the resulting `CanonicalDataset` to a writer for
canonical output, with canonical escaping and statements sorted in code point
order. Isomorphic datasets then write identical bytes. Writing a plain
`RdfDataset` is unchanged.

Canonicalization of some blank-node graphs is exponential, so it counts its
work: each n-degree hash and each permutation it tries. It stops with
`CanonicalizationError.WorkLimitExceeded` at the limit of
`CanonicalizationOptions`. The default limit admits the W3C poison graphs, which
need a few thousand units, and stops a ten-node clique. SHA-256 is the default
hash. `WithHashAlgorithm(CanonicalHashAlgorithm.Sha384)` selects SHA-384.

### F#

```fsharp
open System
open System.Text
open Morphir.Knowledge.Rdf

let dataset =
    match NQuads.Read(ReadOnlyMemory(Encoding.UTF8.GetBytes "_:x <urn:p> _:y .\n_:y <urn:p> _:x .\n")) with
    | Ok dataset -> dataset
    | Error error -> failwith (error.ToString())

match RdfCanonicalization.Canonicalize(dataset, CanonicalizationOptions.Default) with
| Ok canonical ->
    match NQuads.Write canonical with
    | Ok output -> printf "%s" (Encoding.UTF8.GetString output.Bytes)
    | Error error -> failwith (error.ToString())
| Error error -> failwith (error.ToString())
```

### C#

```csharp
using System;
using System.Text;
using Morphir.Knowledge.Rdf;

if (!NQuads.TryRead(Encoding.UTF8.GetBytes("_:x <urn:p> _:y .\n_:y <urn:p> _:x .\n"), out var dataset, out var readError))
    throw new InvalidOperationException(readError.ToString());

var options = CanonicalizationOptions.Default.WithWorkLimit(10_000);
if (!RdfCanonicalization.TryCanonicalize(dataset, options, out var canonical, out var canonicalError))
    throw new InvalidOperationException(canonicalError.ToString());

if (!NQuads.TryWrite(canonical, out var output, out var writeError))
    throw new InvalidOperationException(writeError.ToString());

Console.Write(Encoding.UTF8.GetString(output.Bytes));
```

The pinned [RDFC-1.0 tests](../../tests/Fixtures/RdfCanon/PROVENANCE.md) exercise
the canonicalizer, including the SHA-384, issued-identifier map and poison tests.

The pinned [RDF 1.1 N-Quads syntax cases](../../tests/Fixtures/RdfNQuads/PROVENANCE.md)
exercise the reader, including the blank-node grammar. The applicable references
are [RDF 1.1 Concepts](https://www.w3.org/TR/rdf11-concepts/),
[RDF 1.1 N-Quads](https://www.w3.org/TR/n-quads/) and the
[blank-node label erratum](https://lists.w3.org/Archives/Public/public-rdf-comments/2022Mar/0000.html).
