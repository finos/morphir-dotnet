# Proposal

Archived planning/implementation record. Runtime PR landed; this archive follow-up and coordinator cleanup remain pending. See [SDK runtime delivery](../../../../docs/design/dotnet-csharp-generation/sdk-runtime-delivery.md).

## Why

The existing F# runtime is published as Morphir.SDK. A language-specific package name must be introduced without invalidating generated source, existing binaries or consumers that install the old package.

## What Changes

- Publish Morphir.SDK.FSharp as the canonical F# runtime package while preserving the existing Morphir.SDK namespace and runtime assembly identity.
- Retain Morphir.SDK as a compatibility package that references the canonical runtime, without duplicating implementation assemblies.
- Update F# code generation consumers, packaging and documentation together; existing generated source remains valid.
- Add the F# adapter for the admitted conformance cases and correct covered semantic discrepancies, including first-dictionary-wins union behavior, with migration notes.

## Capabilities

### New Capabilities

- `fsharp-sdk-packaging`: Introduce F# SDK package with explicit compatibility and verification behavior.

### Modified Capabilities

None. Existing backend, model and publication contracts are preserved.

## Tracking

- Requires an integrated, verified commit of [establish-dotnet-sdk-conformance](../2026-11-03-establish-dotnet-sdk-conformance/proposal.md).
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Worker brief: [handoff](handoff.md). Contribution issue: [#465](https://github.com/finos/morphir-dotnet/issues/465).
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own existing/new F# SDK packaging and runtime files, tests/Morphir.SDK.Tests/, F# generator runtime-reference integration and F# migration docs. Package identity changes, not an automatic namespace rename. Corrected union behavior is a documented compatibility-affecting bug fix.

Production implementation, release publication, new Codex thread launch and source-generator delivery are outside this planning action.
