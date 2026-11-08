# Proposal

## Why

The F# frontend refuses every construct outside the Morphir subset, but a user sees these refusals only when they run `morphir compile`. The F# compiler and FSharp.Compiler.Service (FCS) have no analyzer or plugin hook, and Roslyn analyzers do not run on F#. So an editor or a normal `dotnet build` cannot show that `let mutable` or a class is not allowed in a Morphir model. This change shows the same refusals while the user types and when the project builds, from the one rule catalog that the frontend already uses.

## What Changes

- Make the admission rule catalog of `add-fsharp-compile` (task 3.3, FSF-3) a shared source. It has no dependency on `Morphir.Compilation` or on one exact FCS version beyond the FCS syntax and symbol APIs. The frontend, the analyzer, the build target and the generated coverage page all read it. Each refusal has the same `fsharp.unsupported_*` code, message and range everywhere.
- Add the `Morphir.FSharp.Analyzers` package: an FSharp.Analyzers.SDK analyzer that runs the shared catalog on each F# document.
  - Editors that use FsAutoComplete (Ionide for VS Code, and other LSP editors) show the refusals while the user types.
  - The `fsharp-analyzers` dotnet tool runs the analyzer in CI and writes SARIF.
  - The analyzer reports only for projects that are under a `morphir.toml`.
- Add the `Morphir.FSharp.Build` package. Its opt-in MSBuild target checks the project's ordered `Compile` items after `CoreCompile`. Refusals appear in the MSBuild canonical error format, so Visual Studio, Rider and the `dotnet build` CLI list them with file, line and column.
- Add a check-only mode and an MSBuild diagnostic format to `morphir compile` (`--check`, `--diagnostic-format msbuild`). The check runs the same frontend stages and writes no IR, output directory or task record. It adds no MEP member: the host sends the same `compileModel` request and only skips publication.

This change does not include:

- a compiler plugin or a fork of `fsc`;
- a Morphir language server (a later, MEP-based change);
- `[<CompilerMessage(..., IsError = true)>]` on Morphir SDK facade APIs. That works only for APIs that Morphir owns and cannot refuse language constructs. It stays a later option;
- quick fixes or code actions;
- new refusal rules. The rules stay owned by `add-fsharp-compile`.

## Capabilities

### New Capabilities

- `fsharp-editor-diagnostics`: Report the Morphir refusals of F# documents as analyzer diagnostics in editors and in CI, from the shared rule catalog, with the same codes, messages and ranges as the frontend.
- `fsharp-build-diagnostics`: Report the Morphir refusals of an F# project during an MSBuild build in the canonical error format, through a check-only compile that publishes nothing.

### Modified Capabilities

None. The capabilities of `add-fsharp-compile` are not yet archived. This change depends on them and adds no requirement to them.

## Tracking

- [Frontend epic #499](https://github.com/finos/morphir-dotnet/issues/499) tracks the F# frontend. Link this change to it, or to a child issue under it, before implementation.
- Depends on `add-fsharp-compile` task 3.3 (rule catalog) and on `morphir compile` (tasks 4.x). `tasks.md` records progress.

## Impact

- New projects: `src/Morphir.FSharp.Analyzers` (FSharp.Analyzers.SDK 0.41.0, which pins FSharp.Compiler.Service `[43.12.402]` and FSharp.Core `[10.1.402]`) and `src/Morphir.FSharp.Build` (MSBuild props and targets only). New test projects for each.
- `src/Morphir.Frontend.FSharp`: the catalog moves to shared source files, with no change to its behavior.
- `morphir compile`: new `--check` and `--diagnostic-format msbuild` options.
- Build: a CI job builds the analyzer against the SDK's FCS pin and the frontend against the repository pin (43.12.403/10.1.403), to catch FCS API drift. NU1608 is not suppressed.
- Packages: two new NuGet packages. Both are JIT-only. The AOT `morphir` executable gains no FCS dependency.
- Docs: an F# editor and build diagnostics guide.
