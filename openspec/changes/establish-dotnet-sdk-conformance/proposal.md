# Proposal

## Why

The two .NET SDKs need a common, independently sourced behavioral contract. The existing F# SDK is not a reliable oracle for every operation, including dictionary union precedence.

## What Changes

- Curate a bounded, provenance-pinned SDK inventory and language-neutral cases from Morphir Python, MoonBit, and morphir-elm vnext.
- Define tagged expected values, explicit .NET representability limits, strict coverage classifications, and a documented extension process.
- Record existing F# mismatches as named gaps without weakening downstream runtime acceptance.
- Publish the dependency graph, ownership rules and self-contained Codex handoffs for the five changes.

## Capabilities

### New Capabilities

- `dotnet-sdk-conformance`: Establish .NET SDK conformance with explicit compatibility and verification behavior.

### Modified Capabilities

None. Existing backend, model and publication contracts are preserved.

## Tracking

- This is the first implementation change; no other new change is a prerequisite.
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Worker brief: [handoff](handoff.md). Implementation tracking: [issue #460](https://github.com/finos/morphir-dotnet/issues/460), created before test-tool implementation.
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own new tests/Fixtures/MorphirSDK/ fixtures and validation, and docs for the initial SDK contract. Draft the umbrella PRD for serial coordinator integration and ongoing status/index maintenance. Do not modify runtime implementations, emitters, CLI contracts or IR wire formats.

Production implementation, release publication, new Codex thread launch and source-generator delivery are outside this planning action.
