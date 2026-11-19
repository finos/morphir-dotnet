# rdf-datasets Specification

## Purpose

Represent RDF datasets and their addressable graphs and statements so Morphir consumers can link evidence to them and exchange datasets as N-Quads.

## Requirements

### Requirement: Triples and quads expose typed RDF terms

RDF 1.1 subjects SHALL be IRIs or blank nodes. Predicates SHALL be absolute IRIs. Objects SHALL admit IRIs, blank nodes and literals that retain their lexical form, datatype or language tag. A quad SHALL expose subject, predicate, object and graph directly, with conversion to and from a triple plus graph location.

#### Scenario: Flat quad access
- **WHEN** a caller constructs a quad with a typed literal object in a named graph
- **THEN** the caller can access all four fields directly and round-trip the quad through its triple and graph location

### Requirement: A dataset owns a default graph and named graphs

A dataset SHALL have one default graph and zero or more uniquely named graphs, including empty ones. Graph names SHALL admit IRIs and blank nodes. A graph SHALL have set semantics for its triples. Dataset blank-node identities SHALL be distinct from the labels used to serialize them.

#### Scenario: One triple belongs to two graphs
- **WHEN** the same triple is added twice to the default graph and once to a named graph
- **THEN** it is present once in each graph and has two distinct quad addresses

### Requirement: Graphs and statements can be annotation and link targets

A graph target SHALL identify its dataset revision and graph location. A statement target SHALL identify its dataset revision and quad. Authored claims sharing a quad SHALL be able to carry distinct identities and provenance. Changes to a dataset SHALL leave existing targets anchored to their original revision.

#### Scenario: Evidence supports an approved statement
- **WHEN** a text selection links to a quad in the approved graph and the same triple is also present in the draft graph
- **THEN** the link resolves to the approved quad and carries the original dataset revision

#### Scenario: Review an empty graph
- **WHEN** a reviewer annotates an empty named graph
- **THEN** the annotation can resolve to that graph independently of any quad

#### Scenario: Independent claims share a quad
- **WHEN** two authored claims cite the same quad with different provenance
- **THEN** a consumer can identify and annotate each claim independently

### Requirement: N-Quads exchanges RDF 1.1 datasets

Callers SHALL be able to read and write UTF-8 RDF 1.1 N-Quads. An absent graph label SHALL denote the default graph. Parsing SHALL retain blank node identity across repeated labels within one input and allocate fresh identities for independent inputs. Invalid syntax SHALL return typed diagnostics with locations where available.

#### Scenario: Dataset round trip
- **WHEN** an encodable dataset contains default and named graphs, language-tagged and typed literals, and a blank node shared across graphs
- **THEN** writing and reading N-Quads produces an isomorphic dataset

#### Scenario: Independent inputs use the same label
- **WHEN** two N-Quads inputs both use the blank-node label `_:b0` and their results are combined
- **THEN** the imported blank nodes remain distinct

#### Scenario: Illegal graph label
- **WHEN** an N-Quads line supplies a literal as its fourth term
- **THEN** the reader reports an invalid graph label at that term

### Requirement: A writer reports empty named graphs that N-Quads cannot preserve

Writing a dataset with an empty named graph SHALL report the affected graph. The caller SHALL explicitly select omission before a lossy export is emitted.

#### Scenario: Empty named graph at interchange
- **WHEN** the caller attempts to write a dataset containing an empty named graph without allowing omission
- **THEN** the writer reports the graph and returns a typed failure before emitting data

### Requirement: N-Triples exchanges the default graph

Callers SHALL be able to read and write UTF-8 RDF 1.1 N-Triples. Reading SHALL put every triple in the default graph. Blank-node identity and diagnostics SHALL behave as for N-Quads. A graph label on a line SHALL be a syntax error.

#### Scenario: Default graph round trip
- **WHEN** a dataset with only a default graph, holding typed and language-tagged literals and shared blank nodes, is written as N-Triples and read back
- **THEN** the result is isomorphic to the original

#### Scenario: Graph label in N-Triples
- **WHEN** an N-Triples line has a fourth term
- **THEN** the reader reports a syntax error at that term

### Requirement: Writers report dataset content a format cannot hold

Writing a dataset with named graphs as N-Triples or RDF/XML SHALL report every named graph as a typed failure before any data is emitted. The caller SHALL be able to explicitly select writing the default graph only, and the result SHALL name the graphs it omitted.

#### Scenario: Named graph to N-Triples
- **WHEN** a caller writes a dataset with a named graph as N-Triples without allowing omission
- **THEN** the writer returns a typed failure that names the graph, and emits no bytes

#### Scenario: Explicit omission
- **WHEN** the caller writes the same dataset as RDF/XML with omission allowed
- **THEN** the output holds the default graph, and the result names the omitted graph

### Requirement: Datasets have a canonical form

Callers SHALL be able to canonicalize a dataset according to W3C RDF Dataset Canonicalization (RDFC-1.0). Every writer SHALL offer canonical output, in which datasets that are isomorphic produce identical bytes. Canonicalization SHALL stop with a typed failure when it exceeds a work limit the caller can set.

#### Scenario: Isomorphic datasets
- **WHEN** the same N-Quads input is read twice, giving fresh blank-node identities, and both datasets are written canonically
- **THEN** the two outputs are byte-identical

#### Scenario: W3C canonicalization tests
- **WHEN** the canonicalizer runs the pinned W3C RDFC-1.0 test suite
- **THEN** every evaluation test produces the expected canonical N-Quads

#### Scenario: Pathological input
- **WHEN** a dataset needs more canonicalization work than the caller's limit
- **THEN** canonicalization returns a typed failure, and does not run indefinitely

### Requirement: RDF/XML writes the default graph

Callers SHALL be able to write the default graph as RDF/XML, either as a document or as an `rdf:RDF` element for embedding in another XML document. The writer SHALL preserve IRIs, blank nodes, datatypes and language tags. A predicate that cannot be split into a namespace and an XML local name SHALL be a typed failure that names the predicate.

#### Scenario: Round trip through RDF/XML
- **WHEN** a default graph with IRIs, blank nodes, typed literals and language-tagged literals is written as RDF/XML and read back
- **THEN** the result is isomorphic to the original

#### Scenario: Predicate without an XML local name
- **WHEN** the graph has a predicate whose IRI ends in a character that cannot end an XML local name, such as `https://example.org/p/1`
- **THEN** the writer returns a typed failure that names the predicate

#### Scenario: Deterministic output
- **WHEN** the same graph is written twice, with statements added in different orders
- **THEN** both outputs are byte-identical

### Requirement: RDF/XML reading follows RDF 1.1 XML Syntax

Callers SHALL be able to read RDF 1.1 RDF/XML from UTF-8 bytes or from an XML element already parsed from a larger document. The reader SHALL pass the pinned W3C RDF/XML syntax tests. It SHALL refuse document type declarations and SHALL resolve no external resource. Errors SHALL be typed and carry a line and column where available.

#### Scenario: W3C conformance
- **WHEN** the reader runs the pinned W3C RDF 1.1 XML test suite
- **THEN** every positive syntax and evaluation test produces the expected graph, and every negative syntax test is rejected. The only exceptions are tests that need DTD processing, which the pinned fixture provenance lists by name with the reason

#### Scenario: Read an embedded element
- **WHEN** a caller passes the `rdf:RDF` element extracted from an SVG metadata element
- **THEN** the reader uses namespace bindings inherited from the enclosing SVG, and returns the graph

#### Scenario: Document type declaration in RDF/XML
- **WHEN** the input declares a DOCTYPE with an entity
- **THEN** the reader returns a typed error, and makes no file or network access
