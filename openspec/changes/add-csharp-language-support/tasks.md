# Tasks

Tracking: [PRD: C# language support](../../../docs/content/contributing/design/prds/csharp-language-support.md) (approved 2026-11-15), whose Feature Status Tracking table records progress. Link a morphir-dotnet issue here before the pull request opens; none exists yet. Follow TDD: write each failing test or BDD scenario first, and mark a BDD scenario `@ignore @pending:<task>` until its task is done. Use the `test-manager` skill before adding a test project, a feature file or a driver. Follow the `fsharp-csharp-interop` skill for F# code that C# calls. The groups keep the PRD step order, and each group lands as its own commit with its own tests and docs.

## 1. Projects and dependencies (PKG-1)

- [x] 1.1 Add `src/Morphir.Lang.CSharp` (F#), `src/Morphir.Lang.CSharp.Analyzers` and `src/Morphir.Lang.CSharp.Attributes` (C#, `netstandard2.0`) to `Morphir.slnx`, with central package versions for `Microsoft.CodeAnalysis.CSharp` 5.12.0 (no Workspaces). Verify that `dotnet build Morphir.slnx` succeeds and that a restore with the root `NuGet.config` succeeds for each new project.
- [x] 1.2 Add `tests/Morphir.Lang.CSharp.Tests` and `tests/Morphir.Lang.CSharp.Analyzers.Tests`, each with a first test that stays (the package identity test and the analyzer closure test). Verify that `dotnet test` runs both projects.
- [x] 1.3 Move the polyfill members that `netstandard2.0` needs (`MaybeNullWhen`, `NotNullWhen`, `ArrayBufferWriter`, `Char.IsAsciiDigit`, `Double.IsFinite`, `String.Split(string)`, `String.Contains(char)`, `Dictionary.TryAdd`) into one file that `Morphir.Models` compiles for `netstandard2.0` only, and add `netstandard2.0` to the target frameworks of `Morphir.Models`. Verify that the repository tests of `Morphir.Models` pass unchanged and that `dotnet build -f netstandard2.0` succeeds. The only source change in other files is a `#if NETSTANDARD2_0` `open Morphir.Compat` in eight files.

## 2. Frontend thin slice and admission analyzers (FRT-1, ADM-1)

- [x] 2.1 Write failing tests in `Morphir.Lang.CSharp.Tests` for the frontend contract: one document to one module with a record; two documents; `csharp.compiler` error with `CS0029` and no model; a nullable warning with a model; determinism over two runs. Verify that they fail before 2.2.
- [x] 2.2 Implement the frontend skeleton: fixed `CSharpCompilation` options (language version 15, nullable, implicit usings), the embedded fixed reference set, document handling, compiler diagnostics in the MEP diagnostic shape, and no `Emit` or `Assembly.Load`. Verify the tests from 2.1.
- [x] 2.3 Implement namespace and static-class modules, plain records, enums and `closed record` hierarchies, with a test for each of the scenarios in `csharp-source-frontend`. Verify the tests, including a nested static class and the module names of the `Acme.Trading` example.
- [x] 2.4 Implement value lowering for the supported value list (literals to `switch` expressions with exhaustive patterns), SDK mapping with an inventory check, Morphir names, inferred types and a source range on each node. Verify with unit tests and with a test that every mapped SDK entry exists in the reviewed inventory.
- [x] 2.5 Add the admission rules as `DiagnosticAnalyzer` classes in `Morphir.Lang.CSharp.Analyzers` with the `MORPHIR1001`-style ids and IRIs in the help link: loops (code only, lowering comes in group 6), mutation, `dynamic`, mutable properties, symbols outside the reference set, and the model project switch. Verify with Roslyn analyzer tests that each rule reports once, that the analyzer is silent in a project that is not a model project, and that `#pragma warning disable` and `.editorconfig` suppress by id.
- [x] 2.6 Run the analyzers in the frontend and publish the rule catalog (code, message, IRI, example). Verify that a catalog test compiles every example and checks its code and range, and that the frontend and the analyzer give the same diagnostics for the example set.
- [x] 2.7 Register the frontend as a built-in extension in both hosts and select it for `.cs` documents in `morphir compile`. Add BDD scenarios in `tests/Morphir.Tooling.Tests/Features/Compile/` for the C# compile in `dotnet-morphir` and in the AOT `morphir`, and check that the project file provider gives the default `**/*.cs` items of an SDK-style project. Verify the scenarios and that `extension list` shows the `frontend` entry with delivery `built-in` in both hosts.
- [x] 2.8 Add C# frontend conformance cases as Morphir Compatibility Kit Gherkin with the closed step vocabulary and independently written expected IR. Verify that the corpus runs against the frontend.
- [x] 2.9 Document the C# subset, the rule catalog and the module shape rules in `docs/` and verify that the documented `morphir compile` commands run as written on the example project.

## 3. IR upgrade and node addresses (UPG-1, NAD-1)

- [x] 3.1 Add the typed node address (artifact selector, format, root, typed steps, revision, optional guard), the role table and the canonical URI writer and parser in a library that targets `netstandard2.0` and the repository TFMs. Write the tests first from the `uriCases` of the upstream draft fixture that need no resolver. Verify the round trip, the `invalid_node_uri` result for an unknown role, and one spelling for one address.
- [x] 3.2 Add the walk that gives each node of a current-IR module its address. Verify with tests for `body/apply/argument`, `type-exp/record/field/<name>`, tuple elements, match cases and `let` definitions, and that the `LetDefinition` and `LetRecursion` of one name give the same address.
- [x] 3.3 Add the `IIrUpgrade` contract, the chain join with a "no path" diagnostic, and the classic to current IR step as the conversion that the repository lacks. Write the tests first: `3.0.0` to current, already current, no path, node without a target form, determinism, input unchanged, and the application list for a removed unreachable case. Verify the tests.
- [x] 3.4 Make `morphir generate` upgrade a model when the selected backend claims the current IR only, and keep the F# backend on classic models. Add tests for both in `Morphir.Tooling.Tests` (no registered backend reads the current IR only before group 5, so the tests use a recording backend; a black-box scenario with the C# backend lands with group 5). Verify that the existing F# generation tests pass unchanged and that `--target csharp` accepts a classic model.
- [x] 3.5 Document the upgrade steps and the application list in `docs/` and verify that a documented example command runs as written.

## 4. Middle-end contracts (MID-1)

- [ ] 4.1 Add `CurrentModel`, the `IModelTransform` contract with the stage claim, and the `transform` extension type with its registry entries and `extension list` output. Write the registry tests first. Verify that a transform entry is a morphir-dotnet report member that is not mapped into MEP messages.
- [ ] 4.2 Add the rewrite rule record, the rule catalog check (an example is necessary) and the engine with the bottom-up, top-down and fixed-point strategies and the round limit. Write failing tests for: example before and after, rule without an example, non-terminating rule set, replacement keeps its attributes, a second pass keeps the first origin. Verify the tests.
- [ ] 4.3 Add the application log with the address before (pinned to the pipeline input) and after, the module-at-a-time walk and the recorded package view lookups, and report them with `--json`. Verify with a test that a rule that reads another module lists the lookup.
- [ ] 4.4 Add the host check that each feature that a backend cannot take is removed by an earlier pass. Verify with a test that a remaining feature fails before the backend and names the feature.
- [ ] 4.5 Run the passes in `morphir generate` (stage before a backend) and in `morphir compile` (stage after a frontend); skip them with one `info` diagnostic for an older family. Add BDD scenarios. Verify that a pass error stops before publication.
- [ ] 4.6 Move the checks of the old C# backend (`ValueLowering.check` and the collision checks) into the first target-side pass for `csharp`. Verify that the old diagnostics are reported with the same rules, now from the pass.
- [ ] 4.7 Document the pass contract, the rule catalog format and the application list in `docs/`. Verify that the documented example rule runs through the test harness as written.

## 5. Backend on Roslyn (BKD-1, SUM-1, EQL-1)

- [ ] 5.1 Write failing golden tests for the new backend: modules (types only at namespace level, values as static classes, nested static class), plain records, closed records, wrapper unions, mixed custom types, the polyfill file and the layout of a switch expression. Verify that they fail before 5.2.
- [ ] 5.2 Implement `plan`, `syntax` and `layout` with `SyntaxFactory` and no Workspaces assembly, with deterministic names and paths. Verify the golden tests and a test that two runs give the same bytes.
- [ ] 5.3 Implement `LetRecursion` (a `while` loop for a tail call, a local function otherwise) and the list functions of the transform table in the plan. Verify that the old `local-recursion` refusal is gone and that a recursive example executes with the model result.
- [ ] 5.4 Implement the IEEE equality option on records and unions, and plain equality as the default. Write the tests first for the cases in `morphir-csharp-transpilation`. Verify that a `NaN` record is equal by default and not equal with the option.
- [ ] 5.5 Make `Morphir.SDK.CSharp` equality, hashing and collections accept plain records and plain unions, and fail explicitly for a function-typed field. Decide the `NaN` in collections case and record it in `design.html`. Verify with tests for the scenarios in `csharp-sdk-runtime` and with the existing SDK conformance cases.
- [ ] 5.6 Switch the `csharp` target to the new backend, keep the options (language version 15 only, namespace, profile), and run the old conformance and `cli-csharp-generation` cases. Replace the cases that test C# 14 output or the `abstract class` shape. Verify that all remaining cases pass and that `language-version=14` returns code 2 with a diagnostic.
- [ ] 5.7 Remove `src/Morphir.CodeGeneration.CSharp`, its test project and its references. Verify that the solution builds and that no reference remains with `git grep`.
- [ ] 5.8 Add the round-trip test from IR to C# to IR for models of the backend, with source locations left out, and the Native AOT against JIT output test for the backend. Verify both.
- [ ] 5.9 Document the output shape, the polyfill file and the breaking change in `docs/` and in `CHANGELOG.md`. Verify that the documented example generates as written.

## 6. Loop lowering, matching transforms and options (LOP-1, OPT-1)

- [ ] 6.1 Decide the metadata carrier for a backend option on a definition with a test of the round trip, record the choice in `design.html` and update the open question. Verify that the options of 6.5 read it back.
- [ ] 6.2 Write failing tests for the loop recognizer with the ten-loop probe set (map, filter, filterMap, fold with one and two locals, while, for with an exit, break, outside effects) and implement the single recognizer in `Morphir.Lang.CSharp.Analyzers`. Verify that the result equals `spikes/csharp-language-support/roslyn-loop/expected-output.txt`.
- [ ] 6.3 Implement loop lowering in the frontend with the same recognizer, the refusals with reasons, and the analyzer diagnostics. Verify that the analyzer and the frontend refuse the same loops with the same reasons.
- [ ] 6.4 Implement the backend profiles (imperative and functional) for each row of the transform table, including `FilterMap` in the SDK, and the frontend reading of `Select`, `Where`, `Aggregate` and local functions. Write the round-trip tests first for each row and each profile. Verify them, and that `Where` then `Select` reads as `List.map` over `List.filter`.
- [ ] 6.5 Implement the option record with the two carriers (attributes in `Morphir.Lang.CSharp.Attributes` and `morphir.toml` keys), the precedence order and the `partial` check for equality. Write tests first for each scenario in `csharp-handling-options`. Verify them, including a project with attributes and no `morphir.toml`.
- [ ] 6.6 Add the `[codegen.csharp]` keys to the configuration schema, to `--option` and to the reference docs. Verify that a malformed value fails before emission with code 2.
- [ ] 6.7 Add BDD scenarios for the loop and option behavior in `morphir compile` and `morphir generate`. Verify them, and document the options and the profiles in `docs/` with commands that run as written.

## 7. Sum type diagnostics, unions and rewriters (SUM-1)

- [ ] 7.1 Write failing tests and implement union lowering: construction, conversion, patterns, `is`, the refusals (type parameter case, tuple case, shared case type, `default`, `null` arm, `.Value`, `IUnion`). Verify with the inputs in `spikes/csharp-language-support/union/api/inputs/`.
- [ ] 7.2 Implement the backend `union` output for wrapper constructors and the mixed-shape rule. Verify the round trip from IR to C# to IR for a wrapper type and a mixed type.
- [ ] 7.3 Add the analyzer rule for older sum type encodings, the same refusal in the frontend, and the rule that a `closed record` or a union is not reported. Verify with analyzer tests.
- [ ] 7.4 Add `Morphir.Lang.CSharp.CodeFixes` with the code fix to the closed form, loaded by an IDE only. Verify with a Roslyn code fix test, and with a closure test that no host references the assembly or any Workspaces assembly.
- [ ] 7.5 Document the sum type forms and the fix in `docs/`. Verify that the documented example compiles.

## 8. Generation in a user build (BLD-1)

- [ ] 8.1 Add the pipeline function entry point with no file or process access and a test that the CLI, the MSBuild target and the generator give the same bytes. Verify the test.
- [ ] 8.2 Add `Morphir.Lang.CSharp.Generator` (`netstandard2.0`, oldest supported Roslyn, `FSharp.Core` beside it) with the diagnostic path for an invalid model. Verify with a generator driver test and with a consumer build in the repository.
- [ ] 8.3 Add the SDK matrix check from `spikes/csharp-language-support/source-generator/sdk-matrix.sh` as a build or CI job for SDK 10.0.100, 10.0.403 and 11.0.100. Verify that the job passes and that a generator compiled against Roslyn 5.12 fails the check, as the spike showed.
- [ ] 8.4 Add the MSBuild target (opt-in, `LangVersion` 15, generated files in the intermediate folder) and the SDK 11 check with a clear error. Verify with a test project build, and with a build under SDK 10 that reports the error.
- [ ] 8.5 Add the polyfill file emission for targets below `net11.0` and a test with a library and an application that each have their own polyfill file and an exhaustive `switch` across them. Verify with 0 warnings.
- [ ] 8.6 Write the support matrix with Visual Studio, Rider and VS Code as not tested, and document the build integration. Verify that the documented commands run as written.

## 9. Integration checks

- [ ] 9.1 Publish the Native AOT `morphir` for the host RID and verify the publish gate (one more `IL2104`, no per-site error outside `NuGet.*` and `Newtonsoft.Json`), the closure (no Workspaces or code fix assembly), and the size. Record the size and set the new limit in `.agents/aot-optimization.md` and in the check.
- [ ] 9.2 Run the frontend and backend conformance in the Native AOT `morphir` and in `dotnet-morphir`, and verify equal output.
- [ ] 9.3 Run the full test suites, `dotnet format` and the documentation build. Verify that all pass.
- [ ] 9.4 Update the Feature Status Tracking table of the PRD and the PRD index with the delivered steps and the open items: the generator in IDEs and a `net8.0` runtime.

## Workflow follow-up

- Follow AGENTS.md for review, archive, tracker closure and release approval.
- Archive the change after the review requirements are satisfied, and verify the archived result.
- Send the product owner the three upstream node address proposals' status (`finos/morphir` #1178 to #1180) with the pull request.
