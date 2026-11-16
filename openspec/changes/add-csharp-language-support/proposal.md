# Proposal

## Why

Morphir can write C# today, but it cannot read C#, and it cannot show a user why a piece of C# is outside the Morphir subset. The C# backend writes text with `String.concat` and `StringBuilder`. It has no compiler to check what it writes. It refuses `LetRecursion`. It has no source generator for a user build. The `morphir generate` path runs no validation or rewriting between the model and the backend.

This change adds `Morphir.Lang.CSharp`: one package with a C# frontend, a middle end that runs Morphir IR to Morphir IR passes, and a C# backend, all on Roslyn. A C# author then gets Morphir compile errors in the editor and in `dotnet build`, writes a model in plain C# 15 (records, `closed` types, unions), and gets the generated C# back from the model with the same shape. The spike of 2026-11-15 shows that Roslyn runs under Native AOT, so C# is a built-in of the Native AOT `morphir` and not a download. The approved [C# language support PRD](../../../docs/content/contributing/design/prds/csharp-language-support.md) records decisions D1 to D16. This change implements steps 1 to 7 of that PRD in one pull request.

## What Changes

- **Package.** Add `Morphir.Lang.CSharp`: an F# main assembly (frontend lowering, target-side passes, backend), a C# `netstandard2.0` analyzer assembly, a C# code fix assembly that only the IDE loads, and a small attribute assembly. The package carries the analyzers, the MSBuild target and the source generator.
- **Frontend (step 1).** Add a C# frontend on Roslyn: parse, bind, admit a subset of C# with one `DiagnosticAnalyzer` rule for each refusal, and lower symbols and operations to the current IR. It is a built-in of both hosts and handles `.cs` files in `morphir compile`.
- **IR upgrade (step 2).** Add the `IIrUpgrade` contract and the classic to current IR step. `morphir generate` upgrades a classic model when the selected backend claims the current IR only. An upgrade step emits an application list that holds the semantic node address before and after for each node that it moves.
- **Middle end (step 3).** Add the `IModelTransform` registry contract beside `IModelAnalyzer`, the rewrite rule catalog and the engine that records each application, with the typed node address of upstream. The checks of the C# backend become the first target-side pass. `morphir generate` and `morphir compile` run the passes.
- **Backend on Roslyn (step 4).** Replace the text backend with a `SyntaxFactory` backend. The output has C# 15 plain records, the module shapes, `closed` and `union` types, and one polyfill file for older target frameworks. `LetRecursion` and the list functions of the transform table are written. `Morphir.CodeGeneration.CSharp` goes away. **BREAKING:** the output is no longer valid C# 14, and `language-version` 15 is the default and the only value (14 fails with `unsupported-language`). The `abstract class` case shape is gone. A module with values is a static class named after the module, with no `Module` suffix. Generated records no longer carry `IMorphirValue` members unless an option asks for them. The package `Morphir.CodeGeneration.CSharp` is removed; its API moves to `CSharpBackend` in `Morphir.Lang.CSharp.Emit`, and the package `Morphir.Lang.CSharp` replaces it. `Morphir.Tooling` depends on `Morphir.Lang.CSharp` and `Morphir.MiddleEnd`, so both are published with this change; step 7 adds the analyzers folder, the MSBuild target and the generator to the package.
- **Loop lowering and matching transforms (step 5).** Lower `foreach`, `for` and `while` loops to collection traversals or recursion, and write the matching loop form or functional form in the backend by profile. Add the option record with its two carriers: C# attributes and `morphir.toml` keys.
- **Sum type diagnostics and rewriters (step 6).** Report an older encoding of a sum type, and give a code fix that changes it to the preferred `closed record` form. Accept a C# `union` that converts to a Morphir custom type.
- **Generation in a user build (step 7).** Add an MSBuild target and a Roslyn source generator, on one F# core that builds for `netstandard2.0`. Both are in scope.
- **Size and gate.** The Native AOT `morphir` grows by about 37 MB (measured: 47.9 MB to 85.1 MB on `osx-arm64`, with the embedded reference assemblies). The publish gate and the size limit of `.agents/aot-optimization.md` change to match; the PRD has the numbers.

This change does not include:

- decorators, the IR document tree, or incremental compile; the design keeps constraints for them (D11) and builds none of them;
- the node address resolver, guards and sidecars; step 2 adds only the typed address and the walk;
- a port of `Optimizer.fs` or `TypeValidator` to the current IR;
- loop lowering for the F# frontend;
- the upstream node address changes (`finos/morphir` #1178 to #1180); the design works without them.

## Capabilities

### New Capabilities

- `csharp-source-frontend`: Turn C# source documents into a Morphir library model for a defined subset of C# 15, with a stable diagnostic for each refused construct, deterministic names and the module shape rules.
- `csharp-editor-diagnostics`: Report the refusals of the frontend as Roslyn analyzer diagnostics in the compiler, the IDE and `morphir compile`, from one rule catalog, and report older sum type encodings with a code fix.
- `csharp-loop-lowering`: Lower C# loops to list functions or recursion, refuse loops that cannot lower, and write the matching loop form or functional form by profile.
- `csharp-handling-options`: Select handling options (equality, loop lowering, traversal form, model project) with C# attributes or with `morphir.toml` keys, and apply one precedence order.
- `csharp-build-generation`: Generate C# from a model in a user build, with an MSBuild target and a Roslyn source generator that call one pipeline function.
- `ir-upgrade`: Bring a model of an older IR format to the current IR through ordered upgrade steps, each with an application list of moved nodes.
- `model-transforms`: Run transform passes over the current IR at two positions (after a frontend, before a backend), with compiled rewrite rules and a record of each application.

### Modified Capabilities

- `morphir-csharp-transpilation`: The backend uses Roslyn syntax trees. The language profile is C# 15 only. Local recursion is written. Generated records and unions use plain C# equality unless the IEEE option is on. Custom types take the `closed` form or the `union` form. Older target frameworks get one polyfill file.
- `csharp-sdk-runtime`: The equality and hashing of the SDK accept plain records and plain unions. A field with a function type and `null` are refused at compile time by the frontend.
- `cli-csharp-generation`: The backend options include the profile and the handling options. `morphir generate` upgrades a classic model and runs the passes before the backend.
- `cli-model-compilation`: Both hosts register the in-process C# frontend, and the AOT `morphir` contains it. `morphir compile` runs the passes after the frontend.
- `extension-registry`: Both hosts register the C# frontend, the C# backend and the `transform` entries of the passes as built-in extensions.

## Tracking

- [PRD: C# language support](../../../docs/content/contributing/design/prds/csharp-language-support.md) (approved 2026-11-15). The Feature Status Tracking table of the PRD records progress.
- Upstream: `finos/morphir` issues [#1178](https://github.com/finos/morphir/issues/1178), [#1179](https://github.com/finos/morphir/issues/1179) and [#1180](https://github.com/finos/morphir/issues/1180) propose node address changes. They are not blockers.
- A morphir-dotnet issue for this change does not exist yet. Link it here before the pull request opens.
- The PRD planned one OpenSpec change for each step. The product owner decided on 2026-11-15 to implement steps 1 to 7 in one change and one pull request. `tasks.md` keeps the step order, and each task group can land as its own commit.

## Impact

- New projects: `src/Morphir.Lang.CSharp` (F#), `src/Morphir.Lang.CSharp.Analyzers` (C#, `netstandard2.0`), `src/Morphir.Lang.CSharp.CodeFixes` (C#), `src/Morphir.Lang.CSharp.Attributes` (C#, `netstandard2.0`), `src/Morphir.Lang.CSharp.Generator` (C#, `netstandard2.0`), the MSBuild props and targets, and a test project for each assembly that has behavior.
- New or changed contracts: `IModelTransform`, `IIrUpgrade`, the rewrite rule record and the node address type, in a library that also builds for `netstandard2.0`.
- Removed: `src/Morphir.CodeGeneration.CSharp` and its text emission, after the new backend passes its tests.
- `src/Morphir`: links Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.12.0, no Workspaces). The executable grows by about 37 MB (47.9 MB to 85.1 MB on `osx-arm64`; the spike without the embedded references measured 76.0 MB). The publish gate gains one accepted `IL2104`.
- `src/Morphir.SDK.CSharp`: equality and collections accept plain records and unions.
- CLI: `morphir generate` and `morphir compile` run the middle end; `morphir compile` selects the C# frontend for `.cs` files.
- Build: .NET SDK 11 is required to build a C# model project. The generator compiles against the oldest Roslyn that it supports.
- Docs: a C# language support guide, the backend options reference, and updates to the PRD status table.
- Tests: Gherkin corpora with the closed step vocabulary of the Morphir Compatibility Kit, round-trip tests for each row of the transform table, Native AOT against JIT equality tests, and the closure and size checks.
