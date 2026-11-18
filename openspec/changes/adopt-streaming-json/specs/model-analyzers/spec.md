# Spec Delta

## MODIFIED Requirements

### Requirement: validate output is a table or one JSON report
Without `--json`, `morphir validate` SHALL write the diagnostics as a TUI-styled table on stdout, with severity, code, location, message and extension id, followed by a one-line summary. Its report SHALL have `success`, `exitCode`, `input`, the input's family and format version, the analyzers that ran and did not run, and `diagnostics`. With `--json=document`, it SHALL write that report as one JSON document to stdout; with `--json`, the report SHALL be the `completed` record's. Logs SHALL go to stderr only.

#### Scenario: JSON report is pipeable
- **WHEN** a user runs `morphir validate --input model.json --json=document | jq .exitCode`
- **THEN** `jq` reads the report, and stdout holds only that report

#### Scenario: Both hosts agree
- **WHEN** the same `validate --json` command runs in `morphir` and in `dotnet-morphir`
- **THEN** the two reports are equal, except for the host name
