# Tasks

Tracking: [Frontend epic #499](https://github.com/finos/morphir-dotnet/issues/499). This change starts after `add-fsharp-compile` task 3.3 (rule catalog). Group 2 also needs `morphir compile` (`add-fsharp-compile` tasks 4.x). Follow TDD: write each failing test or BDD scenario first. Mark a BDD scenario `@ignore @pending:<task>` until its task is done.

## 1. Shared rule catalog (CAT-1)

- [ ] 1.1 Write a test in `Morphir.Frontend.FSharp.Tests` that compiles every catalog example through the frontend and checks its code, its single finding and its flagged text. Verify that it passes on the current catalog before any move.
- [ ] 1.2 Move the input shape, rules, detectors, findings and nested-refusal reduction into `AdmissionCatalog.fs`, with string codes and no `Morphir.Compilation` or `CompilerEdge` dependency. Keep diagnostic conversion and ordering in `Admission.fs`. Verify that all frontend tests from 1.1 and from `add-fsharp-compile` pass unchanged.
- [ ] 1.3 Add a build check that `AdmissionCatalog.fs` and `TextLines.fs` open only FCS and FSharp.Core namespaces. Verify that the check fails when a test adds an `open Morphir.Compilation` to a copy of the file.

## 2. Check-only compile and MSBuild diagnostic format (BLD-1, BLD-2)

- [ ] 2.1 Add BDD scenarios to `tests/Morphir.Tooling.Tests/Features/Compile/` for: check succeeds, check fails on a refusal, check with output, refusal line in MSBuild format, and JSON unchanged. Verify that they fail (red) before 2.2.
- [ ] 2.2 Add `--check` to `morphir compile`: run every stage, skip IR writing, installation, task records and generation, and refuse `-o`, `--target` and `--generate-output` with exit code 2. Verify the check scenarios from 2.1, and a test that the in-process frontend receives the same request as a normal compile.
- [ ] 2.3 Add a MEP host test: `--check` with a fake MEP guest sends a standard `compileModel` request and writes nothing. Verify the test, and that no new protocol member exists in `Morphir.Mep`.
- [ ] 2.4 Add `--diagnostic-format msbuild` for human-mode stderr lines, with one-based lines and columns from the zero-based UTF-16 ranges. Add a property test that converts generated ranges and checks that the values are one-based. Verify the MSBuild scenarios from 2.1, and that stdout is empty in human mode.
- [ ] 2.5 Document `--check` and `--diagnostic-format` in the `morphir compile` CLI reference. Verify that the documented commands run as written in a temporary project.

## 3. Analyzer package (ANZ-1, ANZ-2, ANZ-3)

- [ ] 3.1 Create `src/Morphir.FSharp.Analyzers` with FSharp.Analyzers.SDK 0.41.0 and `VersionOverride` for FSharp.Compiler.Service 43.12.402 and FSharp.Core 10.1.402. Link `TextLines.fs` and `AdmissionCatalog.fs` as `Compile` items. Verify that the project builds with no NU1608 warning and no suppression, and that no other project references it.
- [ ] 3.2 Create `tests/Morphir.FSharp.Analyzers.Tests` with FSharp.Analyzers.SDK.Testing 0.41.0. Write tests first for: catalog parity on every example, several refusals in one document, an ordinary F# project with no `morphir.toml`, and a document with a type error. Verify that they fail before 3.3.
- [ ] 3.3 Implement the analyzer function with `[<CliAnalyzer>]` and `[<EditorAnalyzer>]`: build the input shape from the context, skip documents with F# errors, find `morphir.toml` with a cached upward search, and report each finding as an error with the catalog code and message. Verify the tests from 3.2.
- [ ] 3.4 Add a parity check that reads the expected codes and ranges for each catalog example from one shared data file, used by both `Morphir.Frontend.FSharp.Tests` and `Morphir.FSharp.Analyzers.Tests`. Verify that both test projects pass, and that changing one expected range fails both.
- [ ] 3.5 Pack the analyzer with its assembly in `analyzers/dotnet/fs/` and no FCS or FSharp.Core assembly. Verify the package layout with `.claude/skills/qa-tester/validate-packages.fsx` or an equivalent test.
- [ ] 3.6 Add a CI step that runs the `fsharp-analyzers` tool 0.41.0 with the packed analyzer on a fixture project with `let mutable total = 0`, and writes SARIF. Verify that the SARIF has one `fsharp.unsupported_mutable` result in `Pricing.fs`, and that the step fails.

## 4. MSBuild build package (BLD-3)

- [ ] 4.1 Add BDD scenarios for the build package: refusal in the build, check is off, no change since the last check, and tool not installed. Use a fixture F# project with a local tool manifest, run through `ChildProcess.RunAsync` in a `TestSandbox`. Verify that they fail before 4.2.
- [ ] 4.2 Create `src/Morphir.FSharp.Build` with `build/` and `buildTransitive/` props and targets: the `MorphirCheck` target after `CoreCompile`, the tool probe with `build.tool_unavailable`, the response file of `Compile` items, `Exec` in the project directory, and the stamp file. Verify the scenarios from 4.1 on Linux, macOS and Windows in CI.
- [ ] 4.3 Verify that canonical lines become MSBuild errors at the right file, line and column: assert the binary log (`-bl`) of the fixture build holds the error with code `fsharp.unsupported_mutable`, file `Pricing.fs`, line 3 and column 9.

## 5. CI, packaging and documentation

- [ ] 5.1 Add the CI job that builds `Morphir.FSharp.Analyzers` and `Morphir.Frontend.FSharp` in one run (ANZ-1). Verify that a deliberate FCS API break in a branch fails the job.
- [ ] 5.2 Add a Renovate group for FSharp.Analyzers.SDK and FSharp.Analyzers.SDK.Testing, separate from `fsharp-compiler`. Verify with the Renovate config validator.
- [ ] 5.3 Verify the open questions of design.html: whether Rider and Visual Studio load FSharp.Analyzers.SDK analyzers, and how FsAutoComplete finds analyzer packages. Record the answers in design.html.
- [ ] 5.4 Write an "F# editor and build diagnostics" guide under `docs/content/docs/guides/`: Ionide settings, the CI SARIF step, the `MorphirCheck` property, the local tool manifest, and the IDE support from 5.3. Use Simplified Technical English. Verify with the documentation link checker and by running each documented command.
- [ ] 5.5 Run the AOT smoke for `morphir` and the assembly-closure check (AOT-1). Verify that FSharp.Compiler.Service and both new packages are not in the closure.

## Workflow follow-up

- Archive the change after review approval and merge, as AGENTS.md and `openspec/config.yaml` require.
- Verify the archived specs `fsharp-editor-diagnostics` and `fsharp-build-diagnostics`.
