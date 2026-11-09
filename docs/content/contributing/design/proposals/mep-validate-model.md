---
title: "Proposal: the validateModel payload for MEP"
linkTitle: "MEP validateModel payload"
description: >
  A proposal for finos/morphir: the model-tier validator method morphir.validator.validateModel, with its Smithy shapes, transcripts and model-transport cases.
---

# Proposal: the validateModel payload for MEP

Status: Upstream as the draft pull request [finos/morphir#1139](https://github.com/finos/morphir/pull/1139) (head `155ce26f40e8caa917ed947d5c6d1be9928c3ee4`), opened by task 7.2 of [`add-mep-core`](https://github.com/finos/morphir-dotnet/blob/main/openspec/changes/add-mep-core/tasks.md).

| Item | Value |
| --- | --- |
| Upstream target | `spec/mep` in `finos/morphir` |
| Contract base | MEP `0.2.0-draft.1`, pinned at `finos/morphir@cde7722eb35987042a49dc646a846d693b0bd7f6` (`tests/Fixtures/Mep/PROVENANCE.md`) |
| Upstream check | On 2026-11-09, `finos/morphir` `main` (`ecb3c781`) has no change under `spec/mep` since the pin |
| Source in this repository | Task 7.1 and design UP-1 of `openspec/changes/add-mep-core`; step 1 of the [Morphir extension model PRD](../prds/morphir-extension-model.md) |

## Summary

This proposal adds one model-tier method to MEP: `morphir.validator.validateModel`. The host sends one `MorphirModel` envelope and optional options. The extension returns a list of diagnostics. Negotiation, envelope admission and limits are the same as for `morphir.frontend.compileModel` and `morphir.backend.generateModel`.

The proposal adds these contract items, and no others:

- the model operation value `morphir.validator.validateModel`;
- the method `morphir.validator.validateModel` (Gleam `Method` constructor `ValidateModel`);
- the request `ValidateModelRequest` and the result `ValidateModelResult`;
- the capability record `ValidatorCapability`, as the member `validator` of `ExtensionCapabilities`.

The legacy method `morphir.validator.validate` stays reserved. This proposal does not define its payload.

## Problem

MEP `0.2.0-draft.1` has three validator items:

- the extension type `validator` (`spec/mep/smithy/model/handshake.smithy`, lines 70 to 77);
- the method name `morphir.validator.validate`, with `EmptyParams` and `OpaqueResult`. Its doc comment says: "The contract does not specify the payloads yet" (`mep.smithy`, lines 280 to 289);
- the error code `ValidationError`, `-32003` (`mep.smithy`, line 363).

MEP has no request, no result, no capability record and no model-transport operation for validation. `spec/mep/README.md` lists "validate and transform payloads" under "Not covered yet". The Smithy model marks the place with the gaps `smithy-2qe.34.20` and `smithy-2qe.34.25`.

Thus an extension cannot claim validation in a form that a host can check, and a host cannot send a model to it. morphir-dotnet needs this contract for external analyzers (steps 2a and 2b of the PRD).

The upstream implementations show the same gap:

- The Rust SDK has a `Validator` trait, and `ValidateRequest { ir, options }` and `ValidateResult { valid, diagnostics }` on the legacy method (`finos/morphir-rust`, `crates/morphir-extension-sdk/src/types.rs`, lines 553 to 571; `src/traits.rs`, lines 69 to 80). These types are not in the contract.
- The Rust host lets a session call `morphir.validator.validate` when `extension.types` contains `validator` (`crates/morphir-host/src/negotiated.rs`, line 111). It has no other check.
- The MoonBit guest and host have no validator method (`finos/morphir-moonbit`, `pkgs/morphir-moonbit/mep/session.mbt`; `pkgs/morphir-host/worker`).

## Scope

In scope:

- the method, its request, its result and its errors;
- the validator capability record and its place in the claim set and in the session;
- the model operation value, and the tuples, limits and admission that apply to it;
- a transcript and model-transport cases.

Out of scope:

- the payload of the legacy `morphir.validator.validate`;
- the transform payload;
- host conversion of a model before validation (see [open question 3](#open-questions));
- a vocabulary of validation rules or options;
- how a host merges the diagnostics of more than one validator;
- how a diagnostic points to a model node (see [open question 4](#open-questions)).

## Upstream conventions

This proposal follows these rules of the pinned contract and of the upstream implementations. It adds no new rule of its own.

| Convention | Source | Use in this proposal |
| --- | --- | --- |
| Model methods need the exact MEP `0.2.0-draft.1`. Legacy `0.1` never enables them. | `model-transport.md`, lines 14 to 20 | `validateModel` needs the same session. |
| A tuple has five members. Compare all five. The extension keeps the host order. The host uses the first agreed tuple. An empty agreement is `extension.tuple_unavailable`, a host outcome, not an RPC error. | `model-transport.md`, lines 34 to 141 | A `validateModel` tuple is an ordinary tuple. |
| Session limits are the bilateral minimum. They bound the sums of all payloads in one request or response. | `model-transport.md`, lines 67 to 71 and 200 to 209 | No change. |
| Admission bounds, decodes and checks before the component runs. Critical paths are a closed registered list. | `model-transport.md`, lines 190 to 198 and 246 to 254 | The same order and list. |
| Unsupported method `-32601`. Invalid shape, digest or bound `-32602`. Unavailable negotiated capability `-32013`. No new error numbers. Errors do not echo payload content. | `model-transport.md`, lines 281 to 285 | The same codes. `-32003` exists already for a validator that cannot run. |
| A model method needs its role capability and `modelTransport` in the session. | `mep.smithy`, lines 254 to 278 (`requires: ["frontend", "modelTransport"]`) | `requires: ["validator", "modelTransport"]`. |
| A model method string is also its `ModelOperation` value. | `model-transport.smithy`, lines 13 to 17 | `morphir.validator.validateModel` is both. |
| Every member is required and written, unless the wire mapping table names an exception. | `spec/mep/README.md`, "Wire mapping" | `diagnostics` is required. `options` is read as empty when absent, as in `GenerateModelRequest`. |
| A role capability record lists the IR versions that the role reads. The host also checks the tuple IR version against it. | `handshake.smithy`, lines 124 to 157; Rust `model/operation.rs`, lines 42 to 51 and 69 to 74 | `ValidatorCapability.irVersions`. |
| Legacy booleans (`compile`, `generate`) authorize only legacy methods. A tuple authorizes a model method. | `model-transport.md`, lines 61 to 65 | `ValidatorCapability` has no boolean. |

## Design

### The method

`morphir.validator.validateModel` is a request from the host to the extension. An extension accepts it only when all of these conditions are true:

1. The session state is `ready`.
2. The session protocol version is `0.2.0-draft.1`.
3. `extension.types` contains `validator`.
4. The session `capabilities.validator` is present.
5. The session `capabilities.modelTransport.tuples` contains a tuple with the operation `morphir.validator.validateModel`.

### The request

| Member | Type | Rule |
| --- | --- | --- |
| `model` | `MorphirModel` | Required. Its tuple is the operation `morphir.validator.validateModel` with the `profile`, `payload.codec`, `envelopeVersion` and `irVersion` of the envelope. That tuple must be a session tuple. |
| `options` | `JsonObject` | Read as empty when absent. The extension owns its members. An extension refuses a member that it does not know with `-32602`. |

The request has no `input` member. The envelope names four members of the tuple, and the method names the operation. See [open question 1](#open-questions).

The request has no `contextDigest` and no `requiredGuarantees`. A validator reads the model and reports. It needs no prerequisite from the host. An `evidence` entry in the envelope gets the usual admission and stays opaque.

### The result

| Member | Type | Rule |
| --- | --- | --- |
| `diagnostics` | `DiagnosticList` | Required. It can be empty. Each item is a contract `Diagnostic`. |

The result has no success flag. The host decides the outcome from the severities. A model that breaks a rule gives diagnostics in a result. A validator that cannot run gives a JSON-RPC error, not a result.

### The capability

`ValidatorCapability` has one member:

| Member | Type | Rule |
| --- | --- | --- |
| `irVersions` | `StringList` | Required. The semantic IR versions that the validator reads, with the same spelling and major-alias rule as `FrontendCapability.irVersions` and `BackendCapability.irVersions`. |

`ExtensionCapabilities.validator` is left out when absent, as `frontend`, `backend` and `workspace` are. `validator` becomes a reserved name, so a writer refuses an `extra` member with that name.

The claim set needs no change. `CapabilityClaimSet.capabilities` is an open object. A validator claims `validator` and its `validateModel` tuples in `modelTransport`. A claim with `validator` and no `validateModel` tuple claims nothing on the model line.

A `validateModel` tuple authorizes the method. The tuple `irVersion` must also be in `validator.irVersions`. This is the same double check that the Rust SDK makes for `compileModel` and `generateModel`.

### Negotiation

Negotiation does not change. The host offers `validateModel` tuples in its order of preference. The extension selects the offered tuples that it supports, in the host order. The session limits are the bilateral minimum.

```text
host offer       [validateModel/ion-binary, validateModel/morphir-json]
extension        [validateModel/morphir-json]
session support  [validateModel/morphir-json]   -> use morphir-json

host offer       [validateModel/morphir-json]   (host requires JSON)
extension        [validateModel/ion-binary]
session support  []                             -> extension.tuple_unavailable
```

A host offers a `validateModel` tuple only to an extension that claims one. See [Compatibility](#compatibility) for the reason.

### Admission by the extension

The extension admits the request in the order of `model-transport.md` (lines 190 to 198), and stops at the first failure:

1. The transport applies its outer frame limit.
2. The extension checks the method conditions above. A failure is `-32013`.
3. The extension reads the params. A shape that is not valid, or an unknown option, is `-32602`.
4. The tuple of `model` must be a session tuple, and its `irVersion` must be in `validator.irVersions`. A tuple that is not in the session is `-32602`. An IR version that the validator does not read is `-32013`.
5. `modelId` and `revision` are not empty.
6. The extension bounds the base64 text of all payloads (model and evidence) before it decodes, and checks the canonical form. It bounds the decoded bytes, checks the exact `decodedByteLength`, then checks `sha256`.
7. The extension decodes the payload under the depth and node limits. It checks the profile, the envelope version, the IR version and a complete distribution.
8. The extension checks each `critical` path against the registered list. It admits `evidence` under its own policy. The validator needs no guarantee, so unknown evidence stays opaque.

Steps 5 to 8 give `-32602`, with the admission code in the message, for example `Model admission refused: digest_mismatch`. That is what both upstream guests send for `generateModel` today.

Only an admitted model reaches the validator component.

### Errors

| Condition | Reply |
| --- | --- |
| The request comes before `morphir.initialize` or after `morphir.shutdown`. | `-32014` |
| The session is not `0.2.0-draft.1`, has no `validator` capability, has no `validateModel` tuple, or the tuple IR version is not in `validator.irVersions`. | `-32013` |
| The extension does not know the method (an older extension). | `-32601` |
| The params have a bad shape, the tuple is not in the session, a bound, the length, the base64 form or the digest is wrong, a critical path is unknown, or an option is unknown. | `-32602` |
| The validator cannot run, for example an internal failure or exhausted resources. | `-32003` |
| The host cancelled the request, and the extension advertised `cancellation`. | `-32800` |

A rule that the model breaks is a diagnostic in a result. Errors and logs do not echo payload content.

### Host behavior

The host sends `validateModel` only in a `ready` session, after its own checks of the method conditions and of the envelope. The host checks the result: `diagnostics` must be a list of valid `Diagnostic` values. A result that is not valid is a protocol fault of the extension.

These items are host-defined:

- how the host merges the diagnostics of more than one validator, and in which order;
- how the host attributes each diagnostic to its extension;
- how the severities map to an outcome or an exit code.

For example, `morphir validate` in morphir-dotnet runs every enabled analyzer, merges the diagnostics by extension id, and exits with 1 when one diagnostic has the severity `error` (`openspec/changes/add-mep-core/specs/model-analyzers/spec.md`).

The host conversion rule for generation (`model-transport.md`, lines 287 to 405) applies only to `generateModel`. This proposal does not extend it.

### The legacy method stays reserved

The `Validate` operation (`morphir.validator.validate`) does not change. It keeps `EmptyParams`, `OpaqueResult` and its gap comments. The Rust SDK `ValidateRequest` and `ValidateResult` stay outside the contract. `validateModel` does not depend on the legacy method. An extension can implement both.

## Smithy shapes

These shapes follow the style of `spec/mep/smithy/model`. The proposal adds one file, `model/validate.smithy`:

```smithy
$version: "2"

// Model validation, MEP 0.2.0-draft.1. See model-transport.smithy for the
// model envelope, and mep.smithy for the scope and the meaning of
// `GAP smithy-` comments.
namespace morphir.mep

/// What a validator can check. `irVersions` lists the semantic IR versions
/// that the validator reads. A `validateModel` tuple authorizes the method,
/// and its `irVersion` must also be in `irVersions`.
structure ValidatorCapability {
    @required
    irVersions: StringList
}

/// Parameters of `morphir.validator.validateModel`. The tuple of `model` is
/// the operation `morphir.validator.validateModel` with the `profile`,
/// `payload.codec`, `envelopeVersion` and `irVersion` of the envelope. It
/// must be an admitted session tuple. `options` belongs to the extension,
/// and is read as empty when absent.
// GAP smithy-2qe.34.24: nothing checks that the tuple of `model` is an
// admitted session tuple, or that its `irVersion` is in
// `validator.irVersions`.
structure ValidateModelRequest {
    @required
    model: MorphirModel

    options: JsonObject = {}
}

/// Result of `morphir.validator.validateModel`. There is no success flag:
/// the host decides the outcome from the severities. A validator that
/// cannot run answers with a JSON-RPC error, not with a result.
structure ValidateModelResult {
    @required
    diagnostics: DiagnosticList
}
```

It changes `model/mep.smithy`:

```smithy
// In @jsonRpcSession, add this capability after `backendGenerate`:
        { name: "validator", method: "morphir.initialize", pointer: "/capabilities/validator" }

// In ExtensionService, add `ValidateModel` after `Validate`.

/// Validate a complete model. Requires the exact MEP 0.2.0-draft.1 session,
/// the `validator` capability and an admitted validateModel tuple.
// GAP smithy-2qe.34.20: the session must use 0.2.0-draft.1, its tuples must
// contain morphir.validator.validateModel, and `extension.types` must
// contain `validator`.
@method(name: "morphir.validator.validateModel", params: "named")
@jsonRpcLifecycle(allowedIn: ["ready"], requires: ["validator", "modelTransport"])
operation ValidateModel {
    input: ValidateModelRequest
    output: ValidateModelResult
}
```

The doc comment of `Validate` becomes: "Validate Morphir IR. Legacy operation. The contract does not specify the payloads. `morphir.validator.validateModel` is the model-tier method."

It changes `model/handshake.smithy`:

```smithy
/// What an extension can do.
///
/// `frontend`, `backend`, `workspace`, `validator` and `modelTransport` are
/// left out when absent. ...
structure ExtensionCapabilities {
    frontend: FrontendCapability

    backend: BackendCapability

    workspace: WorkspaceCapability

    validator: ValidatorCapability

    modelTransport: ModelTransportSupport

    // The four flags do not change.
}
```

It changes `model/model-transport.smithy`:

```smithy
/// A model operation. These methods require the exact MEP draft
/// 0.2.0-draft.1. Legacy 0.1 never enables them.
enum ModelOperation {
    COMPILE_MODEL = "morphir.frontend.compileModel"
    GENERATE_MODEL = "morphir.backend.generateModel"
    VALIDATE_MODEL = "morphir.validator.validateModel"
}
```

## Gleam contract and wire mapping

The Gleam contract is the source of truth, so the upstream pull request changes `spec/mep/contract/src/mep.gleam` first:

```gleam
pub type ModelOperation {
  CompileModelOperation
  GenerateModelOperation
  ValidateModelOperation
}

/// What a validator can check. ...
pub type ValidatorCapability {
  ValidatorCapability(ir_versions: List(String))
}

pub type ValidateModelRequest {
  ValidateModelRequest(model: MorphirModel, options: Dict(String, Json))
}

pub type ValidateModelResult {
  ValidateModelResult(diagnostics: List(Diagnostic))
}
```

`ExtensionCapabilities` gets `validator: Option(ValidatorCapability)`, and `validator` joins its reserved `extra` names. `Method` gets the constructor `ValidateModel`, with the table row `ValidateModel | morphir.validator.validateModel | request`.

The wire mapping table in `spec/mep/README.md` changes:

| Type | Exception |
| --- | --- |
| `ExtensionCapabilities` | Add `validator` to the members that are left out when absent, and to the reserved `extra` names. |
| `ModelOperation` | Add the method string `morphir.validator.validateModel`. |
| `ValidateModelRequest` | Absent `options` defaults to empty. The tuple of `model` must be a session tuple. |
| `ValidateModelResult` | No success flag. A failure to run is a JSON-RPC error. |

The README "Scope" section then lists "validate model payloads" as covered, and keeps "the legacy validate payload and the transform payload" as not covered.

## Transcripts

These transcripts use the upstream format (`spec/mep/smithy/transcripts`). Each fence `jsonrpc host -> extension` or `jsonrpc extension -> host` is one message. The model payload is the exact bytes of `spec/mep/mck/fixtures/plain-ir.json`: 130 bytes, SHA-256 `fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f`. The diagnostic code `example.empty-package` is test vocabulary.

### `session-validate-model.md`

````markdown
---
version: "2.0"
complete: true
---
# A MEP 0.2.0-draft.1 model validation session

The host reads the claims, then offers two validateModel tuples. It prefers
Ion. The extension reads only JSON, so the session agrees on the JSON tuple.
The validateModel request and result are MCK case model-0036. The model is
the plain IR fixture `fixtures/plain-ir.json`. Every other message is
constructed.

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": "describe-1", "method": "morphir.extension.describe", "params": {"protocolVersions": ["0.2.0-draft.1"]}}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": "describe-1", "result": {"claimsVersion": "0.1.0-draft.2", "protocolVersions": ["0.2.0-draft.1"], "extension": {"id": "org.finos.morphir.example", "name": "Morphir example validator", "version": "1.0.0", "types": ["validator"]}, "capabilities": {"validator": {"irVersions": ["4.0.0"]}, "modelTransport": {"tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}], "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256}}, "streaming": false, "incremental": false, "cancellation": false, "progress": false}}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": 0, "method": "morphir.initialize", "params": {"protocolVersions": ["0.2.0-draft.1"], "host": {"kind": "cli", "name": "morphir-cli", "version": "0.4.0"}, "modelTransport": {"tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-model", "codec": "ion-binary", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}, {"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}], "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256}, "guarantees": []}}}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": 0, "result": {"protocolVersion": "0.2.0-draft.1", "extension": {"id": "org.finos.morphir.example", "name": "Morphir example validator", "version": "1.0.0", "types": ["validator"]}, "capabilities": {"validator": {"irVersions": ["4.0.0"]}, "modelTransport": {"tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}], "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256}, "guarantees": []}, "streaming": false, "incremental": false, "cancellation": false, "progress": false}}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "method": "morphir.initialized"}
```

MCK case model-0036, the request and its result with one warning:

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": 1, "method": "morphir.validator.validateModel", "params": {"model": {"envelopeVersion": "0.1.0-draft.1", "modelId": "urn:morphir:mep:fixture:model", "revision": "fixture-revision-1", "irVersion": "4.0.0", "profile": "morphir-ir", "payload": {"codec": "morphir-json", "layout": "single-document", "decodedByteLength": 130, "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f", "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="}, "evidence": [], "critical": []}, "options": {}}}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": 1, "result": {"diagnostics": [{"severity": "warning", "code": "example.empty-package", "message": "Package example/mep-fixture has no module."}]}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": "shutdown-1", "method": "morphir.shutdown"}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": "shutdown-1", "result": {}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "method": "morphir.exit"}
```
````

### `validate-model-failure.md`

````markdown
---
version: "2.0"
complete: true
---
# A validator that cannot run

The extension admits the model, but the validator cannot run. It answers
with -32003 (ValidationError), not with a result. The session stays ready,
so the host can shut it down. Every message is constructed.

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": 0, "method": "morphir.initialize", "params": {"protocolVersions": ["0.2.0-draft.1"], "host": {"kind": "cli", "name": "morphir-cli", "version": "0.4.0"}, "modelTransport": {"tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}], "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256}}}}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": 0, "result": {"protocolVersion": "0.2.0-draft.1", "extension": {"id": "org.finos.morphir.example", "name": "Morphir example validator", "version": "1.0.0", "types": ["validator"]}, "capabilities": {"validator": {"irVersions": ["4.0.0"]}, "modelTransport": {"tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}], "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256}, "guarantees": []}, "streaming": false, "incremental": false, "cancellation": false, "progress": false}}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "method": "morphir.initialized"}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": 1, "method": "morphir.validator.validateModel", "params": {"model": {"envelopeVersion": "0.1.0-draft.1", "modelId": "urn:morphir:mep:fixture:model", "revision": "fixture-revision-1", "irVersion": "4.0.0", "profile": "morphir-ir", "payload": {"codec": "morphir-json", "layout": "single-document", "decodedByteLength": 130, "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f", "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="}}}}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": 1, "error": {"code": -32003, "message": "Validation could not run."}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "id": 2, "method": "morphir.shutdown"}
```

```jsonrpc extension -> host
{"jsonrpc": "2.0", "id": 2, "result": {}}
```

```jsonrpc host -> extension
{"jsonrpc": "2.0", "method": "morphir.exit"}
```
````

The second transcript leaves out `evidence`, `critical` and `options`, which the contract reads as empty. The Smithy README transcript table gets two rows:

| Transcript | Content | Result |
| --- | --- | --- |
| `session-validate-model.md` | a `0.2.0-draft.1` validator session with MCK case model-0036 | Valid |
| `validate-model-failure.md` | a validator that cannot run, then -32003 | Valid |

## Model-transport cases

These cases use the format of `spec/mep/mck/model-transport.cases.json`, and continue its numbering. Upstream appends them to `cases`. The README case table gets the row `0036–0040 | validateModel tuples, protocol line and integrity`. As in the existing corpus, `hostOffer` and `guestSupport` are projections of the handshake, and `expected.code` is an observation label, not a JSON-RPC error number. In case model-0037, the host reports `extension.tuple_unavailable` and sends no request; the request shows what the host would have sent.

```json
{
  "cases": [
    {
      "id": "model-0036",
      "kind": "negotiation",
      "description": "Exact bilateral validateModel tuple admits a model validation request.",
      "hostOffer": {
        "protocolVersions": ["0.2.0-draft.1", "0.1"],
        "modelTransport": {
          "tuples": [
            {"operation": "morphir.validator.validateModel", "profile": "morphir-model", "codec": "ion-binary", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"},
            {"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}
          ],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "guestSupport": {
        "protocolVersion": "0.2.0-draft.1",
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "request": {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "morphir.validator.validateModel",
        "params": {
          "model": {
            "envelopeVersion": "0.1.0-draft.1",
            "modelId": "urn:morphir:mep:fixture:model",
            "revision": "fixture-revision-1",
            "irVersion": "4.0.0",
            "profile": "morphir-ir",
            "payload": {
              "codec": "morphir-json",
              "layout": "single-document",
              "decodedByteLength": 130,
              "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f",
              "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="
            },
            "evidence": [],
            "critical": []
          },
          "options": {}
        }
      },
      "result": {"diagnostics": [{"severity": "warning", "code": "example.empty-package", "message": "Package example/mep-fixture has no module."}]},
      "expected": {"admission": "accept", "code": "accepted", "retainedAsset": "fixtures/plain-ir.json"}
    },
    {
      "id": "model-0037",
      "kind": "negotiation",
      "description": "An empty validateModel agreement makes the operation unavailable. The host sends no request.",
      "hostOffer": {
        "protocolVersions": ["0.2.0-draft.1", "0.1"],
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "guestSupport": {
        "protocolVersion": "0.2.0-draft.1",
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-model", "codec": "ion-binary", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "request": {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "morphir.validator.validateModel",
        "params": {
          "model": {
            "envelopeVersion": "0.1.0-draft.1",
            "modelId": "urn:morphir:mep:fixture:model",
            "revision": "fixture-revision-1",
            "irVersion": "4.0.0",
            "profile": "morphir-ir",
            "payload": {
              "codec": "morphir-json",
              "layout": "single-document",
              "decodedByteLength": 130,
              "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f",
              "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="
            },
            "evidence": [],
            "critical": []
          },
          "options": {}
        }
      },
      "expected": {"admission": "reject", "code": "unsupported_tuple"}
    },
    {
      "id": "model-0038",
      "kind": "negotiation",
      "description": "Generate support does not authorize validateModel.",
      "hostOffer": {
        "protocolVersions": ["0.2.0-draft.1", "0.1"],
        "modelTransport": {
          "tuples": [{"operation": "morphir.backend.generateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "guestSupport": {
        "protocolVersion": "0.2.0-draft.1",
        "modelTransport": {
          "tuples": [{"operation": "morphir.backend.generateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "request": {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "morphir.validator.validateModel",
        "params": {
          "model": {
            "envelopeVersion": "0.1.0-draft.1",
            "modelId": "urn:morphir:mep:fixture:model",
            "revision": "fixture-revision-1",
            "irVersion": "4.0.0",
            "profile": "morphir-ir",
            "payload": {
              "codec": "morphir-json",
              "layout": "single-document",
              "decodedByteLength": 130,
              "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f",
              "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="
            },
            "evidence": [],
            "critical": []
          },
          "options": {}
        }
      },
      "expected": {"admission": "reject", "code": "unsupported_tuple"}
    },
    {
      "id": "model-0039",
      "kind": "negotiation",
      "description": "Legacy MEP0.1 cannot invoke validateModel.",
      "hostOffer": {
        "protocolVersions": ["0.2.0-draft.1", "0.1"],
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "guestSupport": {
        "protocolVersion": "0.1",
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "request": {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "morphir.validator.validateModel",
        "params": {
          "model": {
            "envelopeVersion": "0.1.0-draft.1",
            "modelId": "urn:morphir:mep:fixture:model",
            "revision": "fixture-revision-1",
            "irVersion": "4.0.0",
            "profile": "morphir-ir",
            "payload": {
              "codec": "morphir-json",
              "layout": "single-document",
              "decodedByteLength": 130,
              "sha256": "fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f",
              "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="
            },
            "evidence": [],
            "critical": []
          },
          "options": {}
        }
      },
      "expected": {"admission": "reject", "code": "unsupported_protocol"}
    },
    {
      "id": "model-0040",
      "kind": "integrity",
      "description": "SHA256 binds the decoded bytes of a validated model.",
      "hostOffer": {
        "protocolVersions": ["0.2.0-draft.1", "0.1"],
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "guestSupport": {
        "protocolVersion": "0.2.0-draft.1",
        "modelTransport": {
          "tuples": [{"operation": "morphir.validator.validateModel", "profile": "morphir-ir", "codec": "morphir-json", "envelopeVersion": "0.1.0-draft.1", "irVersion": "4.0.0"}],
          "limits": {"maxEncodedBytes": 8192, "maxDecodedBytes": 4096, "maxDepth": 64, "maxNodes": 256},
          "guarantees": ["urn:morphir:mep:fixture:checked"]
        }
      },
      "request": {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "morphir.validator.validateModel",
        "params": {
          "model": {
            "envelopeVersion": "0.1.0-draft.1",
            "modelId": "urn:morphir:mep:fixture:model",
            "revision": "fixture-revision-1",
            "irVersion": "4.0.0",
            "profile": "morphir-ir",
            "payload": {
              "codec": "morphir-json",
              "layout": "single-document",
              "decodedByteLength": 130,
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "content": "eyJmb3JtYXRWZXJzaW9uIjoiNC4wLjAiLCJkaXN0cmlidXRpb24iOnsiTGlicmFyeSI6eyJwYWNrYWdlTmFtZSI6ImV4YW1wbGUvbWVwLWZpeHR1cmUiLCJkZXBlbmRlbmNpZXMiOnt9LCJkZWYiOnsibW9kdWxlcyI6e319fX19Cg=="
            },
            "evidence": [],
            "critical": []
          },
          "options": {}
        }
      },
      "expected": {"admission": "reject", "code": "digest_mismatch"}
    }
  ]
}
```

The Smithy check compares each case with `spec/mep/smithy/mck-expectations.json`. The entries for these cases:

```json
{
  "model-0036": {"request": [], "result": [], "note": "Valid. The MEP outcome is accept."},
  "model-0037": {"request": [], "note": "Valid. MEP refuses it at negotiation: the agreement for validateModel is empty, so the host sends no request. Not checked: smithy-2qe.34.24."},
  "model-0038": {"request": [], "note": "Valid. MEP refuses it at admission: the session has no validateModel tuple. Not checked: smithy-2qe.34.20."},
  "model-0039": {"request": [], "note": "Valid. MEP refuses it at admission: the session is not MEP 0.2.0-draft.1. Not checked: smithy-2qe.34.20."},
  "model-0040": {"request": [], "note": "Valid. MEP refuses it at admission after decoding: digest mismatch. Out of scope for a message model."}
}
```

## Compatibility

The change is additive. It adds one method, one model operation value, one optional capability member and three shapes. No existing shape, method or wire value changes.

Existing extensions read an offered tuple strictly. Both upstream guests refuse an operation value that they do not know:

- The MoonBit guest refuses the whole offer (`invalid_support`), so `morphir.initialize` fails with `-32602` "Invalid model transport offer" (`finos/morphir-moonbit`, `pkgs/morphir-moonbit/mep/model_transport.mbt`, lines 113 to 135; `session.mbt`, lines 222 to 232).
- The Rust SDK `ModelOperation` is a closed enum, so `InitializeParams` does not parse, and `morphir.initialize` fails with `-32602` (`finos/morphir-rust`, `crates/morphir-extension-sdk/src/model/wire.rs`, lines 11 to 20; `src/lib.rs`, `dispatch_initialize_with_metadata`).

Thus a host offers a `validateModel` tuple only to an extension that claims one, in its `morphir.extension.describe` result or in the recorded claims. The MoonBit host already offers the tuples of one operation for each session (`pkgs/morphir-host/worker/README.mbt.md`, "Probe, activate, call, close"). The other choice is a new reader rule: "A reader ignores an offered tuple whose operation it does not know." That rule changes existing readers. See [open question 2](#open-questions).

An older host never offers a `validateModel` tuple. It reads `capabilities.validator` from a newer extension as an `extra` member, as the contract says, and ignores it.

The name `validator` becomes reserved in `ExtensionCapabilities`. An extension that writes a root `extra` member `validator` today changes meaning. We know of no such extension. The MoonBit guest writes `moonbitFrontendProfiles` only.

The legacy `morphir.validator.validate` does not change.

## Alternatives considered

- **Define the legacy `morphir.validator.validate` payload**, as `{ir, options}` to `{valid, diagnostics}`, like the Rust SDK. Rejected. The legacy `0.1` line is frozen (`spec/mep/README.md`, "Versioning"). Its plain JSON IR has no bounds and no digest. New model operations need the new draft and bilateral transport admission.
- **A success or `valid` flag in the result.** Rejected. It repeats the severities, and the two can disagree. The host decides from the severities, as morphir-dotnet `validate` does.
- **An `input` tuple member, as in `GenerateModelRequest`.** Not chosen for now, because the envelope and the method give the same five values. See [open question 1](#open-questions).
- **A `validate` boolean in `ValidatorCapability`, as `compile` and `generate`.** Rejected. Those booleans authorize only legacy methods, and a tuple authorizes a model method. The legacy validate has no defined payload to authorize.
- **Validation diagnostics in the `compileModel` or `generateModel` result.** Rejected. That joins two roles. `validator` is its own extension type.
- **Validation as a transform that returns the same model.** Rejected. A validator does not change the model, and the transform payload is not defined.

## Open questions

1. **`input` member.** Should `ValidateModelRequest` carry `input: ModelTransportTuple`, as `GenerateModelRequest` does? For: one pattern for all model methods, and the upstream code paths (`selected` in Rust, `retain_input` in MoonBit) take a tuple from the request. Against: the member repeats the envelope.
2. **Draft version and the offer rule.** Does upstream edit `0.2.0-draft.1` in place, or publish `0.2.0-draft.2`? Drafts match exactly. An in-place edit needs the offer rule in [Compatibility](#compatibility), or a new rule that readers ignore unknown operations. A new draft needs every peer to move. morphir-dotnet moves all its MEP parts together in both cases (PRD, "MEP version moves").
3. **Host conversion for validation.** The host can have a model in a tuple outside the agreement. Can it convert the model before validation, as for `generateModel`? The diagnostics then refer to the converted model.
4. **Model-node locations.** `Diagnostic.location` is a source URI and a range. A model validator often has no source. Does the contract define a way to name a model node, for example an FQName in `data`, or a URI scheme? This proposal does not.
5. **Rule identity and options.** The Rust `Validator` trait has `validation_rules()`. Does `ValidatorCapability` list rule ids, and does the contract define common options? This proposal does not.
6. **Generated IR.** `mise run spec:mep` stops now, because the Gleam frontend moved to `finos/morphir-gleam` (`spec/mep/README.md`, "Regenerate"; bead `morphir-sugs`). The upstream pull request needs a new `generated/mep.ir.json`. Who makes it, and with which tool?

## Follow-up in morphir-dotnet

- Task 7.2 opens the upstream draft pull request after maintainer approval, and records its URL and commit in the PRD.
- Steps 2a and 2b pin the upstream draft by commit.
- Until upstream accepts a payload, `Morphir.Mep` has no `validateModel` type. Nothing on the wire in `add-mep-core` depends on this proposal (design UP-1).
- The round-trip check of task 7.1 (transcript messages through the group 2 codecs, in a test-only copy) waits for task 2.3.

## References

| Repository | Commit | Files |
| --- | --- | --- |
| `finos/morphir` | `cde7722eb35987042a49dc646a846d693b0bd7f6` (pin); `ecb3c781c3cc334821588392cf81ee1346455090` (`main`, no `spec/mep` change) | `spec/mep/README.md`, `spec/mep/model-transport.md`, `spec/mep/smithy/README.md`, `spec/mep/smithy/model/*.smithy`, `spec/mep/smithy/transcripts/*`, `spec/mep/mck/README.md`, `spec/mep/mck/model-transport.cases.json`, `spec/mep/contract/src/mep.gleam` |
| `finos/morphir-rust` | `99c0b67637afcdae011cd7c9e93b9fd13bddd9cb` | `crates/morphir-extension-sdk/src/{lifecycle.rs, protocol.rs, lib.rs, types.rs, traits.rs, model/*.rs}`, `crates/morphir-host/src/{session_core.rs, jsonrpc.rs, negotiated.rs}`, `crates/morphir-host-native/src/process/frame.rs` |
| `finos/morphir-moonbit` | `28271c61379523f28312f1989baed4f368722bcd` | `pkgs/morphir-moonbit/mep/{session.mbt, model_transport.mbt, model_input.mbt, model_budget.mbt, framing.mbt}`, `pkgs/morphir-host/worker/{session.mbt, README.mbt.md}` |
