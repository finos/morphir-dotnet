# Tasks

All work is on `feat/model-convert`. Milestones end with a commit that builds and passes its tests, and a "what to try" note added under the group. The maintainer decides after each milestone whether it ships as a pull request. Use the `test-manager` skill before adding any test, feature file, step or driver, and run the `test-manager-reviewer` agent on each milestone's diff. No Rust code changes.

## 1. Tracking

- [ ] 1.1 File a morphir-dotnet GitHub issue for this change (maintainer approves the text first), link it in `proposal.md` and the first pull request, and verify the link resolves.

## 2. Step zero: extract Morphir.Files (milestone 0)

- [x] 2.1 Create `src/Morphir.Files` targeting `$(MorphirLibraryTargetFrameworks);netstandard2.0`, move `Property.fs`, `File.fs` and `FileTree.fs` out of `src/Morphir.IR.Pipeline` with their tests into a new `tests/Morphir.Files.Tests`, and make `Morphir.IR.Pipeline`, `Morphir.Ion`, `Morphir.Tooling`, `Morphir.CodeGeneration.FSharp` and `Morphir.IR.Pipeline.Plugins` reference it; verify the solution builds and the moved tests and every existing pipeline test pass unchanged on `net11.0` (scenario "Existing pipeline behavior is unchanged").
- [x] 2.2 Port `Morphir.Files` to `netstandard2.0` with `Polyfill.fs` and `#if NETSTANDARD2_0` shims for the gaps the trial build found, and verify the `netstandard2.0` build has zero warnings and a `netstandard2.0` consumer builds a tree (scenario "A netstandard2.0 model library uses virtual file trees").
- [x] 2.3 Implement `IFileProvider` on `VFileTree` and a tree builder from any `IFileProvider`, and verify the Expecto contract tests for the five provider scenarios of `virtual-file-providers` pass, including embedded and physical providers.
- [x] 2.4 Reference `Morphir.Files` from `Morphir.Models` and verify the C# generator and MSBuild task still load in their hosts (the existing generator package and build tests pass).
- [x] 2.5 Document `Morphir.Files` (README in the project, a CHANGELOG line) and verify the README's example compiles as an Expecto test.

**What to try (milestone 0):** `dotnet run --project tests/Morphir.Files.Tests -- --filter "FileProviders"` runs the provider scenarios (a tree as an `IFileProvider`, and trees built from embedded and physical providers without reading a file). In F#, `VFileProviders.asFileProvider tree` gives a tree to any API that takes an `IFileProvider`, and `VFileProviders.ofFileProvider (new PhysicalFileProvider "some/dir")` loads a directory as a tree; `src/Morphir.Files/README.md` has the example. `Morphir.Models` now references `Morphir.Files`, and the `Morphir.Lang.CSharp` package carries it in its analyzer and MSBuild task folders.

## 3. Test structure foundation (milestone 1)

- [x] 3.1 Create the F# `tests/Morphir.Models.TestSupport` library with `DistributionMother` (named cases and a test-data builder) and the FsCheck generators that share the builder; verify an Expecto test builds every named case and a generated case.
- [x] 3.2 Add `FixtureCatalog` to `tests/Morphir.TestSupport`, owning every file it serves with a provenance entry and hash; pin the kit's tree cases (`document-tree.feature` and the tree cases of `distributions.feature` and `versions.feature`) from one finos/morphir commit as reference data with `PROVENANCE.md`, and translate YAML spellings to JSON with a test-only parser; verify hash checks pass and a YAML case and its JSON twin (`document-tree-0006`) load equal.
- [x] 3.3 Add the guard test that fails on any file under `tests/Fixtures` without a `FixtureCatalog` owner, and verify it fails on a planted unowned file (scenario "An unowned fixture fails a guard test").
- [x] 3.4 Update the `test-manager` skill and AGENTS.md with the object-mother and fixture-catalog pattern and the `model verify` example, and verify the skill review scenarios of `test-authoring-guidance` ("The skill gives the pattern", "A feature file names a case by intent", "A test that starts the CLI for one capability").

## 4. Document trees in the model layer (milestone 1)

- [x] 4.1 Add `DocumentAddress`, `ModuleDocumentAddress` and the v3.1 and v4 `DocumentTree` instances with `split` and `join` in `Morphir.Models`, and verify the round-trip laws (`join (split d) = d` over generated and named models at 3.1.0 and 4) and the address scenarios pass.
- [x] 4.2 Move `TreeStems.fs` from `Morphir.Ion` into `Morphir.Models` with shims for `SHA256.HashData` and `Convert.ToHexString`, point the Ion tree reader at it, and verify the existing Ion tree tests pass unchanged.
- [x] 4.3 Implement `DocumentTreeStore.write` and `read` for v4 over `VFileTree` (paths, escaping, budget, truncation with `fileNames`, bare `@` segments, sorted order, `$meta`, manifest notices), add `DocumentTreeMother` and `DocumentTreeStoreDriver`, and verify `read (write t) = t`, the address-to-path bijection law and the v4 scenarios of `model-document-trees`.
- [x] 4.4 Add the declared classic writer for `3.1.0` `Specs`, and the v3 store (3.1.0 labels, Library back to `3`, Specs stays `"3.1.0"`); verify the `ir-model-writing` scenarios and the v3 scenarios of `model-document-trees`.
  - [x] The declared classic writer for `3.1.0` `Specs` (`ClassicWireProfile.v3_1_0`), verified by the `ir-model-writing` scenarios "A classic Specs distribution at 3.1.0" and "Unsupported output version".
  - [x] The v3 store (3.1.0 labels, Library back to `3`, Specs stays `"3.1.0"`), verified by the v3 scenarios of `model-document-trees` and the kit's v3 cases 0010 to 0017 (`ClassicStoreTests`).
- [x] 4.5 Implement every tree refusal with a named diagnostic and a `DocumentTreeMother` invalid shape each, and verify the refusal scenarios ("Ambiguous profile", "Missing definition file", "Versioned dependency segment", plus unknown member, over-budget path, outside `pkg/`/`deps/`, wrong v3 manifest version).
- [x] 4.6 Add the architecture tests (only the store refers to `Morphir.Files` types; addresses hold only `PackageName`, `ModulePath` and `Name`) and the "The same tree from two sources" test, and verify they pass and fail on a planted violation.
  - [x] "The same tree from two sources" for v4: a hand-written tree (`FixtureCatalog.Trees.AcmeDemo`) read from a directory on disk and from an embedded provider gives the same model and the same diagnostics (`V4StoreTests`, `PlatformSpecific`).
  - [x] "The same tree from two sources" for v3: the kit's v3 tree `document-tree-0015` read from a directory on disk and from a file provider over memory (`ClassicStoreTests`, `PlatformSpecific`).
  - [x] The architecture tests (`ArchitectureTests`): reflection over `Morphir.Models`, its signatures and its method bodies, finds `Morphir.Files` only in the store's modules, and the cases of `DocumentAddress` and `ModuleDocumentAddress` hold only `ModulePath`, `Name`, `PackageName` and `ModuleDocumentAddress`. Each check also finds a violation planted in the test assembly.

## 5. The model command group (milestone 1)

- [ ] 5.1 Replace the `ir` group with `model` and `ir verify` with `model verify` (no alias), update `ModelVerificationDriver`, the `@needs-model-verification` description and `ModelVerification.feature` (now intent phrases from the fixture catalog), and verify the `cli-model-commands` scenarios, including "The former command is unknown".
- [ ] 5.2 Update the docs pages and CHANGELOG for the rename, and verify the documented commands run as written in the black-box docs checks.
- [ ] 5.3 Draft the companion finos/morphir change (`tools/cli-release.ts` packaged-input check, `INSTALLING.md`, `docs/getting-started/morphir-cli.md`) on a parent branch, to land with the pin move that brings the rename; verify `bun test` for `tools/` passes there with a `model verify` stub.
- [ ] 5.4 Pin the parent's `morphir-ir-v4.json` and `morphir-ir-v4-document-tree-files.json` schemas into `src/Morphir.Tooling/Infrastructure/Schemas` with provenance, make `model verify` detect and check IR v4 single files and accept `--schema-version=4`, and verify "A valid v4 model is valid" and "An invalid v4 model names its error".
- [ ] 5.5 Make `model verify` check a document tree after group 4 lands: each v4 file against its file-kind schema, the tree rules and every document through the document-tree store (v3 trees through the store alone, as the parent publishes no v3 tree-file schema), errors naming file and JSON path, the report's layout and file count, and a `--schema-version` that contradicts the manifest exiting 2; verify the tree scenarios of `cli-model-commands` in `ModelVerification.feature`, including on trees `model convert` wrote.

## 6. The convert command (milestone 1)

Depends on `adopt-streaming-json` group 2 for the `--json` convention.

- [ ] 6.1 Add `morphir model convert` in `Morphir.Tooling`: input detection, `--layout`, the nothing-to-do usage error, staged output with no partial result, refusal of a non-empty output, the report and `fileWritten` events; a compacted input is refused with `model.unsupported_context`. Verify the in-process `Features/ModelConvert/ModelConvert.feature` (events flushed before the next stage via a fake store, `fileWritten` per file, streamed diagnostics equal the report's).
- [ ] 6.2 Add the model-conversion capability: `IModelConversionDriver`, `ModelConversionDriver`, `ConversionOptions`, `ConversionReport`, the `@needs-model-conversion` tag and the `TestEligibility` case, with driver tests in `tests/Morphir.TestSupport.Tests/Drivers`; verify the `test-driver-contract` scenarios.
- [ ] 6.3 Write `ModelConversion.feature` and `ModelConversionKitCases.feature` (one scenario per pinned kit tree case, named with its kit id) in `tests/Morphir.BlackBox.Tests/Integration/`, and verify they pass on the development build and the Native AOT executable.
- [ ] 6.4 Document `model convert` (CLI page with the try-it commands) and the CHANGELOG entry, add the milestone 1 "what to try" note here, and verify the documented commands run as written.

## 7. Read compacted files (milestone 2)

- [ ] 7.1 On a finos/morphir branch, edit `ir/name-compaction` to identify the envelope by schema URL, complete parent task 1.1 (grammar, tables, `$name`, `critical`, JSON Schema under `website/static/schemas`, valid and invalid examples as plain spec artifacts), and mark which requirements the whole-file reader and writer meet versus bounded reading; verify the parent's existing checks pass with no Rust change.
- [x] 7.2 Pin the parent examples in `FixtureCatalog` from that branch's commit, add `MorphirFileMother`, and verify the hash checks.
  - The 27 files of `docs/spec/ir/context/examples/` at finos/morphir `e0a3449c` (branch `feat/model-file-context`), byte-exact in `tests/Fixtures/MorphirContext/examples/` with `PROVENANCE.md`, served as `FixtureCatalog.Context` (its `cases.json` index read as `ContextExample`s); the pin moves with 8.5.
- [x] 7.3 Implement envelope reading and expansion in the store and for single files (typed name positions only; classic arrays for v3 and canonical strings for v4; no relabel; context bytes counted toward limits), and verify the reading scenarios of `model-name-compaction`.
  - `MorphirFile.readEnvelope` and `MorphirFile.expand` read a single file or one tree file; `ContextExpansion` resolves references only at the typed name positions of the v4 and classic decoders' grammar. `DocumentTreeStore.read` reads every tree file through them and lists the compacted documents in `ReadReport.CompactedFiles`. Verified by `ExpansionTests`: each valid parent example expands to its plain twin, "A compacted v3 single file expands to v3", "A compacted tree value file expands in place" (and every file of a v3 and a v4 tree), "A literal that looks like an alias stays a literal", "Classic and v4 slots expand differently", and expansion with no alias giving every fixture model and kit tree file back unchanged.
- [x] 7.4 Implement the context refusals (undeclared URL, unknown alias, duplicate entry, unknown `critical` path, entry limit, name-length limit, context after the document), and verify each scenario.
  - `MorphirFile.readEnvelope` and `MorphirFile.expand` (`Morphir.Models/IR/Files`) refuse each of the parent's seven invalid examples with its code, naming the file ("Each invalid example is refused with its diagnostic", "An unknown envelope is refused", "Unknown alias", "Context after the document" in `ExpansionTests`), and the store refuses a tree with such a file. A malformed envelope or entry, which the parent gives no code, is `context.invalid_envelope` or `context.invalid_entry`.
- [ ] 7.5 Replace the `model.unsupported_context` refusal with expansion in `convert`, write `ModelConversionCompaction.feature` (reading part), update the docs and CHANGELOG, add the milestone 2 note, and verify "A compacted tree becomes a plain single file" and the docs commands.
- [ ] 7.6 Make `model verify` expand compacted single files and tree files before checking them, list the compacted files in its report, and apply the context refusals; verify "A compacted tree is verified after expansion" and "An invalid context fails verification".

## 8. Write compacted files (milestone 3)

- [ ] 8.1 On the same finos/morphir branch, complete parent task 1.4: the policy `morphir.compact/0.1.0-draft.1` with JCS bytes and UTF-8 size, and its hand-derived fixed cases (v3 and v4 single files, each tree file kind, name used once, non-ASCII name, alias order, ties, table order, look-alike literals, a loss case), reviewed in the parent PR; verify the parent's checks pass with no Rust change.
- [x] 8.2 Add the AOT-safe JCS writer to `Morphir.Models` and verify the RFC 8785 test vectors and zero trim and AOT warnings.
  - `Morphir.Json.CanonicalJson` (`write`, `number`, `byteLength`) passes the vectors of RFC 8785 itself, pinned whole as `FixtureCatalog.Rfc8785` (every row of Appendix B, the bytes of Sections 3.2.2 and 3.2.4, the sorting sample of 3.2.3), plus "Non-ASCII content is measured as UTF-8" (`CanonicalJsonTests`). `Morphir.Models` builds for `net11.0`, `net10.0` and `netstandard2.0` with 0 warnings under its trim and AOT analyzers; the Native AOT publish of `morphir` is checked with 9.1.
- [ ] 8.3 Implement the writer policy and `--compact[=<mode>]` (bare means `names`, `=` only, unknown mode exits 2, report names mode and policy), and verify the parent's fixed cases byte for byte, `expand (compact f) = f`, determinism, and the compaction scenarios of `cli-model-conversion` and `model-name-compaction`.
  - [x] The parent's 19 fixed cases of `morphir.compact/0.1.0-draft.1` pinned byte-exact from finos/morphir `e0a3449c` in `tests/Fixtures/MorphirContext/policy/` with provenance, served as `FixtureCatalog.Context.Policy` and by `MorphirFileMother` (`policyCases`, `policyInput`, `policyExpectedBytes`); the hash checks pass. As a check of the reader and the JCS writer against the parent's hand-made bytes, each expected file is its own canonical form, each declined case is its input in canonical JSON, and each compacted case expands to its input.
- [ ] 8.4 Finish `ModelConversionCompaction.feature` (writing part), update the docs and CHANGELOG, add the milestone 3 note, and verify the docs commands run as written.
- [ ] 8.5 Move the parent pins to the merged finos/morphir commit when the parent change merges, and verify the hash checks.

## 9. Integration checks

- [ ] 9.1 Publish the Native AOT `morphir` and verify zero IL2026 and IL3050 warnings and that the black-box conversion features pass against it.
- [ ] 9.2 Verify every row of the traceability table below names a passing test, and `openspec validate add-model-convert --strict` passes.

## Traceability

Every scenario of this change and the test that proves it. Unchanged scenarios of modified requirements keep their existing tests.

| Capability | Scenario | Test |
| --- | --- | --- |
| virtual-file-providers | A netstandard2.0 model library uses virtual file trees | `Morphir.Files.Tests`: netstandard consumer build test (2.2) |
| virtual-file-providers | Existing pipeline behavior is unchanged | the moved and existing pipeline tests (2.1) |
| virtual-file-providers | A file is found by its location | `Morphir.Files.Tests`: provider contract (2.3) |
| virtual-file-providers | A missing file is reported as not existing | `Morphir.Files.Tests`: provider contract (2.3) |
| virtual-file-providers | A directory lists its entries | `Morphir.Files.Tests`: provider contract (2.3) |
| virtual-file-providers | Watching never signals a change | `Morphir.Files.Tests`: provider contract (2.3) |
| virtual-file-providers | Embedded fixtures become a tree | `Morphir.Files.Tests`: provider builder (2.3) |
| virtual-file-providers | Content is not read while building | `Morphir.Files.Tests`: provider builder (2.3) |
| model-document-trees | Two spellings of one name have one address | `Morphir.Models.Tests`: addresses (4.1) |
| model-document-trees | A dependency document is addressed by its package | `Morphir.Models.Tests`: addresses (4.1) |
| model-document-trees | A v4 model round-trips | `Morphir.Models.Tests`: split/join law (4.1) |
| model-document-trees | A v3 model round-trips | `Morphir.Models.Tests`: split/join law (4.1) |
| model-document-trees | A long name is truncated and recorded | `Morphir.Models.Tests`: store (4.3), with "A path exactly at the budget is not truncated" |
| model-document-trees | A dependency is written under a bare version segment | `Morphir.Models.Tests`: store (4.3) |
| model-document-trees | Kit tree cases are met | `Morphir.Models.Tests`: store, v4 cases (4.3) and v3 cases (4.4); `ModelConversionKitCases.feature` (6.3) |
| model-document-trees | Directory order does not change the model | `Morphir.Models.Tests`: store (4.3) |
| model-document-trees | A v3 Library returns its original label | `Morphir.Models.Tests`: v3 store (4.4); `ModelConversion.feature` |
| model-document-trees | A v3 Specs tree keeps 3.1.0 | `Morphir.Models.Tests`: v3 store (4.4) |
| model-document-trees | Version and created are reported | `Morphir.Models.Tests`: store (4.3); `ModelConversion.feature` |
| model-document-trees | A meta member does not change the model | `Morphir.Models.Tests`: store (4.3) |
| model-document-trees | Ambiguous profile | `Morphir.Models.Tests`: refusals (4.5); `ModelConversion.feature` |
| model-document-trees | A v3 manifest of another release | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | Unknown member | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | Path over the budget | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | Missing definition file | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | A file outside the tree | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | Versioned dependency segment | `Morphir.Models.Tests`: refusals (4.5) |
| model-document-trees | The same tree from two sources | `Morphir.Models.Tests`: v4 store (4.3) and v3 store (4.6) |
| model-name-compaction | An unknown envelope is refused | `Morphir.Models.Tests`: envelope (7.4) |
| model-name-compaction | A compacted v3 single file expands to v3 | `Morphir.Models.Tests`: expansion (7.3); `ModelConversionCompaction.feature` |
| model-name-compaction | A compacted tree value file expands in place | `Morphir.Models.Tests`: expansion (7.3) |
| model-name-compaction | A literal that looks like an alias stays a literal | `Morphir.Models.Tests`: expansion (7.3) |
| model-name-compaction | Classic and v4 slots expand differently | `Morphir.Models.Tests`: expansion (7.3) |
| model-name-compaction | Unknown alias | `Morphir.Models.Tests`: context refusals (7.4) |
| model-name-compaction | Context after the document | `Morphir.Models.Tests`: context refusals (7.4); `ModelConversionCompaction.feature` |
| model-name-compaction | Expand after compact | `Morphir.Models.Tests`: compaction law (8.3) |
| model-name-compaction | A name used once stays spelled out | `Morphir.Models.Tests`: policy (8.3) |
| model-name-compaction | Compaction that would be a loss is not applied | `Morphir.Models.Tests`: policy (8.3) |
| model-name-compaction | Non-ASCII content is measured as UTF-8 | `Morphir.Models.Tests`: JCS and policy (8.2, 8.3) |
| model-name-compaction | The parent's fixed cases match byte for byte | `Morphir.Models.Tests`: fixed cases (8.3) |
| cli-model-conversion | A directory is read as a tree | `ModelConversion.feature` |
| cli-model-conversion | The IR version is kept | `ModelConversion.feature` |
| cli-model-conversion | A single file is compacted and stays one file | `ModelConversionCompaction.feature` |
| cli-model-conversion | A compacted tree becomes a plain single file | `ModelConversionCompaction.feature` |
| cli-model-conversion | Nothing to do | `ModelConversion.feature` |
| cli-model-conversion | Bare compact means names | `ModelConversionCompaction.feature` |
| cli-model-conversion | Unknown mode | `ModelConversionCompaction.feature` |
| cli-model-conversion | A separated mode is refused with a hint | `ModelConversionCompaction.feature` |
| cli-model-conversion | A refused tree writes nothing | `ModelConversion.feature` |
| cli-model-conversion | A non-empty output is refused | `ModelConversion.feature` |
| cli-model-conversion | Every written file is reported | `ModelConversion.feature`; `Features/ModelConvert/ModelConvert.feature` |
| cli-model-conversion | Notices are in the report | `ModelConversion.feature` |
| cli-model-commands | The group lists its commands | `ModelVerification.feature` |
| cli-model-commands | A valid v3 model is valid | `ModelVerification.feature` |
| cli-model-commands | A file without a format version is not valid | `ModelVerification.feature` |
| cli-model-commands | The former command is unknown | `ModelVerification.feature` |
| cli-model-commands | A valid v4 model is valid | `ModelVerification.feature` (5.4) |
| cli-model-commands | An invalid v4 model names its error | `ModelVerification.feature` (5.4) |
| cli-model-commands | A valid v4 tree is valid | `ModelVerification.feature` (5.5) |
| cli-model-commands | A valid v3 tree is valid | `ModelVerification.feature` (5.5) |
| cli-model-commands | An error inside one tree file names the file | `ModelVerification.feature` (5.5) |
| cli-model-commands | A broken tree rule is an error | `ModelVerification.feature` (5.5) |
| cli-model-commands | A schema version that contradicts the manifest | `ModelVerification.feature` (5.5) |
| cli-model-commands | A compacted tree is verified after expansion | `ModelVerification.feature` (7.6) |
| cli-model-commands | An invalid context fails verification | `ModelVerification.feature` (7.6) |
| ir-model-writing | Version-specific spelling | existing writer tests, unchanged |
| ir-model-writing | Unsupported output version | existing writer tests, extended (4.4) |
| ir-model-writing | A classic Specs distribution at 3.1.0 | `Morphir.Models.Tests`: classic writer (4.4) |
| test-driver-contract | A verification report | `ModelVerification.feature` report checks (5.1) |
| test-driver-contract | A generation report | existing, unchanged |
| test-driver-contract | A conversion report | `tests/Morphir.TestSupport.Tests/Drivers` (6.2) |
| test-driver-contract | A feature file states intents only | `BlackBoxRulesTests` guard (6.2) |
| test-driver-contract | A target without the capability skips the test | `tests/Morphir.TestSupport.Tests/Drivers` (6.2) |
| test-authoring-guidance | A test that starts the CLI for one capability | skill review check (3.4) |
| test-authoring-guidance | A feature file names a case by intent | skill review check (3.4) |
| test-authoring-guidance | An unowned fixture fails a guard test | `FixtureOwnershipTests.AnUnownedFixtureFailsAGuardTest` (3.3) |
| test-authoring-guidance | The skill gives the pattern | skill review check (3.4) |
| test-authoring-guidance | the other scenarios of "The skill classifies a test in a fixed order" | existing, unchanged |

## Workflow follow-up

- Follow AGENTS.md for review, merge approval, archive and release approval.
- Archive the change after the last milestone merges and the parent pins point at merged commits.
