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
