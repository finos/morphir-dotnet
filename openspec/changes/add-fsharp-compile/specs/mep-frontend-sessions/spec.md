# Spec Delta

## Purpose

Runs Morphir frontends as separate processes through the latest Morphir Extension Protocol contract, `0.2.0-draft.1`, and its `compileModel` model transport from `finos/morphir`. The AOT `morphir` executable can then use frontends it cannot contain, and Morphir .NET frontends work with other Morphir hosts.

## ADDED Requirements

### Requirement: Sessions follow the pinned MEP 0.2.0-draft.1 contract
Hosts and guests SHALL use JSON-RPC 2.0 over stdio with `Content-Length` framing. They SHALL use these methods with the member spelling of the pinned upstream contract: `morphir.extension.describe`, `morphir.initialize`, `morphir.initialized`, `morphir.extension.info`, `morphir.extension.capabilities`, `morphir.frontend.compileModel`, `morphir.shutdown` and `morphir.exit`. No message, member or method outside that contract SHALL be added.

#### Scenario: Upstream transcript replay
- **WHEN** the frontend messages of the pinned upstream model-transport session and lifecycle transcripts are replayed against the guest, with the documents changed to F#
- **THEN** every response has the upstream envelope, member names and value types, and messages from the guest outside that set fail the test

### Requirement: Only the latest protocol version is spoken
The guest SHALL claim and accept only protocol version `0.2.0-draft.1`, which must match exactly. The host SHALL offer only that version. Initialization with no common version SHALL fail with `-32011`. A request other than `describe` or `initialize` before initialization SHALL fail with `-32014`. A second `initialize` SHALL fail. An unknown method SHALL fail with `-32601`, and the session SHALL stay usable.

#### Scenario: Legacy-only host
- **WHEN** a host initializes with `protocolVersions: ["0.1"]`
- **THEN** the guest returns `-32011`, and a later `initialize` with `0.2.0-draft.1` succeeds

### Requirement: Model tuples are negotiated exactly
The host SHALL offer complete `compileModel` tuples, each with operation, profile `morphir-ir`, codec `morphir-json`, envelope version `0.1.0-draft.1` and IR version, in its preference order, together with its limits. The guest SHALL select the offered tuples it supports, in host order, with limits at the bilateral minimum. The host SHALL verify this selection, and it SHALL send no request when the agreement is empty (`extension.tuple_unavailable`).

#### Scenario: Host preference kept
- **WHEN** the host offers compile tuples at `4.0.0` and then `3.0.0`
- **THEN** the guest selects them in that order, and the host compiles at `4.0.0` unless a different version was requested

#### Scenario: Ion-only offer
- **WHEN** the host offers only a `morphir-model`/`ion-binary` compile tuple
- **THEN** the agreement is empty, and the host reports `extension.tuple_unavailable` without sending `compileModel`

### Requirement: The F# guest claims what it supports
The F# guest's claim set SHALL use claims version `0.1.0-draft.2`. It SHALL claim:

- language `fsharp` with file extension `.fs`;
- `multiDocument: true`;
- the legacy `compile` flag as false;
- `incremental: false`;
- JSON `compileModel` tuples for IR versions `3.0.0` and `4.0.0`.

The IR version in each tuple SHALL be the exact format version string.

#### Scenario: Claimed versions work
- **WHEN** each claimed IR version is requested through the guest for the same source
- **THEN** each request succeeds, and the decoded model reads back through that version's reader

### Requirement: Classic versions without a tuple are re-encoded by the host
Classic format versions 1 and 2 have no tuple. For them, the host SHALL compile at `3.0.0` and write the decoded classic model with the requested version's wire profile. It SHALL NOT convert a model from another family to produce them.

#### Scenario: Classic version 2 through MEP
- **WHEN** the AOT executable compiles with `--ir-version 2` through the guest
- **THEN** the host requests `3.0.0`, writes the decoded classic model as format version 2 without converting it to another family, and the result equals the in-process IR

### Requirement: Models travel in verified envelopes
A successful `compileModel` result SHALL hold one `MorphirModel` envelope with envelope version `0.1.0-draft.1`, a stable `modelId`, a deterministic `revision`, the selected `irVersion` and profile, and a payload. The payload SHALL be canonical padded base64 of the UTF-8 IR JSON, with an exact `decodedByteLength` and a lowercase SHA-256. The receiver SHALL check bounds, encoding, length, digest, envelope version, IR version and critical paths before decoding. Failures SHALL use `-32602`.

#### Scenario: Digest mismatch
- **WHEN** a payload's SHA-256 does not match its decoded bytes
- **THEN** the receiver refuses with `-32602`, does not decode the IR, and keeps the session usable

#### Scenario: Request context digest
- **WHEN** the host sends `compileModel`
- **THEN** the request carries a 64-digit lowercase SHA-256 context digest computed by the documented construction, and `options.irVersion` equals the output tuple's IR version

### Requirement: Source problems are results, not protocol errors
`compileModel` SHALL return the success/failure result sum:

- on success, `success: true` with the model and no error diagnostic;
- on failure, `success: false` with diagnostics and no `model` member, not even `null`.

Unsupported constructs and F# errors SHALL be failures with diagnostics. JSON-RPC errors SHALL be used only for protocol, lifecycle, admission and internal failures.

#### Scenario: Warnings travel with a successful model
- **WHEN** a compile request produces a model and an `fsharp.compiler` warning
- **THEN** the response has `success: true`, the model, and that warning in `diagnostics`, and the host reports the warning with exit code 0

#### Scenario: Unsupported construct through MEP
- **WHEN** a compile request contains `let mutable`
- **THEN** the response is a JSON-RPC result with `success: false`, no `model` member and an `fsharp.unsupported_mutable` diagnostic

### Requirement: Guest and in-process results are equivalent
For the same documents, package and IR version, the IR decoded from the guest's model SHALL equal the in-process frontend's written IR byte for byte. The diagnostics SHALL be equal too.

#### Scenario: Parity
- **WHEN** the shared F# compile corpus runs in process and through the guest for every IR version
- **THEN** the IR bytes and the diagnostic lists are equal for every case

### Requirement: Hosts refuse requests the guest did not admit
The host SHALL consult the initialized session capabilities on every run. Before dispatch, it SHALL refuse languages, multi-document requests, tuples and IR versions that the session does not admit, and the refusal SHALL name the extension and the capability. It SHALL NOT invent a tuple or a version the guest did not select.

#### Scenario: Unadmitted version
- **WHEN** a configured guest selects only `4.0.0` compile tuples, and the compile requests `3`
- **THEN** the host refuses before sending `compileModel`, naming the extension id and its tuples. It does not convert a v4 model to classic.

### Requirement: Host sessions are bounded and clean up
The host SHALL start the configured extension executable with explicit arguments and no shell. It SHALL enforce frame, payload, depth and node limits and response deadlines. It SHALL drain the guest's stderr to its own stderr, and it SHALL always end with `shutdown` and `exit`, or terminate the process tree after a deadline. Guest output that is not a valid frame SHALL fail the run with exit code 2.

#### Scenario: Guest writes a log line to stdout
- **WHEN** a guest writes non-framed text to stdout during a session
- **THEN** the host fails the compile with a protocol diagnostic naming the extension, exits with code 2 and leaves no child process running

### Requirement: The host works in trimmed and AOT builds
The MEP host client SHALL run in the trimmed, Native AOT `morphir` executable with source-generated serialization and no reflection-based loading.

#### Scenario: AOT executable compiles through the guest
- **WHEN** the published AOT `morphir` executable compiles F# with `--extension` set to the guest executable
- **THEN** the compile succeeds, and the IR equals the IR that `dotnet-morphir` produces in process
