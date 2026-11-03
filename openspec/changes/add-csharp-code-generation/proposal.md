# Proposal

## Why

Morphir .NET needs reusable C# generation for types and executable business logic that respects target language capabilities and can later be hosted by a source generator.

## What Changes

- Add a deterministic in-memory typed-model generator for classic and v4 Library distributions, using Morphir.SDK.CSharp.
- Support explicit C# 14 and 15 target profiles with stable defaults and a shared public representation; distinguish toolchain release channel from language version.
- Generate the bounded type/expression/SDK subset and return located refusals for unsupported semantics, never placeholder implementations.
- Verify generated behavior across IR and language versions, immutability, naming, cancellation, and trimming; establish compiler-host packaging evidence before freezing the public entry point.

## Capabilities

### New Capabilities

- `morphir-csharp-transpilation`: Add C# code generation with explicit compatibility and verification behavior.

### Modified Capabilities

None. Existing backend, model and publication contracts are preserved.

## Tracking

- Requires an integrated, verified commit of [add-immutable-csharp-sdk](../archive/2026-11-03-add-immutable-csharp-sdk/proposal.md).
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Worker brief: [handoff](handoff.md). Implementation is tracked by [issue #474](https://github.com/finos/morphir-dotnet/issues/474) and the [feature PRD](../../../docs/content/contributing/design/prds/add-csharp-code-generation.md).
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own src/Morphir.CodeGeneration.CSharp/, tests/Morphir.CodeGeneration.CSharp.Tests/ and backend docs. Keep Tooling and Roslyn host APIs out of the core library. The later Roslyn adapter is deferred; modern compiler-host compatibility smoke evidence belongs here.

Production implementation, release publication, new Codex thread launch and source-generator delivery are outside this planning action.
