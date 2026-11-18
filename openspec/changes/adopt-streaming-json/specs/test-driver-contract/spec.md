# Spec Delta

## MODIFIED Requirements

### Requirement: Standard output holds only the output of the command
The testing library SHALL give one assertion for each machine-readable output form: that standard output of a
`--json=document` run is exactly one JSON document with no other text, and that standard output of a `--json`
run is JSON Lines with exactly one `completed` record, as its last line, whose report the assertion returns.
Logs are on standard error.

#### Scenario: A JSON command writes one document
- **WHEN** a test runs a command with `--json=document` and asserts one JSON document
- **THEN** the assertion fails if standard output is empty, is not JSON, or has text before or after the document

#### Scenario: A log line on standard output fails
- **WHEN** a command with `--json` or `--json=document` writes a log line to standard output
- **THEN** the assertion fails and shows the line

#### Scenario: A stream gives its report
- **WHEN** a test runs a command with `--json` and asserts a stream
- **THEN** the assertion fails if a line does not parse as an object with an `event` member, if there is no `completed` record or more than one, or if it is not the last line, and otherwise returns the record's report
