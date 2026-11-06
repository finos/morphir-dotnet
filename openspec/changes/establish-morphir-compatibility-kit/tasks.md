# Tasks

This change starts after `add-fsharp-compile` lands, because it generalizes that change's `conformance/` seed corpus, its `Morphir.Conformance` runner and its adapters (MCK-1 there). It does not create a second runner. Every behavior task follows red/green/refactor, and each group lands its own tests and documentation. The oracle is reviewed case data: when morphir-dotnet fails a reviewed case, fix the code or record a named `knownGap`; never change the expectation to match the implementation.

## 1. Tracking, format decision and baseline

- [ ] 1.1 Open an MCK epic, linked to the default-CLI decision record, and add a feature PRD and index entry under `docs/content/contributing/design/prds/`. Verify that the PRD links this change and `add-fsharp-compile`, and that its status table separates kit data, evidence, execution, reports, the command and the suites.
- [ ] 1.2 Write the normative MCK step vocabulary 1 (`conformance/steps/steps-1.md`): the grammar, parameters, docstring content types and meaning of each step, covering every row of the content model table in design.html. Verify that every step in the design's example feature and in the seed suite's planned features matches the grammar, and that each content-model part has at least one step.
- [ ] 1.3 Confirm that `add-fsharp-compile` has landed, and list its seed corpus, runner and adapter interfaces in the PRD. Verify that the seed suite runs green before any change from this group.

## 2. Kit data (conformance-case-format)

- [ ] 2.1 Check the official `Gherkin` parser package for trim and AOT warnings in a published test app. Then implement the corpus reader (Gherkin into the content model), the step-vocabulary matcher, and `kit.toml` (kit version, vocabulary version, suites, profiles, normalization catalog, reserved ids). Verify:
  - a fixture kit is read into the content model;
  - a script in another language with a stock Gherkin parser lists the same cases;
  - the newer-vocabulary scenario passes.

  If the parser warns, implement the AOT-safe subset parser from the design's risk table, and verify that both parsers give the same parse for every kit file.
- [ ] 2.2 Implement the linter, which applies the validation rules: ids `<suite>-NNNN` that are unique, not reused and use their suite's prefix; steps only from the vocabulary; no adapter or executable names; known tags only (`@basis:`, `@review:`, `@import:`, `@knownGap:`, `@exclusion`, `@excluded`, `@normalize:`, `@requires:`, `@since:`); outline completeness with non-empty `reason` columns in `@excluded` blocks; every sidecar referenced. Verify the malformed-case, unknown-step, adapter-in-a-feature, excluded-version, unreferenced-sidecar and missing-version-row scenarios, each naming the file and line.
- [ ] 2.3 Implement the canonical comparison for each observation kind and the named normalization catalog (`@normalize:` tags), with a written justification for each normalization. Verify the unnamed-list-order and named-normalization scenarios, the stable-diagnostic-members scenario, and the first-difference JSON Pointer in failures.
- [ ] 2.4 Implement kit versioning checks. Verify that a stricter expectation without a breaking version change fails the kit release check, and that a pre-release kit matches only itself.
- [ ] 2.5 Adopt the `add-fsharp-compile` seed suite `frontend-fsharp`, which is already written in Gherkin. Align it with step vocabulary 1, the kit tags (`@basis:`, `@review:` and the others) and the review sidecar files, keeping every expectation unchanged. Verify that the seed gives the same verdicts before and after the alignment, by comparing the verdict reports case by case, and that the linter accepts every seed feature.
- [ ] 2.6 Implement the literate page generator: each feature becomes a Markdown page with prose, sources, expectations (sidecars included) and exclusions with reasons, under a "generated, do not edit" header. Add the CI check that generates the pages again and fails when they differ. Verify the stale-page scenario, and that a hand edit to a generated page fails the check.
- [ ] 2.7 Write the kit reference and authoring guide under `docs/content/docs/conformance/`. Cover the Gherkin conventions, the step vocabulary, tags, sidecars, classification, basis, comparison and normalizations. Verify that each documented example is a feature that the linter accepts in the fixture kit.

## 3. Evidence (conformance-evidence)

- [ ] 3.1 Implement the basis and review rules: every expected observation has a basis and evidence, and every tool-produced expectation has a review record. Verify the recorded-output-without-review scenario, and the rule that a failing morphir-dotnet run becomes a `knownGap` and never an edited expectation.
- [ ] 3.2 Import the pinned upstream `spec/mep/mck/` material and the Smithy transcripts from `finos/morphir@cde7722eb` under `conformance/imports/`, unchanged and with `PROVENANCE.md`. Reuse the existing pins in `tests/Fixtures/MorphirIon/mep/` where the bytes are the same. Verify the digest checks and the pinned-bytes-change scenario.
- [ ] 3.3 Record a disposition for every imported case. These are expected:
  - the legacy `0.1` stage is excluded from guest profiles that speak only `0.2.0-draft.1`;
  - the frontend+backend round trip is excluded from frontend-only profiles;
  - recorded Python IR is either admitted after review against the contract or superseded.

  Verify that validation fails on an unclassified imported case, and that the two exclusion scenarios pass.
- [ ] 3.4 Publish the supersession log with upstream issue links, and open those issues in `finos/morphir`. Verify that the release check fails when a supersession has no issue link, and that the log is rendered in the documentation.

## 4. Execution (conformance-execution)

- [ ] 4.1 Implement plan building from profiles and claims (MEP describe and initialize, a CLI capability probe, library registrations). Verify the frontend-only-guest and nothing-selected scenarios, and verify that the plan is stored in a form that `report-check` can rebuild.
- [ ] 4.2 Generalize the `cli` adapter from `add-fsharp-compile`: any executable, no shell, a fresh workspace, a fixed environment, no network by default, deadlines. Verify the another-CLI scenario with a fake executable, and the repeat-run scenario.
- [ ] 4.3 Generalize the `mep-guest` adapter over `Morphir.Mep`, with contract-shape checks before case comparison. Verify the guest-hangs scenario and process-tree cleanup with scripted fake guests.
- [ ] 4.4 Generalize the `library` adapter, registered only in hosts that contain its libraries. Verify the same-verdicts scenario for the seed suite through `library` and through `cli`.
- [ ] 4.5 Verify AOT readiness: publish the trimmed, Native AOT `morphir` with the runner, and run a `cli` profile. Verify that there are no new trim or AOT warnings and that the closure check shows no FCS.

## 5. Reports (conformance-reports)

- [ ] 5.1 Define the report document (`reportVersion`, kit version and digest, profile, plan, claims, runner identity, adapter, executable identity, identity files, cases in plan order with observations, normalizations, verdicts and first differences). Generate its serialization with a source generator. Verify the executable-identity scenario and that no absolute path appears outside the identity entries.
- [ ] 5.2 Implement `report-check`: kit digest, identity digests, plan rebuild, and verdicts derived again. Verify the edited-verdict, different-kit and shared-report scenarios, and the known-gap rule.
- [ ] 5.3 Document the report format and how to check a report. Verify that a documented sample report passes `report-check` against the fixture kit.

## 6. The `morphir mck` command (cli-conformance-command)

- [ ] 6.1 Add the `mck` scenario, with the `run`, `report-check` and `list` commands as mediator handlers, registered in both hosts. Verify the wrong-kit-digest, valid-report, list-known-gaps and missing-option scenarios.
- [ ] 6.2 Implement the exit codes 0, 1 and 2 to match upstream `mck`, and `--json` and `--json-lines` output with the STRM-2 streaming rule. Verify the streamed-case-results scenario, JSON stdout purity, and that logs go only to stderr.
- [ ] 6.3 Write `docs/content/docs/cli/mck.md`, including how to move from upstream `morphir mck mep guest-run`. Verify that each documented command runs against the fixture kit.

## 7. Suites

- [ ] 7.1 Author the `ir-writing` suite: reader and writer cases for classic `1`, `2` and `3` and for IR `4`, with independently written expected IR and review records. Verify that the morphir-dotnet writers pass, or are reported as named gaps.
- [ ] 7.2 Author the `cli` suite: compile and generate behavior, task records and tombstones, `--json`, the `--json-lines` records and exit codes, from the specs of `add-fsharp-compile`. Verify that the suite runs through the `cli` adapter against `dotnet-morphir` and the AOT `morphir`.
- [ ] 7.3 Author the `mep-guest` and `mep-host` suites from the contract and the admitted imports. Verify that they run against the morphir-dotnet F# guest and the host client.
- [ ] 7.4 Define the first profiles (`fsharp-frontend-v1`, `mep-frontend-v1`, `cli-v1`, `ir-writing-v1`) in the kit manifest. Verify that each profile selects only cases for the capabilities it names.

## 8. Evidence runs and readiness

- [ ] 8.1 Run every profile against `dotnet-morphir`, the published AOT `morphir` and the F# MEP guest, and keep the reports. Verify that every report passes `report-check`, and record the results and gaps in the PRD.
- [ ] 8.2 Run the `cli-v1` and `ir-writing-v1` profiles against the Rust `morphir` CLI at a pinned release. Publish the reports and gaps as evidence for the default-CLI decision record. Verify that each Rust gap is either reported upstream or recorded as a disposition.
- [ ] 8.3 Run the repository test command, formatting and coverage. Check every spec scenario against the implementation, and verify that `openspec validate establish-morphir-compatibility-kit --strict` passes.

## Workflow follow-up

- Import `tests/Fixtures/MorphirSDK` and `tests/Fixtures/MorphirIon/kit` as kit suites in later changes, through the import and disposition rules.
- Add an adapter that reads upstream report formats as evidence, if upstream reports must be checked.
- Propose the corrected kit upstream as the shared Morphir Compatibility Kit.
- Get the required review and explicit merge approval. This proposal does not authorize implementation, publication or merge. Archive only after implementation and review requirements are met.
