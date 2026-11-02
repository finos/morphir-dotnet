# Tasks

Related work: [#363](https://github.com/finos/morphir-dotnet/issues/363), [#368](https://github.com/finos/morphir-dotnet/issues/368). All boxes track implementation, not completion of this proposal. Follow red/green/refactor for behavior changes; each group owns its tests and documentation. V4 F# generation is required before this change is complete. TEST-1 applies to every group: extend one behavior corpus, compiler/execution runner and assertion approach across versions/formats. Implementation groups do not authorize separate v3/v4 test strategies or duplicated oracles.

## 1. Baseline and acceptance contract

- [x] 1.1 Integrate the merged PR #456 baseline into the implementation branch without reset or loss of local work; verify the history contains `0a7f6ca` or an equivalent reviewed integration and locked restore succeeds.
- [x] 1.2 Create the feature PRD and index entry under `docs/content/contributing/design/prds/`, linking this change, #363 and #368; verify the status table distinguishes implemented generation from future MEP/configuration work and includes v4 in the first delivery.
- [x] 1.3 Extend the existing generator/compiler helpers into the common TEST-1 harness and behavior inventory before adding version-specific generation work; verify one independently specified case runs through multiple representation adapters and matrix completeness checks fail on missing required rows while accounting for unrepresentable combinations with reasons.
- [x] 1.4 Add parameterized generation BDD scenarios under `tests/Morphir.Tooling.Tests/Features/Generate/` before production changes, reusing the same runner and step definitions across versions; verify red cases cover explicit CLI input, nonempty classic/v4 success, format parity, expected refusals, failed-output preservation and JSON stdout purity.

## 2. Typed contracts, settings and backend selection

- [x] 2.1 Add an additive versioned semantic wrapper over existing classic/v4 distributions in Morphir.Models, preserving exact source version context; verify model tests distinguish family, version and distribution kind without a Morphir.Ion reference or wire-format change.
- [x] 2.2 Add the Tooling resolved-settings, retained-input, typed outcome, backend descriptor/request/result contracts from APP-1 and MDL-1; compile representative C#/F# consumer snippets and verify existing public Ion and Myriad consumers still compile.
- [x] 2.3 Implement the CLI-only settings resolver after failing tests for missing values, explicit false, invocation-relative paths and JSON-first option parsing; verify last-option-wins, parse-value ownership and outside-project invocation without reading config files.
- [x] 2.4 Implement exact target/version/distribution resolution over statically registered providers; verify a fake second backend works without changing the command and that unknown, ambiguous and incompatible provider cases invoke no backend.
- [x] 2.5 Document the contracts and CLI-only resolution in the relevant library/Tooling documentation; verify examples compile and TOML/YAML/MEP examples are labeled future-only with no claim of implemented discovery.

## 3. Typed F# entry point and existing-consumer compatibility

- [x] 3.1 Add the typed classic generation path while preserving the existing strict JSON-file wrapper; verify existing v1/v2/v3 representations of shared corpus cases produce equivalent source, diagnostics and ordered manifests through both APIs, using the common runner.
- [x] 3.2 Carry existing scope/type/naming/dependency/SDK validation into the typed path and retain original input context; verify invalid typed models, unmapped references and cross-module cycles fail without successful files.
- [ ] 3.3 Expose typed generation options with Boolean optimize defaulting to true; verify the existing optimized/unoptimized compiler-behavior tests and Myriad/source snapshots remain valid without caller changes.
- [ ] 3.4 Document the additive typed API and the legacy JSON-only wrapper contract; compile the supported public usage sample as a consumer test.

## 4. Extend generation semantics and the shared corpus through v4

All tests in this group extend the harness and case inventory from group 1. Version-specific input adapters and explicit applicability data are allowed; a second fixture strategy, compiler runner, behavioral oracle or assertion path is not.

- [ ] 4.1 Add v4 type validation and emission for aliases, custom unions, generics, tuples, closed records, functions, unit, visibility and dependency order; add v4 representations of the shared type cases, observe red, and verify the common compiler runner accepts the generated output.
- [ ] 4.2 Add v4 scalar emission using the documented exact F# scalar profile; extend common literal cases with version applicability and expected outcomes for Int32 boundaries, finite floats, decimal exactness/scale, BMP characters, overflow, rounding and supplementary-plane Char refusal.
- [ ] 4.3 Add v4 expression/pattern emission for constructors, tuples/lists/records, variables/references, field access, application, lambdas, conditionals and ordered matching; verify each applicable representation against the case's single independently specified expected behavior through the shared runner.
- [ ] 4.4 Add v4 let/letrec, destructuring and record-update behavior; run existing recursion, shadowing, currying and record-operation cases through added v4 representations using the same compile/invoke/assert steps.
- [ ] 4.5 Add v4 scope/type checks, compatible SDK reference mapping and inferred/declared type consistency; add inferred-type cases with explicit applicability to the common diagnostic corpus and verify shared assertions identify contradictions and unresolved references with no successful source.
- [ ] 4.6 Implement specific v4 refusal diagnostics for Specs/Application, DocumentLiteral, open extensible records, holes/incomplete types/bodies, NativeBody/ExternalBody and nonempty constraints or unknown required semantics; add expected-refusal rows to the shared corpus and verify the common publication checks preserve earlier output.
- [ ] 4.7 Add exact v4 constant folding or reuse version-safe helpers without converting through classic IR; run the shared optimization cases across applicable versions and verify common behavioral expectations plus inexact-fold preservation.
- [ ] 4.8 Expose v4 through the shared typed entry point and registered F# descriptor only after the prior tasks pass; verify all applicable pricing representations compile and execute against the same expected behavior, with cross-version parity as an additional check and no JSON downgrade bridge.
- [ ] 4.9 Document the versioned generation API, supported/refused feature matrix, scalar profile and TEST-1 extension rules; compile the public typed-model sample and verify every documented capability/refusal maps to the common case inventory.

## 5. Shared bounded model loading

- [ ] 5.1 Implement privately owned bounded file/tree capture before decoding, with cancellation, aggregate byte/entry/depth/path budgets and link/special-file refusal; verify limit boundaries, unreadable entries and post-load input mutation cannot produce a partial or changing model.
- [ ] 5.2 Add JSON codec adapters over shared classic/v4 readers and deterministic format selection; verify all supported versions, explicit format conflicts, missing/unknown versions, malformed input and exact source-version retention.
- [ ] 5.3 Add Ion text/binary and tree adapters over existing readers without JSON serialization; verify binary-marker detection without suffix hints, tree-relative diagnostics and no fallback after recognized malformed Ion.
- [ ] 5.4 Add explicit unsupported-format/profile outcomes for YAML models, JSON/YAML trees and MEP envelopes; verify each reports its actual boundary rather than defaulting to JSON or v3.
- [ ] 5.5 Extend representation adapters for the shared nonempty behavior cases across applicable IR versions and JSON/Ion text/binary/tree formats; verify same-version source/manifest parity and common compiled behavior, with a completeness check for required matrix rows and explicit reasons for unsupported combinations.
- [ ] 5.6 Document supported input/version combinations, limits and raw-retention versus semantic-view guarantees; verify every in-scope input example loads through the public adapter and unsupported examples receive the stated diagnostic.

## 6. CLI handler and reporting

- [ ] 6.1 Implement generation orchestration in the existing mediator/host and register the first-party F# adapter through a generation scenario; verify the BDD success path reaches settings, loader, resolver and backend without target switches in command actions.
- [ ] 6.2 Add `--input`, `--target`, `--output`, `--input-format`, repeated `--option` and `--json` binding; verify missing settings, unsupported flags and unknown/invalid backend options return code 2 without publication.
- [ ] 6.3 Add source-generated generation report serialization and typed exit mapping; verify stdout is one JSON document for parsed JSON-mode success/failure, logs stay on stderr, semantic failures return 1, operational failures return 2 and cancellation before publication returns 130.
- [ ] 6.4 Add the command reference, getting-started examples and troubleshooting entries under `docs/content/docs/`; run each delivered CLI example against fixtures and verify future config/MEP syntax is not advertised as available.

## 7. Owned publication

- [ ] 7.1 Adapt generated file trees to the existing publisher with a centralized ownership-manifest policy and preserved declared order; verify successful publication and repeat-generation unchanged receipts with no unnecessary rewrites.
- [ ] 7.2 Validate input/output separation and artifact paths before writes; verify overlap, traversal, rooted paths, case collisions, symlinks and generation errors leave previous output untouched.
- [ ] 7.3 Verify stale owned-file removal, unowned-file preservation, staging failures and commit-time failure reporting using the existing publisher contract; document the ownership manifest and the absence of directory-wide rollback guarantees in the CLI reference.

## 8. Integration and release-readiness checks

- [ ] 8.1 Run the complete relevant model, Ion, pipeline, F# generator, Tooling and Myriad suites plus the repository's full test command; verify the shared behavior/version/format matrix covers nonempty generation, compilation, execution and refusals, reject unexplained skips or missing required rows, and record real results in the PRD.
- [ ] 8.2 Run repository formatting and required coverage collection; verify affected unit coverage meets the repository's >90% requirement and overall coverage remains at least 80%, or record an existing unrelated baseline gap for maintainer review.
- [ ] 8.3 Publish representative trimmed and Native AOT CLI builds using the repository's supported host/toolchain; run selected shared corpus cases across applicable versions/formats through those published CLI executables, verify parseable JSON reports, and review every new trimming/AOT warning in affected code.
- [ ] 8.4 Check all current examples, spec scenarios, publication guarantees and capability claims against implementation; verify `openspec validate add-generate-command --strict` passes and update the PRD's feature status and implementation notes.

## Workflow follow-up

- TOML/YAML generation configuration and MEP host/guest adapters are separate follow-up changes through the contracts in design.html.
- Obtain required review and explicit merge approval; creating this proposal does not authorize implementation, publication or merge.
- Archive only after implementation, validation and repository review requirements are satisfied.
