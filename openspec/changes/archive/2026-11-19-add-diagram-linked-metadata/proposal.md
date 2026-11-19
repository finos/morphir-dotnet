# Proposal

## Why

A Morphir diagram today is a picture and nothing else. The SVG that `Morphir.Diagrams` renders cannot say that the `Billing` node is implemented by a particular range of `Billing.fs`, that a sequence message is explained by a paragraph in an OKF concept, that a flowchart step expands into another diagram, or that a reviewer left a question on an edge. OKF documents already reach that kind of context through `Morphir.Knowledge` selections, annotations and links, and through RDF datasets. Diagrams should take part in the same knowledge graph, so text, code snippets and diagrams can be linked together, commented on, and extended with vocabulary no diagram format knows about.

For example, an author keeps `checkout.mmd` with a sidecar `checkout.mmd.nq`:

```text
<https://example.org/diagrams/checkout#node=Billing> <https://example.org/vocab/implementedBy> _:snippet .
_:snippet <http://www.w3.org/ns/oa#hasSource> <https://example.org/src/Billing.fs> .
_:comment <http://www.w3.org/ns/oa#hasTarget> <https://example.org/diagrams/checkout#edge=Cart-Billing.0> .
_:comment <http://www.w3.org/ns/oa#bodyValue> "Is this call idempotent?" .
```

Rendering embeds the dataset in the SVG's `<metadata>` as RDF/XML, so the single SVG file carries its context. The offline player shows the comment pinned to the edge. A later tool reads the same statements back out of the SVG.

## What Changes

- Every element of a parsed diagram (node, edge, group, participant, message, note, frame, playback step) gets a stable IRI derived from the diagram's document IRI. The rendered SVG marks each element with its reference.
- New diagram selections, the diagram counterpart of `TextSelection`: one or more elements of one diagram revision, checked against the model. They serve as targets and endpoints of the existing `Annotation` and `KnowledgeLink` types. Comments, links to text and code, and links between diagrams need no new record types.
- A documented mapping between diagram annotations and links and RDF, using the W3C Web Annotation vocabulary for comments and caller-chosen predicate IRIs for links. Statements with predicates the library does not know are kept unchanged.
- Two places to keep the data, holding the same dataset: a sidecar RDF file supplied by the caller, and the SVG's `<metadata>` element. The renderer embeds the dataset, and a reader extracts it from an SVG that Morphir produced.
- `Morphir.Knowledge.Rdf` gains N-Triples and RDF/XML reading and writing alongside N-Quads. Formats that cannot hold named graphs report the loss as a typed error, in the same way as empty named graphs in N-Quads today.
- `Morphir.Svg` gains namespaced elements and attributes and a metadata node, so an SVG document can carry foreign XML such as RDF/XML. Reading XML refuses DTDs and external entities.
- The offline player shows comments and links on the elements they target, with a panel listing them.
- Reading inline metadata from Mermaid source, JSON-LD, and CLI commands are out of scope. They can follow once this model exists.

## Capabilities

### New Capabilities

- `diagram-linked-metadata`: element identity in rendered diagrams, diagram selections, annotations and links on diagram elements, their RDF mapping, sidecar and embedded carriage, and how SVG and player output present them.
- `svg-metadata`: namespaced content and the `<metadata>` element in SVG documents written by `Morphir.Svg`, and safe extraction of that metadata from SVG input.

### Modified Capabilities

- `rdf-datasets`: adds N-Triples and RDF/XML interchange, and typed reporting when a format cannot represent part of a dataset.

## Tracking

Tracking issue: [finos/morphir-dotnet#689](https://github.com/finos/morphir-dotnet/issues/689). Related work: the diagrams migration PRD at `docs/content/contributing/design/prds/migrate-diagrams.md`, and the archived change `2026-11-18-add-morphir-knowledge`, which introduced the knowledge, OKF and RDF packages this change builds on.

## Impact

- `Morphir.Diagrams`: new public types for element references and diagram selections. Render options take an optional document identity and dataset. SVG and player output change, because every element carries a reference attribute and the SVG has a `<metadata>` element when a dataset is supplied. New package references to `Morphir.Knowledge` and `Morphir.Knowledge.Rdf`.
- `Morphir.Svg`: namespaced names and a metadata node in the scene model, and a safe metadata reader. Existing unqualified names keep working.
- `Morphir.Knowledge.Rdf`: new `NTriples` and `RdfXml` codecs and new typed errors. The N-Quads API is unchanged.
- The player scripts (`Player.js`, `Viewport.js`) gain an annotation layer. The player redesign happening now on the diagrams branch should land first.
- All affected packages remain trimmable and Native AOT compatible. The RDF/XML reader uses `System.Xml` with DTD processing prohibited.
