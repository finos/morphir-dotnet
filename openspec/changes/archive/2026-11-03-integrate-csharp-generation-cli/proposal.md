# Proposal

## Why

The C# generation library needs a thin adapter into the existing morphir generate command so users receive the same bounded loading, typed failures and safe publication as the F# backend.

## What Changes

- Register exact target csharp through IGenerationBackend with immutable validated options and explicit model capabilities.
- Expose language-version=14 or 15 and optional namespace through existing repeated --option settings; default to 14.
- Reuse existing model loading, publication, JSON reports and exit codes without a second orchestration path.
- Add end-to-end compiled/executed examples, failure preservation, cancellation, and trimmed/Native AOT smoke coverage for the new target.
- Replace five diagnostic-only structural reflection formats discovered by actual NativeAOT execution with explicit cancellable structural spellings.

## Capabilities

### New Capabilities

- `cli-csharp-generation`: Integrate C# generation into the CLI with explicit compatibility and verification behavior.

### Modified Capabilities

- `morphir-csharp-transpilation`: Add a validated request-local namespace override
  and source-compatible overload to deterministic core naming. Preserve existing
  options, default output, typed models, source paths and runtime protocol.

## Tracking

- Requires an integrated, verified commit of [add-csharp-code-generation](../2026-11-03-add-csharp-code-generation/proposal.md).
- Delivery plan, merged-to-main completion and cleanup gates: [orchestration](../../../../docs/design/dotnet-csharp-generation/orchestration.md).
- Contribution issue: [#481](https://github.com/finos/morphir-dotnet/issues/481).
- Worker brief: [handoff](handoff.md). The feature PRD is
  [CLI integration](../../../../docs/content/contributing/design/prds/integrate-csharp-generation-cli.md).
- Establish the feature PRD and index entry during implementation before production code, as specified in tasks. Implementation is not authorized by creating this proposal.

## Impact

Own CSharpGenerationBackend.cs, its Tooling tests, GenerationScenario.cs registration and additive C# CLI documentation. The existing cli-model-generation capability is defined by the completed add-generate-command change but is not yet in main specs here; this additive capability does not rewrite that change.

The coordinator approved a narrow integration lease for the core Types,
TypeEmission, ValueEmission and Generator files, namespace tests and the public
API/diagnostic guide. A privately constructed immutable `NamespaceOverride` and
four-argument `Generate` overload retain the existing `GenerationOptions` record
and three-argument entry point. Core naming applies the namespace to the input
Library package's declarations and qualified references. It preserves package
source paths and never rewrites generated source text or migrates the input IR.

Actual NativeAOT execution required a separate bounded correction in
TypeEmission and ValueLowering, with public NamespaceCases regressions and a
diagnostic-guide addition. Only diagnostic message text and the eager module
validation location spelling change. Rule IDs, public APIs, refusal/cancellation
behavior and source artifacts remain unchanged. See [validation](validation.md)
for observed failures, warning policy and acceptance limits.

Release publication, new Codex thread launch and full source-generator adapter
delivery remain outside this CLI implementation.
