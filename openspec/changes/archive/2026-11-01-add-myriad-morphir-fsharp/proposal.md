# Proposal

## Why

F# applications need to compile business logic from existing Morphir model JSON files without translating it by hand. A Myriad plugin can make that conversion part of an ordinary F# build, starting with the classic IR formats already used by Morphir toolchains.

## What Changes

- Add a consumer-facing `Morphir.Myriad` plugin with the generator name `morphir-fsharp`, accepting a versioned Morphir library model `.json` file and generating one F# source file per Morphir module.
- Support explicit integer `formatVersion` values 1, 2 and 3 through version-aware decoding into the existing classic IR. Generate type definitions and executable value definitions, including functions and pattern matching.
- Preserve package/module qualification, access control, references, recursion and deterministic declaration ordering. Map supported Morphir SDK references explicitly and reject unsupported constructs with actionable diagnostics instead of emitting incomplete code.
- Provide a build coordinator that discovers modules, invokes Myriad for each selected module, and adds generated files to F# compilation in dependency order. Include local package validation, compiler and behavior tests, reviewed snapshots, and a documented working example.
- Deliver a plugin reference guide, a getting-started article and an end-to-end car rental tutorial, linked from the documentation site and package README. Use one runnable example covering inventory, reservation, pickup, return and billing, with checked-in classic model JSON and a thin F# consumer of the generated functions.
- Verify the car rental lifecycle and rejected transitions through the real packaged plugin. Keep the example simple: one active reservation per car, explicit whole-day durations and integer-cent billing. Demonstrate a model rate change, regeneration and the changed invoice without editing generated F#.
- Defer IR 4+, document-tree inputs or conversions, a Morphir CLI command, watch mode, optional JSON codecs and lenses, and arbitrary external package linking to later initiatives. Open row-polymorphic records are explicitly unsupported in this pass.

## Capabilities

### New Capabilities

- `morphir-fsharp-transpilation`: Decode classic Morphir model JSON, translate supported types and values to deterministic F#, and report translation failures.
- `myriad-model-generation`: Discover and run the JSON-to-F# generator through Myriad and compile its output in a consuming F# project.

### Modified Capabilities

None. The repository currently has no main OpenSpec capability specs.

## Tracking

- This change records the approved first Myriad delivery. Implementation and verification evidence are tracked in [tasks.md](tasks.md).
- Related planning: [existing F# backend PRD](../../../../docs/design/PRD-fsharp-backend.md) and [backend milestones](../../../../docs/design/fsharp-backend-maturity-milestones.md). Implementation must reconcile those documents and the PRD index with this narrower delivery before marking work in progress.
- This proposal deliberately changes the draft PRD's v3-only compatibility and CLI-first integration while retaining its per-module file layout. It replaces the PRD's lossy extensible-record fallback with an explicit error and rejects cross-module cycles that cannot compile across separate files. The broader backend roadmap remains separate.
- No GitHub issue was supplied. Link an existing or newly authorized issue during implementation preparation; do not invent an issue number or publish issues as part of this proposal.

## Impact

- Proposed projects: `src/Morphir.CodeGeneration.FSharp` for the reusable decoder/backend, `src/Morphir.Myriad` for the Myriad adapter and package, and `src/Morphir.Myriad.Build` for the build coordinator task, with corresponding tests and a buildable example.
- Proposed example: `examples/car-rental/`, with equivalent v1/v2/v3 fixtures, readable model/rule explanations, an executable F# consumer and automated lifecycle checks. Planned articles live under `docs/content/docs/getting-started/` and `docs/content/docs/guides/`; documentation commands must run against the shipped example.
- Reuse `Morphir.Models` classic ADTs and existing JSON primitives. Full library, module, value and pattern decoding must be added where missing; existing validation fixtures contain empty libraries and do not demonstrate transpilation.
- Use the centrally managed Myriad.Core, Fabulous.AST and Fantomas dependencies. Bundle the matched Myriad host and its notices in the plugin package so a cold first restore supplies the complete build toolchain.
- Keep the development-only, `.fs`-input `Morphir.Internal.CodeGeneration` generators separate. No public IR/JSON format changes or changes to existing generator behavior are required.
- Implementation includes solution/build integration, local package assets, usage documentation and PRD tracking. Publication and archiving remain separate review steps.
