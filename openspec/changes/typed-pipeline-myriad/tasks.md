# Tasks

Follow [the design](design.html) and the [processing](specs/pipeline-processing/spec.md), [property bag](specs/typed-property-bags/spec.md), [content](specs/vfile-content-sources/spec.md), [publication](specs/safe-tree-publication/spec.md) and [Myriad](specs/myriad-pipeline-generation/spec.md) specs. Within every behaviour task, run a failing test first, implement the minimum change, then refactor with green tests. Run a test project with its configured runner: plain `dotnet test` from the root fails for TUnit projects on the .NET 10 SDK. The repository builds and affected tests pass at the end of each group. Track work against [#376](https://github.com/finos/morphir-dotnet/issues/376) and [#375](https://github.com/finos/morphir-dotnet/issues/375). Do not invent issue numbers.

## 1. Typed property bags

- [x] 1.1 Add `Property<'T>`, `PropertyValue` and `PropertyBag` with the BAG-1 function API, custom equality and name-plus-type identity. Verify with Expecto tests for typed read and write, missing values, same-name different-type isolation, separately created properties, last-value-wins, `|>`, `<|` and `>>` usage and bag equality.
- [x] 1.2 Replace `VFile.Data`, `VFileTree.Metadata`, `TreeConfig.Extensions` and `MorphirProcessor.Data` with the bag, add `set`, `tryGet` and `remove` on each owner taking the owner last, and move `__stay_mutable__` to a real field. Verify `Morphir.IR.Pipeline.Tests` and `Morphir.IR.Pipeline.Plugins.Tests` pass and no `obj` remains on these data APIs by inspecting the public signatures.
- [x] 1.3 Document property bags in the pipeline README, including the naming convention and the mixed `|>` and `<|` precedence trap. Verify by running the documented examples in an F# script.

## 2. File content sources

- [x] 2.1 Add `VFileContent`, `StreamSource`, `ContentError` and reading functions per SRC-1, replacing `Content: obj option`. Verify tests for each content kind, repeat opening, absent content, read failure with the original exception, and byte order mark handling.
- [x] 2.2 Add the inline `VFile.create` content resolution, non-inline `ofText`, `ofBytes`, `ofStream` and `fromDisk`, and migrate pipeline and plugin tests. Verify with a negative compile check for an unsupported content type (an F# script run through `dotnet fsi` that must fail to compile) and that the non-inline functions are callable from a small C# test or the task project.
- [x] 2.3 Change `loadFromDisk` to record stream sources without opening files, and change `toStringMap` to return `Result` with `ContentError`. Verify a test that a counting stream source is never opened during a pipeline pass, that an unreadable file surfaces only on read, and exact byte and text round trips.
- [x] 2.4 Update the README's loading section, which currently says binary files are not preserved. Verify by running its examples in a script.

## 3. Typed processing and diagnostics

- [x] 3.1 Add `Path` and `Location` to `VMessage`, `Messages` to `VFileTree`, `VMessage.describe`, `VFile.report`, and `ParseFailure` with its rule, location and path on `ParseError`. Include tree-level messages in `hasErrors`, `collectMessages` and `statistics`. Verify tests for each query, the description format and `VFile.message` keeping its signature.
- [x] 3.2 Make `Parser`, `Plugin`, `Compiler` and `MorphirProcessor` generic, narrow `Configure` to the data bag, rename `TreeContent` to `TreeNode`, add `Parser.forFile`, `Parser.ofParseable` and `Plugin.focus`, and update the builders so the model type is inferred. Verify typed tests, including `Plugin.focus` running `Configure`, and a negative compile check for mismatched stages. Verify that no public signature in `Morphir.IR.Pipeline` exposes `obj` by reviewing the generated XML docs or a signature listing.
- [x] 3.3 Implement `run`, `processFile` and `processPath` with TYPE-2 and TYPE-4 behaviour. Verify tests for one-to-many output, compiler merge order, parser fallback warnings, promotion of malformed input to an error keeping its rule, the generic error when all parsers report unsupported input, plugins continuing after an error, `None` and fatal stopping them, compilers skipped on error, and messages and file data moving to the output tree. Migrate the processor and builder tests, keeping the "accumulate errors while continuing" behaviour, and convert assertions that only proved boxing into typed ones.
- [x] 3.4 Implement `runEachFile`, and a directory-accepting parser fixture for `run`. Verify tests for location preservation, one failing file among three, per-file data staying on its own output files, and many-to-one directory input.
- [x] 3.5 Update `docs/content/docs/pipeline/plugin-development-guide.md` and the README's parser section to the typed API. Verify that their examples compile in a script and that no `obj` plugin example remains.

## 4. Safe publication

- [x] 4.1 Port `OwnedPath` and collision behaviour into `validatePublish` with the PUB-1 `TreeWriteProblem` cases. Verify tests for rooted and `..` locations, normalization of `.` and empty segments, resolved-path escape, a symlinked root and subdirectory (skipped only where links cannot be created), case-only duplicates, pathless files and all problems reported together.
- [x] 4.2 Implement `publish` with staging, the temp-directory lock, streamed identical-file comparison, `PublishResult` and refusal of trees with errors. Verify tests for a stream failure during staging leaving output unchanged and no staging directory, timestamps unchanged on repeat publication, concurrent publication producing one complete set, no lock or staging file left in the output, and an output root that is a file.
- [x] 4.3 Add manifest handling, `declaredOrder` and `readManifest`. Verify tests for declared order across directories, obsolete removal, unowned files left alone, a tampered manifest entry, and `DeclaredOrderMismatch`.
- [x] 4.4 Reimplement `writeToDisk` on `publish`, migrate its tests and update the README's disk section. Verify the earlier traversal, absolute root, duplicate and exact round-trip tests still pass, and that the README examples run.

## 5. Real plugins

- [x] 5.1 Generalize `Optimizer` attributes and traversal, match SDK functions by fully qualified name, apply the PLG-1 folding limits, and add `Optimizer.Options` with `foldingOnly`. Verify tests for a user `add` left alone, `int32` overflow left alone, division by zero, mixed literal types, unmapped `and` and `or` left alone, traversal into every value form, folded nodes keeping attributes, and the other rewrites running only when enabled.
- [x] 5.2 Generalize `TypeValidator`, build its environment from inputs and bindings, compare only SDK primitive types using the backend's fully qualified names, and treat unknowns silently. Verify tests for inputs in scope, pattern and let bindings, a string literal against `Int` as an error with a location, a literal under a model alias producing no error, and silence on unsupported forms.
- [x] 5.3 Replace the three plugin stubs with `Plugin<PackageDefinition<'ta, 'va>>` implementations, with `PrettyPrinter` writing to a typed property. Verify a test that each plugin changes or reports on a real package in a typed processor, and remove assertions on the old stub messages.
- [x] 5.4 Update the plugin guide with the real plugins and `Plugin.focus`. Verify that its examples compile in a script.

## 6. Myriad through the pipeline

- [x] 6.1 Reference the pipeline projects from `Morphir.CodeGeneration.FSharp`, then pack and run `tests/Morphir.Myriad.Tests/package-tests.fsx` unchanged. Verify that the cold-cache package consumer still passes with the new assemblies present, before any behaviour changes.
- [x] 6.2 Add `Transpiler.generate`, the `manifest` property and the model parser using a cloned `JsonElement`, compiling with today's emission and no plugins yet. Add a fixture helper in `Fixtures.fs`, migrate `TranspilerTests.fs`, and remove `GeneratedModule`, `Diagnostic` and the old `Generate`. Verify that all backend tests and Verify snapshots pass with no snapshot change, including stream versus text input and rule ids on every rejection path.
- [x] 6.3 Move `Validate.scopes` to a plugin, then add `TypeValidator` and verify the v1, v2 and v3 fixtures produce no warnings or errors and that definite-mismatch and unbound-variable models fail with the expected rules.
- [x] 6.4 Add `Optimizer` to the pipeline in folding-only mode with an internal off switch. Verify an execution test compiling car rental and a dedicated folding fixture with optimization on and off and comparing results, that `true && x` and an unsupported reference in a dead branch stay rejected, and review and accept any Verify snapshot change.
- [x] 6.5 Rewrite `MorphirFSharpGenerator` on `Transpiler.generate` and the manifest. Verify the selector failure tests, direct-host invocation and stderr diagnostics in `Morphir.Myriad.Tests`.
- [x] 6.6 Rewrite `GenerateMorphirModules.cs` per MYR-2, removing `OwnedPath`, its collision and comparison code, and its manifest code. Verify `Morphir.Myriad.Integration.Tests`, including invalid model after success, module add and remove, host failure leaving output unchanged, colliding paths failing before the host starts, repeat builds and multiple models.
- [x] 6.7 Rerun `package-tests.fsx` and the documented car rental workflow from a clean copy, including the rate change exercise. Verify the lifecycle results match the docs, and record build time and peak memory for car rental and a generated large fixture, before and after.
- [x] 6.8 Update `docs/design/PRD-fsharp-backend.md`, `docs/design/unified-file-architecture.md`, `src/Morphir.Myriad/README.md` and `docs/content/docs/guides/myriad-fsharp.md` to describe the real pipeline and any changed generated source. Verify links and that the guide's commands run as written.

## 7. Integration

- [x] 7.1 Run every affected test project with its configured runner, the `Precommit` target, `mise run openspec:verify` and `dotnet fsi scripts/validate-changelog.fsx`. Add the `CHANGELOG.md` entries before this step. Verify all pass, and report by name any failure unrelated to this change.
- [x] 7.2 Search the pipeline, plugin and backend public APIs for `obj`, `box` and `unbox`. Verify that the only hits are private to `PropertyBag` or in unrelated code, and note each exception in the PR description.
- [x] 7.3 Run a trim and AOT publish of a representative generated consumer as the earlier change did. Verify that it still succeeds, and confirm that no new warnings come from generated code.

## Verification evidence

Recorded 2026-11-01 on Windows arm64 with SDK 10.0.403. No commit, push, merge or archive was performed.

- **Tests:** `Morphir.IR.Pipeline.Tests` 299, `Morphir.IR.Pipeline.Plugins.Tests` 125, `Morphir.CodeGeneration.FSharp.Tests` 80, `Morphir.Myriad.Tests` 4, `Morphir.Myriad.Integration.Tests` 12 and 15 cold-package checks all pass. Verify snapshots did not change.
- **Unrelated failures:** the Nuke `Test` target reports 2 failures in `Morphir.Tooling.Tests`: `DiscoverWorkspaceRoot_ShouldReturnNull_WhenNoWorkspaceFound` and `ResolveConfigAsync_ShouldReturnDefaults_WhenNoWorkspace`. They expect no workspace marker above the temp directory and find one at the user's home directory. That project references nothing changed here.
- **Compile-time checks:** scripts confirm that an unsupported content type, a wrong property value type and mismatched stages, through both functions and the computation expression, each fail to compile, with a compiling control.
- **`obj` scan:** the only `obj`, `box` and `unbox` in the pipeline, plugin, backend and Myriad sources are private to `PropertyBag`, plus the required `Equals(obj)` override.
- **Documented examples:** the README and plugin guide examples ran in a script and printed the documented results.
- **C# access:** `GenerateMorphirModules.cs` compiles against `fromDisk`, `ofDiskFile`, `publish`, `validatePublish`, `readManifest` and the typed errors.
- **Trim and AOT:** the car rental consumer publishes with `PublishTrimmed` and with `PublishAot`, and both print the documented lifecycle. The only analysis warnings are `IL2104` and `IL3053` for `FSharp.Core`. A very long package path made the native linker fail with `LNK1181`, on this machine only.
- **Test-first exceptions:** groups 2 and 3 landed as one step, because `VFile.Content` cannot change until the processor stops storing the model in it. In tasks 6.3 and 6.4, the plugin stages were added to the pipeline together with their tests. The plugins' own tests failed first, but the pipeline-level tests did not get a separate failing run.

Measurements compare commit `1048f93` with this change. The large model has 40 modules with 25 values each, or 1,000 values in 0.2 MB. Each generation run confirmed that it produced every file.

| Measure | Car rental before | Car rental after | Large before | Large after |
| --- | --- | --- | --- | --- |
| Generation, best of 5 | 13 ms | 15 ms | 109 ms | 124 ms |
| Allocated per generation | 9.8 MB | 10.2 MB | 77.2 MB | 83.9 MB |
| Peak working set, process | 83 MB | 85 MB | 81 MB | 84 MB |
| Consumer build, best of 2 | 7.6 s | 8.0 s | 29.6 s | 31.6 s |

The cost is within the design's accepted risk. Host runs dominate build time, and the N+1 decode remains deferred.

## Workflow follow-up

- Land the #376 commit and this change together as one PR, whose description notes where the result differs from #376's text.
- Reconcile #375 and the signature notes on #376 when this lands.
- Follow `AGENTS.md`, `CONTRIBUTING.md` and repository approval rules for review, merge and archive.
