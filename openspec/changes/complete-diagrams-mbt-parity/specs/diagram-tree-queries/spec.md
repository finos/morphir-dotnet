# Spec Delta

## Purpose

Lets callers select and project nodes of immutable trees, such as BPMN documents, with one versioned, bounded structural query language that never infers graph edges.

## ADDED Requirements

### Requirement: Versioned query language
Version 1 of the language SHALL support exact kinds, categories, wildcards, ordered direct-child patterns, named and absent child fields, alternatives, named captures and native predicates for attribute equality, inequality, existence, absence and membership. Unsupported Tree-sitter features SHALL fail to compile with a stable code and span.

#### Scenario: Capture user tasks
- **WHEN** the query `(userTask @name="Approve") @task` runs over a BPMN document
- **THEN** each matching task is returned with capture `task`

#### Scenario: Quantifier
- **WHEN** a query uses `+`
- **THEN** compilation fails with the unsupported-feature code at that position

### Requirement: Deterministic matches
Matches SHALL be ordered by candidate preorder, then pattern order, then child positions, and SHALL keep pattern index, capture names, node identities and source locations.

#### Scenario: Two patterns match one node
- **WHEN** two alternatives both match the same node
- **THEN** both matches are returned, in pattern order

### Requirement: Bounded execution
Compilation and evaluation SHALL enforce limits on source, patterns, depth, work, pending matches, captures and results, cumulative across batches. Exhaustion SHALL name the bound. Batch size SHALL NOT change results.

#### Scenario: Batch sizes
- **WHEN** the same query is read in batches of 1, 64 and 256
- **THEN** all three give the same matches

### Requirement: Containment only
Patterns SHALL follow only the tree's containment. IDs, reference attributes, coordinates and labels SHALL NOT create edges.

#### Scenario: Reference attribute
- **WHEN** a sequence flow's `targetRef` names a task
- **THEN** a child pattern under the flow does not match that task
