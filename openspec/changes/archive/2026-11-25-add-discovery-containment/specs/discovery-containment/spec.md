# Spec Delta

## Purpose

Let users, CI jobs and test harnesses contain every discovering Morphir command, with a boundary and an isolation switch that come from command-line values or the environment, so Morphir never reads configuration outside the space it was given.

## ADDED Requirements

### Requirement: Boundary sources have deterministic precedence

A discovering command SHALL take its boundary from `--discovery-boundary` when supplied, otherwise from a nonempty `MORPHIR_DISCOVERY_BOUNDARY`, otherwise from the filesystem root of the start path. An empty variable SHALL count as unset. A relative value SHALL resolve against the current directory. Native absolute paths SHALL be accepted with either separator.

#### Scenario: Environment boundary applies

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` names a directory that contains the start path and no flag is given
- **THEN** discovery stops at that directory and reports no source above it

#### Scenario: Flag overrides the environment

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` names one directory and `--discovery-boundary` names a different directory that also contains the start path
- **THEN** discovery uses the flag's directory

#### Scenario: Empty variable is unset

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` is present but empty and no flag is given
- **THEN** discovery uses the start path's filesystem root, as when the variable is absent

#### Scenario: Relative environment boundary

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` is `..` and the current directory is a project directory
- **THEN** the boundary is the project directory's parent

#### Scenario: Windows native boundary

- **WHEN** on Windows `MORPHIR_DISCOVERY_BOUNDARY` is a drive path written with backslashes
- **THEN** it is treated as absolute and bounds discovery

### Requirement: Start paths outside the boundary fail before discovery

When the start path or a supplied target directory is outside the effective boundary, the command SHALL fail with exit code 2 and a `discovery.boundary` diagnostic that names the boundary and its source. It SHALL do so before reading any configuration or writing any file.

#### Scenario: Environment boundary excludes the current directory

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` names a directory that does not contain the current directory and `config path --json` runs
- **THEN** the command exits 2 with a `discovery.boundary` diagnostic identifying `MORPHIR_DISCOVERY_BOUNDARY`, and reports no configuration sources

### Requirement: Isolation sources have deterministic precedence

A command that loads machine and personal sources SHALL be isolated when `--isolated` is given without a value or with `true`, SHALL NOT be isolated when given `--isolated false`, and otherwise SHALL follow a nonempty `MORPHIR_ISOLATED`. With neither, it SHALL NOT be isolated. `MORPHIR_ISOLATED` SHALL accept `true`, `false`, `1` and `0`, ignoring case. An empty value SHALL count as unset.

#### Scenario: Environment isolates

- **WHEN** `MORPHIR_ISOLATED` is `1` and `config path` runs without `--isolated`
- **THEN** system, global and personal override sources are not consulted

#### Scenario: Flag turns isolation off

- **WHEN** `MORPHIR_ISOLATED` is `true` and `config path --isolated false` runs
- **THEN** system, global and personal override sources are consulted

#### Scenario: Bare flag isolates

- **WHEN** `MORPHIR_ISOLATED` is `0` and `config path --isolated` runs
- **THEN** system, global and personal override sources are not consulted

### Requirement: Invalid containment values fail as usage errors

An unrecognized `MORPHIR_ISOLATED` value SHALL fail the command with exit code 2 and a diagnostic that names the variable and the accepted values, before any configuration input or output. Help output SHALL NOT validate or read containment variables.

#### Scenario: Unrecognized isolation value

- **WHEN** `MORPHIR_ISOLATED` is `yes-please` and `config get project.name` runs
- **THEN** the command exits 2 naming `MORPHIR_ISOLATED` and its accepted values, and reads no configuration

#### Scenario: Help ignores invalid variables

- **WHEN** `MORPHIR_ISOLATED` is `yes-please` and `config get --help` runs
- **THEN** help is printed and the command exits 0

### Requirement: Configuration commands honor both controls

`config get`, `list`, `show`, `path`, `explain`, `set` and `unset` SHALL apply the effective boundary and isolation from both their flags and the environment.

#### Scenario: Edits stay inside an environment boundary

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` names a workspace directory and `config set` runs in a project below it
- **THEN** discovery and the edit target stay within that workspace directory

#### Scenario: Edits honor environment isolation

- **WHEN** `MORPHIR_ISOLATED` is `true` and `config set --dry-run --json` plans an edit
- **THEN** the plan's resulting effective values exclude system, global and personal override sources

### Requirement: Migration commands honor the boundary

`project migrate` SHALL apply the effective boundary from its flag or the environment. `workspace migrate` SHALL accept `--discovery-boundary` and SHALL require its supplied or current directory to lie inside the effective boundary. Both commands SHALL keep excluding machine and personal sources whatever `MORPHIR_ISOLATED` says.

#### Scenario: Workspace directory outside the environment boundary

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` does not contain the directory passed to `workspace migrate --dry-run --json`
- **THEN** the command exits 2 with a `discovery.boundary` diagnostic and writes nothing

#### Scenario: Project migration stays inside the environment boundary

- **WHEN** `MORPHIR_DISCOVERY_BOUNDARY` names a directory containing a classic project and `project migrate --dry-run --json` runs there
- **THEN** the plan reads no file outside that directory

#### Scenario: Migration ignores isolation off

- **WHEN** `MORPHIR_ISOLATED` is `false` and `project migrate --dry-run --json` runs
- **THEN** verification still excludes machine and personal sources
