# architecture-diagrams Specification

## Purpose
Represents architecture diagrams as groups, services, junctions and explicit connections with ports, independent of source syntax, and renders them with symbols, group boundaries and alignment constraints as themed standalone SVG.

## Requirements

### Requirement: Architecture model
The library SHALL represent groups, services, junctions, connections and row and column alignments as immutable values in declaration order. Each connection SHALL keep two endpoints with a side (top, bottom, left or right), an optional group-boundary attachment, an optional label and independent arrowheads.

#### Scenario: Bidirectional edge
- **WHEN** an edge has arrowheads at both ends
- **THEN** the model records both heads and neither endpoint is treated as the source by position

### Requirement: Architecture validation
Validation SHALL report duplicate IDs, missing endpoints, invalid parents, ownership cycles, ineligible group attachments, invalid ports and invalid alignment members with stable codes. Conflicting alignments, such as `A` before `B` in one row and after it in another, SHALL produce a constraint finding.

#### Scenario: Conflicting rows
- **WHEN** two row constraints order `A,B` and `B,A`
- **THEN** rendering returns a constraint finding and no SVG

### Requirement: Port and boundary routing
Each connection SHALL attach to its declared sides, route to the actual node or group outline, and avoid unrelated shapes, labels and group titles. Parallel edges and self-connections SHALL stay distinguishable.

#### Scenario: Declared sides
- **WHEN** an edge is `api:R --> L:db`
- **THEN** it leaves the right side of `api` and enters the left side of `db`

### Requirement: Deterministic placement
Placement SHALL keep nested group containment and alignment order, and SHALL break ties between disconnected elements by declaration order. Equal inputs SHALL give identical SVG bytes.

#### Scenario: Render twice
- **WHEN** the same architecture diagram is rendered twice
- **THEN** the two SVG outputs are identical

### Requirement: Service symbols
Groups and services SHALL show their icon from the symbol catalogue. Quoted icon text SHALL render as escaped text and never as an asset address.

#### Scenario: Built-in symbol
- **WHEN** a service is declared `service db(database)[Orders]`
- **THEN** the SVG shows the database symbol with the label `Orders`
