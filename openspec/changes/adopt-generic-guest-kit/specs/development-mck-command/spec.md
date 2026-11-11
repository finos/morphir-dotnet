# Spec Delta

## Purpose

Gives contributors one separate development executable for the shared crafted WASM host and generic guest runners, with reproducible reports and no kit linked into the product CLI.

## ADDED Requirements

### Requirement: Separate development executable
The internal experimental executable `morphir-mck` SHALL expose `wasm-host run`, `wasm-host report-check`, `mep guest-run` and `mep report-check`. Crafted and generic commands SHALL use the shared conformance runner and comparisons. The product CLI SHALL continue forwarding every `morphir mck` invocation unchanged before parsing, without linking MCK or its kit.

#### Scenario: Crafted command migration
- **WHEN** a contributor invokes morphir-mck wasm-host run or report-check with the existing crafted command options
- **THEN** the command preserves the runner's argument validation, observations, report checks and qualification notice

#### Scenario: Product forwarding
- **WHEN** the product receives morphir mck with an existing or unknown subcommand
- **THEN** it forwards the remaining arguments unchanged to separately resolved morphir-mck before CLI parsing

### Requirement: Explicit generic inputs
Generic guest-run SHALL require an explicit kit, host executable, guest module and report destination, with explicit host arguments when needed. It SHALL verify pinned identities and record exact digests and worker observations. Report-check SHALL rederive the generic result using the supplied kit and recorded observations rather than trust stored success fields.

#### Scenario: Missing kit option
- **WHEN** guest-run has no kit option
- **THEN** it fails as invalid invocation before starting a worker

#### Scenario: Modified observation
- **WHEN** a report's malformed-source result is edited to include a model
- **THEN** report-check fails the case even if the stored result says passed

### Requirement: Output and status
Development commands SHALL reserve stdout for command data, write diagnostics to stderr, and return 0 for successful run/check, 1 for failed checks or operational errors and 2 for invalid invocation. Every command SHALL identify itself as internal and experimental in its help and documentation.

#### Scenario: JSON output purity
- **WHEN** a run fails with diagnostics while JSON output is requested
- **THEN** stdout contains only valid command JSON and diagnostics appear on stderr

### Requirement: Bounded existing worker edge
Runs SHALL use one existing worker process edge with explicit arguments, no shell, bounded output and deadlines, and whole-tree termination followed by awaiting exit on timeout or cancellation. A shared helper with unresolved review defects SHALL NOT be adopted as a validated dependency or duplicated by the development executable.

#### Scenario: Worker exceeds deadline
- **WHEN** a worker or its descendant hangs beyond the granted deadline
- **THEN** the run fails and its owned process tree is terminated and awaited without a successful report

### Requirement: Development closure and qualification boundary
The development executable SHALL be built independently of product offerings. It SHALL NOT require publishing Conformance solely for Tooling, change official package identities, or infer host qualification from generic profile success. Crafted HostReport SHALL retain its observations-only behavior until the parent normative verdict contract is supplied and reviewed.

#### Scenario: Successful generic run
- **WHEN** all 21 generic cases pass and report-check succeeds
- **THEN** the result describes that generic profile run and does not claim a platform, package or host is qualified

#### Scenario: Parent host verdict missing
- **WHEN** crafted evidence is checked before the parent verdict contract lands
- **THEN** its qualification notice remains and a qualification verdict is not fabricated
