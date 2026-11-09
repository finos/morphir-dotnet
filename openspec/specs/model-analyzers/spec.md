# model-analyzers Specification

## Purpose
Lets users check a Morphir model with analyzers that read the model and report diagnostics without changing it, and gives built-in and later external analyzers one diagnostic shape and one run rule.

## Requirements

### Requirement: Analyzer diagnostics have the MEP diagnostic shape
Each analyzer diagnostic SHALL have a severity (`error`, `warning`, `info` or `hint`), a message, an optional code, an optional location, and the id of the extension that reported it. The fields SHALL map one to one to the MEP `Diagnostic` type of the pinned contract, plus the extension id.

#### Scenario: Type mismatch diagnostic
- **WHEN** the type validator finds a definite type mismatch in a value definition
- **THEN** the diagnostic has the severity `error`, the code `type-mismatch`, a message that names both types, and the extension id `morphir.type-validator`

### Requirement: Analyzers do not change the model
An analyzer SHALL read the decoded model and return diagnostics only. It SHALL NOT change the model, write files or depend on the result of another analyzer.

#### Scenario: Model unchanged
- **WHEN** `morphir validate` runs every built-in analyzer on a model file
- **THEN** the model file and the working directory have the same content as before the run

### Requirement: Analyzers are additive
`morphir validate` SHALL run every enabled analyzer that reads the input's IR family, and merge their diagnostics into one result. The order of the result SHALL be stable: by extension id, then by the order in which each analyzer reported. Setting `extensions."<id>".enabled = false` in the layered configuration of the invocation directory SHALL turn off the analyzer with that id.

#### Scenario: Analyzer turned off
- **WHEN** the configuration sets `extensions."morphir.type-validator".enabled = false` and a user runs `morphir validate` on a model with a type mismatch
- **THEN** the result has no diagnostic from `morphir.type-validator`, and the report lists the analyzer as not run because it is turned off

### Requirement: The type validator is a built-in analyzer
Both hosts SHALL include the built-in analyzer `morphir.type-validator`. It SHALL read classic models (format versions 1 to 3) and report the type mismatches that the existing pipeline type validator finds.

#### Scenario: Classic model with a mismatch
- **WHEN** a user runs `morphir validate --input` on a classic model that adds an `Int` to a `String`
- **THEN** the result has one `type-mismatch` error from `morphir.type-validator`

#### Scenario: Same results as the pipeline plugin
- **WHEN** the existing type validator tests run through `morphir validate`
- **THEN** each test gives the same diagnostics as the pipeline plugin gives

### Requirement: An input with no analyzer for its family is reported
When no enabled analyzer reads the input's IR family, `morphir validate` SHALL report one `info` diagnostic that says no analyzer ran for that family, and it SHALL NOT convert the model to another family.

#### Scenario: V4 model with built-ins only
- **WHEN** a user runs `morphir validate --input` on a V4 model and no enabled analyzer reads V4
- **THEN** the result has one `info` diagnostic that names the family, and the exit code is 0

### Requirement: validate reads a model file as generate does
`morphir validate --input <file>` SHALL read JSON and Ion model files with the same reader, detection and refusals as `morphir generate --input`, with an `--input-format` option of `auto`, `json` or `ion`. An input that cannot be read SHALL give exit code 2 with a diagnostic and SHALL run no analyzer.

#### Scenario: Missing input
- **WHEN** a user runs `morphir validate --input missing.json`
- **THEN** the exit code is 2, a diagnostic names the path, and no analyzer runs

### Requirement: validate exit codes follow the diagnostics
`morphir validate` SHALL exit with 0 when no diagnostic has the severity `error`, with 1 when at least one has it, with 2 for an invalid invocation, an unreadable input or an analyzer that fails, and with 130 when cancelled.

#### Scenario: Warnings only
- **WHEN** the analyzers report warnings and no error
- **THEN** the exit code is 0, and the warnings are in the result

#### Scenario: An analyzer fails
- **WHEN** a built-in analyzer throws an exception
- **THEN** the exit code is 2, and the result names the analyzer and says that it failed, and still holds the diagnostics of the other analyzers

### Requirement: validate output is a table or one JSON report
Without `--json`, `morphir validate` SHALL write the diagnostics as a TUI-styled table on stdout, with severity, code, location, message and extension id, followed by a one-line summary. With `--json`, it SHALL write one JSON report to stdout with `success`, `exitCode`, `input`, the input's family and format version, the analyzers that ran and did not run, and `diagnostics`. Logs SHALL go to stderr only.

#### Scenario: JSON report is pipeable
- **WHEN** a user runs `morphir validate --input model.json --json | jq .exitCode`
- **THEN** `jq` reads the report, and stdout holds only that report

#### Scenario: Both hosts agree
- **WHEN** the same `validate --json` command runs in `morphir` and in `dotnet-morphir`
- **THEN** the two reports are equal, except for the host name
