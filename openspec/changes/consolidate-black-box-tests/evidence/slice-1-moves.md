# Slice 1: the tests that moved to the black-box project (tasks 4.1 to 4.4)

Date: 2026-11-12. The rows are the tests that the runner discovers, from the TRX report of the complete
`Morphir.Tooling.Tests` before the move (`evidence/tooling-baseline.md`): 27 scenarios in
`Integration/CLI/` and 33 cases in `Features/Generate/CSharpCliGenerationTests.cs`.

| | Before | After |
| --- | ---: | ---: |
| `Morphir.Tooling.Tests`, tests | 1,287 | 1,243 (27 scenarios and 17 cases moved) |
| `Morphir.Tooling.Tests`, passed / failed / skipped | 1,283 / 0 / 4 | 1,239 / 0 / 4 |
| `Morphir.BlackBox.Tests`, tests | 0 | 65: integration 60, smoke 4, journey 1 |
| `Morphir.BlackBox.Tests`, passed / failed / skipped | | 65 / 0 / 0 |

The old feature files and their step class are removed in the same commit as the new scenarios.

## The form of the checks

- `the output should contain "<text>"` compared with the case. `the output contains "<text>"` does the same.
- `the output should contain "<a>" or "<b>"` ignored the case. `the output contains "<a>" or "<b>"` compares with the
  case, so the new check is not weaker.
- `the output should be valid JSON` parsed standard output. `standard output is one JSON document` also refuses text
  before or after the document.
- A check of a field of the JSON output is now a check of the report type of the command (`VerificationReport`,
  `GenerationReport`). `GenerationReport` also fails when `success` or `exitCode` disagrees with the exit code of the
  process, in each scenario that reads the report.
- A generation report must have each of its lists (`artifacts`, `written`, `unchanged`, `removed`, `diagnostics`), as
  the old tests required with `GetProperty`. A check that a list is empty does not pass for a report with no such
  list.
- `I remember the files of the output` fails when the output has no file, as the old snapshot failed for a
  directory that was not there.
- A scenario runs in one workspace. The old helper made a new sandbox for each command; a test that ran two commands
  shared a temporary folder of the test process for its files.

## `Integration/CLI/CliVerifyIRIntegration.feature` (13 scenarios)

New file: `tests/Morphir.BlackBox.Tests/Integration/ModelVerification.feature`, `@integration
@needs-model-verification`, on `IModelVerificationDriver`. Each scenario has the same exit code check and the same
text checks.

| Old scenario | New scenario |
| --- | --- |
| Verify valid IR v3 file returns success | A valid IR v3 file is valid |
| Verify valid IR v2 file returns success | A valid IR v2 file is valid |
| Verify valid IR v1 file returns success | A valid IR v1 file is valid |
| Verify invalid IR file returns failure | An IR file with no format version is not valid |
| Verify with explicit schema version | The schema version can be given |
| Verify with JSON output format | The result of a valid file as JSON. `IsValid` true and `SchemaVersion` "3" are checks of `VerificationReport`. |
| Verify with quiet mode for valid file | Quiet mode writes nothing for a valid file |
| Verify with quiet mode for invalid file | Quiet mode writes nothing for a file that is not valid |
| Verify shows enhanced error details for type mismatch | A value of the wrong type shows what was expected and what was found |
| Verify nonexistent file returns error | A file that is not there is an error |
| Verify malformed JSON returns error | A file that is not JSON is an error |
| Verify with auto-detected v3 format | The v3 format is detected |
| Verify JSON output for invalid file includes errors | The result of a file that is not valid as JSON has the errors |

## `Integration/CLI/CliManagementCommands.feature` (14 scenarios)

After the move, `main` (#608) changed the old feature: it removed the row of `extension install` and added the
scenario `The obsolete extension install URL arguments fail as usage`. The merge of `main` put the same change in
the new feature: 13 rows, and the scenario `The obsolete URL arguments of extension install fail as usage` with the
same two checks. The table below is the state at the move.

New file: `tests/Morphir.BlackBox.Tests/Integration/ManagementCommands.feature`, `@integration`, on `ICliDriver`.
One scenario outline with the same 14 rows and the same three checks (exit code 1, the "not implemented yet" text,
the issue link).

| Old rows | New rows |
| --- | --- |
| `dist`: list, install, use, remove, which | The same 5 rows |
| `tool`: list, install, use, remove, which | The same 5 rows |
| `extension`: install, use, remove, which | The same 4 rows |

## `Features/Generate/CSharpCliGenerationTests.cs` (33 cases in 6 methods)

New file: `tests/Morphir.BlackBox.Tests/Integration/Generation.feature`, `@integration @needs-generation`, on
`IGenerationDriver`.

| Old method | Cases | Result |
| --- | ---: | --- |
| `EveryAdmittedWireFormUsesTheRealProviderAndMatchesCoreBytes` | 16 | **Stays** in `Morphir.Tooling.Tests`: it compares the files of the CLI with `CSharpGenerator.Generate` in the test process, so it needs the generator assembly. **Also** 16 new scenarios check that each admitted wire form generates through the CLI (exit code 0, `success`, the backend `morphir.csharp`, the source version, and each artifact is a file): `Each admitted wire form generates through the CLI` (13 rows: JSON, Ion text, Ion binary) and `Each admitted tree form generates through the CLI` (3 rows). The inputs are the files of `Fixtures/WireForms/`. |
| `LastOptionsWinBeforeBackendValidationAndNamespaceEmission` | 1 | Moved: `The last option of a name wins before the backend checks the options`. |
| `InvalidOptionsAreInvocationReportsAndPreservePriorOutput` | 12 | Moved: `An option that is not valid is an invocation failure and changes no file`, with the same 12 options. |
| `SemanticFailuresUseJsonDataOrHumanStderr` | 2 | Moved: `A model that the backend cannot generate is a semantic failure in the JSON report`, and `... is a failure on standard error with no JSON report`. |
| `MissingInputIsAnOperationalJsonReport` | 1 | Moved: `An input that is not there is an operational failure`. |
| `RerunsKeepTimestampsAndRemoveOnlyStaleOwnedModules` | 1 | Moved: `A second run keeps the files that did not change and removes only its own stale files`. A rerun of one command is integration, not a journey. |

Moved cases: 17. Cases that stay: 16. New scenarios in `Generation.feature`: 33.

## New tests with no old test

| Test | Group | Why |
| --- | --- | --- |
| `Smoke/Version.feature`, 4 scenarios | smoke | Each host of the development build starts (task 2.2). |
| `Journeys/ModelToCSharp.feature`, 1 scenario | journey | Two capabilities for one goal: verify a model, then generate a C# library from it (task 4.3). The model is `tests/Fixtures/MorphirIR/daily-rate-v3.json`. |

## The `PlatformSpecific` marks (task 4.4)

| Place | Mark |
| --- | --- |
| The two feature files of `Integration/CLI/` | Removed with the files (`@PlatformSpecific` on each feature). |
| `CSharpCliGenerationTests` | Stays on the class: the 16 cases that stay start the CLI. |
| `Morphir.BlackBox.Tests` | No mark on a test. The assembly has the category. |

## Fixtures

| File | Handling |
| --- | --- |
| `TestData/invalid-wrong-type-formatversion.json` | Moved to `tests/Morphir.BlackBox.Tests/Fixtures/Models/`. No other test used it. |
| `TestData/valid-ir-v1.json`, `valid-ir-v2.json`, `valid-ir-v3.json`, `invalid-missing-formatversion.json`, `malformed.json` | Linked in the project file of the black-box project as `Fixtures/Models/<name>`. Unit tests of `Morphir.Tooling.Tests` and of other projects still read the files in `TestData/`, so the files stay there and have one source. |
| `Fixtures/WireForms/` | New. See its `README.md` for the source of each file. |

## Findings about the product (not in the scope of this change)

- `morphir ir verify examples/car-rental/models/car-rental-v3.json` (207 KB) runs for 2 minutes and is then killed
  by the operating system (exit code 137). So the journey does not use the car rental model.
- `morphir ir verify` says that `tests/Fixtures/MorphirIon/pairs/v3-daily-rate.json` is not valid, and
  `morphir generate` generates C# from the same file.
- `morphir ir verify` says that `tests/Fixtures/MorphirIR/daily-rate-v4.json` is not valid (the command knows the
  schema versions 1 to 3).
