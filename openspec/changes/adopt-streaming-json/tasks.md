# Tasks

Work is on `feat/model-convert`, before `add-model-convert` group 6, which builds on it. Use the `test-manager` skill before adding any test, feature file, step or driver, and run the `test-manager-reviewer` agent on the diff. No Rust code changes.

## 1. Tracking

- [ ] 1.1 File a morphir-dotnet GitHub issue for this change (maintainer approves the text first), link it in `proposal.md` and the pull request, and verify the link resolves.

## 2. The convention

- [ ] 2.1 Add the shared `--json[=stream|document]` option and the bare-flag rewrite (after response-file expansion, never after `--`) in `Morphir.Tooling`, with the mode hint for an unrecognized argument that names a mode; verify parser unit tests for every row of the design's probe table, `--json=bogus`, `--json:document` and `--json=true`.
- [ ] 2.2 Generalise compile's JSON Lines writer into the shared output writer (events, flush per record, one final `completed`, the document mode, a `completed` record on parse errors and cancellation), using each command's source-generated JSON context; verify writer unit tests and zero trim and AOT warnings.
- [ ] 2.3 Add `JsonOutputAssertions` beside `JsonCommandReport` in `tests/Morphir.TestSupport` (stream and document forms, returns the report) and move every driver's report reading onto it; verify the `test-driver-contract` scenarios in `tests/Morphir.TestSupport.Tests/Drivers`.
- [ ] 2.4 Document the convention on the CLI index page and in the CHANGELOG, and verify the documented examples run as written.

## 3. Commands adopt the convention

- [ ] 3.1 `compile`: move the event stream under `--json`, the single report under `--json=document`, remove `--json-lines`; verify the `cli-model-compilation` scenarios in the existing compile command tests and black-box features, and update `docs/content/docs/cli/compile.md`.
- [ ] 3.2 `validate`, `generate` (including C# generation) and `ir verify` (renamed to `model verify` by `add-model-convert`): adopt the writer; verify the `model-analyzers` and `cli-csharp-generation` scenarios and update their CLI pages.
- [ ] 3.3 `config` (show, set, unset), `extension list`, `extension runtimes`, `extension worker --help --json`, `project migrate` and `workspace migrate`: adopt the writer; verify their existing report tests through `JsonOutputAssertions` and update their CLI pages.
- [ ] 3.4 Write `JsonOutput.feature` in `tests/Morphir.BlackBox.Tests/Integration/` with scenario outlines over the commands for every `cli-json-output` scenario, and verify it passes on the development build and the Native AOT executable.

## 4. Consumers

- [ ] 4.1 Update `src/Morphir.Conformance/CliAdapter.fs` to drive `compile --json`, and `GuestKit/WorkerSessions.fs` and `WasmHost/Worker.fs` to read the new output; verify the frontend and WASM host conformance suites pass unchanged.
- [ ] 4.2 Draft the finos/morphir change that makes `tools/cli-release.ts` read the `completed` line, to land with the pin move that carries this change; verify its `tools/` tests pass there.
- [ ] 4.3 Record in `establish-morphir-compatibility-kit` (a note in its proposal) that `mck run --json-lines` adopts this convention when the change is next revised; verify `openspec validate establish-morphir-compatibility-kit` still passes.

## 5. Integration checks

- [ ] 5.1 Run the black-box suite on the Native AOT executable and verify every feature that reads JSON output passes.
- [ ] 5.2 Verify every row of the traceability table below names a passing test, and `openspec validate adopt-streaming-json --strict` passes.

## Traceability

| Capability | Scenario | Test |
| --- | --- | --- |
| cli-json-output | A bare flag streams | `JsonOutput.feature` |
| cli-json-output | Unknown mode | `JsonOutput.feature`; parser unit tests (2.1) |
| cli-json-output | Every line parses | `JsonOutput.feature` |
| cli-json-output | A usage error still ends with completed | `JsonOutput.feature` |
| cli-json-output | A command with nothing to stream | `JsonOutput.feature` |
| cli-json-output | An early warning is visible before a slow stage ends | compile command tests with a slow fake frontend (3.1) |
| cli-json-output | The two modes carry one report | `JsonOutput.feature` |
| cli-json-output | A document is pipeable | `JsonOutput.feature` |
| cli-json-output | The next argument stays the command's | `JsonOutput.feature`; parser unit tests (2.1) |
| cli-json-output | A separated mode is refused with a hint | `JsonOutput.feature`; parser unit tests (2.1) |
| cli-json-output | A log line goes to stderr | `JsonOutput.feature` |
| cli-json-output | A named member in both modes | `JsonOutput.feature` (`extension list`) |
| cli-json-output | The old flag is unknown | `JsonOutput.feature` |
| cli-model-compilation | Warnings from every stage in a successful report | existing compile tests, on `--json=document` (3.1) |
| cli-model-compilation | Unsupported construct in JSON mode | existing compile tests, on `--json=document` (3.1) |
| cli-model-compilation | Project-file warning before compilation ends | existing, unchanged |
| cli-model-compilation | JSON mode is not streamed | compile command tests (3.1) |
| cli-model-compilation | Events in order | compile command tests, on `--json` (3.1) |
| cli-model-compilation | Failure still ends with completed | compile command tests, on `--json` (3.1) |
| cli-model-compilation | Both JSON flags | compile command tests (3.1) |
| cli-model-compilation | Cancellation | compile command tests, on `--json` (3.1) |
| model-analyzers | JSON report is pipeable | validate black-box feature (3.2) |
| model-analyzers | Both hosts agree | existing, on the new output (3.2) |
| cli-csharp-generation | Structured semantic failure | existing C# generation tests (3.2) |
| cli-csharp-generation | Unavailable provider option | existing C# generation tests (3.2) |
| test-driver-contract | A JSON command writes one document | `tests/Morphir.TestSupport.Tests/Drivers` (2.3) |
| test-driver-contract | A log line on standard output fails | `tests/Morphir.TestSupport.Tests/Drivers` (2.3) |
| test-driver-contract | A stream gives its report | `tests/Morphir.TestSupport.Tests/Drivers` (2.3) |

## Workflow follow-up

- Follow AGENTS.md for review, merge approval, archive and release approval.
