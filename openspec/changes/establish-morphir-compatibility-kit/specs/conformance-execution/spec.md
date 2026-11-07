# Spec Delta

## Purpose

Runs the Morphir Compatibility Kit against any implementation through defined adapters. The plan depends only on what the implementation claims, and every run records exactly what was tested.

## ADDED Requirements

### Requirement: The plan comes from claimed capabilities
The runner SHALL build a plan from the selected profile and from the implementation's claims: MEP describe and initialize results for guests, and the declared capabilities for a CLI or library. Required cases for capabilities that are not claimed SHALL NOT be planned. A profile that selects no case SHALL fail the run.

#### Scenario: Frontend-only guest
- **WHEN** a guest claims an F# frontend with JSON `compileModel` tuples and no backend
- **THEN** the plan has the F# frontend and MEP guest cases, and no generation case

#### Scenario: Nothing selected
- **WHEN** a guest claims no language or IR version that the selected profile covers
- **THEN** the run is not qualified, and the report says which claim was missing

### Requirement: The cli adapter drives any morphir executable
The `cli` adapter SHALL start a named executable with explicit arguments and no shell, in a private working directory. It SHALL observe exit codes, stdout reports from `--json` and records from `--json-lines`, stderr, and the files written. It SHALL need no build of the executable.

#### Scenario: Another CLI
- **WHEN** the CLI suite runs against a Rust `morphir` executable
- **THEN** the same cases run, and each difference is a failed observation with the first difference shown

### Requirement: The mep-guest adapter drives any stdio guest
The `mep-guest` adapter SHALL run a guest over MEP `0.2.0-draft.1` stdio framing. It SHALL enforce frame, payload and deadline limits, drain stderr, and end the process tree. It SHALL check every response against the pinned contract shapes before comparing it with the case.

#### Scenario: Guest hangs
- **WHEN** a guest does not answer within the case deadline
- **THEN** the observation fails as a timeout, the process tree ends, and later cases run in a new process

### Requirement: The library adapter runs in process
The `library` adapter SHALL run cases through the morphir-dotnet libraries in process. It SHALL use the same cases and comparisons as the process adapters and SHALL NOT add expectations of its own.

#### Scenario: Same verdicts
- **WHEN** one case runs through `library` and through `cli` with the morphir-dotnet executable
- **THEN** both observations get the same verdict

### Requirement: Runs are isolated and repeatable
Each case SHALL run with a fresh working directory, a fixed environment, and no network access unless the case states otherwise. The same kit, implementation and options SHALL give the same observations, except for members that the case marks as variable, such as times.

#### Scenario: Repeat run
- **WHEN** a run is repeated with the same inputs
- **THEN** every verdict is the same, and only the declared variable members differ

### Requirement: The runner works in the Native AOT CLI
The runner and all process adapters SHALL work in the trimmed, Native AOT `morphir` executable, with source-generated serialization and no reflection-based loading. The `library` adapter SHALL be available only where its libraries are present.

#### Scenario: AOT executable runs the kit
- **WHEN** the published AOT `morphir` executable runs `morphir mck run` with the `cli` adapter
- **THEN** the run completes without loading the F# compiler service
