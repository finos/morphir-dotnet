# Slice 2: the tests of `Morphir.E2E.Tests` and their new places (tasks 6.1, 6.4, 6.6 and 6.7)

Date: 2026-11-12. The rows are the tests that the runner discovers in `tests/Morphir.E2E.Tests`, from
`--list-tests` on the base of this pull request (`c041f454`): **158 tests**. They are 77 scenarios of the 11 feature
files and 81 cases of the 58 framework test methods in 13 classes. Each one has a row below. The TRX report of a
run with `MORPHIR_EXECUTABLE_TYPE=tool` and the filter of `scripts/run-e2e-tests.cs` gives the class of each of the
126 tests that it runs; the other 32 (20 `@manual-only`, 12 `@packaged`) come from the list.

The classification follows the steps of the `test-manager` skill, by purpose and not by project: the form of the
product is the target of a run, not the group.

| Feature file or class | Discovered | Destination |
| --- | ---: | --- |
| `Features/ExecutableBasicCommands.feature` | 3 | Smoke (2 new scenarios), and one scenario that was there |
| `Features/ExecutableErrorHandling.feature` | 2 | Integration, scenarios that were there |
| `Features/ExecutableIRVerification.feature` | 8 | Integration, scenarios that were there, and 1 new scenario |
| `Features/ExecutableManagementCommands.feature` | 4 | Integration, rows that were there |
| `Features/ExecutableMckWasmHost.feature` | 4 | Removed (the command left the CLI) |
| `Features/ExecutableValidate.feature` | 8 | Integration, `Validation.feature` |
| `Features/ExecutableExtensionList.feature` | 5 | Integration, `ExtensionList.feature` |
| `Features/ExecutableExtensionWorker.feature` | 11 | Integration, `ExtensionWorker.feature` |
| `Features/ExecutableExtensionRuntimesTests` | 7 | Integration, `ExtensionRuntimes.feature` (Gherkin: not one of the three forms of FORM-1) |
| `Features/ExecutableExtensionWorkerTests` | 23 | Integration, framework test `ExtensionWorkerCorpusTests` |
| `Features/ExecutableGuestKitModelTierTests` | 1 | Conformance, `Morphir.Extensions.Wasm.Tests` |
| `Features/Packaged/PackagedArtifacts.feature` | 12 | End-to-end, published lane, `EndToEnd/PackagedArtifacts.feature` |
| `Features/AOT/NativeAOTCompilation.feature` | 10 | End-to-end, published lane (2 new tests), groups on the target `aot`, or removed |
| `Features/AOT/AssemblyTrimming.feature` | 10 | Removed, or groups on the targets `trimmed` and `untrimmed` |
| `Infrastructure/ExecutableRunnerTests` | 9 | Removed with the helper (8), smoke scenario that was there (1) |
| `InfrastructureTests/*` (9 classes) | 41 | `Morphir.TestSupport.Tests/Packaging/` with their helpers (40), journey framework test (1) |
| **Total** | **158** | |

## Counts before and after

The "before" runs use the build of the old project from the base commit, on this machine (macOS arm64), with the
filter of the old script for each type. The "after" runs use `Morphir.BlackBox.Tests` at the head of this pull request
with `MORPHIR_TEST_LANE=published` and the target of the type, on the same artifacts. 48 tests of each old run do not
test the target: 39 of the 41 `InfrastructureTests` (the Windows test and the opt-in journey are skipped), the 8 log
filter tests of `ExecutableRunnerTests`, and the sample check of the corpus. They run once in
`Morphir.TestSupport.Tests` now, or they are removed with their helper. So the column "product tests" (the passed
tests less these 48) is the count to compare with the passed tests after.

| Type | Before: passed / failed / skipped | Before: product tests passed | After: passed / failed / skipped | Skips after, by reason |
| --- | --- | ---: | --- | --- |
| `tool` (`dev`, tool host) | 118 / 4 / 4 | 70 | 120 / 0 / 16 | capability 14, Windows only 1, opt-in 1 |
| `aot` | 112 / 4 / 10 | 64 | 111 / 0 / 25 | host 2, capability 15, no engine library file 6, Windows only 1, opt-in 1 |
| `trimmed` | 118 / 4 / 4 | 70 | 115 / 0 / 21 | host 2, capability 17, Windows only 1, opt-in 1 |
| `untrimmed` | 118 / 4 / 4 | 70 | 115 / 0 / 21 | host 2, capability 17, Windows only 1, opt-in 1 |
| `native-aot` | 121 / 7 / 10 | 73 | 120 / 3 / 13 | host 2, capability 3, no engine library file 6, Windows only 1, opt-in 1 |
| `self-contained` | 130 / 4 / 4 | 82 | 127 / 0 / 9 | host 2, capability 5, Windows only 1, opt-in 1 |
| `framework-dependent` | 130 / 4 / 4 | 82 | 127 / 0 / 9 | host 2, capability 5, Windows only 1, opt-in 1 |
| `dotnet-tool` | 130 / 4 / 4 | 82 | 127 / 0 / 9 | host 2, capability 5, Windows only 1, opt-in 1 |
| `executable` (before: the native development build at a path; after: the trimmed executable, native host) | 117 / 5 / 4 | 69 | 114 / 0 / 22 | host 2, capability 18, Windows only 1, opt-in 1 |

The development build in the pull request run (no lane): 120 passed, 0 failed, 16 skipped (the 14 tests of the
published lane by lane, the Windows test and the opt-in journey).

- The 4 failures of each type before are the four `mck wasm-host` scenarios (see below).
- The 3 failures of `native-aot` before and after are the same: `ExecutableStructure.Of` finds the bundle signature
  in the Native AOT `morphir` of macOS, so "The installed layout is what the offering claims", "No installed .NET
  runtime" and "The declared runtime" fail on macOS. The helper says that it is verified only on PE files. This is a
  finding about the helper, not about this change.
- A skip by host is a smoke scenario with `@host-managed` or `@host-tool` on a target with one host. A skip by
  capability is a test of another target (the packaged scenarios, the Native AOT tests), or a test that compares two
  hosts on a target with one host (before, these ran the other host from a build folder; a target never uses
  the artifact of another form now), or the three scenarios of a copy with no engine library on a host that carries
  no engine library file (these were skipped before too, from the test).

## `Features/ExecutableBasicCommands.feature` (3 scenarios)

| Old scenario | New test |
| --- | --- |
| Executable shows help information | `Smoke/BasicCommands.feature`: The help lists the commands. The same three checks. |
| Executable shows version information | `Smoke/Version.feature`: The default host of the run gives its version (pull request 1). The step "standard output is a version" now uses the complete SemVer 2.0 pattern of the old step; it reads standard output, where the old step read both streams. |
| Executable info command works | `Smoke/BasicCommands.feature`: The info command exits with 0. |

## `Features/ExecutableErrorHandling.feature` (2 scenarios)

| Old scenario | New test |
| --- | --- |
| Verify nonexistent file returns error | `Integration/ModelVerification.feature`: A file that is not there is an error (pull request 1). The file is in the work area, which exists, as in the old scenario. The text check compares with the case. |
| Verify malformed JSON returns error | `Integration/ModelVerification.feature`: A file that is not JSON is an error (pull request 1). |

## `Features/ExecutableIRVerification.feature` (8 scenarios)

| Old scenario | New test |
| --- | --- |
| Verify valid IR file returns success (versions 1, 2, 3) | A valid IR v1 / v2 / v3 file is valid (pull request 1), the same checks. |
| Verify invalid IR file returns failure | An IR file with no format version is not valid (pull request 1). |
| Verify with JSON output format | The result of a valid file as JSON (pull request 1). `IsValid` is a check of `VerificationReport`. |
| Verify with quiet mode (`@Ignore`) | Quiet mode writes nothing for a valid file (pull request 1). It runs now, and it passes on each target. |
| Verify with explicit schema version and JSON output | New: The schema version can be given with the result as JSON. `IsValid` true, `SchemaVersion` "3" and `DetectionMethod` "manual" are checks of `VerificationReport`. |
| Verify JSON output for invalid file includes errors | The result of a file that is not valid as JSON has the errors (pull request 1). |

## `Features/ExecutableManagementCommands.feature` (4 rows)

The rows `dist list`, `dist install https://example.com/d.tar.gz 1.0.0`, `tool use t 1.0.0` and `extension which e` are
rows of `Integration/ManagementCommands.feature` (pull request 1). Its check of the text is stronger: it names the
group and the subcommand, and the complete URL of the issue.

## `Features/ExecutableMckWasmHost.feature` (4 scenarios): removed

The four scenarios fail on `main` on each type, with exit code 127: since `7e9ebdeb` ("route morphir mck to the
separately installed morphir-mck", 2026-11-10), `morphir mck` starts the external `morphir-mck` of finos/morphir, so
`mck wasm-host run` is no longer a command of this CLI. The tests of that routing are
`tests/Morphir.Tests/Hosts/MckRoutingTests.cs` and `MckRoutingHarnessTests.cs` (pull request 3). No pull request ran
these four scenarios, so no one saw that they failed.

## `Features/ExecutableValidate.feature` (8 scenarios)

New file: `Integration/Validation.feature`, `@integration @needs-validation`, on `IValidationDriver` and
`ValidationReport`. The models are the files of `tests/Fixtures/MorphirIR`, linked as fixtures.

| Old scenario | New scenario |
| --- | --- |
| A classic model with a type mismatch gives one type-mismatch error | The same name and the same checks. |
| A clean classic model gives exit code 0 | The same name and the same checks. |
| A V4 model gives one info diagnostic that names the family | The same name and the same checks. |
| A missing input gives exit code 2 and a diagnostic that names the path | The same name and the same checks, with the text output as before (no `--json`). The path is absolute in the work area. |
| The text output is a table and a summary line | The same name and the same checks. |
| The validator entries of extension list are the analyzers of validate | The same name and the same checks. Integration: `extension list` is the source of the expected value, not a second goal of the user. |
| Both hosts write the same report apart from the host (2 rows) | The same outline, `@needs-native-and-tool-hosts`. Before, it ran the other host from a build folder on each type; now it runs on the development build, which has both hosts, in each pull request. |

## `Features/ExecutableExtensionList.feature` (5 scenarios)

New file: `Integration/ExtensionList.feature`, `@integration`, on `ExtensionListReport`. The extension commands get
their driver interface with the other command families in pull request 3 (task 7.2), so the steps give the arguments
of the report type to `ICliDriver`.

| Old scenario | New scenario |
| --- | --- |
| The built-in generators and analyzers are listed as one JSON document on stdout | The same checks. The expected host is the host of the run (`morphir` or `dotnet-morphir`), and the in-process F# frontend is expected exactly when the drivers of the target and the host have the capability `InProcessFSharpFrontend`. |
| The list names the deployment that built the executable | `@needs-known-form`. The expected deployment and execution come from the target (`ExtensionListReport.ExpectedDeploymentOf`), as before from the executable type. An executable at a path has no known form, so the scenario is skipped there; before, the type of such a run was `aot` by default. |
| The type validator is a built-in validator for the classic IR versions | The same checks. |
| The text table shows the validator claim | The same check, on standard output, with the step "I list the extensions as text". |
| Both hosts list the same shared built-ins | `@needs-native-and-tool-hosts`, as for validate. |

## `Features/ExecutableExtensionWorker.feature` (11 scenarios)

New file: `Integration/ExtensionWorker.feature`, `@integration`, on `IProcessSessionDriver`. The corpus modules are
the files of `tests/Fixtures/MepWasm`, linked as fixtures. The two worker guests moved from
`tests/Morphir.E2E.Tests/TestData/Wasm` to `tests/Fixtures/WorkerGuests` (with a README); `Morphir.Tooling.Tests`
links them from there. Each scenario has the same checks. The worker gets the temporary directory of the workspace
(`TMPDIR`, `TEMP`, `TMP`), as before it got a temporary directory of its sandbox.

| Old scenario | New scenario |
| --- | --- |
| The help as JSON gives the build identity and says the worker is internal and experimental | The same, with "that". |
| A guest that prints answers with frames and nothing else on stdout | The same, with "standard output". |
| A guest that logs answers with frames and nothing else on stdout | The same, with "standard output". |
| An echo guest answers each request with one frame and each notification with none | The same. |
| A refused module gives one refused line, no stdout bytes and status 3 | The same, with "no bytes on standard output". |
| A stale build identity is refused before the module is read | The same. |
| A file that is not a WASM module is a fault before started | The same. |
| A module file that does not exist is a fault before started | A module file that is not there is a fault before started. |
| An argument the worker does not know is a usage error with no stdout bytes | An argument that the worker does not know is a usage error with no bytes on standard output. |
| A guest that writes 20 MiB per call keeps stdout to frames and its report bounded | A guest that writes 20 MiB for each call keeps standard output to frames and its report bounded. |
| Ctrl+C stops a spinning guest within one second with status 130 and no fault | An interrupt stops a spinning guest within one second with status 130 and no fault. The interrupt is SIGINT, or Ctrl+C on Windows, as before. The time is from the interrupt to the moment that the worker closes its output; before, it was to the exit time that the operating system recorded, which is a little earlier. |

## `Features/ExecutableExtensionRuntimesTests` (4 methods, 7 cases)

New file: `Integration/ExtensionRuntimes.feature`, `@integration`. These are not a corpus loop, a property test or a
test with a time budget, so they are Gherkin (FORM-1). The copy of the host with no engine library is made once for
each run (`HostCopies`), below a temporary folder of the run, as before once for each class. The pinned version of
Wasmtime comes from `Directory.Packages.props`, linked as a fixture.

| Old case | New scenario |
| --- | --- |
| `The_executable_reports_its_platforms_wasm_availability_with_the_engine_of_its_package` | The host reports the WASM availability of its platform with the engine of its package. The same checks. |
| `Without_its_engine_library_the_executable_reports_the_applicable_unavailable_reason` | Without its engine library the host reports the applicable unavailable reason. The same checks, the host name included. |
| `Without_its_engine_library_a_worker_is_refused_by_name_with_no_stdout_bytes` | Without its engine library a worker is refused by name with no bytes on standard output. The same checks, with the worker steps of `ExtensionWorker.feature`. |
| `Without_its_engine_library_the_commands_that_need_no_wasm_still_work` (4 arguments) | The outline with the same 4 commands and the same checks. The model of `ir verify` is a fixture copy in the workspace. |

A host that carries no engine library file skips the three scenarios of the copy with the same reason as before.

## `Features/ExecutableExtensionWorkerTests` (3 methods, 23 cases)

New class: `Integration/ExtensionWorkerCorpusTests.cs`, a framework test (a corpus loop, and a test with a time
budget), `[Category("integration")]`, on `IProcessSessionDriver` and `WorkerCorpus` of the testing library. The
integration group has tests with a time budget now, so its suite runs alone (`Alone()` in `build/Helpers/TestPlan.cs`).

| Old case | New test |
| --- | --- |
| `The_sample_has_every_worker_case_and_every_group` | The same name and the same checks, in `Morphir.TestSupport.Tests/Drivers/WorkerCorpusTests.cs`: it reaches no product, so it is a unit test of the sample, which is `WorkerCorpus.Sample` of the testing library now. |
| `A_corpus_case_gives_its_expected_observation_through_the_worker` (21 cases of the sample) | The same name, the same 21 cases and the same checks. `MORPHIR_WORKER_CORPUS=all` still runs each case of the corpus. |
| `Ctrl_Break_stops_a_spinning_guest_within_one_second_with_130_and_no_fault` (`RunOn(Windows)`) | The same name, the same checks, and `RunOn(Windows)`. The interrupt is Ctrl+Break through `IProcessSession.InterruptAsync`. |

## `Features/ExecutableGuestKitModelTierTests` (1 case)

The test judges the run with `ModelTierRunner` of the kit library in the test process, so it is a conformance test
(step 1 of the classification) and cannot be in the black-box project. New test:
`tests/Morphir.Extensions.Wasm.Tests/WorkerGuestKitModelTierTests.cs`, with the same checks. That project has the
kit and the pin of the published Python guest (`PublishedGuest`) already. It gets its executable from the target of
the testing library (`TargetCli`), with the native host of the development build as its default, and imports
`Morphir.CliArtifacts.targets`. It passes on `dev` and on `aot`.

## `Features/Packaged/PackagedArtifacts.feature` (12 scenarios)

New file: `EndToEnd/PackagedArtifacts.feature`, `@e2e @lane @published @needs-packaged-installation`, on
`IPackagedArtifactDriver`. It proves the delivered form itself (installation, layout, start), so it is end-to-end; it
needs a packaged offering, so it is in the published lane. The run installs the offering once before the first test
(`PackagedTarget`), as before once for each type. A run from a new empty directory is a new directory below the work
area of the scenario, with a space in its name, and the process is contained in the workspace. The checks of the old
steps are in the driver now (`LayoutProblems`, `EngineProblems`, `RunsWithoutARuntimeAsync`,
`UsesTheDeclaredRuntimeAsync`), so the steps start no process and read no file. The checks of the worker session use
the worker steps of `ExtensionWorker.feature` (the shared `WorkerRun`).

| Old scenario | New scenario |
| --- | --- |
| The installed artifact is the one its launch metadata names | The installed artifact is the one that its launch metadata names. The same checks. |
| The installed layout is what the offering claims | The same name and the same checks. |
| Startup from a clean directory | The same. |
| Core commands | The same. |
| An F# compile follows the artifact's frontend | An F# compile follows the frontend of the artifact. The same checks. |
| Reading an input file | The same. The model is the fixture `Models/daily-rate-v3.json`, copied to a folder whose path has spaces. |
| No installed .NET runtime | The same, with the same skip where the runtime cannot be hidden. |
| The declared runtime | The installed artifact uses the runtime that its launch metadata declares, with the .NET runtime of the machine reachable. The same checks. |
| The engine files are those the build contents record | The same checks. |
| The runtime report of the installed artifact | The same checks. |
| A WASM worker session from the installed artifact | The same checks. The temporary directory of the session is a folder of the workspace whose path has a space, as before. |
| A WASM guest runs from a read-only install directory, and its first run extracts nothing | The same checks. The second installation is in the workspace; the end of the scenario gives back its permissions before it removes the workspace. |

## `Features/AOT/NativeAOTCompilation.feature` (10 scenarios, `@manual-only`)

Each scenario published `src/Morphir` itself, with its own MSBuild properties, in the test. The published targets
are made by the publish targets of the release (`PublishExecutable`, `PackRelease`) now, and the tests run against
them. A check of the delivered executable moved; a check that only a publish with other settings could make is
removed with its reason.

| Old scenario | New test, or the reason that it is removed |
| --- | --- |
| Successful AOT compilation | The checks of the executable: `EndToEnd/NativeAotExecutable.feature` (`@e2e @lane @published @needs-native-aot-executable`): no `morphir.deps.json` and no `morphir.runtimeconfig.json` beside the executable. The check that the publish exits with 0 is the publish step of the workflow. Removed, and an open point: the check that the publish output has no `IL2026`, `IL2060` or `IL2070`. The release publish fails on `IL3050` only (`src/Morphir/Morphir.csproj`), so no check now refuses those three warnings for the CLI. Slice 5 checks the trim warnings of the library hosts and the probes, not of the CLI; a check of the CLI publish needs an owner. |
| AOT with size optimizations | Removed. Its limits (12 MB on linux-x64, 15 MB on win-x64) are for a publish with `IlcOptimizationPreference=Size` that no release makes. The Native AOT `morphir` of the release is 37 MB on osx-arm64 (it links the WASM engine). `artifacts/release/size-report.md` records the size of each artifact. |
| AOT executable runs correctly | `Smoke/Version.feature` on the target `aot`. |
| All CLI commands work in AOT | `Smoke/BasicCommands.feature` (`--help`) and `Integration/ModelVerification.feature` on the target `aot`. The old scenario looked for its model in `tests/TestData/IR`, which does not exist, so it failed with `DirectoryNotFoundException`. |
| JSON output works in AOT | `Integration/ModelVerification.feature` ("The result of a valid file as JSON") on the target `aot`. The old scenario failed in the same way. |
| Detecting reflection usage during build | Removed: its only check was that the publish output is not null. |
| Size target for minimal CLI | Removed: a publish with `IlcDisableReflection=true` that no release makes, and a limit of 5 to 8 MB. |
| Size target for feature-rich CLI | Removed: a limit of 8 to 12 MB, which the executable of the release is not near (37 MB on osx-arm64). |
| Cross-platform AOT builds | Removed: Native AOT does not compile for another operating system, so the scenario could pass on no machine. The release workflow publishes each platform on a runner of that platform. |
| AOT build performance | The start time: `EndToEnd/NativeAotStartupTests.cs`, a framework test with a time budget (`--version` in less than 100 ms), `@needs-native-aot-executable`, published lane. Removed: "memory usage should be less than 50MB", whose step checked only the exit code. |

## `Features/AOT/AssemblyTrimming.feature` (10 scenarios, `@manual-only`)

Each scenario published `src/Morphir` itself with trimming and other properties. Their checks were the exit code of
the publish and the presence of a file, except two size comparisons. The release workflows publish the trimmed and
the untrimmed executables with `PublishSingleFile` and `PublishSingleFileUntrimmed`, and each group runs on them
(`manual-aot-test.yml`, suite `trimming`).

| Old scenario | New test, or the reason that it is removed |
| --- | --- |
| Trimming with link mode | Removed. Its checks: the publish exits with 0 (the publish step of the workflow), the output folder has a file, and the executable exists (the target `trimmed` stops the run with the publish command when it does not). The size comparison of this scenario ran only with a baseline, and this scenario made no baseline. |
| Preserving types with DynamicDependency | Removed: its two checks were the exit code of the publish. |
| Trimming warnings detection | Removed: its checks were that the publish output is not null. |
| JSON serialization preservation | Removed: its check was the exit code of the publish. JSON output on the trimmed executable: each `--json` scenario of the groups on the target `trimmed`. |
| Embedded resources in trimmed build | Removed: its check was the exit code of the publish. The schemas are read by `ir verify` in `Integration/ModelVerification.feature` on the target `trimmed`. |
| Trimmed build size comparison | Removed: the check that the trimmed executable is at least 50% smaller than the untrimmed one compares two forms in one test, and a target uses only its own form. The size of each form of a release is in `artifacts/release/size-report.md`. This is an open point for the owner: a check of the size of a form can be an end-to-end test with a limit for that form. |
| Trimming with third-party dependencies | Removed: its check was the exit code of the publish. The groups on the target `trimmed` run the commands. |
| Feature switches for size reduction | Removed: its check was that the executable is smaller than 100 MB. |
| Trimmer root descriptors | Removed: its checks were that `src/Morphir/TrimmerRootDescriptor.xml` exists and the exit code of the publish. |
| Invariant globalization size savings | Removed: its checks were that the executable exists. |

## `Infrastructure/ExecutableRunnerTests` (9 cases)

| Old case | New test, or the reason that it is removed |
| --- | --- |
| `IsInfrastructureLogMessage_ShouldFilter_WolverineFx58Patterns` | Removed with the log filter of `ExecutableRunner`, which has no successor. |
| `IsInfrastructureLogMessage_ShouldFilter_WolverineFx59Patterns` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldNotFilter_ActualCommandOutput` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldNotFilter_ErrorMessages` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldNotFilter_JsonOutputErrorsField` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldFilter_InfoPrefixedLogs` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldFilter_SerilogFormattedLogs` | Removed, as above. |
| `IsInfrastructureLogMessage_ShouldNotFilter_NonSerilogBracketedContent` | Removed, as above. |
| `ToolTypeRunsTheBuiltDotnetMorphir` | `Smoke/Version.feature`: The tool host gives its version (pull request 1): `dotnet-morphir` of the development build exits with 0 and writes a version. |

The CLI driver removes no line of the output: `CommandResult` has both streams as the process wrote them, and
`ShouldWriteOneJsonDocument` refuses a log line on standard output. So the filter, which hid log lines of the host,
has no successor, and the checks of the black-box tests are stronger without it. Design FORM-1 said that the eight
tests go to `Morphir.TestSupport.Tests` with the driver that replaces the helper; task 6.1 permits the removal with a
helper that has no successor, and the implementation record of `design.html` states it.

## `InfrastructureTests/` (9 classes, 41 cases)

The helpers moved from `tests/Morphir.E2E.Tests/Infrastructure` to `tests/Morphir.TestSupport/Packaging`
(`CanonicalPath`, `ExecutableStructure`, `HostTrace`, `PackagedArtifact`, `PackagedEngineLayout`,
`PackagedInstallation`, `PackagedRun`, `ReadOnlyTree`), where `PackagedTarget` and `PackagedArtifactDriver` use
them. Their tests moved with them to `tests/Morphir.TestSupport.Tests/Packaging`, with the same names and checks.

| Old class | Cases | New place |
| --- | ---: | --- |
| `CanonicalPathTests` | 3 | `Packaging/CanonicalPathTests` (`AWindowsShortNameIsTheLongName` runs on Windows only, as before) |
| `ExecutableStructureTests` | 2 | `Packaging/ExecutableStructureTests`. The control apphost is the apphost of `Morphir.TestSupport.Tests`. |
| `HostTraceTests` | 7 | `Packaging/HostTraceTests`, with the trace fixtures in `Packaging/Fixtures` |
| `PackagedArtifactTests` | 4 | `Packaging/PackagedArtifactTests` |
| `PackagedEngineLayoutTests` | 11 | `Packaging/PackagedEngineLayoutTests` |
| `PackagedInstallationTests` | 3 | `Packaging/PackagedInstallationTests`. `PackagedInstallation` installs into a directory that the caller gives; the static cache of the old helper and its Reqnroll clean-up hook are gone, because the session installs once and removes the folder at the end of the run. |
| `PackagedRunTests` | 8 | `Packaging/PackagedRunTests`. `PackagedRun.RunAsync` runs in a folder that the caller contains (the workspace). |
| `ReadOnlyTreeTests` | 2 | `Packaging/ReadOnlyTreeTests` |
| `LocalDeveloperToolchainTests` | 1 | `Journeys/LocalCandidateJourneyTests` (`@journey @lane @published`, category `LocalCandidate`): a journey (morphirup installs, selects, compiles and generates) with a time budget, so a framework test. Its explicit inputs (`MORPHIR_LOCAL_*`) and its file work are in the driver `LocalCandidateJourney` of the testing library, with the same checks. It is skipped with the same two reasons when the inputs are not set or the machine is not Windows. It was not run here: it needs a candidate and a Windows x64 machine. |

## `CliAdapterTests.fs` of `Morphir.Frontend.FSharp.Tests` (task 6.6)

The test stays a conformance test (it runs the kit against an executable). It gets its executables from the target
of the testing library (`TargetCli`), not from `MORPHIR_DOTNET_TOOL_CLI` and `MORPHIR_AOT_CLI`, and the project
imports `Morphir.CliArtifacts.targets`. The default is the development build with the tool host. A target and a host
with no in-process F# frontend skip the two `dotnet-morphir` cases with the capability as the reason. The AOT case
runs the native host of a published target of the run (before: `MORPHIR_AOT_CLI`) when `MORPHIR_FSHARP_GUEST` is set;
the development build skips it.

| Run | Before | After |
| --- | --- | --- |
| Development build | 5 passed, 1 ignored (the AOT case, no `MORPHIR_AOT_CLI`) | 5 passed, 1 ignored (the AOT case, no `MORPHIR_FSHARP_GUEST`) |
| `MORPHIR_TEST_TARGET=executable` with the app host of the built `dotnet-morphir` and `MORPHIR_TEST_HOST=tool` | | 5 passed, 1 ignored |
| `MORPHIR_TEST_TARGET=executable` with the trimmed `morphir` and `MORPHIR_TEST_HOST=native` | | 3 passed, 3 ignored (the two `dotnet-morphir` cases by capability, and the AOT case) |

## The `PlatformSpecific` marks

The old project had no mark; the black-box project has the assembly-level category. `WorkerGuestKitModelTierTests`
is in `Morphir.Extensions.Wasm.Tests`, whose assembly-level category marks each test.

## Findings about the product (not in the scope of this change)

No issue tracks these findings yet; this pull request does not open one.

- On the Native AOT `morphir` (the targets `aot` and `native-aot`), `ir verify` of a file in a directory that does
  not exist ends with an unhandled `System.IO.DirectoryNotFoundException` and exit code 1, with the stack trace on
  standard error. The development build reports the error. The scenario of pull request 1 used such a path
  (`Models/nonexistent.json`); it uses a file that is not there in the work area now, as the old scenarios did.
- `ExecutableStructure.Of` gives `Other`, not `NativeAot`, for the Native AOT `morphir` of macOS (see "Counts").

## Files that still name the removed project

A search finds `tests/Morphir.E2E.Tests` only in the records of OpenSpec changes (this change, the other changes in
`openspec/changes/` and the archive) and in `.claude/skills/aot-guru/IMPLEMENTATION.md`, which says that the project
was removed. Those are records of what was there; they are not changed.
