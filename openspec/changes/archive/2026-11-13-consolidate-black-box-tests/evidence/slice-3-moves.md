# Slice 3: the CLI tests of `Morphir.Tooling.Tests` and the host tests (task group 7)

Date: 2026-11-12. The rows are the tests that the runner discovers, from TRX reports of the complete test assemblies
before and after the move, at the base commit of this pull request (`c041f454`, the head of pull request 1):

```bash
dotnet exec tests/<P>/bin/Release/net11.0/<P>.dll --report-trx --report-trx-filename <P>-before.trx --results-directory <folder>
```

The scope is the 30 files of `evidence/inventory.md` for pull request 3 and
`Features/Compile/InstalledProviderJourneyTests.cs`: 470 discovered tests in 37 classes.

## Counts

| | Before | After |
| --- | ---: | ---: |
| `Morphir.Tooling.Tests`, tests | 1,255 | 1,023 |
| `Morphir.Tooling.Tests`, passed / failed / skipped | 1,250 / 0 / 5 | 1,018 / 0 / 5 |
| `Morphir.Tests`, passed / failed / skipped | 60 / 0 / 2 | 40 / 0 / 2 |
| `Morphir.Managed.Tests`, passed / failed / skipped | 13 / 0 / 1 | 8 / 0 / 1 |
| `Morphir.BlackBox.Tests`, tests | 65 | 337: integration 330, smoke 4, journey 3 |
| `Morphir.BlackBox.Tests`, passed / failed / skipped | 65 / 0 / 0 | 337 / 0 / 0 |

Of the 470 tests in scope: 257 moved, 213 stay. The moved tests are 232 of `Morphir.Tooling.Tests`, 20 of
`Morphir.Tests` (18 routing cases and the 2 version cases) and 5 of `Morphir.Managed.Tests`. The 233 tests that the
report of `Morphir.Tooling.Tests` has no more include `GenerateCommandTests.HumanOutputReservesStdoutForArtifacts(True)`,
which stays with the name `HumanOutputReservesStdoutForArtifacts` (its other case moved). The black-box project has
272 new tests: the new tests of the 257 moved tests (some old tests are two scenarios) and the scenarios that a split adds for the
part of a test that stays.

The skipped tests are the same before and after: `InstalledProviderJourneyTests` (no inputs), two Windows tests of
`ConfiguredWasmCommandTests`, one of `WasmRuntimeProbeTests`, one of `MckToolResolutionTests`; the Windows kill tests of
`MckRoutingTests` (2) and of `ManagedMckRoutingTests` (1).

## The rules of the move

- **Moved:** a test that reaches the product only through the CLI. The old test is removed in the same commit as its
  new scenario, after the new scenario passed.
- **Split:** a test that runs the CLI and compares with a product function in the test process (the generator, the
  corpus runner `GenerationCases`, the configuration library) stays whole in its project, as the wire-form rows of
  `CSharpCliGenerationTests` did in pull request 1. A new scenario checks the part that needs only the CLI.
- **Stays:** a test that calls a product function in the test process and starts no product process. The scan of the
  inventory found it because it reads a fixture that is an artifact (a WASM module) or starts a helper process.

## The new drivers and capabilities (task 7.2)

| Interface | Command | Report | Capability (tag) |
| --- | --- | --- | --- |
| `ICompilationDriver` | `compile` | `CompilationReport` | `Compilation` (`@needs-compilation`) |
| `IConfigurationDriver` | `config` | `ConfigurationReport` | `Configuration` (`@needs-configuration`) |
| `IMigrationDriver` | `project migrate`, `workspace migrate` | `MigrationReport` | `Migration` (`@needs-migration`) |
| `IExtensionDriver` | `extension list`, `extension runtimes` | `ExtensionListReport`, `ExtensionRuntimesReport` | `Extensions` (`@needs-extensions`) |
| `IModelValidationDriver` | `validate` | `ValidationReport` | `ModelValidation` (`@needs-model-validation`) |
| `IToolRoutingDriver` | `mck`, with the stub `morphir-mck` | `StubReport` | `ToolRouting` (`@needs-tool-routing`) |

The reports derive from `JsonCommandReport`: one JSON object, `exitCode` agrees with the exit code of the process,
and the list `diagnostics`. The tests are in `tests/Morphir.TestSupport.Tests/Drivers/CommandFamilyDriverTests.cs`:
each command line with paths that have a space, and each report type against a real report.

Capabilities that only the development build has: `BuiltExtensions` (the F# backend guest and the stub `morphir-mck`,
from `tests/Morphir.TestSupport/build/Morphir.ExtensionArtifacts.targets`), `HostWithoutDynamicCode` (a host with the
dynamic-code switch of the runtime off, as in Native AOT), `AllHosts` (two hosts in one test, `DriverSet.CliOf`) and
`UnprofiledBuild` (a host that states no deployment).

## Compile (`Features/Compile`)

| Old class | Cases | Result |
| --- | ---: | --- |
| `Compile.feature` (`CompileFSourceToMorphirIRFeature`) | 25 | Moved. 24 scenarios to `Integration/Compilation.feature` with `@host-tool @needs-in-process-fsharp-frontend`, the same names and the same checks. The scenario of the AOT executable, which has no F# frontend, to `Integration/CompilationHosts.feature` with `@host-native`. "with the dotnet tool" left the step texts: the host is the tag. |
| `CompileTutorialTests` | 4 | Moved. `TheTutorialFilesEqualTheExample`, `TheQuickstartRuleEqualsTheExample` and `TheTutorialCommandsGiveTheDocumentedResults` are the journey `Journeys/FSharpPricingTutorial.feature` (compile, generate, validate: three capabilities). `TheGuardedDiscountIsRefusedAsDocumented` (compile only) is `Integration/FSharpPricingTutorial.feature`. The page, the quickstart and the example are links of the project file. |
| `CompileDocumentationTests` | 2 | Moved to `Integration/CompilationDocumentation.feature` (one capability): one scenario on the tool host and one on the native host. `ThePageDocumentsTheExpectedCommands` compared the host and the last word of each command; the new check compares each complete command line. **Changed source of the inputs:** the old test read the sources of the corpus cases `frontend-fsharp-0116` and `frontend-fsharp-0001` with the kit library (`Morphir.Conformance.Corpus.read`). The black-box project does not see the kit library (design PROJ-2), so the feature has the same three sources as doc strings. A change of those cases in `conformance/suites/frontend-fsharp` must change the feature too. |
| `InstalledProviderJourneyTests` | 1 | **Stays**, changed destination. It needs a product DLL, a runtime, provider archives, a frozen case and an evidence folder from five `MORPHIR_INSTALLED_PROVIDER_*` variables, and it is skipped in each CI run. No driver gives such inputs. Open in task 7.3. |

## Generate (`Features/Generate`)

| Old class | Cases | Result |
| --- | ---: | --- |
| `Generate.feature` (`GenerateExecutableLibrariesFromMorphirModelsFeature`) | 11 | 7 moved to `Integration/FSharpGeneration.feature`. **Split:** the 4 rows of "Nonempty models produce executable F#" run the generated F# against `GenerationCases` in the test process, so they stay; the new outline "A nonempty model generates F# with one JSON report on standard output" checks the report and the files for the same 4 inputs. |
| `GenerateExtension.feature` (`SelectAnExternalGenerationBackendFeature`) | 4 | 3 moved to `Integration/GenerationExtension.feature` (`@needs-built-extensions`). **Split:** "An explicit extension gives the built-in's files" compares with the built-in backend in the test process and stays; the new scenario "An explicit extension generates F#, and the report names the extension" checks the CLI part. The guest id `org.finos.morphir.fsharp-backend` is a text of the feature; the old test took it from `FSharpGuest.extension.Id`. |
| `CSharpGeneration.feature` (`GenerateCThroughTheExistingCommandFeature`) | 4 | Moved to `Integration/CSharpGenerationCommand.feature`, with the same checks. |
| `GenerateCommandTests` | 9 | 8 moved to `Integration/FSharpGeneration.feature`: the 6 invalid invocations (three outlines and one scenario), the text output of an unknown target, and the repeated options. **Split:** `HumanOutputReservesStdoutForArtifacts(True)` runs `GenerationCases` and stays as `HumanOutputReservesStdoutForArtifacts`; the scenario "Text output keeps standard output for the artifacts" checks that standard output is `Inventory.fs`. |
| `GenerationReportingTests` | 9 | 2 moved. `OperationalCliFailureReturnsTwoAndOneJsonDocument` is "An input that is not there is an operational failure of the F# backend". `OperationalCliFailureBudgetStartsAfterTheProcessSlotIsGranted` tested `CliTestHelper` with a gate: it is `CliDriverTimeLimitTests.TheTimeLimitStartsWhenTheCommandHasAPlaceOfTheGate` in `Morphir.TestSupport.Tests`, on the driver that replaces the helper. 7 stay: they call `GenerationReport.From` or run `MorphirCli` in the test process. |
| `GenerateFormattingTests` | 11 | 3 moved to `Integration/GenerationFormatting.feature` (`@needs-host-without-dynamic-code`). The new feature also has the CLI part of the two tests that stay. 8 stay: 6 in the test process; `WithFormatFalseThePublisherWritesTheFrozenBytes...` (the built-in in the test process, and the guest) and `WithFormatFalseAHostThatCannotFormatWritesTheSameBytesAsJit...` (compares with the generator in the test process) are splits. The guest part is "With format false the guest writes the frozen bytes of the toolchain case" in `GenerationExtension.feature`: it compares with the pinned `expected/generated/Pricing.fs`; the old test also compared the length and the SHA-256 with two constants of the same file. The tests that stay take the native host from the assembly metadata, not from a path below the repository root. |
| `GenerateDocumentationTests` | 2 | Moved to the journey `Journeys/TwoStepGeneration.feature` (compile, then generate: two capabilities). The two tests are one scenario: `TheSectionDocumentsCompileThenGenerate` is its check of the documented commands, which now compares each complete command line. **Changed source of the inputs:** as for `CompileDocumentationTests`, the sources of the corpus case `frontend-fsharp-0116` are doc strings of the feature, not a read of the kit library. |
| `GenerationWireConformanceTests` | 4 | Stay: they run the corpus in the test process. With `MORPHIR_TEST_TARGET=executable` they run the matrix through that CLI (task 7.5). |

## Validate, extensions and mck (`Features/Validate`, `Features/Extensions`, `Features/Mck`)

| Old class | Cases | Result |
| --- | ---: | --- |
| `ValidateCliTests` | 8 | Moved to `Integration/Validation.feature`. `BothHostsWriteTheSameReportApartFromTheHost` runs both hosts in one scenario (`@needs-all-hosts`). |
| `ValidateDocumentationTests` | 3 | 2 moved to `Integration/ValidationDocumentation.feature`. `TheConfigurationExampleTurnsOffTheTypeValidator...` runs `ModelValidation` in the test process and stays. |
| `ExtensionListCliTests` | 4 | Moved to `Integration/Extensions.feature`. The check that a development build states no deployment needs `@needs-unprofiled-build`. |
| `ExtensionRuntimesReportTests` | 15 | 2 moved to `Integration/Extensions.feature`. The platform is the platform of this machine (`ExtensionPlatform.OfThisMachine`); the old test took it from `WasmRuntime.Process.Platform`. The engine version `50.0.1` is a text of the feature; the old test took it from `WasmRuntimeIdentity.EngineVersion`, and its text test already had the same text. 13 stay: in the test process, with no OS use, so the class has no `PlatformSpecific` mark now. |
| `ConfiguredWasmCommandTests` | 14 | **Stay**, changed destination. 6 run in the test process. The 8 CLI cases need the pinned published Python guest, which `PublishedGuest` of `Morphir.Extensions.Wasm.Tests` downloads and caches, or a WASM module of the kit corpus with its digest. No driver gives a pinned guest module. They read the target of the testing library now. Open in task 7.3. |
| `ExtensionWorkerArgumentsTests`, `ExtensionWorkerRuntimeTests`, `ExtensionWorkerTests`, `WasmRuntimeAgreementTests`, `WorkerBuildIdentityTests` | 116 | **Stay**, changed destination: they run `ExtensionWorker.RunAsync`, the module loader or the registry in the test process with the fixture modules. No product process starts. |
| `WasmHostCommandTests` | 13 | 2 moved to `Integration/ToolRouting.feature`, "The experimental wasm-host commands go to the separate runner": 2 rows for each of the two hosts (one for each command; the old test ran both commands in one case). 11 stay: they run `MckCommandProvider` in the test process. |

## Configuration and migrations (`Features/Configuration`, `Features/ProjectMigration`, `Features/WorkspaceMigration`)

Each moved scenario has the same name, the same steps and the same checks (a check that no source is outside the
boundary now also requires one project source, as `OnlyContain` of the old steps did), in `Integration/` with
`@integration @needs-configuration` or `@needs-migration`. The paths of the reports are compared in the form of
`ReportPath`, which is the form of `ProjectPath` of the product. The reports must match the published schemas, which
are links of the project file (`Fixtures/Schemas`).

| Old feature | Cases | Result |
| --- | ---: | --- |
| `Configuration.feature` | 48 | Moved to `ConfigurationInspection.feature`. |
| `ConfigurationContainment.feature` | 10 | Moved to `ConfigurationContainment.feature`. |
| `MorphirHomeDiscovery.feature` | 2 | Moved to `MorphirHomeDiscovery.feature`. |
| `ConfigurationEditing.feature` | 36 | 27 moved to `ConfigurationEditing.feature`. **Split:** 9 stay: "Explicit personal edits retain another member's ownership" (it loads the project system of the product), the 6 rows of "Explicit input typing is retained in TOML", "Complete typed objects reach the reusable editor" and "Literal dotted keys use the shared key grammar" (they read the edited file with the configuration reader of the product). The black-box feature has the same 9: the type check reads the value back with `config get` and the TOML text; the ownership check reads the report and the file, not the project system. |
| `ConfigurationEditingContainment.feature` | 2 | Moved to `ConfigurationEditingContainment.feature`. |
| `ProjectMigration.feature` | 31 | 28 moved to `ProjectMigration.feature`. **Split:** the 3 rows of "Supported primary outputs retain the project source directory" read the written file with the configuration reader and stay; the black-box feature has the same 3 rows, which read the file back with `config get`. |
| `ProjectMigrationContainment.feature` | 2 | Moved. |
| `WorkspaceMigration.feature` | 34 | 32 moved. 2 stay: "A pending durable transaction blocks a workspace preview" and "A process interrupted after directory creation reports uncertain ownership" make and recover a configuration transaction with the product library. |
| `WorkspaceMigrationContainment.feature` | 3 | Moved. |

The machine of these scenarios: the step classes give the product a working directory, a home and the Morphir
variables of the scenario through `ScenarioMachine`, as `DevelopmentCli` did; the workspace removes
`MORPHIR_HOME`, `MORPHIR_DISCOVERY_BOUNDARY` and `MORPHIR_ISOLATED`. The old "outside" folder was a sibling of the
temporary folder; the new one is `outside` in the temporary directory of the workspace, which is outside the work
area. The run of the black-box project ends with the `MachineTripwire` check of the session, so a scenario that
changes the developer's Morphir locations fails the run (task 7.4).

## Hosts (`Morphir.Tests/Hosts`, `Morphir.Managed.Tests`)

| Old test | Cases | Result |
| --- | ---: | --- |
| `HostVersionTests.VersionOutputParsesAsAReleaseVersion` | 2 | Moved to `Smoke/Version.feature` (native and tool host). The step "standard output is a version" now checks the grammar of SemVer 2.0.0; the old test parsed with `ReleaseVersion.TryParse` of the product and compared the text. `TheVersionsThatTheBuildWritesParse` (2) stays. |
| `ManagedEntryPointTests.VersionOutputParsesAsAReleaseVersion` | 1 | Moved to `Smoke/Version.feature` (`@host-managed`). |
| `ManagedEntryPointTests.TheManagedExecutableCompilesFSharpWithTheInProcessFrontend` | 1 | Moved to `Integration/CompilationHosts.feature` (`@host-managed`). |
| `ManagedEntryPointTests.TheManagedExecutableListsTheSharedBuiltIns` | 1 | Moved to `Integration/Extensions.feature` (`@host-managed`). 8 tests of the composition stay. |
| `MckRoutingTests` | 22 | 18 moved to `Integration/ToolRouting.feature`, with an Examples block for each host (`@host-native`, `@host-tool`) and a check that the command ran on that host. 4 stay: `KillingTheCliOnWindowsStopsTheToolAndItsChild` and `SigtermToTheCliOnUnixStopsTheToolsTree` send a signal to the CLI or kill it, and no driver does that yet (`IProcessSessionDriver` of pull request 2). Open in task 7.3. |
| `ManagedMckRoutingTests` | 3 | 2 moved to `Integration/ToolRouting.feature` (`@host-managed`). The Windows kill test stays. |
| `MckRoutingHarnessTests` | 3 | **Stay**, changed destination: they test the harness `MckRouting` of the tests that stay. |

"Another unknown command starts nothing" now also runs the stub `morphir-lint` alone and expects its exit code 42, so
the check that the CLI did not run it can fail. The old harness renamed each file of the stub, also its assembly, so
the renamed apphost could not start and the check passed in each case; the driver renames only the executable.

The routing tests started the apphost in the test output folder, and that folder also had a stub, so they also
checked that the CLI does not search the folder of its executable. The CLI driver starts the built assembly with
`dotnet` and writes nothing to the build output, so the new scenarios do not have that check. The other checks are
the same.

## The `PlatformSpecific` marks

| Place | Mark |
| --- | --- |
| The moved features and classes | Removed with them. |
| `ExtensionRuntimesReportTests` | Removed from the class: the 13 tests that stay use no operating system. |
| `HostVersionTests` | The 2 tests that stay had no mark. |
| The other classes that stay | The mark stays. |

## New tests with no old test

| Test | Why |
| --- | --- |
| `CommandFamilyDriverTests` (25), `CliDriverTimeLimitTests` (1), `DotnetHostTests` (6), new cases of `BlackBoxSessionTests`, `ScenarioWorkspaceTests`, `TestEligibilityTests` | The drivers and capabilities of task 7.2. |
| `ToolRouting.feature`, "The experimental wasm-host commands go to the separate runner" | One row for each command: the old test ran two commands in one case. |

## Findings about the product (not in the scope of this change)

- `morphir config get` and `morphir config set` with `--file` join an absolute Windows path with backslashes, such as
  `D:\a\work\morphir.toml`, to the working directory: `ProjectPath.join` knows an absolute path only as `/...` or
  `X:/...`. The report then reads `D:/a/work/D:/a/work/morphir.toml`, which is not there. A path with `/`, as a report
  writes it, works. The check of "Supported primary outputs retain the project source directory" gives the path
  relative to the work area, so it runs on Windows; the old test read the file with the configuration library and
  gave no path to the CLI.
