# Spec Delta

## Purpose

Produces Morphir IR documents in a requested family and format version from typed models, as canonical JSON that existing Morphir readers accept. Any requested version that cannot hold the model exactly is refused instead of written with losses.

## ADDED Requirements

### Requirement: Format versions are exact Morphir format versions
A format version SHALL be either an integer classic version or a SemVer version, which can have pre-release and build parts. It SHALL never be reduced to its major number. An integer `N` and SemVer `N.0.0` SHALL be the same version. A pre-release SHALL match only itself.

#### Scenario: Pre-release is distinct
- **WHEN** a writer declares `4.1.0-draft.1` and a caller requests `4.1.0` or `4.1.0-draft.2`
- **THEN** that writer is not selected for either request

#### Scenario: Release equivalence
- **WHEN** a caller requests `3.0.0` and the classic writer declares `3`
- **THEN** the classic writer is selected

### Requirement: Writers declare the exact versions they write
Each writer SHALL declare the exact format versions it writes. Selection SHALL use the requested version against those declarations, and SHALL NOT use version ranges or a nearest match. The `formatVersion` member SHALL be the integer `N` for classic versions and `N.0.0` releases, and the exact SemVer string otherwise.

#### Scenario: Canonical spelling
- **WHEN** a model is written for requested version `4.0.0`, and also for a declared `4.1.0-draft.1`
- **THEN** the first document has `"formatVersion": 4`, and the second has `"formatVersion": "4.1.0-draft.1"`

### Requirement: Every supported format version has a writer
The system SHALL write library distributions as JSON for classic format versions 1, 2 and 3 and for IR v4 (`4`). Each writer SHALL use its version's tag spelling, access wrapper, module list shape and distribution tag. A new version, including a pre-release, SHALL be added by declaring a writer, not by changing an existing one.

#### Scenario: Version-specific spelling
- **WHEN** one model with a let-definition and a public module is written as versions 1, 2 and 3
- **THEN** version 1 uses `let_definition` and `["public", ...]`, version 2 uses `let_definition` and `{"access":"Public"}`, and version 3 uses `LetDefinition` and `{"access":"Public"}`

#### Scenario: Unsupported output version
- **WHEN** a caller requests format version 5 or a Specs or Application distribution
- **THEN** writing fails with a diagnostic that names the requested version or kind and the supported set, and writes no document

### Requirement: Written documents round-trip through the readers
A document written for a version SHALL be accepted by the existing reader for that version. Reading it back SHALL give a model equal to the written model, including names, order, access, documentation and attributes.

#### Scenario: Corpus round trip
- **WHEN** each nonempty classic and v4 fixture in the repository is read, written in its own version, and read again
- **THEN** the second model equals the first for every fixture

#### Scenario: Schema validity
- **WHEN** a written classic document is checked against the bundled JSON schema for its version
- **THEN** it is valid

### Requirement: Output is canonical and deterministic
Writers SHALL produce the same bytes for equal models: object members and arrays in a fixed order, no insignificant whitespace in compact mode, and two-space indentation in indented mode. Numbers SHALL use the exact lexical form that each version allows, and SHALL NOT go through binary floating-point rounding.

#### Scenario: Decimal and integer exactness
- **WHEN** a v4 model holding the integer 9007199254740993 and the decimal `1.10` is written
- **THEN** the JSON contains those exact lexemes, and reading it back gives the same values and decimal scale

### Requirement: Each family is built natively from one lowering
A frontend SHALL lower its source once, against a contract that does not depend on the IR family. Each IR family SHALL build its own nodes natively from that lowering. Classic output SHALL NOT be derived from a v4 model, and v4 output SHALL NOT be derived from a classic model. Adding a family or format version SHALL NOT change any frontend.

#### Scenario: Same source in every version
- **WHEN** one F# source set is compiled with IR versions 1, 2, 3 and 4
- **THEN** each document reads back through its own reader, and F# code generated from each one gives the same results for the same calls

#### Scenario: Classic output without v4
- **WHEN** a build of the tool has no IR v4 support registered and compiles with IR version 3
- **THEN** the compile succeeds, and its output equals the IR version 3 output of a build with v4 support

#### Scenario: Classic member without documentation
- **WHEN** a type or value without documentation is built in the classic family and written as versions 1, 2 and 3
- **THEN** by default each version writes the member without a documentation wrapper, and with the `EmptyWrapper` write option each version writes it in the `{"doc": "", "value": ...}` wrapper that morphir-elm writes; the reader accepts both forms

### Requirement: Unrepresentable content is refused
When the requested family cannot hold a model element exactly, building SHALL fail with `morphir.unrepresentable`. The diagnostic SHALL name the element, its source location and the requested version, and no document SHALL be written. Nothing SHALL be rounded, dropped or renamed to make the model fit. Source locations, which classic IR has no place for, SHALL NOT count as model elements for this rule.

#### Scenario: Integer outside the classic range
- **WHEN** a model given to the classic builder has an integer literal larger than the 64-bit range (F# source cannot write one, so a frontend that can, or a test, supplies it) and IR version 3 is requested
- **THEN** the result is a `morphir.unrepresentable` error at that literal, with no document, and the same source succeeds with IR version 4

#### Scenario: Supplementary character in a classic character literal
- **WHEN** a model given to the classic builder has a character literal outside the Basic Multilingual Plane (an F# `char` is one UTF-16 unit and cannot hold one) and IR version 2 is requested
- **THEN** the result is a `morphir.unrepresentable` error, with no document

### Requirement: Supplied input is not migrated
No stage SHALL convert a model from one IR family to another. Loading an existing IR document for generation SHALL keep its family and version, and the backend SHALL receive that family's model directly.

#### Scenario: Generation keeps the input version
- **WHEN** `morphir generate` reads a v4 document
- **THEN** the backend receives a v4 model, and no classic document is created on the way
