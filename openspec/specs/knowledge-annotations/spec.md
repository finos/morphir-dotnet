# knowledge-annotations Specification

## Purpose

Let consumers attach reviews, dictionary mentions and other metadata to a precise selection of a document revision, and link typed knowledge resources across formats.

## Requirements

### Requirement: Document and revision identities locate immutable text

A document identity SHALL use an absolute IRI independent of its storage path. A revision identifier SHALL be a nonempty key scoped to its document. A provider SHALL resolve the document and revision pair to one immutable text snapshot.

#### Scenario: Review survives a move
- **WHEN** a concept moves to a different bundle path while the document identity and revision remain available
- **THEN** its existing selection resolves to the same snapshot and text ranges

#### Scenario: A newer revision exists
- **WHEN** text is inserted before a reviewed passage in a later revision
- **THEN** the original annotation still refers to its original revision and offsets

### Requirement: A selection groups disjoint ranges in one revision

A selection SHALL contain at least one nonempty, in-bounds, half-open UTF-16 range. Ranges SHALL belong to the same snapshot and SHALL be non-overlapping and presented in source order. Selection construction SHALL report invalid ranges as typed failures.

#### Scenario: One annotation covers two passages
- **WHEN** a reviewer selects two disjoint passages in reverse input order
- **THEN** one target contains both ranges in source order

#### Scenario: Invalid selection ranges
- **WHEN** a selection contains an empty or out-of-bounds range, overlapping ranges or no ranges
- **THEN** the constructor reports the corresponding failure

### Requirement: Callers can define typed annotation bodies and targets

An annotation SHALL carry its own identity, a typed target and body, and provenance. Callers SHALL be able to define additional body and target types through the shared API. Creating an annotation SHALL require no mutation of the source document.

#### Scenario: Consumer-specific review metadata
- **WHEN** a caller annotates a text selection with a custom review body
- **THEN** another caller using the same type can read the body and target, and the document bytes are unchanged

#### Scenario: Dictionary mention
- **WHEN** a selected phrase is annotated with the absolute IRI of a dictionary concept
- **THEN** a consumer can obtain the exact selected text and the referenced concept identity from the annotation

### Requirement: A directional link can reference typed knowledge targets

A link SHALL carry its own identity, a source, an absolute predicate IRI and a target. Callers SHALL be able to use compatible typed endpoints from document, dictionary and RDF models without changing the shared link API.

#### Scenario: Text supports a graph statement
- **WHEN** a caller links a versioned selection to a statement occurrence using an evidence predicate
- **THEN** traversal can inspect the direction, relation and both endpoint identities
