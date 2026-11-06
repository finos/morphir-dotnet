# Spec Delta

## Purpose

Exposes the Morphir Compatibility Kit through `morphir mck`. Developers, CI and other Morphir implementations can then run, list and check qualifications with the same command.

## ADDED Requirements

### Requirement: mck run executes a kit against an implementation
`morphir mck run` SHALL take:

- `--kit <dir>` and `--expect-kit-sha256 <digest>`;
- `--profile <id>`;
- `--adapter cli|mep-guest|library`;
- `--host <executable>` with repeated `--host-arg`, and repeated `--identity-file`;
- `--report <file>`.

It SHALL write the qualification report to the report file. Without `--expect-kit-sha256`, it SHALL report the kit digest it used.

#### Scenario: Wrong kit digest
- **WHEN** `--expect-kit-sha256` does not match the kit
- **THEN** the run stops before any case, and the exit code is 1

### Requirement: mck report-check checks a report
`morphir mck report-check --kit <dir> <report>` SHALL check the report as the conformance-reports rules require. It SHALL write a summary of verdicts and differences.

#### Scenario: Valid report
- **WHEN** an unchanged report from a qualified run is checked with the same kit and identity files
- **THEN** the exit code is 0

### Requirement: mck list shows the kit
`morphir mck list --kit <dir>` SHALL list profiles, suites and cases, with classification, basis and applicable format versions. It SHALL be able to filter by suite, profile and classification.

#### Scenario: List known gaps
- **WHEN** `morphir mck list --classification knownGap` runs
- **THEN** only cases classified `knownGap` are listed, each with its gap name

### Requirement: Exit codes match upstream mck
The command SHALL return:

- `0` when every required observation qualifies, or the report check passes;
- `1` when an observation fails, a check fails, or an operational error stops the run;
- `2` for an invalid invocation, such as a missing required option.

These codes SHALL stay the same as upstream `morphir mck`, so that scripts keep working.

#### Scenario: Missing option
- **WHEN** `morphir mck run` is called without `--adapter`
- **THEN** the exit code is 2, and nothing is run

### Requirement: Output follows the CLI conventions
`--json` SHALL write one JSON summary to stdout. `--json-lines` SHALL write one JSON record per line as each case completes, ending with one `completed` record that holds the summary. Logs and progress SHALL go to stderr.

#### Scenario: Streamed case results
- **WHEN** `morphir mck run --json-lines` runs ten cases
- **THEN** stdout has ten case records written as each case completes, and then one `completed` record
