# Proposal

## Why

Morphir .NET has an F# transpiler and JSON/Ion readers, but no CLI command that connects model input to generated source. Add the ecosystem's `morphir generate` workflow with separate settings, model-loading, backend and publication boundaries so future project configuration and MEP integration can reuse it.

## What Changes

- Add `morphir generate --input <file-or-directory> --target fsharp --output <directory>`, with `--input-format auto|json|ion`, repeated `--option KEY=VALUE`, and `--json` reporting. Input, target and output are explicit in this delivery; the default input format is `auto`.
- Generate F# from classic JSON v1/v2/v3 and v4 libraries, plus their supported Ion text/binary and Ion-tree representations. V4 generation is required in this delivery, including nonempty compiled-and-executed fixtures. Do not silently migrate v4 to classic IR.
- Use one behavior-oriented test corpus, compiler/execution harness and assertion approach across supported versions and formats. Version, codec, layout and optimization are case dimensions, not separate testing strategies. Represent real version differences as explicit applicability or expected-outcome data.
- Retain the captured input alongside a transport-neutral typed model. Keep exact source versions and representation facts distinct from backend capabilities; never use JSON serialization as the bridge from Ion to F# generation.
- Add a versioned typed-model F# generation entry point while preserving the existing strict classic-JSON file API. Use typed classic and v4 validation/emission paths with shared F# naming and artifact helpers; expose Boolean `optimize`, defaulting to true.
- Register F# through a backend contract with target/version/distribution capabilities. Resolve settings and provider selection outside the generator; use static registration compatible with trimming and Native AOT.
- Publish generated `VFileTree` output through the existing publisher, including an ownership manifest, staged writes, portable path checks and written/unchanged/removed reporting. Diagnostics identify input, rule and available model location; JSON reports remain parseable on stdout.
- Define the settings-resolution and retained-model contracts for future `morphir.toml`/`morphir.yml` adapters and MEP invocation. Include illustrative API snippets, equivalent configuration examples and protocol mappings in `design.html`.

This delivery does not implement TOML/YAML generation settings, configuration discovery or merging, MEP sessions or extension installation, YAML model input, MEP `morphir_model` envelope admission, implicit prior-compile input, generated project files, or build/execute/verify commands. Generation targets reusable Library distributions; Application packaging and Specs-only generation remain outside this delivery. V4 incomplete/native/external bodies, document literals and unrepresentable target values receive explicit diagnostics as detailed in the specs. Future integration examples in the design are clearly labeled. Unknown targets and unimplemented flags fail rather than pretending to work.

## Capabilities

### New Capabilities

- `cli-model-generation`: Resolve an explicit generation request, select a compatible registered backend, report generation results and safely publish artifacts.
- `generation-model-loading`: Load supported file/tree representations into retained, versioned typed models with format-specific diagnostics and preservation guarantees.

### Modified Capabilities

- `morphir-fsharp-transpilation`: Add typed classic and v4 library generation for JSON/Ion inputs while keeping the existing JSON-file API and its compatibility rules.

## Tracking

- Related: [F# backend epic #363](https://github.com/finos/morphir-dotnet/issues/363) and [CLI integration #368](https://github.com/finos/morphir-dotnet/issues/368).
- The issue's earlier `morphir gen fsharp` spelling is superseded in this proposal by the user-requested `morphir generate --target fsharp`; this is a new command, not removal of an implemented alias.
- Implementation progress belongs in this change's `tasks.md`. Establish the repository-required feature PRD and index entry as part of implementation, before production changes.

## Impact

- CLI registration: `src/Morphir/Program.cs` and a generation scenario in `src/Morphir.Tooling/Scenarios/`.
- Application orchestration and adapters: `src/Morphir.Tooling/`, using its existing mediator and host registration. No generation policy in command actions.
- Shared semantic model types: additive contracts in `src/Morphir.Models/`; `Morphir.Ion` remains a codec dependency of the loader, not of the F# generator.
- Generator: `src/Morphir.CodeGeneration.FSharp/Transpiler.fs`, version-specific typed validation/emission and shared naming/artifact helpers. Preserve the current JSON wrapper, Myriad consumers and public IR/JSON formats. V4 library support extends generator behavior without changing existing classic generated APIs.
- Reuse `src/Morphir.IR.Pipeline/` publication and file/tree contracts. No new configuration parser, dynamic assembly loader, or MEP runtime dependency is required.
- Verification: extend the existing tests with a shared behavior corpus, representation adapters, compiler/execution runner and assertions. Run supported version/format combinations through that common approach, including CLI, publication, optimization, trimming/AOT and explicit refusal outcomes. Do not create parallel v3/v4 harnesses or duplicate behavioral oracles.
- Baseline: merged Ion reading work at `0a7f6cafbeb33fef63640e1faf988091c6180617` (PR #456). This planning checkout predates that commit; implementation must first integrate that baseline without overwriting local work.
