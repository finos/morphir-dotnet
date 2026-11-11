# Spec Delta

## Purpose

Defines the generic JSON Library guest profile adopted from finos/morphir#1154 for the .NET development morphir-mck runner, with fixed data and replayable evidence. The parent requirements and scenario structure are retained below; additional local requirements make the fixed plan and evidence boundaries explicit.

## ADDED Requirements

### Requirement: One session on MEP 0.2
For every guest, the runner SHALL start one process and negotiate MEP
`0.2.0-draft.1` with the `morphir-json` tuples of the selected IR versions. It
SHALL NOT offer MEP `0.1`. For each selected IR version it SHALL compile the
fixed source with `morphir.frontend.compileModel`, generate with
`morphir.backend.generateModel` from the compiled model and from a model built
from the kit's expected IR, and compile the files of each generate case again.

#### Scenario: Guest claims JSON model tuples at both IR versions
- **WHEN** a guest claims `0.2.0-draft.1` and JSON `compileModel` and `generateModel` tuples at `3.0.0` and `4.0.0`
- **THEN** the plan has the cases of `3.0.0` and then of `4.0.0`, all in one process

#### Scenario: Guest that speaks only MEP 0.1
- **WHEN** a guest claims only protocol version `0.1`
- **THEN** the describe case fails and the run is not qualified

#### Scenario: Guest that also claims MEP 0.1
- **WHEN** a guest claims protocol versions `0.1` and `0.2.0-draft.1`
- **THEN** the runner offers only `0.2.0-draft.1` and no request uses `0.1`

### Requirement: Any extension identity
The profile `generic-json-library-v1` SHALL accept any extension id. The
describe result SHALL give a non-empty `id`, a non-empty `name` and a SemVer
`version`, and every initialize result SHALL repeat the same `extension`
value.

#### Scenario: SDK fixture identity
- **WHEN** a guest describes itself as `morphir-sdk-fixture` version `0.1.0`
- **THEN** the identity check passes

#### Scenario: Initialized identity changes
- **WHEN** the initialize result gives an `extension` value that differs from the describe result
- **THEN** the run is not qualified

### Requirement: One fixed package with one expected IR per version
The kit SHALL fix one package, `mck/generic` with module `main`, and one
expected IR for each of `3.0.0` and `4.0.0`. Every language source set in the
kit SHALL describe the same package and SHALL compare with the same expected
IR.

#### Scenario: Two languages, one expectation
- **WHEN** the Python source set and the flags source set compile at `3.0.0`
- **THEN** the runner compares both results with the one `3.0.0` expected IR

### Requirement: Language source sets
The kit SHALL give, for each kit language, the documents of the fixed package
and the documents of a malformed source. Each document URI SHALL be relative to
the source folder. It SHALL NOT be absolute, have a scheme, a `src/` prefix, an
empty segment, or a `.` or `..` segment.

#### Scenario: Relative URI
- **WHEN** the Python source set gives the document `main.py`
- **THEN** the kit loads

#### Scenario: Prefixed URI
- **WHEN** a source set gives the document `src/main.py` or `/work/main.py`
- **THEN** the kit is refused before any guest starts

### Requirement: Language selection from the claim
The runner SHALL select the first kit language, in lexical order of its id,
that the guest claims both as a frontend language id and as a backend target. When the guest
claims no such language, the run SHALL NOT be qualified.

#### Scenario: Guest claims Python
- **WHEN** the guest claims frontend language `python` and backend target `python`
- **THEN** the runner uses the Python source set

#### Scenario: Language without a source set
- **WHEN** the guest claims only a language that the kit has no source set for
- **THEN** the describe case fails and the run is not qualified

### Requirement: IR versions from the claim
The runner SHALL test each IR version, of `3.0.0` and `4.0.0`, that the guest
claims for both its frontend and its backend and for which it claims both a
`compileModel` and a `generateModel` `morphir-json` tuple. A claim of `3` or
`3.0.0` SHALL select `3.0.0`, and a claim of `4` or `4.0.0` SHALL select
`4.0.0`. When no version is selected, the run SHALL NOT be qualified.

#### Scenario: Guest claims only IR 3
- **WHEN** the guest claims IR version `3` for its frontend and its backend, and the JSON tuple pair at `3.0.0`
- **THEN** the plan has the `3.0.0` cases and no `4.0.0` cases

#### Scenario: Frontend and backend differ
- **WHEN** the frontend claims `3` and `4`, the backend claims only `4`, and the guest claims the JSON tuple pair at both versions
- **THEN** the plan has only the `4.0.0` cases

#### Scenario: Claimed version without a JSON tuple pair
- **WHEN** the frontend and the backend claim `3` and `4` and the guest claims the JSON tuple pair only at `4.0.0`
- **THEN** the plan has only the `4.0.0` cases

#### Scenario: No JSON tuple pair
- **WHEN** a guest claims `0.2.0-draft.1` but no JSON `compileModel` and `generateModel` pair at an IR version its frontend and backend claim
- **THEN** the describe case fails and the run is not qualified

### Requirement: Negotiated tuples and limits
The initialize result SHALL select exactly the offered tuples, in
the host order within each operation; operations can interleave. Its limits
SHALL be the bilateral minimum. A compiled model SHALL stay inside those limits
for encoded bytes, decoded bytes, depth (root is one) and value nodes.

#### Scenario: Reversed versions
- **WHEN** the offer lists compile tuples at `3.0.0` then `4.0.0` and the guest selects `4.0.0` first
- **THEN** the model-initialize case fails

#### Scenario: Interleaved operations
- **WHEN** the guest selects the generate tuples before the compile tuples and keeps the order within each operation
- **THEN** the model-initialize case passes

#### Scenario: Model above a negotiated limit
- **WHEN** the guest negotiates `maxDecodedBytes` of 64 and returns a larger model
- **THEN** the compile-model case fails

### Requirement: Semantic IR comparison
A compiled IR SHALL equal the expected IR after these rules only: JSON object
member order is ignored; in IR `3` the order of the `modules`, `types` and
`values` name-keyed lists is ignored; in IR `4`, at an `outputType` or a member of `inputTypes`, a `Reference` with
an `fqname` and no or empty `args` equals its FQName string.

#### Scenario: Different value order
- **WHEN** a guest returns IR `3` with `blocked` before `ready`
- **THEN** it equals the expected IR, which has `ready` first

#### Scenario: Long form type reference
- **WHEN** a guest returns IR `4` with `{"Reference": {"fqname": "morphir/SDK:basics#bool"}}` as an output type
- **THEN** it equals the expected `"morphir/SDK:basics#bool"`

#### Scenario: Reference outside a type position
- **WHEN** a guest returns IR `4` with `{"Reference": {"fqname": "mck/generic"}}` as the `packageName`
- **THEN** the comparison fails

#### Scenario: Wrong type
- **WHEN** a guest returns IR `4` with output type `Unit` for `ready`
- **THEN** the comparison fails

### Requirement: Successful compile
A successful compile SHALL have `success` true, a diagnostics array without an
`error` diagnostic, and a valid `morphir-json` model that equals the expected
IR. Warning and info diagnostics SHALL be allowed.

#### Scenario: Compile with a warning
- **WHEN** a compile succeeds with one `warning` diagnostic and the expected IR
- **THEN** the compile case passes

### Requirement: Generated files and round trip
Every successful generate SHALL return a non-empty list of text artifacts with
unique, relative paths. After every generate case, the artifacts whose path
ends in a claimed file extension of the selected language SHALL compile again,
in the same session, to the expected IR. The kit SHALL NOT fix generated bytes.

#### Scenario: Round trip keeps the IR
- **WHEN** the generated `main.flags` compiles again to the expected IR
- **THEN** the round trip case passes

#### Scenario: Round trip loses a type
- **WHEN** the generated file compiles again with a different output type
- **THEN** the round trip case fails and the run is not qualified

### Requirement: Refusals and lifecycle
The runner SHALL require that a malformed source gives `success` false with no
model and an `error` diagnostic, that an unknown method gives `-32601`, that a
request after shutdown gives `-32014`, that notifications give no output, that
`describe` does not change, and that the process exits cleanly. A payload with
a wrong SHA-256 SHALL give `-32602`.

#### Scenario: Malformed source
- **WHEN** the guest compiles the kit's malformed source with `morphir.frontend.compileModel`
- **THEN** the result has `success` false, no model and an `error` diagnostic

#### Scenario: Request after shutdown
- **WHEN** the runner sends a request after the guest answered `morphir.shutdown`
- **THEN** the guest answers with error `-32014`

### Requirement: Numeric request ids
Each request SHALL have a numeric id, counted from 1 in each guest process.
Identity echo of string or other id forms is outside this profile.

#### Scenario: Guest that reads only numeric ids
- **WHEN** a guest accepts only numeric JSON-RPC ids
- **THEN** the profile can still qualify it

### Requirement: Supported claims format
The describe result SHALL use a supported claims format: `claimsVersion`
`0.1.0-draft.2`, or the draft.1 field `statementVersion` `0.1.0-draft.1`. A
claim with both fields, another version, or a `critical` path other than its
version field SHALL fail the describe case.

#### Scenario: Draft.1 claim
- **WHEN** a guest describes itself with `statementVersion` `0.1.0-draft.1`
- **THEN** the describe case passes the format check

#### Scenario: Unknown claims version
- **WHEN** a guest sends `claimsVersion` `garbage` or `0.1.0-draft.3`
- **THEN** the describe case fails

### Requirement: Recheck from the recorded claim
`morphir-mck mep report-check` SHALL rebuild the plan from the recorded
describe observation, and SHALL check the case order, every request and every
response against the kit. A changed claim in a stored report SHALL fail the
recheck unless the full recorded plan agrees with it.

#### Scenario: Report with a removed case
- **WHEN** a stored report omits the round trip case of a selected IR version
- **THEN** `report-check` fails

#### Scenario: Earlier legacy-tier report
- **WHEN** a report contains legacy-tier cases or the earlier Rust plan
- **THEN** the changed report check refuses it, even when its stored verdict says qualified

#### Scenario: Edited claim changes selection
- **WHEN** a stored claim is edited to remove the JSON tuple pair for one recorded IR version
- **THEN** report-check refuses the report because the rebuilt plan differs

### Requirement: Fixed complete plan
The plan SHALL use the exact IDs and order of parent TIER-1: describe, initialize, initialized notification, unknown notification, five cases per selected IR version with IR 3 before IR 4, bad digest, malformed source, unknown method, immutable describe, shutdown, after-shutdown and exit. There SHALL be 11 fixed cases and five per selected IR version. An unrun required case SHALL prevent qualification.

#### Scenario: Python 0.6.0 full plan
- **WHEN** Python 0.6.0 claims both selected IR versions
- **THEN** its plan has exactly 21 cases, including the five moved lifecycle cases with their original IDs

#### Scenario: One selected pair
- **WHEN** only IR 4.0.0 has both frontend/backend claims and a JSON tuple pair
- **THEN** its plan has exactly 16 cases with no vthree case

#### Scenario: Model methods without legacy flags
- **WHEN** valid model tuples and version claims are present with frontend.compile and backend.generate false
- **THEN** those flags do not prevent selection or require legacy calls

#### Scenario: Required case did not run
- **WHEN** a selected required case is missing or recorded as not-run
- **THEN** the report cannot qualify, and no case is silently skipped

### Requirement: Unchanged kit and independent expectations
The kit SHALL retain its fixed file bytes, contractVersion 0.1.0-draft.1 and SHA-256 f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae. Expected IR SHALL be fixed reviewed data, never computed by the implementation under test. Provenance SHALL retain its historical Python 0.4.2 origin and distinguish later contract adoption and independent review.

#### Scenario: Expectation mutation
- **WHEN** a returned Bool output type becomes Unit, or a non-normalized array is reordered
- **THEN** comparison fails against the unchanged expected IR

#### Scenario: Edited kit file
- **WHEN** a kit byte changes while the recorded digest stays fixed
- **THEN** the run or report check refuses the input before claiming conformance

### Requirement: Full evidence through both workers
Complete generic guest evidence SHALL run pinned Python 0.6.0 through both MoonBit and .NET workers, recording exact kit, guest module, host executable and runner identities and digests, selected claims and all 21 cases. Both reports SHALL pass the same report check. Generic profile results SHALL NOT imply host, platform, package or release qualification.

#### Scenario: Model-tier-only evidence
- **WHEN** evidence contains only the earlier 16 model-tier steps for a two-version guest
- **THEN** it does not satisfy the full generic profile or any host qualification gate

#### Scenario: Lifecycle difference on a real worker
- **WHEN** either worker's real guest answers a moved lifecycle case differently in MEP 0.2
- **THEN** the difference is recorded as a guest defect or contract question before evidence is accepted, without weakening the expectation
