# Spec Delta

## Purpose

Makes every Morphir Compatibility Kit result checkable after the run. A report shows exactly which kit tested which implementation, and anyone can derive each verdict again from the recorded observations.

## ADDED Requirements

### Requirement: Reports identify the kit and the implementation
A report SHALL record:

- the kit version and the SHA-256 of the case set;
- the profile and the plan;
- the runner's identity and version;
- the adapter;
- the executable's resolved path, SHA-256 and ordered arguments;
- the SHA-256 of each identity file;
- the claims the implementation made.

#### Scenario: Executable identity
- **WHEN** a guest run passes `--identity-file` for each module the guest loads
- **THEN** the report holds the path and SHA-256 of each file, and of the executable

### Requirement: Reports keep every observation
A report SHALL keep every planned case in plan order. For each case it SHALL record the request or invocation, the complete observation, the verdict and, for a failure, the first difference. A known gap SHALL be reported as a gap and SHALL NOT count as a pass.

#### Scenario: Known gap
- **WHEN** a case classified `knownGap` for the implementation fails as expected
- **THEN** the report lists it as a gap, and the run is not qualified while a required case has an open gap

### Requirement: Report checking re-derives verdicts
`report-check` SHALL rebuild the plan from the recorded claims. It SHALL compare every recorded observation with the same kit, verify the kit digest and the identity digests, and derive each verdict again. A verdict or `qualified` flag stored in the report SHALL NOT be trusted.

#### Scenario: Edited verdict
- **WHEN** a report's stored verdict for a failed case is changed to pass
- **THEN** `report-check` derives the failure again and the check fails, naming the case

#### Scenario: Different kit
- **WHEN** `report-check` runs with a kit whose digest differs from the report's kit digest
- **THEN** the check fails before any comparison, naming both digests

### Requirement: Reports are portable JSON
A report SHALL be one UTF-8 JSON document with a report schema version. It SHALL contain no absolute path outside the declared identity entries. Payloads SHALL be kept exactly, with binary content as base64.

#### Scenario: Shared report
- **WHEN** a report from one machine is checked on another machine with the same kit and identity files
- **THEN** `report-check` gives the same result
