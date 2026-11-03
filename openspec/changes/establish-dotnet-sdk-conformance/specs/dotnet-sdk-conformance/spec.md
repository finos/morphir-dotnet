# Spec Delta

## Purpose

Provide independently sourced, reproducible SDK behavior cases that both .NET runtimes and generated programs can execute without treating either runtime as the semantic oracle.

## ADDED Requirements

### Requirement: Pinned behavioral evidence

Every admitted SDK operation SHALL identify a semantic reference, immutable upstream revision, license, inputs, expected observations and applicable representation profile. Fixture validation SHALL run offline against checked-in evidence.

#### Scenario: Reproducible evidence

- **WHEN** the SDK baseline is validated without network access
- **THEN** every admitted case resolves to checked-in evidence and an exact source revision, with no moving branch used as the oracle

#### Scenario: Upstream discrepancy

- **WHEN** Python, MoonBit or Elm references differ for an operation
- **THEN** the inventory records the discrepancy and selected behavior or explicit exclusion instead of silently choosing whichever runtime passes

### Requirement: Bounded initial inventory

The baseline SHALL enumerate the initial operations and distinguish required cases, representation exclusions and known implementation gaps. A known gap SHALL NOT count as a passing implementation. Unknown operations and unclassified cases SHALL fail coverage validation.

#### Scenario: Initial collection contract

- **WHEN** the baseline admits List, Dict, Set, Maybe and Result operations
- **THEN** it includes updates preserving old values, structural equality and hashing, ordered traversal, dictionary duplicate handling and Result argument meaning

#### Scenario: Known F# discrepancy

- **WHEN** first-dictionary-wins union is admitted while the existing F# implementation disagrees
- **THEN** the case retains the reference expectation and a named F# gap, and the F# delivery cannot pass acceptance until that covered gap is fixed

### Requirement: Portable case observations

Cases SHALL describe observable values and errors independently of C# or F# object layouts. Dictionary and set observations SHALL use defined order. Decimal and nonfinite numeric observations SHALL avoid lossy JSON-number conversion.

#### Scenario: Equivalent values in different runtimes

- **WHEN** C# and F# construct equivalent supported SDK values
- **THEN** both project to the same tagged expected observation without exposing their implementation types

#### Scenario: Malformed case

- **WHEN** a case has duplicate identity, an unknown tag or an incomplete expected error
- **THEN** validation fails with the case identity and invalid field

### Requirement: Explicit scalar boundaries

Each admitted scalar profile SHALL specify numeric range, arithmetic failure behavior, decimal exactness and scale, floating-point observations and text/character ordering. Excluded domains SHALL be visible and SHALL NOT be interpreted as passing oracle cases.

#### Scenario: Boundary distinction

- **WHEN** a reference value cannot be represented by an admitted .NET profile
- **THEN** the case is classified with the precise representation limit rather than rounded, wrapped or omitted

#### Scenario: Runtime arithmetic boundary

- **WHEN** an admitted arithmetic operation reaches overflow or a zero divisor
- **THEN** the contract identifies the expected value or failure and both adapters test that outcome

### Requirement: Shared evidence without shared implementation

Both SDK deliveries SHALL consume the same case identities and expected observations for overlapping admitted behavior. Conformance evidence SHALL remain usable without installing either SDK or invoking another language toolchain at test runtime.

#### Scenario: Second runtime adapter

- **WHEN** a new runtime adapter executes an existing admitted case
- **THEN** it reuses the expected observation rather than generating a new expectation from its own implementation

#### Scenario: Contract extension

- **WHEN** an operation is added to the inventory
- **THEN** its evidence, profile applicability and expected observations are validated together before downstream adapters claim support
