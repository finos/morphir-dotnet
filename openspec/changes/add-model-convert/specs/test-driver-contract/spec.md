# Spec Delta

## MODIFIED Requirements

### Requirement: The status of a report belongs to its command
The meaning of a report (which field states success, which fields hold the diagnostics) SHALL belong to the
capability of its command, not to the general assertion on a command result. A capability SHALL give the checks
for its own report.

#### Scenario: A verification report
- **WHEN** a test asserts that a model is valid after `model verify --json`
- **THEN** the check reads the `IsValid` field of the verification report

#### Scenario: A generation report
- **WHEN** a test asserts that `generate --json` succeeded
- **THEN** the check reads the `success` field of the generation report and compares it with the exit code

#### Scenario: A conversion report
- **WHEN** a test asserts that `model convert --json` converted a model to a tree
- **THEN** the check reads the `success` field and the written files of the conversion report, and compares success with the exit code

## ADDED Requirements

### Requirement: Model conversion is a driver capability
Tests of `model convert` SHALL reach the command through a model-conversion driver interface that takes the model, the target layout and the compaction as intents and builds the command line itself. The capability SHALL have its own tag, its own report type and driver tests.

#### Scenario: A feature file states intents only
- **WHEN** a scenario converts a model to a compacted tree
- **THEN** its steps name the model case, the layout and the compaction, the driver builds the arguments, and the feature file holds no command line

#### Scenario: A target without the capability skips the test
- **WHEN** a conversion test runs on a target whose driver does not support model conversion
- **THEN** the test is skipped with the capability and the target as the reason
