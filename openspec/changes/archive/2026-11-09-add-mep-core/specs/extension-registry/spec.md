# Spec Delta

## ADDED Requirements

### Requirement: Built-in analyzers are registered extensions
Each host SHALL register one built-in extension with the type `validator` for each built-in analyzer that `morphir validate` runs, and no validator entry for an analyzer that no command runs. Pipeline plugins that no command runs SHALL NOT be registered.

#### Scenario: The type validator is listed
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the result has an entry with id `morphir.type-validator`, the type `validator`, the delivery `built-in` and the hosts `morphir` and `dotnet-morphir`

#### Scenario: The list agrees with validate
- **WHEN** the set of validator entries in `extension list --json` is compared with the analyzers that `morphir validate --json` reports as run or not run
- **THEN** the two sets of ids are equal

### Requirement: Validator entries claim the IR versions they read
A `validator` entry SHALL include a validator claim with at least one IR version that the analyzer reads, in the spelling of the model readers. This claim is a morphir-dotnet report member. It SHALL NOT be written into MEP messages until the upstream contract defines a validator capability.

#### Scenario: The type validator claim
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the `morphir.type-validator` entry has `capabilities.validator.irVersions` equal to the classic format versions `1`, `2` and `3`

### Requirement: Entries map to the MEP extension record
Each entry SHALL map to a MEP `ExtensionInfo` of the pinned contract, with the same id, name, version, description and types. The mapping SHALL include only capability claims that the pinned contract defines.

#### Scenario: Built-ins encode as contract records
- **WHEN** each built-in entry is mapped to `ExtensionInfo` and written as MEP JSON
- **THEN** the JSON has the members of the contract's `ExtensionInfo`, the `types` hold the wire values of the entry's types, and an entry with no description has no `description` member

#### Scenario: No invented claim
- **WHEN** the `morphir.type-validator` entry is mapped to MEP
- **THEN** its `types` hold `validator`, and the MEP capability claims hold no validator member
