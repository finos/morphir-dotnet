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

The parent lets a runner execute the corpus in another form, as long as that form is derived from the JSON and checked against it. The kit's case format is Gherkin, so this repository generates a feature from each pinned case, one scenario per case and per alternative. The step wording here is illustrative; the step vocabulary fixes it.

```gherkin
Scenario: wasm-host-0001 classification/unknown-namespace
  Given the module "env-open-socket" declared as "extism-kernel-v1"
  When the worker is sent the frames "initialize"
  Then the worker exits with status 3
  And stdout is empty
  And stderr has a "refused" event with code "extension.abi_unsupported" whose message includes "open_socket"
  And stderr has no "fault" event
```

The runner checks the module's digest, starts `<host> extension worker --abi <profile> --module <wasm> --build-identity <identity> --json`, writes the frame, reads stdout and the stderr events, and compares them with the expectation. It knows nothing about the host except its path and arguments, so the same run qualifies the MoonBit host and the .NET host.

No expectation ever comes from a host run. The runner compares; it never records.

At finos/morphir `938f798`, the pin, the corpus has 59 cases and 51 modules. 49 cases are for `extism-kernel-v1` and apply to every host; 10 are for `morphir-bytes-v1`.

The generic guest kit is a separate kit with its own runner. Its legacy tier negotiates MEP `0.1`, which this host does not support, so that tier is not run against this host, and a run of its model tier is not reported as a complete run of that kit.

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

### Requirement: A case's worker arguments reach the worker
The runner SHALL start the worker with each argument the case's `worker` object gives, as the corpus's `worker` convention maps it: `requestBytes` as `--request-bytes`, `responseBytes` as `--response-bytes` and `instructions` as `--instructions`. It SHALL leave out the arguments `worker.omit` names. The generated scenario SHALL state each of these arguments in its own step.

#### Scenario: Small instruction budget
- **WHEN** the runner runs `bounds/small-budget`, whose `worker` object gives `instructions` 1,000,000
- **THEN** the worker is started with `--instructions 1000000`
- **AND** the generated scenario has a step that states that budget, and reading the feature gives the same worker arguments as reading the pinned case

#### Scenario: Case without a worker object
- **WHEN** a case gives no `worker` object
- **THEN** the worker is started without `--request-bytes`, `--response-bytes` and `--instructions`, so the contract's defaults apply

### Requirement: Each alternative is its own scenario
A run with alternatives SHALL be generated as one scenario per alternative, tagged `@requires:` with that alternative's condition. Two scenarios of one case MAY carry the same `@requires:` tags. The runner SHALL record the case as passed when the observation satisfies at least one scenario whose tags match what the host declares, and as failed when it satisfies none of them.

#### Scenario: Two alternatives with the same condition
- **WHEN** a host that declares the instruction budget enforced runs `bounds/spin-extism`, whose two alternatives for that condition expect `budget-exhausted` before 30 seconds and `call-deadline` between 30 and 32 seconds
- **THEN** the generated feature has two scenarios with the same id and the same `@requires:` tags, one for each expectation
- **AND** the case passes when the call ends either way

#### Scenario: No applicable alternative satisfied
- **WHEN** the same host ends the call with fault `trap`
- **THEN** neither applicable scenario is satisfied and the case fails

#### Scenario: Alternative for another condition
- **WHEN** a host that declares the instruction budget enforced runs a case with an alternative for an unenforced budget
- **THEN** that alternative's scenario does not apply, and its expectation neither passes nor fails the case

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
A case whose module never returns SHALL pass when the host ends the call no later than the profile's call deadline plus the corpus's fixed margin, by the instruction budget or by the deadline. The report SHALL record which bound ended it. A call still running after the margin SHALL fail the case, and the runner SHALL stop the worker. The case that tests budget exhaustion with a small budget SHALL pass only on `budget-exhausted`.

#### Scenario: Metering host
- **WHEN** a host that enforces the instruction budget runs the spinning Extism module under the default budget
- **THEN** the case passes when the call ends with fault `budget-exhausted` before 30 seconds
- **AND** it also passes when the call ends with fault `call-deadline` between 30 and 32 seconds, because the budget unit is engine-defined

#### Scenario: Small budget on a metering host
- **WHEN** a host that enforces the instruction budget runs the spinning Extism module with a budget of 1,000,000 units
- **THEN** the call ends with fault `budget-exhausted` before 30 seconds
- **AND** a fault of `call-deadline` fails the case

#### Scenario: Host without metering
- **WHEN** a host without metering runs the same module
- **THEN** the call ends with fault `call-deadline` between 30 and 32 seconds and the case passes

### Requirement: Report identity
A report SHALL identify the runner, corpus, contract, host executable and modules by their recorded identities and digests. The runner SHALL require the host's offering, platform and engine as inputs because a worker reports none of them.

#### Scenario: Host identity is missing
- **WHEN** offering, platform or engine is absent or empty
- **THEN** the checker refuses the report as `identity_missing`

### Requirement: Reports adopt the pinned normative envelope
Reports SHALL adopt the report contract and controls from finos/morphir `d5890d00f4d7839a5edfbcca100ffaaf45281508`, with a computed verdict, ordered reasons and per-profile citation list. Cases and case reasons SHALL follow corpus order while stable generated ids remain unchanged.

#### Scenario: Edited verdict
- **WHEN** a report's verdict is changed to qualified while a recorded observation fails
- **THEN** the checker refuses it as `verdict_mismatch`

#### Scenario: Report envelope is written
- **WHEN** the runner writes a report
- **THEN** its first member is `$schema` naming `https://morphir.finos.org/schemas/mep-wasm-host-kit/0.1.0-draft.1/report.schema.json`
- **AND** it writes `verdict.qualified`, ordered `verdict.reasons`, and `guestKitCitations` with a profile per entry
- **AND** it omits obsolete `report`, `reportVersion` and `qualificationNotice` members

### Requirement: Report refusals have a fixed order
The checker SHALL return every applicable refusal in the pinned contract's order and SHALL NOT return a verdict for a refused report.

#### Scenario: Multiple report inconsistencies
- **WHEN** several refusal conditions apply
- **THEN** their codes follow this order: `identity_missing`, `host_profiles`, `corpus_mismatch`, `case_inventory`, `outcome_inconsistent`, `totals_mismatch`, `citation_invalid`, `verdict_mismatch`

### Requirement: Fixed controls and recorded observations are checked independently
The shared contract check SHALL satisfy all 22 independently fixed controls and an independent inventory guard. The runner SHALL additionally recompute outcomes from recorded runs and require its own exact reconstructed report bytes.

#### Scenario: Independent report controls
- **WHEN** the shared report checker receives the 22 fixed policy inputs from the pinned parent controls
- **THEN** each result matches the fixed verdict or ordered refusal list
- **AND** removing a control fails the independent inventory test
- **AND** these synthetic inputs establish no actual host or inner-guest qualification

### Requirement: One executable per report
A report SHALL qualify exactly one host executable on one platform. A release SHALL NOT cite a report for an offering or platform other than the one recorded in it.

#### Scenario: AOT report cited for the tool
- **WHEN** release metadata cites the Native AOT Linux x64 report as evidence for the `dotnet-tool` offering
- **THEN** the release check rejects the citation

### Requirement: A host does not qualify on crafted cases alone
A qualified report SHALL cite a passing, independently checked generic-kit run through the same executable for every declared profile, including a published Extism guest. Each citation SHALL identify its profile, report digest, executable digest and guest-module digest.

#### Scenario: Historical model-tier evidence
- **WHEN** evidence covers only the historical model tier
- **THEN** it is not presented as the full 21-case run owned by the distinct `adopt-generic-guest-kit` change

#### Scenario: Refusals pass but compile fails
- **WHEN** a host passes every crafted case and the Python guest's compile returns a different IR
- **THEN** the host is not qualified

#### Scenario: Guest evidence from another executable
- **WHEN** the cited generic guest kit report records a host executable digest that differs from the one the crafted cases ran against
- **THEN** the report has an unqualified verdict with reason `guest_kit_host_mismatch` for that profile
- **AND** the executable mismatch alone is not a check refusal

#### Scenario: Generic report checker is unavailable
- **WHEN** a citation-bearing run is requested before the actual generic-kit report checker is integrated
- **THEN** admission fails with an explicit operational unavailable result before any worker or output report
- **AND** neither file hashes nor caller-supplied digests are treated as positive guest evidence
- **AND** no fabricated invalid-guest verdict or new normative reason is emitted

### Requirement: Known deviations are recorded, not hidden
When a host is known to differ from the contract, the runner SHALL still run the case that shows it and the report SHALL show the failure. A deviation list MAY explain a failure and link its tracking issue. It SHALL NOT turn a failure into a pass.

#### Scenario: Rust host workspace-info signature
- **WHEN** the Rust host is run against the case that imports `morphir_get_workspace_info` as `() -> i64`
- **THEN** the report records the observed result and the linked deviation
- **AND** the Rust host is not reported as qualified for that case
