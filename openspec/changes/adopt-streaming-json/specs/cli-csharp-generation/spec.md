# Spec Delta

## MODIFIED Requirements

### Requirement: C# reporting and exit outcomes

Parsed C# invocations with --json SHALL write one parseable generation report,
including structured diagnostics, to stdout without plain-text logs: as one
document with --json=document, and as the report of the one `completed` record
with --json. Human-mode diagnostics and any emitted log text SHALL use stderr.
JSON mode need not duplicate structured report diagnostics as human-readable
stderr text. Exit codes SHALL remain 0 for success, 1 for model/semantic
failure, 2 for invocation/operational failure and 130 for cancellation.

#### Scenario: Structured semantic failure

- **WHEN** a parsed JSON invocation reaches an unsupported C# construct
- **THEN** stdout contains one failure report with stable diagnostics and exit code 1 without log contamination

#### Scenario: Unavailable provider option

- **WHEN** the user supplies an unsupported C# language version
- **THEN** the report identifies invocation failure, exit code 2, and no published artifacts
