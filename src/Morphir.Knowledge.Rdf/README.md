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

The pinned [RDF 1.1 N-Quads syntax cases](../../tests/Fixtures/RdfNQuads/PROVENANCE.md)
exercise the reader, including the blank-node grammar. The applicable references
are [RDF 1.1 Concepts](https://www.w3.org/TR/rdf11-concepts/),
[RDF 1.1 N-Quads](https://www.w3.org/TR/n-quads/) and the
[blank-node label erratum](https://lists.w3.org/Archives/Public/public-rdf-comments/2022Mar/0000.html).
