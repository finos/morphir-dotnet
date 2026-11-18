# Tasks

Work is on `feat/model-convert`, before `add-model-convert` group 6, which builds on it. Use the `test-manager` skill before adding any test, feature file, step or driver, and run the `test-manager-reviewer` agent on the diff. No Rust code changes.

## 1. Tracking

- [ ] 1.1 File a morphir-dotnet GitHub issue for this change (maintainer approves the text first), link it in `proposal.md` and the pull request, and verify the link resolves.

## 2. The convention

- [x] 2.1 Add the shared `--json[=stream|document]` option and the bare-flag rewrite (after response-file expansion, never after `--`) in `Morphir.Tooling`, with the mode hint for an unrecognized argument that names a mode; verify parser unit tests for every row of the design's probe table, `--json=bogus`, `--json:document` and `--json=true`.
- [x] 2.2 Generalise compile's JSON Lines writer into the shared output writer (events, flush per record, one final `completed`, the document mode, a `completed` record on parse errors and cancellation), using each command's source-generated JSON context; verify writer unit tests and zero trim and AOT warnings. Every command writes through it; the zero-warning check of a Native AOT publish is not yet run (it runs with 5.1).
- [x] 2.3 Add `JsonOutputAssertions` beside `JsonCommandReport` in `tests/Morphir.TestSupport` (stream and document forms, returns the report) and move every driver's report reading onto it; verify the `test-driver-contract` scenarios in `tests/Morphir.TestSupport.Tests/Drivers`.
- [x] 2.4 Document the convention on the CLI index page and in the CHANGELOG, and verify the documented examples run as written.

## 3. Commands adopt the convention

- [x] 3.1 `compile`: move the event stream under `--json`, the single report under `--json=document`, remove `--json-lines`; verify the `cli-model-compilation` scenarios in the existing compile command tests and black-box features, and update `docs/content/docs/cli/compile.md`.
- [x] 3.2 `validate`, `generate` (including C# generation) and `ir verify` (renamed to `model verify` by `add-model-convert`): adopt the writer; verify the `model-analyzers` and `cli-csharp-generation` scenarios and update their CLI pages.
- [x] 3.3 `config` (show, set, unset), `extension list`, `extension runtimes`, `extension worker --help --json`, `project migrate` and `workspace migrate`: adopt the writer; verify their existing report tests through `JsonOutputAssertions` and update their CLI pages.
- [x] 3.4 Write `JsonOutput.feature` in `tests/Morphir.BlackBox.Tests/Integration/` with scenario outlines over the commands for every `cli-json-output` scenario, and verify it passes on the development build and the Native AOT executable. It passes on the development build; the Native AOT run is task 5.1.

## 4. Consumers

- [x] 4.1 Update `src/Morphir.Conformance/CliAdapter.fs` to drive `compile --json`, and `GuestKit/WorkerSessions.fs` and `WasmHost/Worker.fs` to read the new output; verify the frontend and WASM host conformance suites pass unchanged.
- [ ] 4.2 Draft the finos/morphir change that makes `tools/cli-release.ts` read the `completed` line, to land with the pin move that carries this change; verify its `tools/` tests pass there.
- [x] 4.3 Record in `establish-morphir-compatibility-kit` (a note in its proposal) that `mck run --json-lines` adopts this convention when the change is next revised; verify `openspec validate establish-morphir-compatibility-kit` still passes.

## 5. Integration checks

- [x] 5.1 Run the black-box suite on the Native AOT executable and verify every feature that reads JSON output passes.
  - Run on osx-arm64 at `d574dbf8` with `MORPHIR_TEST_TARGET=aot`: `JsonOutput.feature` 73 passed, 2 skipped by design (a log line of `generate` needs dynamic code, which the AOT host lacks), and every other feature that reads JSON output passes; the whole black-box suite has no failure (integration 483 passed, 119 skipped by host or capability). The ILC step of the publish gives no IL2026 and no IL3050, which closes the zero-warning check of 2.2.
- [x] 5.2 Verify every row of the traceability table below names a passing test, and `openspec validate adopt-streaming-json --strict` passes.
  - Each row of the table names its tests by their final names; every one was run and passes (`JsonOutput.feature`, `Validation.feature`, `CSharpGenerationCommand.feature`, `ExtensionWorker.feature`'s help scenario, `Morphir.Tooling.Tests`, `Morphir.TestSupport.Tests` and the worker test of `Morphir.Conformance.Tests`). `openspec validate --all --strict` passes.

## Traceability

Every scenario of this change and the test that proves it, by its final name: a scenario of a feature file of `tests/Morphir.BlackBox.Tests/Integration/` (an outline runs for each command of its examples), a TUnit test as `Class.Method`, or an Expecto test by its path of list names. Each row was run and passes at the commit that fills this table (5.2).

| Capability | Scenario | Test |
| --- | --- | --- |
| cli-json-output | A bare flag streams | `JsonOutput.feature`: "A bare flag streams"; `OptionValueRewriteTests.ABareFlagStreams` |
| cli-json-output | Unknown mode | `JsonOutput.feature`: "Unknown mode"; `OptionValueRewriteTests.AnUnknownModeIsRefusedAndTheKnownModesAreListed`, `OptionValueRewriteTests.TheColonSeparatorIsAcceptedQuietly` and `OptionValueRewriteTests.TheFormerBooleanValueIsRefused` |
| cli-json-output | Every line parses | `JsonOutput.feature`: "Every line parses" |
| cli-json-output | A usage error still ends with completed | `JsonOutput.feature`: "A usage error still ends with completed"; `CompileCommandTests.AUsageErrorStillEndsWithCompleted` |
| cli-json-output | A command with nothing to stream | `JsonOutput.feature`: "A command with nothing to stream" |
| cli-json-output | An early warning is visible before a slow stage ends | `CompileCommandTests.AnEarlyWarningIsVisibleBeforeASlowStageEnds` (a slow fake frontend) |
| cli-json-output | The two modes carry one report | `JsonOutput.feature`: "The two modes carry one report"; `CompileCommandTests.TheStreamCarriesTheReportOfTheDocumentAndItsDiagnostics` |
| cli-json-output | A document is pipeable | `JsonOutput.feature`: "A document is pipeable" |
| cli-json-output | The next argument stays the command's | `JsonOutput.feature`: "The next argument stays the command's"; `OptionValueRewriteTests.TheNextArgumentStaysTheCommands` |
| cli-json-output | A separated mode is refused with a hint | `JsonOutput.feature`: "A separated mode is refused with a hint"; `OptionValueRewriteTests.ASeparatedModeIsRefusedWithAHint` |
| cli-json-output | A log line goes to stderr | `JsonOutput.feature`: "A log line goes to stderr" |
| cli-json-output | A named member in both modes | `JsonOutput.feature`: "A named member in both modes" (`extension list`) |
| cli-json-output | The old flag is unknown | `JsonOutput.feature`: "The old flag is unknown"; `CompileCommandTests.TheOldFlagIsUnknown` |
| cli-json-output | The worker's help result keeps its contract form | `ExtensionWorkerTests.Help_with_json_writes_one_command_result_with_the_build_identity_on_stdout` (each mode); `Morphir.Conformance.Tests`: `WASM host worker adapter and runner (task 1.5)/A help result wrapped in a completed record is refused`; `ExtensionWorker.feature`: "The help as JSON gives the build identity and says that the worker is internal and experimental" |
| cli-model-compilation | Warnings from every stage in a successful report | `CompileCommandTests.WarningsFromEveryStageInASuccessfulReport` (on `--json=document`) |
| cli-model-compilation | Unsupported construct in JSON mode | `CompileCommandTests.UnsupportedConstructInJsonMode` (on `--json=document`) |
| cli-model-compilation | Project-file warning before compilation ends | `CompileCommandTests.HumanModeStreamsAProjectFileWarningBeforeASlowFrontendCompletes` (existing, unchanged) |
| cli-model-compilation | JSON mode is not streamed | `CompileCommandTests.JsonModeIsNotStreamed` |
| cli-model-compilation | Events in order | `CompileCommandTests.EventsInOrder` (on `--json`) |
| cli-model-compilation | Failure still ends with completed | `CompileCommandTests.FailureStillEndsWithCompleted` (on `--json`) |
| cli-model-compilation | Both JSON flags | `CompileCommandTests.BothJsonFlags` |
| cli-model-compilation | Cancellation | `CompileCommandTests.Cancellation` (on `--json`) |
| model-analyzers | JSON report is pipeable | `Validation.feature`: "Standard output holds only the JSON report, and its exit code is the exit code of the process" |
| model-analyzers | Both hosts agree | `Validation.feature`: "Both hosts write the same report apart from the host" (on the new output) |
| cli-csharp-generation | Structured semantic failure | `CSharpGenerationCommand.feature`: "A semantic refusal is structured command data" |
| cli-csharp-generation | Unavailable provider option | `CSharpGenerationCommand.feature`: "An unsupported C# language alias is an invocation failure" and "An older C# language version names the supported version" |
| test-driver-contract | A JSON command writes one document | `JsonOutputAssertionsTests.OneDocumentGivesTheReport`, with `JsonOutputAssertionsTests.TextAfterTheDocumentFails` and `JsonOutputAssertionsTests.ACompletedRecordIsAStreamNotADocument` |
| test-driver-contract | A log line on standard output fails | `JsonOutputAssertionsTests.ALogLineBeforeTheDocumentFailsAndShowsTheLine` and `JsonOutputAssertionsTests.ALogLineInAStreamFailsAndShowsTheLine` |
| test-driver-contract | A stream gives its report | `JsonOutputAssertionsTests.AStreamGivesTheReportOfItsCompletedRecord` |

## Workflow follow-up

- Follow AGENTS.md for review, merge approval, archive and release approval.
