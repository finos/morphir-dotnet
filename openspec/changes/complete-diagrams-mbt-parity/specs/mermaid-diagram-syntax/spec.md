# Spec Delta

## Purpose

Defines the Mermaid 12.2.0 syntax that `Morphir.Diagrams` reads beyond its first flowchart and sequence subset: the rest of the flowchart and sequence grammars, and the state, architecture, swimlane and ZenUML grammars. Each grammar converts to a typed model with reference-compatible rules; presentation data is kept but never executed.

## ADDED Requirements

### Requirement: Complete flowchart grammar
The flowchart reader SHALL accept the Mermaid 12.2.0 flowchart grammar: the shape catalogue (each shape drawn as the nearest built-in shape), shape data, Markdown and entity-coded labels, edge identifiers, open labelled links, links to subgraphs, repeated subgraphs and the presentation statements. It SHALL accept or refuse every fixture of the pinned reference as Mermaid 12.2.0 does.

#### Scenario: Classes and a click callback
- **WHEN** a flowchart declares `classDef hot fill:#f00`, `class A hot` and `click A callback`
- **THEN** reading succeeds with one unapplied-presentation warning per statement, and the callback is never run

#### Scenario: Pinned reference
- **WHEN** the 148 fixtures of the pinned flowchart reference are read
- **THEN** each is accepted or refused as Mermaid 12.2.0 does, with its node identifiers and edge count

### Requirement: Complete sequence grammar
The sequence reader SHALL accept every Mermaid 12.2.0 sequence statement and arrow form, including participant kinds `boundary`, `control`, `entity`, `database`, `collections` and `queue`, `create` and `destroy`, `box` groups, `critical`/`option`, `break`, `rect` colours, half-arrows, `link`/`links`, central connections, and `autonumber <start> <step>`.

#### Scenario: Creation and destruction
- **WHEN** a sequence has `create participant B` before `A->>B: new` and `destroy B` before `A->>B: stop`
- **THEN** B's box is drawn at the creation row and its lifeline ends with a cross after the last message

#### Scenario: Destroyed participant used again
- **WHEN** a message targets a participant after its destruction
- **THEN** reading reports `mermaid/destroyed-participant` at that message

### Requirement: State diagram grammar
The reader SHALL accept `stateDiagram` and `stateDiagram-v2`: declarations, aliases, descriptions, implicit states, transitions with labels, `[*]` initial and final states scoped to their region, composites, `--` concurrent regions, `<<choice>>`, `<<fork>>`, `<<join>>`, notes and `direction`.

#### Scenario: Scoped initial states
- **WHEN** a composite state `Active` contains its own `[*] --> Idle`
- **THEN** the converted model has distinct initial pseudostates at the top level and inside `Active`

### Requirement: Architecture grammar
The reader SHALL accept `architecture-beta`: groups, services, junctions, every port and arrow form, labelled edges, `{group}` endpoints, row and column alignment, quoted icon text, title and accessibility statements. A group ID used directly as an endpoint SHALL produce an endpoint diagnostic.

#### Scenario: Group-boundary edge
- **WHEN** an edge is written `db{group}:R --> L:api`
- **THEN** the connection keeps `db` as its endpoint and records a group-boundary attachment

### Requirement: Swimlane grammar
The reader SHALL accept `swimlane-beta` using the shared flowchart grammar, with `TB` as the default direction and `TD` read as `TB`. Subgraphs SHALL become lanes; repeated subgraph IDs SHALL merge their members, with the first completed declaration supplying the title.

#### Scenario: Same body, two headers
- **WHEN** the same body is read under `swimlane-beta` and under `flowchart`
- **THEN** the first gives lanes and the second gives groups

### Requirement: ZenUML grammar
The reader SHALL accept `zenuml` source: participants, aliases, annotators, synchronous and asynchronous messages, nested calls with bodies, creation, assignments, `return` and `@return`/`@reply`, comments, `while`/`for`/`forEach`/`loop`, `if`/`else`, `opt`, `par` and `try`/`catch`/`finally`. It SHALL convert to the shared sequence model, keeping try, catch and finally as distinct ordered sections. Annotators that name a participant kind choose it; other annotators give a plain participant and a warning. A ZenUML printer writes calls without bodies.

#### Scenario: Exception fragment
- **WHEN** a ZenUML body has `try { A.save() } catch (e) { A.log() } finally { A.close() }`
- **THEN** the sequence model has one try frame with three sections whose roles are try, catch and finally

### Requirement: Presentation data stays inert
Styles, classes, callbacks, links, configuration and directives SHALL stay in the document's kept source, and each unapplied statement SHALL produce a warning on the document and on the render result. Canonical printers write the model and do not print presentation. Source configuration SHALL NOT override the caller's render options.

#### Scenario: Init directive asks for a theme
- **WHEN** the source has `%%{init: {"theme": "forest"}}%%` and the caller renders with Cobalt
- **THEN** the SVG uses Cobalt and a diagnostic reports the unapplied directive
