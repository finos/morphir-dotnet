# Spec Delta

## Purpose

Provide an immutable C# runtime for the admitted Morphir SDK operations, with behavior independently checked against the shared .NET conformance contract.

## ADDED Requirements

### Requirement: Independent C# runtime package

Morphir.SDK.CSharp SHALL provide the admitted SDK operations through C#/.NET public types without requiring FSharp.Core, the Morphir compiler or CLI. The same public runtime API SHALL be consumable under both supported C# language profiles.

#### Scenario: Ordinary package consumer

- **WHEN** a C# application installs only the SDK package and constructs supported SDK values
- **THEN** it compiles and runs without compiler-host, model-reader or F# runtime dependencies

#### Scenario: Two language profiles

- **WHEN** the same consumer source is compiled with the stable and RC compiler profiles
- **THEN** it observes equivalent public SDK types and supported behavior

### Requirement: Persistent immutable collections

Collection updates SHALL return immutable values and preserve previously returned values. Public collection values SHALL NOT expose mutable arrays, builders or writable backing collections. Constructed dictionaries and sets SHALL retain the admitted Morphir ordering.

#### Scenario: Old value survives update

- **WHEN** a list, dictionary or set is updated and both versions remain referenced
- **THEN** the old value and its observations remain unchanged

#### Scenario: Ordered dictionary and set traversal

- **WHEN** equivalent entries are inserted in different orders
- **THEN** enumeration produces the same admitted order, including string and compound-key cases

### Requirement: Structural equality and hashing

Supported SDK values SHALL compare by Morphir value semantics, including nested collections, records and variants. Equal hashable values SHALL have equal hashes independent of insertion history. Unsupported equality or ordering domains SHALL fail explicitly.

#### Scenario: Independent equal structures

- **WHEN** two independently constructed values contain equal nested immutable collections
- **THEN** value equality succeeds and their hashes agree

#### Scenario: Distinct keys and values

- **WHEN** two dictionaries have the same keys with a different value at one key
- **THEN** they compare unequal while preserving the selected key ordering

### Requirement: Immutable optional and result values

The SDK SHALL distinguish absent and present values, success and error values, and unit without using null as an absence or failure encoding. Public constructors and operations SHALL preserve the admitted payload and Result type-argument meanings.

#### Scenario: Maybe and Result mappings

- **WHEN** a consumer maps a present value, an absent value, a success and an error
- **THEN** only the intended branch changes and the other branches retain their identity and payload semantics

#### Scenario: Different error and success types

- **WHEN** a Result uses string errors and integer successes
- **THEN** constructors and combinators preserve each payload type and cannot silently reverse them

### Requirement: Admitted SDK behavior

All cases required of the C# runtime by the shared baseline SHALL pass through its public API. Unsupported operations and representability limits SHALL be documented. Reference behavior SHALL NOT be replaced with whatever the chosen collection library does by default.

#### Scenario: Conflicting dictionary entries

- **WHEN** the first and second dictionary contain different values for one key
- **THEN** union preserves the first dictionary's value according to the admitted reference case

#### Scenario: Scalar boundaries

- **WHEN** the runtime executes an admitted numeric or text boundary case
- **THEN** the projected value or failure matches the shared contract without culture-dependent behavior

### Requirement: Runtime publication compatibility

The packaged runtime SHALL work when a consumer is trimmed or Native AOT compiled. Runtime operations SHALL NOT require dynamic code generation or reflection-based discovery.

#### Scenario: Published consumer

- **WHEN** a small consumer using variants, immutable collections and admitted SDK operations is trimmed or Native AOT published
- **THEN** it executes the same expected behavior as its ordinary build
