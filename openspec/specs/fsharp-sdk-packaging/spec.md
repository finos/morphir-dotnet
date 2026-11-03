# fsharp-sdk-packaging Specification

## Purpose

Offer a language-specific F# SDK package while preserving existing generated source and runtime type identities and checking covered behavior against independent conformance evidence.

## Requirements

### Requirement: Canonical and compatibility packages

Morphir.SDK.FSharp SHALL be the canonical F# SDK package. Morphir.SDK SHALL remain installable as a compatibility package resolving the same runtime implementation. Installing either or both SHALL NOT create competing copies of SDK types.

#### Scenario: New and legacy package installation

- **WHEN** equivalent F# consumers install the canonical package, the legacy package, or both
- **THEN** each resolves one SDK runtime implementation and builds without ambiguous types or duplicate runtime assemblies

### Requirement: Source and binary identity preservation

Existing Morphir.SDK namespaces and runtime assembly/type identities SHALL remain valid during this migration. Introducing the canonical package SHALL NOT require editing previously generated F# source or recompiling existing binaries solely to rename SDK identities.

#### Scenario: Previously generated source

- **WHEN** an existing generated F# file imports Morphir.SDK and references Order
- **THEN** it compiles unchanged when the consumer installs Morphir.SDK.FSharp

#### Scenario: Previously compiled consumer

- **WHEN** a consumer binary built against the old runtime is run with the migrated runtime
- **THEN** SDK type loading and covered behavior succeed without type-identity substitution errors

### Requirement: Generated F# runtime references

New F# generation consumers SHALL document and resolve the canonical runtime package while continuing to accept previously generated source and existing generation entry points. The migration SHALL NOT change IR decoding, generated public declaration names or accepted model versions.

#### Scenario: Current generator consumers

- **WHEN** existing classic and v4 fixtures are generated and compiled after migration
- **THEN** they use the canonical runtime package and preserve their generated API and model compatibility

#### Scenario: Legacy dependency declaration

- **WHEN** a consumer retains its Morphir.SDK package reference
- **THEN** its unchanged generated source remains buildable through the compatibility package

### Requirement: Covered SDK semantic corrections

The F# runtime SHALL pass the cases admitted for it by the shared conformance baseline. Covered semantic corrections SHALL have regression tests and migration notes identifying observable changes; they SHALL NOT silently rewrite the reference expectations.

#### Scenario: Dictionary union discrepancy

- **WHEN** two dictionaries share a key with different values
- **THEN** union preserves the first dictionary's value and the regression case passes

#### Scenario: Uncovered SDK operation

- **WHEN** an operation lies outside the agreed initial conformance inventory
- **THEN** the migration does not claim complete SDK conformance or broaden unrelated behavior changes

### Requirement: Packaged consumer evidence

Migration acceptance SHALL use restored package consumers as well as project-reference tests. The canonical and compatibility packages SHALL expose equivalent covered runtime behavior without shipping compiler or CLI dependencies.

#### Scenario: Local package restore

- **WHEN** fresh consumers restore the locally packed canonical and compatibility packages
- **THEN** all documented package combinations compile and execute their SDK and generated-code examples
