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
| `DriverSet` | The workspace and the drivers of one test on one host. `DriverSet.CliOf` gives the CLI driver of another host of the target, for a test that compares the hosts (`@needs-all-hosts`). |
| `IProcessSessionDriver` (`ProcessSessionDriver`) | A product process that stays alive: framed input (`ContentLengthFrames`), output as it comes, an interrupt (SIGINT, or `ConsoleControlEvent` on Windows), and the exit with the time from the interrupt to the exit (`SessionExit`). It runs on `ChildProcess.ConverseAsync`. `ExtensionWorker` and `WorkerCorpus` use it for `extension worker`. |
| `ExtensionListReport`, `ExtensionRuntimesReport` | The reports of `extension list --json` and `extension runtimes --json`, with their entries `ListedExtension` and `ExtensionRuntime`. Pull request 3 added them with `IExtensionDriver`; pull request 2 added the members that its steps read: the arguments of each command, `Extension(id)`, `ExtensionListReport.HostName`, `ExtensionListReport.ExpectedDeploymentOf`, `Wasm`, and the types, delivery, hosts, validator IR versions, profiles and memory bound of an entry. |
| `PinnedPackages` | The version that `Directory.Packages.props` pins for a package, for a check of a reported version. |
| `IPackagedArtifactDriver` (`PackagedArtifactDriver`) | The installed packaged offering of the run: its launch metadata, runs of the installed executable from new empty directories with the runtime of the machine reachable or hidden, the layout claim, the engine files, and a second installation that is read-only. The helpers are in the namespace `Morphir.TestSupport.Packaging`. |
| `HostCopies` | A copy of the folder of a host with each WASM engine library deleted (`HostCopy`), made once for each run. |
| `TargetCli` | The executable of the target of the run, for a test outside the black-box project that starts it with its own runner (a conformance test). |
| `IPackageDriver` (`PackageDriver`) | Packs, installs and consumes packages in the workspace. It packs a project of the development build with no build and no restore (`IPackageDriver.PackAsync`), or builds and packs it with `--artifacts-path` in the workspace (`IPackageDriver.BuildAndPackAsync`), into the feed of the workspace. So no file of the solution build changes. A consumer restores into the packages folder of the workspace. In the packages lane it reads the packed packages (`PackedPackages`) only from the folder that the build gives. A package is a `PackedPackage`: its `PackageManifest`, its entries and the `AssemblyIdentity` of each assembly. |
| `ILibraryHostDriver` (`LibraryHostDriver`) | A program of the repository that is not the CLI (`TestProgram`, from `TestPrograms`): a library host, a probe or `morphirup`. It runs the built assembly, or publishes the program into the workspace in a `ProgramForm` (managed, trimmed, Native AOT) and runs the `PublishedProgram`. A `PublishWarning` is a trim or AOT warning of the publish; `AllowedWarning` permits one. |
| `IArtifactInspectionDriver` (`ArtifactInspectionDriver`) | A `BuiltFile` of the build output of such a program, and its metadata: `AssemblyFacts` (the assemblies, namespaces and members that an assembly references) or the file as JSON. |
| `ICompilationDriver` (`CompilationDriver`) | The `compile` command as an intent, with `CompilationOptions`. Its report is `CompilationReport`. |
| `IConfigurationDriver` (`ConfigurationDriver`) | The `config` command, in the `ScenarioMachine` of a scenario. Its report is `ConfigurationReport`. |
| `IMigrationDriver` (`MigrationDriver`) | The `project migrate` and `workspace migrate` commands, in the `ScenarioMachine` of a scenario. Its report is `MigrationReport`. |
| `IExtensionDriver` (`ExtensionDriver`) | The `extension list` and `extension runtimes` commands, with `ExtensionListOptions`. Their reports are `ExtensionListReport` and `ExtensionRuntimesReport`. |
| `IModelValidationDriver` (`ModelValidationDriver`) | The `validate` command, with `ValidationOptions`. Its report is `ValidationReport`. |
| `ExtensionArtifacts` | The files of the extension guests of the development build (`BuiltExtension`), from the assembly metadata that `build/Morphir.ExtensionArtifacts.targets` writes. Only the development build has them (`@needs-built-extensions`). |
| `CliRunOptions.WithoutDynamicCode` | Runs an assembly host with the dynamic-code switch of the runtime off, as in a Native AOT executable, from a copy of its runtime configuration in the workspace (`@needs-host-without-dynamic-code`). |
| `ExtensionPlatform` | The platform of this machine as the extension reports name it, for example `osx-arm64`, and the platform with no WASM runtime (`win-x64`). |
| `IToolRoutingDriver` (`ToolRoutingDriver`) | The routing of a command to an external tool, as `morphir mck` runs `morphir-mck`: it installs the stub of the tool in the workspace, runs the product with the search path of the scenario (`ToolRoutingOptions`), and runs the stub alone. The stub reports in a `StubReport`. |
| `DotnetHost` | The full path of the dotnet host that starts an assembly host: `DOTNET_HOST_PATH`, else the dotnet executable of the test process. A driver does not start it by its name: on Linux the process runner starts each child through `setsid`, which looks up the program in the `PATH` of the child, and a scenario can give the product a `PATH` with no dotnet. |
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

Each target resolves to the artifact of its own form, once before the first test (`BlackBoxSession`):

| Target | Artifact | Command that makes it |
| --- | --- | --- |
| `dev` | The build output of the three entry points, from the assembly metadata | `dotnet build`, or the build of the test project |
| `trimmed` | `artifacts/single-file/<rid>/morphir` | `./build.sh --target PublishSingleFile --rid <rid>` |
| `untrimmed` | `artifacts/single-file-untrimmed/<rid>/morphir` | `./build.sh --target PublishSingleFileUntrimmed --rid <rid>` |
| `aot` | `artifacts/executables/<rid>/morphir` | `./build.sh --target PublishExecutable --rid <rid>` |
| `native-aot`, `self-contained`, `framework-dependent`, `dotnet-tool` | The archive or the tool package in the release folder (`ArtifactLocations`), installed into a new directory whose path has a space (`PackagedTarget`) | `./build.sh --target PackRelease --deployment <offering> --rid <rid>` |
| `executable` | The file at `MORPHIR_TEST_EXECUTABLE` | |

| Host | Project | Executable |
| --- | --- | --- |
| `native` | `src/Morphir` | `morphir` |
| `managed` | `src/Morphir.Managed` | `morphir` |
| `tool` | `src/Morphir.Tool` | `dotnet-morphir` |

A target never uses the artifact of another form or another configuration. A missing artifact stops the run with
the command that builds it: a missing trimmed executable does not run the untrimmed one.

### The development build

A test project that uses the CLI driver imports `build/Morphir.CliArtifacts.targets`. That file adds build-order
references to the three entry point projects and writes their target paths into the test assembly as assembly
metadata. So a build of the test project builds the hosts first, a Debug test build runs the Debug hosts, and no
assembly of an entry point is visible to the test code.

### Other programs of the repository

A test project that runs or inspects a program that is not the CLI names it with an item and imports
`build/Morphir.TestPrograms.targets`:

```xml
<ItemGroup>
  <MorphirTestProgram Include="../Morphir.Compilation.Smoke/Morphir.Compilation.Smoke.fsproj" Name="compilation-smoke" />
</ItemGroup>
<Import Project="../Morphir.TestSupport/build/Morphir.TestPrograms.targets" />
```

The build of the test project then builds the program first, and writes its target path and its project path into
the test assembly. A publish of `ILibraryHostDriver` uses `--artifacts-path` in the workspace, so it replaces no
assembly of the solution build, and a publish for a runtime identifier writes its lock files below `obj/`.

### The packages

A test project that uses the package driver imports `build/Morphir.PackageSources.targets`. That file adds
build-order references to the projects that the driver packs with no build (`Morphir.Myriad`,
`Morphir.Myriad.SystemTextJson` and `Morphir.Myriad.SystemTextJson.Annotations`), and writes
the path of each project file, of each built assembly and the configuration into the test assembly as assembly
metadata. The SDK projects (`Morphir.SDK.FSharp`, `Morphir.SDK`) are not built first: the driver builds and packs
them in the workspace.

A command of the package driver for a consumer gets `NUGET_PACKAGES` set to the packages folder of the workspace, so
no package of a test goes into the NuGet folder of the machine, and `NUGET_HTTP_CACHE_PATH` set to the HTTP cache of
NuGet of the machine, so a restore does not download a package from nuget.org again. A restore from nuget.org writes
that cache. A build and pack in the workspace restores the projects of the repository with the NuGet folder of the
machine, as the solution build does. The driver packs no dotnet tool: a pack of a tool publishes into the `obj` and
`bin` folders of its project, so a test reads a tool package of the packages lane.

### Environment variables

| Variable | Values |
| --- | --- |
| `MORPHIR_TEST_TARGET` | The target. Default `dev`. |
| `MORPHIR_TEST_HOST` | The host of a test with no host tag: `native`, `managed` or `tool` (`dotnet-morphir` is also the tool host). Default: the default host of the target. |
| `MORPHIR_TEST_EXECUTABLE` | The path of the executable, for the target `executable`. |
| `MORPHIR_TEST_LANE` | The lane of the run: `none` (default), `packages`, `native-aot` or `published`. The build sets it. |
| `MORPHIR_TEST_KEEP` | `failed` keeps the workspace of a failed test and prints its path. |
| `MORPHIR_TEST_MAX_PROCESSES` | The limit of product processes at the same time in one test process (`ProcessGate`). The build gives each test process its share. |
| `MORPHIR_RELEASE_DIR` | The release folder of the packaged offerings. Default: `artifacts/release`. |
| `MORPHIR_WORKER_CORPUS` | `all` runs each case of the crafted worker corpus, not the sample. |
| `MORPHIR_TEST_PACKAGES` | The folder of the packed packages, for the packages lane. `./build.sh --target TestBuild` gives the output folder of `./build.sh --target PackAll`. A packages run with no package there stops before the first test with the pack command. |

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
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend`, `@needs-built-programs` | The test needs that `DriverCapability`. `@needs-built-programs`: the build output and the project files of the programs of the repository; only the development build has them. |
| `@needs-development-packages` | The test packs a project of the development build (`IPackageDriver`). Only the target `dev` has it. |
| `@needs-compilation`, `@needs-configuration`, `@needs-migration`, `@needs-extensions`, `@needs-model-validation`, `@needs-built-extensions`, `@needs-host-without-dynamic-code`, `@needs-all-hosts`, `@needs-unprofiled-build`, `@needs-tool-routing` | The test needs that `DriverCapability`: a command family, the extension guests of the development build, a host with the dynamic-code switch of the runtime off, each host of the target in one test, a host that no release profile built, or the routing of a command to an external tool. |
| `@needs-packaged-installation`, `@needs-native-aot-executable`, `@needs-known-form` | The test needs an installed packaged offering; a Native AOT executable of a release (`aot`, `native-aot`); a target whose form is known (each target except `executable`). |
| `@needs-wasm-guests` | The test runs a WASM guest. Each platform except Windows x64 has it: there the host refuses each guest (#609). `DriverCapabilities.Of` takes the platform; the default is this machine. |

## How to add a capability

1. Add a case to `DriverCapability`. Its tag is `needs-` and the name in lower case with hyphens.
2. Add the capability to `DriverCapabilities` for each target and host whose drivers support it.
3. Add the driver interface and its report type, with tests in `tests/Morphir.TestSupport.Tests/Drivers`: the
   command line with a path that has a space, and the report type against a real report of the command.
4. Add the driver to `DriverSet`.
5. Add the tag to this document and to `tests/Morphir.BlackBox.Tests/README.md`.

Add an interface in the change that has its first two scenarios, not before.

## A test outside the black-box project

A conformance test judges the output of an executable with the kit library in the test process, so it stays in a
project that references the kit. It gets its executable from the target of the run with `TargetCli.OfTestRun`, with
a default host for a run that names none, and imports `build/Morphir.CliArtifacts.targets`. Examples:
`tests/Morphir.Frontend.FSharp.Tests/CliAdapterTests.fs` (the tool host by default) and
`tests/Morphir.Extensions.Wasm.Tests/WorkerGuestKitModelTierTests.cs`.

