# Spec Delta

## Purpose

Lets a caller hand any supported diagram source to one entry point, learn which family it is, get a typed model or source-located diagnostics, and print text families back in a canonical form. The supported inputs are Mermaid, PlantUML and BPMN 2.0 XML, following diagrams.mbt at 60a8dd1 and its pinned baselines: Mermaid 12.2.0, PlantUML 1.2026.9, ZenUML core 3.47.9 and BPMN 2.0.2.

## ADDED Requirements

### Requirement: Family detection
The library SHALL detect the family of Mermaid input from its header, of PlantUML input from its `@start` keyword, and of BPMN from its root element, and SHALL return a typed diagram of that family. Keywords inside comments, strings or labels SHALL NOT drive detection.

#### Scenario: Mermaid header selects the family
- **WHEN** the source starts with `stateDiagram-v2`
- **THEN** reading returns a state diagram

#### Scenario: Keyword in a comment is ignored
- **WHEN** a flowchart contains the comment `%% sequenceDiagram`
- **THEN** reading returns a flowchart

### Requirement: Unsupported families are reported
The library SHALL report a stable `unsupported-kind` diagnostic, and return no diagram, for a recognised header of a family it does not support, such as `classDiagram` or a class `@startuml` block.

#### Scenario: Class diagram
- **WHEN** the source starts with `classDiagram`
- **THEN** reading fails with an unsupported-kind diagnostic and no diagram

### Requirement: Source-located diagnostics
Every reader SHALL report diagnostics with a stable code, severity, message and a half-open span in UTF-16 code units with one-based line and column, where CRLF counts as one line break. The readers of the new families SHALL continue at the next statement after an error, report every error up to the diagnostic limit, and never drop a statement silently.

#### Scenario: Two errors in one source
- **WHEN** a state diagram has two malformed transitions on lines 3 and 7
- **THEN** reading reports both, each with its line and column, and returns no diagram

### Requirement: Lossless source reproduction
Every parsed document SHALL keep the exact source it was read from, including comments, directives, front matter, whitespace, CRLF, non-BMP characters and a byte order mark.

#### Scenario: Reproduce a source with CRLF and an emoji
- **WHEN** a PlantUML state block with CRLF line breaks and an emoji label is read
- **THEN** the document's source equals the original text code unit for code unit

### Requirement: Canonical printing
Every Mermaid and PlantUML family SHALL have a canonical printer that writes the model in its native language. Printing what was printed SHALL give identical text, and a model the language cannot write SHALL produce diagnostics and no text. PlantUML activity diagrams print in the legacy graph dialect, in which decision and merge roles and lanes read back as plain activities outside lanes.

#### Scenario: Activity printed as a graph
- **WHEN** a modern activity diagram with an `if` is printed with PlantUml.Print
- **THEN** the text is a legacy `(*) -->` graph, and printing what it reads back gives the same text

#### Scenario: Print twice
- **WHEN** an architecture diagram is printed, read and printed again
- **THEN** both printed texts are identical

### Requirement: Bounded processing
Every reader, converter and printer SHALL enforce documented limits on source size, output size, nesting depth, element counts, diagnostics and work, checked before allocation where an expansion is involved. Exceeding a limit SHALL produce a limit diagnostic distinct from invalid input and SHALL NOT produce a partial result or a stack overflow.

#### Scenario: Deep nesting
- **WHEN** a flowchart or state diagram nests subgraphs or composites 10,000 levels deep
- **THEN** reading returns a depth-limit diagnostic and no diagram, without a stack overflow

### Requirement: Inert source content
Readers SHALL treat directives, configuration, callbacks, includes, links and labels as data. They SHALL NOT read files, use the network, execute code, expand includes or change the caller's options.

#### Scenario: PlantUML include
- **WHEN** a PlantUML block contains `!include https://example.org/x.puml`
- **THEN** reading reports a refusal diagnostic and makes no network request
