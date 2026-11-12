# Morphir.TestSupport

Shared test infrastructure for this repository. It is never packed or shipped.

It has two layers:

- **Containment:** `TestSandbox`, `ChildProcess`, `ProcessGate`, `MachineTripwire` and `TolerantDirectory`. See
  [Test Isolation](../../docs/content/docs/contributing/qa/test-isolation.md).
- **Drivers** (namespace `Morphir.TestSupport.Drivers`): the one way in which a black-box test reaches the product.
  This document describes the drivers.

A black-box test reaches the product only through a built artifact. A step or a test of
`tests/Morphir.BlackBox.Tests` starts no process, makes no temporary directory and reads no environment variable.
It calls a driver interface, and the driver owns the folder, the processes and the clean-up.

## The types

| Type | What it owns |
| --- | --- |
| `IScenarioWorkspace` (`ScenarioWorkspace`) | The contained folder of one test: the work area, the home, the Morphir home, the temporary directory, the fixture copies, and the list of files written since a `WorkspaceMark`. A step gets a file as a `WorkspaceFile` or a `WorkspaceDirectory`. |
| `ICliDriver` (`CliDriver`) | Runs one command line on one host of the target and returns a `CommandResult`. It contains the process in the workspace, gives it a time limit (`CliRunOptions`), and holds a place of the `ProcessGate`. It starts a built assembly with `dotnet`, not with `dotnet run`. |
| `IModelVerificationDriver` (`ModelVerificationDriver`) | The `ir verify` command as an intent, with `VerificationOptions`. Its report is `VerificationReport`. |
| `IGenerationDriver` (`GenerationDriver`) | The `generate` command as an intent, with `GenerationOptions`. Its report is `GenerationReport`. |
| `BlackBoxSession` | One for each test process. It reads the environment once, resolves the target to its own artifact (`CliArtifacts`), makes the one eligibility decision of a test, and makes a `DriverSet` for a test. It records the Morphir locations of the machine at the start (`MachineTripwire`), and at the end of the run it prints the skip counts and fails if a test changed those locations. |
| `DriverSet` | The workspace and the drivers of one test on one host. |
| `ICompilationDriver` (`CompilationDriver`) | The `compile` command as an intent, with `CompilationOptions`. Its report is `CompilationReport`. |
| `IConfigurationDriver` (`ConfigurationDriver`) | The `config` command, in the `ScenarioMachine` of a scenario. Its report is `ConfigurationReport`. |
| `IMigrationDriver` (`MigrationDriver`) | The `project migrate` and `workspace migrate` commands, in the `ScenarioMachine` of a scenario. Its report is `MigrationReport`. |
| `IExtensionDriver` (`ExtensionDriver`) | The `extension list` and `extension runtimes` commands, with `ExtensionListOptions`. Their reports are `ExtensionListReport` and `ExtensionRuntimesReport`. |
| `IModelValidationDriver` (`ModelValidationDriver`) | The `validate` command, with `ValidationOptions`. Its report is `ValidationReport`. |
| `ExtensionArtifacts` | The files of the extension guests of the development build (`BuiltExtension`), from the assembly metadata that `build/Morphir.ExtensionArtifacts.targets` writes. Only the development build has them (`@needs-built-extensions`). |
| `CliRunOptions.WithoutDynamicCode` | Runs an assembly host with the dynamic-code switch of the runtime off, as in a Native AOT executable, from a copy of its runtime configuration in the workspace (`@needs-host-without-dynamic-code`). |
| `ScenarioMachine` | The machine that a scenario of discovery and containment gives to the product: the working directory, the home and the machine locations, and exactly the Morphir variables of the scenario. |

A capability driver builds its command line and calls `ICliDriver`. Only `ICliDriver` and `IScenarioWorkspace` know
the target and the machine.

## The result of a command

`CommandResult` has the command line, the exit code, standard output, standard error, the duration and the files
that the command wrote in the work area. Two layers check it:

- `CommandResultAssertions` has the general checks: the exit code, exactly one JSON document on standard output, a
  written file, text on standard error. These checks read no field of a report.
- A report type has the checks of its own command. `VerificationReport` reads the field `IsValid`.
  `GenerationReport` reads the field `success`, and fails when the field disagrees with the exit code.
- The reports of the other command families derive from `JsonCommandReport`: one JSON object, a field `exitCode`
  that agrees with the exit code, and the list `diagnostics` as `ReportDiagnostic` items. `CompilationReport` and
  `ValidationReport` also read `success`; `ConfigurationReport` and `MigrationReport` read `outcome`. A path in a
  configuration or migration report has the form of `ReportPath`.

Each failure is a `CommandAssertionException`. Its message has the command line, the exit code and both streams.

## The target and the host

The target is the form of the product (`TestTarget`). The host is the entry point (`MorphirHost`). Both are closed
sets. `TestRunSettings` parses the settings once; a wrong value stops the run with a `TestSessionException` that
lists the valid values.

| `MORPHIR_TEST_TARGET` | Hosts that it has | Default host |
| --- | --- | --- |
| `dev` (default) | `native`, `managed`, `tool` | `native` |
| `trimmed`, `untrimmed`, `aot`, `native-aot` | `native` | `native` |
| `self-contained`, `framework-dependent` | `managed` | `managed` |
| `dotnet-tool` | `tool` | `tool` |
| `executable`, with `MORPHIR_TEST_EXECUTABLE` | the host that `MORPHIR_TEST_HOST` names | that host |

| Host | Project | Executable |
| --- | --- | --- |
| `native` | `src/Morphir` | `morphir` |
| `managed` | `src/Morphir.Managed` | `morphir` |
| `tool` | `src/Morphir.Tool` | `dotnet-morphir` |

A target never uses the artifact of another form or another configuration. A missing artifact stops the run with
the command that builds it. The CLI driver has the targets `dev` and `executable` now; a later pull request of the
change `consolidate-black-box-tests` adds the published and packaged targets.

### The development build

A test project that uses the CLI driver imports `build/Morphir.CliArtifacts.targets`. That file adds build-order
references to the three entry point projects and writes their target paths into the test assembly as assembly
metadata. So a build of the test project builds the hosts first, a Debug test build runs the Debug hosts, and no
assembly of an entry point is visible to the test code.

### Environment variables

| Variable | Values |
| --- | --- |
| `MORPHIR_TEST_TARGET` | The target. Default `dev`. |
| `MORPHIR_TEST_HOST` | The host of a test with no host tag: `native`, `managed` or `tool` (`dotnet-morphir` is also the tool host). Default: the default host of the target. |
| `MORPHIR_TEST_EXECUTABLE` | The path of the executable, for the target `executable`. |
| `MORPHIR_TEST_LANE` | The lane of the run: `none` (default), `packages`, `native-aot` or `published`. The build sets it. |
| `MORPHIR_TEST_KEEP` | `failed` keeps the workspace of a failed test and prints its path. |
| `MORPHIR_TEST_MAX_PROCESSES` | The limit of product processes at the same time in one test process (`ProcessGate`). The build gives each test process its share. |

### Old names

The new settings replace these names when the tests that read them move. Do not use an old name in a new test.

| Old | New |
| --- | --- |
| `CliTestHelper` with `CliHost.Morphir` | `dev`, host `native` |
| `CliTestHelper` with `CliHost.DotnetTool`; `MORPHIR_EXECUTABLE_TYPE=tool` | `dev` with `MORPHIR_TEST_HOST=tool` |
| `MORPHIR_EXECUTABLE_TYPE=aot`, `trimmed`, `untrimmed`; `MORPHIR_AOT_CLI` | `MORPHIR_TEST_TARGET=aot`, `trimmed`, `untrimmed` |
| `MORPHIR_EXECUTABLE_TYPE` with a packaged offering | `MORPHIR_TEST_TARGET` with the same value |
| `MORPHIR_EXECUTABLE_PATH`, `MORPHIR_GENERATION_CLI`, `MORPHIR_DOTNET_TOOL_CLI`, `MORPHIR_CONFIGURATION_CLI` | `MORPHIR_TEST_TARGET=executable` with `MORPHIR_TEST_EXECUTABLE` and `MORPHIR_TEST_HOST` |

## The one decision that says if a test runs

Before the first step of a test, `TestEligibility` makes one decision from what the test needs (`TestNeeds`, read
from its tags) and what the run gives (`RunProfile`). It checks the lane, then the host, then the capabilities, and
stops at the first check that fails. The result is an `Eligibility`: the test runs on a host, or it is skipped with
exactly one reason. `SkipCounts` counts the skips by `SkipKind`, and the test process prints the counts when it ends.

| Tag | Meaning |
| --- | --- |
| `@lane` with one of `@packages`, `@native-aot`, `@published` | The test needs more than the development build (`TestLane`). |
| `@host-native`, `@host-managed`, `@host-tool` | The test runs on that host. A test with no host tag runs on the default host of the run. |
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend` | The test needs that `DriverCapability`. |
| `@needs-compilation`, `@needs-configuration`, `@needs-migration`, `@needs-extensions`, `@needs-model-validation`, `@needs-built-extensions`, `@needs-host-without-dynamic-code` | The test needs that `DriverCapability`: a command family, the extension guests of the development build, or a host with the dynamic-code switch of the runtime off. |

## How to add a capability

1. Add a case to `DriverCapability`. Its tag is `needs-` and the name in lower case with hyphens.
2. Add the capability to `DriverCapabilities` for each target and host whose drivers support it.
3. Add the driver interface and its report type, with tests in `tests/Morphir.TestSupport.Tests/Drivers`: the
   command line with a path that has a space, and the report type against a real report of the command.
4. Add the driver to `DriverSet`.
5. Add the tag to this document and to `tests/Morphir.BlackBox.Tests/README.md`.

Add an interface in the change that has its first two scenarios, not before.
