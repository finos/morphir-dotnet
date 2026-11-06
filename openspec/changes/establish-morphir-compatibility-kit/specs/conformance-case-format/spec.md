# Spec Delta

## Purpose

Defines the portable, versioned data of the Morphir Compatibility Kit: cases, suites, profiles and comparison rules. Any Morphir implementation, in any language, can then be checked against the same expectations.

## ADDED Requirements

### Requirement: The kit has its own version
The kit SHALL declare a SemVer kit version, separate from MEP, IR format and CLI versions. A change that can turn a passing implementation into a failing one SHALL increase the major version, or the minor version on the `0.y.z` line. A pre-release kit SHALL match only itself.

#### Scenario: Stricter expectation
- **WHEN** a case's expected IR is corrected so that a previously passing output now fails
- **THEN** the kit version change is a breaking change under these rules, and the change log names the case

### Requirement: Cases are Gherkin scenarios
Cases SHALL be scenarios in `.feature` files under `conformance/suites/<suite>/`, and an official Gherkin parser SHALL accept them. Each scenario name SHALL start with a stable id `<suite>-NNNN`. Ids SHALL be unique in the kit and SHALL NOT be reused. A case SHALL express its sources, settings, expected observations, classification, basis and evidence.

#### Scenario: Read without .NET
- **WHEN** a program in another language parses the kit with an official Gherkin parser and reads the step vocabulary
- **THEN** it can list every case, its sources, its settings and its expected observations

#### Scenario: Malformed case
- **WHEN** a scenario has a duplicate id, an id outside its suite's prefix, an unknown classification tag or no expected observation
- **THEN** kit validation fails, naming the file, the scenario and the problem

### Requirement: Steps come from a closed, versioned vocabulary
Every step SHALL match the grammar of the kit's declared MCK step vocabulary version. A linter SHALL reject any other step. Each runner SHALL declare the vocabulary versions it implements, and a run SHALL refuse a kit whose vocabulary version it does not implement.

#### Scenario: Unknown step
- **WHEN** a feature uses the step `Then the output looks right`, which the vocabulary does not define
- **THEN** the linter fails, naming the file, the line and the step

#### Scenario: Newer vocabulary
- **WHEN** a runner that implements vocabulary 1 is given a kit that declares vocabulary 2
- **THEN** the run stops before any case, naming both versions

### Requirement: Features do not name an implementation
A feature SHALL NOT name an adapter, executable or implementation. The implementation under test SHALL be chosen when the runner starts, so the same features run against every implementation.

#### Scenario: Adapter in a feature
- **WHEN** a step or tag names the `cli` adapter or a specific executable
- **THEN** the linter fails, naming the step or tag

### Requirement: Matrices use outlines with explained exclusions
A case that varies over format versions, profiles or other dimensions SHALL be a `Scenario Outline` with `Examples`. Excluded combinations SHALL be rows in `Examples` blocks tagged `@excluded`, with a non-empty `reason` column. A combination that is neither listed nor excluded SHALL fail validation.

#### Scenario: Excluded version
- **WHEN** an outline lists IR versions `3` and `4` and excludes `1` and `2` in an `@excluded` block with reasons
- **THEN** validation passes, and the report lists the excluded rows with their reasons

### Requirement: Artifacts are inline or sidecar files
Small artifacts SHALL be docstrings with a content type, for example `"""fsharp` or `"""json`. Large expectations, for example expected IR, SHALL be sidecar files next to the feature, referenced by a step. Every sidecar file SHALL be referenced, and SHALL be part of the case-set digest.

#### Scenario: Unreferenced sidecar
- **WHEN** a suite directory has an expected-IR file that no step references
- **THEN** validation fails, naming the file

### Requirement: Literate documentation is generated
The documentation pages for the kit SHALL be literate Markdown generated from the features. They SHALL show the prose, sources and expectations of each case, and SHALL NOT be edited by hand. A check SHALL fail when a generated page differs from what the current features produce.

#### Scenario: Stale page
- **WHEN** a feature changes and its generated page is not generated again
- **THEN** the documentation check fails, naming the page

### Requirement: Expected IR is stored for each exact format version
An expected IR document SHALL be a complete sidecar file for exactly one format version, named with the IR's own version spelling. A pre-release version SHALL have its own file. A case SHALL state, for each format version, whether it applies, and if not, why, using an outline row or an `@excluded` row.

#### Scenario: Missing version row
- **WHEN** a frontend case gives expected IR for version `4` but says nothing about version `3`
- **THEN** kit validation fails, naming the case and the missing version

### Requirement: Expected diagnostics compare stable members
An expected diagnostic SHALL state its severity, its code, and its location as a document and a zero-based UTF-16 range. A message SHALL be compared only when the case marks it as normative. An expected result SHALL state whether extra `warning`, `info` or `hint` diagnostics are allowed.

#### Scenario: Message text changes
- **WHEN** an implementation reports the expected code and range with a reworded message
- **THEN** the observation passes, unless the case marks the message as normative

### Requirement: Comparison rules are explicit
The default comparison SHALL be exact, after the documented canonical form for the observation kind. A case that needs a semantic comparison SHALL name each normalization it uses from the kit's normalization catalog. An unnamed normalization SHALL NOT apply.

#### Scenario: Unnamed list-order leniency
- **WHEN** an implementation writes the `values` of a classic module in a different order and the case names no normalization
- **THEN** the observation fails, and the report shows the first difference

#### Scenario: Named normalization
- **WHEN** a case names the normalization `ignore-json-member-order`
- **THEN** only JSON object member order is ignored, and every other difference still fails

### Requirement: Suites and profiles organize cases
Every case SHALL belong to exactly one suite. A profile SHALL name the suites and the capability claims that select them. Unknown suites or profiles SHALL fail kit validation.

#### Scenario: Frontend-only profile
- **WHEN** a profile in the kit manifest selects the F# frontend suite for implementations that claim the `fsharp` language as a frontend
- **THEN** that profile needs no backend, generation or legacy-protocol case
