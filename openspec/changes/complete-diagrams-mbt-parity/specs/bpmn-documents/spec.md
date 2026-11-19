# Spec Delta

## Purpose

Reads, validates, traverses and writes BPMN 2.0.2 XML documents independently of diagram rendering, keeping the source text, namespaces, diagram interchange data and extension content.

## ADDED Requirements

### Requirement: Safe XML reading
The BPMN reader SHALL prohibit DTD processing and use no XML resolver. A DTD or external entity declaration SHALL produce a `prohibited-declaration` diagnostic and no document, without expansion.

#### Scenario: Billion laughs
- **WHEN** the input declares nested entities in a DTD
- **THEN** reading fails with a prohibited-declaration diagnostic and allocates no expansion

### Requirement: Namespace-aware typed document
The reader SHALL identify elements and attributes by namespace URI and local name, and SHALL represent the BPMN 2.0.2 semantic elements, including processes, collaborations, choreographies and conversations, keeping present and absent values, child order, documentation and expressions as text. The typed view gives each element its kind, identity, fields and references over the lossless element tree; it does not generate a class per schema type.

#### Scenario: Prefix alias
- **WHEN** a document binds the BPMN namespace to prefix `b` instead of `bpmn`
- **THEN** the typed document is the same as with `bpmn`

### Requirement: Diagram interchange and extensions
The reader SHALL keep DI planes, shape bounds, edge waypoints and label bounds with decimals unchanged, and SHALL keep foreign-namespace attributes and extension elements in order.

#### Scenario: Decimal precision
- **WHEN** a waypoint has `x="123.4567890123456789"`
- **THEN** writing the document gives the same text for that value

### Requirement: Validation and references
Validation SHALL distinguish XML, structure and reference findings, including duplicate IDs, missing required fields and wrong reference targets. An unresolved reference SHALL keep its original value with a finding.

#### Scenario: Duplicate IDs
- **WHEN** two tasks share an ID
- **THEN** validation reports the duplicate and looking up that ID reports an ambiguity

### Requirement: Deterministic writing
The writer SHALL produce documented canonical and pretty formats that keep order, IDs, references and extensions. Writing a canonical document twice SHALL give identical bytes.

#### Scenario: Canonical round trip
- **WHEN** a document is written canonically, read and written again
- **THEN** both outputs are identical

### Requirement: Traversal and graph queries
The library SHALL provide read-only traversal of the document tree with continue, skip-children and stop controls, independent of call-stack depth, and bounded reachability and shortest-route queries over caller-selected edge kinds (containment, sequence flow, message flow, association, reference). A query that reaches a reference into an imported document SHALL report a missing element; imports are not resolved.

#### Scenario: Deep tree
- **WHEN** a document nests 10,000 elements
- **THEN** traversal visits all of them without a stack overflow

#### Scenario: Cycle
- **WHEN** a reachability query starts inside a loop of sequence flows
- **THEN** it terminates and returns each element once
