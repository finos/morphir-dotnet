# Proposal

## Why

Step 1 of the [Morphir extension model](../../../docs/content/contributing/design/prds/morphir-extension-model.md) PRD. External analyzers (step 2a, WASM; step 2b, process) and the MEP path of the F# frontend (`add-fsharp-compile`, groups 7 to 9) all need the same Morphir Extension Protocol (MEP) core in .NET. There is no MEP code in the repository yet, and the two plans must not make two copies of it. Analyzers also need a contract in process before they need one on the wire: the built-in `TypeValidator` can prove the analyzer shape now, with no runtime risk.

## What Changes

- Extend the Myriad JSON generator first with opt-in missing-member defaults, empty-collection omission, flattened extension data and versioned member/path migration. These additional wire rules were confirmed against the pinned contract and approved on 2026-11-08 UTC. Deliver this prerequisite separately from the protocol integration.

- Add `src/Morphir.Mep`, an F# project that is trimmable and AOT-compatible. It is the one MEP layer for every morphir-dotnet host, guest and frontend. It holds:
  - our own JSON-RPC 2.0 layer on System.Text.Json (the [JSON-RPC spike](../../../docs/content/contributing/design/spikes/extension-model/jsonrpc.md) decision): messages, `u64`-or-string request ids, error replies, one dispatcher for single messages (`bytes -> bytes`), `Content-Length` framing with the upstream limits, a connection with a pending-request table, `$/cancelRequest` and notifications;
  - the MEP `0.2.0-draft.1` session types and the lifecycle (`describe`, `initialize`, `initialized`, `info`, `capabilities`, `shutdown`, `exit`) as one pure state machine for hosts and guests;
  - the model transport: tuple negotiation and `MorphirModel` envelope admission (bounds, canonical base64, length, SHA-256, limits), shared by `compileModel` now and by `generateModel` and the validator later;
  - the MEP `Diagnostic` type.

  The JSON converters and the context for the MEP types are generated at build time by the Myriad JSON generator of the `add-myriad-json-codecs` change, which this change depends on. Only the classification of a JSON-RPC message (request, notification, response or error, by the members it has) is written by hand. No reflection. No upstream protocol member is added.
- Add an in-process analyzer contract: an analyzer reads a decoded model and returns diagnostics in the MEP `Diagnostic` shape, together with its extension id. Analyzers are additive: every enabled analyzer runs, and the diagnostics merge.
- Register the existing `TypeValidator` as the built-in analyzer `morphir.type-validator`, with the MEP type `validator`. The other plugins in `src/Morphir.IR.Pipeline.Plugins` stay unregistered, because no command runs them.
- Add `morphir validate --input <file> [--json]`. It reads a model file in the same way as `morphir generate --input` and runs the enabled built-in analyzers. Human output is a TUI table on stdout; `--json` writes one report. External analyzers come in step 2a. `validate` is the command name that the upstream Rust CLI reserves.
- Map registry entries to the MEP `ExtensionInfo` and capability claims, as step 0 planned for when `Morphir.Mep` lands.
- Write the upstream proposal for the validate payload in `finos/morphir`: a validator capability record, a `validateModel` model-transport operation, and its request and result types. Steps 2a and 2b build against the pinned draft.
- Change the `add-fsharp-compile` plan: tasks 7.1 to 7.3 build on `Morphir.Mep` from this change, and that plan adds only the `compileModel` types and the context digest.

## Capabilities

### New Capabilities

- `mep-protocol`: the MEP `0.2.0-draft.1` core in .NET: JSON-RPC 2.0 messages and ids, framing and limits, dispatch and cancellation, the session lifecycle, model-transport negotiation and envelope admission, and AOT safety.
- `model-analyzers`: the in-process analyzer contract, the diagnostic shape, the built-in type validator, the additive run rule and the `morphir validate` command.

### Modified Capabilities

- `myriad-json-generation`: opt-in record defaults, empty omission, extension data, versioned aliases and critical-path validation for the MEP wire rules.
- `extension-registry`: built-in analyzers are registered entries with the type `validator`, and every entry maps to a MEP `ExtensionInfo` with claims that the pinned contract accepts.

## Tracking

- Roadmap: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 1. Step 0 landed in #503.
- Issue: [#512](https://github.com/finos/morphir-dotnet/issues/512). The F# frontend epic is #499.
- Depends on: `add-myriad-json-codecs` (the Myriad JSON generator, [#511](https://github.com/finos/morphir-dotnet/issues/511)).
- Related: `add-fsharp-compile` (#495) shares `Morphir.Mep`. #505 (MCK suite for step 0) waits for the compatibility kit.

## Impact

- New projects: `src/Morphir.Mep` and `tests/Morphir.Mep.Tests`. Both join `Morphir.slnx`, the AOT closure check and the trim and AOT analysis.
- `src/Morphir.Extensions`: a validator claim and the MEP `ExtensionInfo` mapping. It references `Morphir.Mep`.
- `src/Morphir.IR.Pipeline.Plugins`: no behavior change. An adapter calls `TypeValidator`.
- `src/Morphir.Tooling`: a `Features/Validate` slice and the `validate` command in both hosts. It reuses the generate model loader.
- Documentation: `docs/content/docs/cli/validate.md`, the PRD status table and the `add-fsharp-compile` tasks.
- No new third-party package dependency. `Morphir.Mep` uses the `Morphir.Myriad.SystemTextJson` plugin at build time and its annotations package. The `morphir` binary grows only by the MEP layer (estimate: 100 to 300 KB). The WASM runtime comes in step 2a.
- Upstream: one draft pull request in `finos/morphir` for the validate payload, opened after maintainer approval.
