# Spec Delta

## ADDED Requirements

### Requirement: The C# frontend is a built-in extension in both hosts

Each host SHALL register one built-in extension of the type `frontend` for C#, with a frontend claim for the language `csharp`, the extension `.cs` and the IR versions that it writes. The entry SHALL be the same in `morphir` and `dotnet-morphir`.

#### Scenario: Extension list

- **WHEN** a user runs `extension list` in either host
- **THEN** the result has a `frontend` entry for C# with delivery `built-in`

### Requirement: Transform entries claim their stage

Each host SHALL register one built-in extension of the type `transform` for each built-in pass that a command runs. The entry SHALL include a claim with the stage, and for the stage before a backend, the target id. A host SHALL NOT register a transform entry for a pass that no command runs.

#### Scenario: Target-side pass entry

- **WHEN** a user runs `extension list`
- **THEN** each C# target-side pass is a `transform` entry with the stage and the target id `csharp`

### Requirement: Claims that MEP does not define stay out of messages

A transform claim SHALL be a morphir-dotnet report member and SHALL NOT be written into MEP messages. The mapping to the MEP extension record SHALL include only members that the pinned contract defines.

#### Scenario: MEP mapping

- **WHEN** a `transform` entry maps to the MEP `ExtensionInfo`
- **THEN** the record has the id, name, version, description and types, and no stage member
