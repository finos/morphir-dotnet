# Spec Delta

## Purpose

Defines the runner that qualifies a WASM extension host against the shared execution profiles. The crafted modules and their fixed expectations are data in finos/morphir; this capability covers how the .NET runner turns that data into Gherkin cases, drives a host and records what it saw. It adopts the runner side of the capability `mck/wasm-host-qualification` of finos/morphir.

### One case, from data to verdict

A crafted case names one module that does one wrong thing, the frames to send and the observation to expect. finos/morphir publishes the cases as JSON today:

```json
{
  "caseId": "classification/unknown-namespace",
  "module": "env-open-socket",
  "runs": [{
    "frames": ["initialize"],
    "expect": {
      "exitCode": 3,
      "stdoutBytes": 0,
      "events": [{"event": "refused", "code": "extension.abi_unsupported", "messageIncludes": ["open_socket"]}]
    }
  }]
}
```

The kit's case format is Gherkin, so this repository generates a feature from each pinned case. The step wording here is illustrative; the step vocabulary fixes it.

```gherkin
Scenario: wasm-host-0001 classification/unknown-namespace
  Given the module "env-open-socket" declared as "extism-kernel-v1"
  When the worker is sent the frames "initialize"
  Then the worker exits with status 3
  And stdout is empty
  And stderr has a "refused" event with code "extension.abi_unsupported" whose message includes "open_socket"
  And stderr has no "fault" event
```

The runner checks the module's digest, starts `<host> extension worker --module <wasm> --json`, writes the frame, reads stdout and the stderr events, and compares them with the expectation. It knows nothing about the host except its path and arguments, so the same run qualifies the MoonBit host and the .NET host.

No expectation ever comes from a host run. The runner compares; it never records.

## ADDED Requirements

### Requirement: Crafted cases are Gherkin features generated from the pinned corpus
The crafted cases SHALL be Gherkin features in the kit's closed step vocabulary, generated from the pinned upstream corpus and never edited by hand. Reading the features and reading the pinned corpus SHALL give the same cases, frames and expectations. A check SHALL fail when the features differ from what the pinned corpus generates.

#### Scenario: Both formats give the same cases
- **WHEN** the generated features and the pinned corpus are each read into the runner's case model
- **THEN** the two models are equal for every case, with no case in one and not in the other

#### Scenario: Stale features
- **WHEN** the pinned corpus changes and the features are not generated again
- **THEN** the check fails and names each case that differs

#### Scenario: Hand edit
- **WHEN** a contributor changes an expectation in a generated feature by hand
- **THEN** the check fails, and the expectation is not accepted until the upstream corpus holds it

#### Scenario: Step outside the vocabulary
- **WHEN** a feature of the suite uses a step the vocabulary does not define
- **THEN** the reader refuses the feature and names the file and line

### Requirement: Generated cases keep stable ids
Each generated scenario SHALL have an id of the form `wasm-host-NNNN` and SHALL keep its upstream case id in its name. The number of a case SHALL NOT change when the corpus changes, and a number SHALL NOT be reused.

#### Scenario: Case added upstream
- **WHEN** the pinned corpus gains a case
- **THEN** the new scenario takes the next unused number, and every existing scenario keeps its number

#### Scenario: Case removed upstream
- **WHEN** the pinned corpus loses a case
- **THEN** its scenario is removed and its number stays reserved

### Requirement: Module digests are checked before a case runs
Before a case runs, the runner SHALL compare the SHA-256 of the module binary with the digest the corpus records, and SHALL refuse the case when they differ. A corpus or vocabulary version the runner does not implement SHALL stop the run before any case.

#### Scenario: Binary does not match its text
- **WHEN** a built module's digest differs from the recorded digest
- **THEN** the runner refuses to run that case and reports both digests

#### Scenario: Corpus version the runner does not know
- **WHEN** the corpus declares a kit version the runner does not implement
- **THEN** the runner stops with an invalid-invocation result and runs no case

### Requirement: Hosts are driven through a stdio worker
The runner SHALL drive a host through a command that answers Content-Length framed MEP messages on stdio for one module and reports startup, refusals and faults on stderr as JSON lines. The runner SHALL select the host by executable path and arguments and SHALL contain no host-specific code.

#### Scenario: Two hosts, one command line
- **WHEN** the runner is given the .NET host and then the MoonBit host with the same worker arguments
- **THEN** both runs use the same runner code path and the same corpus

#### Scenario: Worker left running
- **WHEN** a case ends while the worker process is still alive
- **THEN** the runner stops the worker's whole process tree before the next case starts

### Requirement: Cases follow the profiles a host declares
Every case for `extism-kernel-v1` SHALL apply to every host. A case that needs a `morphir-bytes-v1` session SHALL apply only to a host that declares that profile, and SHALL be recorded as `not_applicable` for any other. A host that does not declare the profile SHALL still pass the case that refuses a byte-profile module by name.

#### Scenario: Extism-only host
- **WHEN** the .NET host, which declares only `extism-kernel-v1`, is qualified
- **THEN** the byte-profile result-code cases are recorded as `not_applicable`
- **AND** the case that offers it a byte-profile module passes when the host refuses with `extension.abi_unsupported`

### Requirement: Unenforced bounds stay visible
The report SHALL record, for each bound, whether the host declared it enforced. A case for a bound the host does not enforce SHALL be recorded as `not_enforced`, not as passed. Qualification SHALL require the memory ceiling and the call deadline; the instruction budget MAY be `not_enforced`.

#### Scenario: Engine without metering
- **WHEN** a host declares the instruction budget as not enforced
- **THEN** the budget case is recorded as `not_enforced` and the host can still qualify

#### Scenario: Deadline not enforced
- **WHEN** a host declares the call deadline as not enforced
- **THEN** the host is not qualified

### Requirement: A runaway guest is stopped in time
A case whose module never returns SHALL pass when the host ends the call no later than the profile's call deadline plus the corpus's fixed margin, by the instruction budget or by the deadline. The report SHALL record which bound ended it. A call still running after the margin SHALL fail the case, and the runner SHALL stop the worker.

#### Scenario: Metering host
- **WHEN** a host that enforces the instruction budget runs the spinning Extism module
- **THEN** the call ends with fault `budget-exhausted` before 30 seconds and the case passes

#### Scenario: Host without metering
- **WHEN** a host without metering runs the same module
- **THEN** the call ends with fault `call-deadline` between 30 and 32 seconds and the case passes

### Requirement: Report identity
A host qualification report SHALL identify the runner, the corpus digest, the profile contract version, the host executable's path and digest, its declared offering, platform and engine, and each module's digest. Checking a report SHALL recompute the verdict from the recorded observations; a stored verdict SHALL NOT be trusted.

#### Scenario: Edited verdict
- **WHEN** a report's verdict is changed to qualified while a recorded observation fails
- **THEN** the report check rejects the report

### Requirement: One executable per report
A report SHALL qualify exactly one host executable on one platform. A release SHALL NOT cite a report for an offering or platform other than the one recorded in it.

#### Scenario: AOT report cited for the tool
- **WHEN** release metadata cites the Native AOT Linux x64 report as evidence for the `dotnet-tool` offering
- **THEN** the release check rejects the citation

### Requirement: A host does not qualify on crafted cases alone
A report SHALL record a host as qualified only when it also cites a passing run of the generic guest kit through the same host executable for at least one published `extism-kernel-v1` guest. The citation SHALL hold the digest of that run's report, the host executable's digest and the guest module's digest.

#### Scenario: Refusals pass but compile fails
- **WHEN** a host passes every crafted case and the Python guest's compile returns a different IR
- **THEN** the host is not qualified

#### Scenario: Guest evidence from another executable
- **WHEN** the cited generic guest kit report records a host executable digest that differs from the one the crafted cases ran against
- **THEN** the report check rejects the report

### Requirement: Known deviations are recorded, not hidden
When a host is known to differ from the contract, the runner SHALL still run the case that shows it and the report SHALL show the failure. A deviation list MAY explain a failure and link its tracking issue. It SHALL NOT turn a failure into a pass.

#### Scenario: Rust host workspace-info signature
- **WHEN** the Rust host is run against the case that imports `morphir_get_workspace_info` as `() -> i64`
- **THEN** the report records the observed result and the linked deviation
- **AND** the Rust host is not reported as qualified for that case
