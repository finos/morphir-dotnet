# Proposal

## Why

The approved parent [finos/morphir#1154](https://github.com/finos/morphir/pull/1154), merged as `9e7d90e2fb5708580baef7cdd323cd994030c237`, requires a complete generic guest run in one MEP `0.2.0-draft.1` process. The existing .NET runner records 16 model cases across separate describe and model sessions, and cannot supply the required 21-case evidence.

## What Changes

- Adopt parent TIER-1, LIFE-1, SEL-1, KIT-1 and RUN-1 without changing their case IDs, requests, comparisons or order. Select each IR version from the frontend/backend intersection and its JSON model tuple pair; legacy compile/generate flags are irrelevant.
- **BREAKING** Replace the generic runner's legacy/model split with one required session. Legacy-only guests fail describe. Python `0.6.0` with both supported versions has 21 cases; a single selected version has 16.
- Expose the existing crafted WASM host commands and generic `mep guest-run` / `mep report-check` commands through a dedicated development `morphir-mck` executable using `Morphir.Conformance`. Preserve product #590 forwarding before parsing and the product dependency closure without MCK.
- Preserve the fixed kit bytes and digest. Add the approved parent delta as separately attributed evidence, independent reviewed expectations and mutation controls. Report checking rebuilds the plan and comparisons from the recorded claim and observations.
- Record complete Python `0.6.0` runs through pinned MoonBit and .NET workers, including the five real moved lifecycle cases. Keep crafted host observations distinct from the parent-owned host qualification verdict.

## Capabilities

### New Capabilities

- `mep/generic-guest-profile`: repository-local adoption of the approved generic guest profile, its single-session plan, fixed comparisons and reproducible report checking.
- `development-mck-command`: an internal, experimental executable exposing the shared crafted and generic runners independently of the product CLI.

### Modified Capabilities

None. Existing `mep-protocol` and `mep-frontend-sessions` remain the protocol implementation contracts. This profile consumes them; it does not change MEP.

## Tracking

- Runtime dependency and local issue: [#563](https://github.com/finos/morphir-dotnet/issues/563), implemented in [#570](https://github.com/finos/morphir-dotnet/pull/570). This branch starts at its normal commit `1e244a7c`; receive refreshed committed source from its owner without rewriting that history.
- Parent implementation tasks 2.1 through 2.4 of `drop-generic-guest-legacy-tier`. This distinct planning change must pass independent/parent review and merge before production implementation.
- The planning ownership map is in `design.html`; the [feature PRD](../../../docs/content/contributing/design/prds/adopt-generic-guest-kit.md) and index row are included in this planning tranche. Task 1.2 confirms their tracking state before production changes. The broad `establish-morphir-compatibility-kit` change is not silently implemented or rewritten here.

## Impact

- `src/Morphir.Conformance/GuestKit/`, its tests and pinned evidence; proposed new `src/Morphir.Mck/` development executable and command tests.
- Existing `WasmHostCommands` remain the crafted suite implementation. Host report verdict/schema/fixed data remain the parent contract owner's responsibility.
- Worker process lifecycle reuses the existing edge. The held generated-file prototype is not an approved helper; its unresolved defects and watchdog fix block adopting it. No second native process edge is introduced.
- Contributor and CLI docs will name the separate development executable and remove obsolete model-tier-only descriptions after full evidence exists. Product `MorphirApplication` / Tooling removal belongs to the Extism owner.
- No public NuGet publication, official offering change, release, version, tag, Rust runner replacement, CET change or Spectre mitigation change is included.
