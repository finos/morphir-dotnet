# Spec Delta

## Purpose

Groups the commands that act on a Morphir model, the IR together with its decorators, metadata and later additions, under `morphir model`, which replaces the former `morphir ir` group.

## ADDED Requirements

### Requirement: Model commands are under one model group
The CLI SHALL offer a `model` command group holding `verify` and `convert`. `morphir model --help` SHALL list both.

#### Scenario: The group lists its commands
- **WHEN** a user runs `morphir model --help`
- **THEN** the help lists `verify` and `convert` with a one-line description each

### Requirement: Model verify keeps the behavior of the former IR verify
`morphir model verify` SHALL accept the same inputs and options, write the same report, and exit with the same codes as `morphir ir verify` did.

#### Scenario: A valid v3 model is valid
- **WHEN** a user runs `morphir model verify` on a valid IR v3 file
- **THEN** the command exits 0 and reports the file as valid with schema version v3

#### Scenario: A file without a format version is not valid
- **WHEN** a user runs `morphir model verify` on a file with no `formatVersion`
- **THEN** the command exits 1 and names the missing member

### Requirement: The IR command group is removed
The CLI SHALL NOT offer an `ir` command group or an alias for it. `ir` SHALL be treated as any other unknown command, including routing to an external `morphir-ir` tool when one is on the search path. The changelog SHALL state the rename.

#### Scenario: The former command is unknown
- **WHEN** no `morphir-ir` tool is on the search path and a user runs `morphir ir verify model.json`
- **THEN** the command fails with a non-zero exit code, names `ir` as unknown, and verifies nothing
