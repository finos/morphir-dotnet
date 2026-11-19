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
Every reader SHALL report diagnostics with a stable code, severity, message and a half-open span in UTF-16 code units with one-based line and column, where CRLF counts as one line break. A reader SHALL recover at the next statement boundary and SHALL never drop a statement silently.

#### Scenario: Two errors in one source
- **WHEN** a state diagram has two malformed transitions on lines 3 and 7
- **THEN** reading reports both, each with its line and column, and keeps the valid statements between them

### Requirement: Lossless source reproduction
Every text reader SHALL keep a syntax tree that reproduces the input exactly, including comments, directives, front matter, whitespace, CRLF, non-BMP characters and invalid regions.

#### Scenario: Reproduce an invalid source
- **WHEN** a PlantUML state block with an unterminated note is read
- **THEN** reproducing its syntax tree gives the original text byte for byte

### Requirement: Canonical printing
Every Mermaid and PlantUML family SHALL have a canonical printer that writes the typed syntax in the native language. Reading printed text SHALL give an equivalent syntax tree, and printing it again SHALL give identical text. An invalid syntax tree SHALL produce diagnostics and no text.

#### Scenario: Print twice
- **WHEN** an architecture diagram is printed, read and printed again
- **THEN** both printed texts are identical

### Requirement: Bounded processing
Every reader, converter and printer SHALL enforce documented limits on source size, output size, nesting depth, element counts, diagnostics and work, checked before allocation where an expansion is involved. Exceeding a limit SHALL produce a limit diagnostic distinct from invalid input and SHALL NOT produce a partial result or a stack overflow.

#### Scenario: Deep nesting
- **WHEN** a state diagram nests composites 10,000 levels deep
- **THEN** reading returns a depth-limit diagnostic and no diagram, without a stack overflow

### Requirement: Inert source content
Readers SHALL treat directives, configuration, callbacks, includes, links and labels as data. They SHALL NOT read files, use the network, execute code, expand includes or change the caller's options.

#### Scenario: PlantUML include
- **WHEN** a PlantUML block contains `!include https://example.org/x.puml`
- **THEN** reading reports a refusal diagnostic and makes no network request
