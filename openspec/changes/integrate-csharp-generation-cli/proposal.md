# Proposal

## Why

The C# generation library needs a thin adapter into the existing morphir generate command so users receive the same bounded loading, typed failures and safe publication as the F# backend.

## What Changes

- Register exact target csharp through IGenerationBackend with immutable validated options and explicit model capabilities.
- Expose language-version=14 or 15 and optional namespace through existing repeated --option settings; default to 14.
- Reuse existing model loading, publication, JSON reports and exit codes without a second orchestration path.
- Add end-to-end compiled/executed examples, failure preservation, cancellation, and trimmed/Native AOT smoke coverage for the new target.

## Capabilities

### New Capabilities

- `cli-csharp-generation`: Integrate C# generation into the CLI with explicit compatibility and verification behavior.

### Modified Capabilities

None. Existing backend, model and publication contracts are preserved.

## Tracking

- Requires an integrated, verified commit of [add-csharp-code-generation](../archive/2026-11-03-add-csharp-code-generation/proposal.md).
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Worker brief: [handoff](handoff.md). No GitHub issue exists for this new change yet; the coordinator must create or link one before contribution, without inventing an ID.
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own CSharpGenerationBackend.cs, its Tooling tests, GenerationScenario.cs registration and additive C# CLI documentation. The existing cli-model-generation capability is defined by the completed add-generate-command change but is not yet in main specs here; this additive capability does not rewrite that change.

Production implementation, release publication, new Codex thread launch and source-generator delivery are outside this planning action.
