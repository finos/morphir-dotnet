# Spec Delta

## Purpose

Extends the classic IR model to the IR 3.1.0 `Specs` distribution and gives callers a public JSON reader for complete classic distributions, including their dependencies.

## ADDED Requirements

### Requirement: Specs distributions are modeled

A classic distribution SHALL be a `Library` or a `Specs` value. A `Specs` value SHALL hold its package name, dependencies and package specification, and SHALL hold no definitions. Reading a package definition from a distribution SHALL return nothing for `Specs`.

#### Scenario: Package definition of each kind

- **WHEN** the package definition is requested
- **THEN** a `Library` returns its definition and a `Specs` returns nothing

#### Scenario: Shared accessors

- **WHEN** the package name or dependencies are requested from a `Specs`
- **THEN** they return the values it was built with

### Requirement: A public reader decodes complete distributions

A public classic JSON reader SHALL decode a v1, v2 or v3 `Library`, and a `Specs` at `3.1.0`, including dependency specifications, into a classic distribution. Attributes SHALL stay JSON values. Failure SHALL return a typed error with a rule and JSON location.

#### Scenario: Reference fixture

- **WHEN** `daily-rate-v3.json` is read
- **THEN** the result is a `Library` whose package, dependencies and module definitions match the document

#### Scenario: Dependency specifications

- **WHEN** a document's dependencies hold type and value specifications
- **THEN** the result holds them decoded, including opaque, custom and derived types

#### Scenario: Specs version floor

- **WHEN** a JSON `Specs` distribution says `formatVersion: 3`
- **THEN** the reader returns `specs_before_3_1`

#### Scenario: Unsupported distribution

- **WHEN** the distribution tag is not one the document's version allows
- **THEN** the reader returns `unsupported-distribution` with location `$.distribution`

#### Scenario: Out-of-range literal

- **WHEN** a whole number does not fit the classic literal
- **THEN** the reader returns `literal_out_of_range` and does not round the number

### Requirement: The transpiler's behavior does not change

F# generation from classic JSON SHALL keep its current acceptance rules, rule IDs and locations after the shared reader replaces its private decoder.

#### Scenario: Existing transpiler tests

- **WHEN** the existing `Morphir.CodeGeneration.FSharp.Tests` suite runs
- **THEN** it passes with no change to its expected diagnostics

#### Scenario: Specs given to the transpiler

- **WHEN** a classic `Specs` document is given to F# generation
- **THEN** generation refuses it with `unsupported-distribution`
