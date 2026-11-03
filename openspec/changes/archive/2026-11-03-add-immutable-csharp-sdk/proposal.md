# Proposal

Archived planning/implementation record. Runtime PR landed; this archive follow-up and coordinator cleanup remain pending. See [SDK runtime delivery](../../../../docs/design/dotnet-csharp-generation/sdk-runtime-delivery.md).

## Why

Generated C# needs a runtime with immutable C# public types and Morphir behavior, without exposing FSharp.Core types or depending on compiler and CLI libraries.

## What Changes

- Add Morphir.SDK.CSharp with immutable Maybe, Result, Unit, Order and the initial scalar/collection operations.
- Use immutable collections with structural value equality, consistent hashing and explicit Morphir ordering.
- Implement the admitted conformance inventory and document supported semantics and representability limits.
- Prove packaged use from ordinary C# consumers under the stable and RC compiler profiles.

## Capabilities

### New Capabilities

- `csharp-sdk-runtime`: Add immutable C# SDK with explicit compatibility and verification behavior.

### Modified Capabilities

None. Existing backend, model and publication contracts are preserved.

## Tracking

- Requires an integrated, verified commit of [establish-dotnet-sdk-conformance](../2026-11-03-establish-dotnet-sdk-conformance/proposal.md).
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Worker brief: [handoff](handoff.md). Contribution issue: [#464](https://github.com/finos/morphir-dotnet/issues/464).
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own src/Morphir.SDK.CSharp/, tests/Morphir.SDK.CSharp.Tests/ and C# SDK documentation. Runtime API is one baseline shared by C# 14 and 15; no dependency on Morphir.Tooling, Morphir.Models, Roslyn, or FSharp.Core.

Production implementation, release publication, new Codex thread launch and source-generator delivery are outside this planning action.
