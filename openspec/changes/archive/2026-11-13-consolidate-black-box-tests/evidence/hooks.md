# The black-box project and its hooks (tasks 2.1 and 2.2)

Date: 2026-11-12. Machine: macOS arm64, .NET SDK 11.0.100, TUnit 1.81.10, Reqnroll 3.3.4, Release configuration.
The fixture scenarios and fixture tests below were temporary files in `tests/Morphir.BlackBox.Tests/SelfCheck/`.
They are not in the repository: a fixture with a lane or with an unsupported capability is a test that no pull
request run executes, and the guard tests of task 2.7 refuse such a test. The decision itself has permanent unit
tests in `tests/Morphir.TestSupport.Tests/Drivers/TestEligibilityTests.cs` and `BlackBoxSessionTests.cs`.

## The project (task 2.1)

| Check | Result |
| --- | --- |
| `dotnet build tests/Morphir.BlackBox.Tests -c Release` | Passes. It builds `src/Morphir`, `src/Morphir.Managed` and `src/Morphir.Tool` first. |
| Assembly metadata of the Release build | `Morphir.TestSupport.Cli.Native`, `.Managed` and `.Tool` are paths below `bin/Release/net11.0`; `Morphir.TestSupport.Cli.Configuration` is `Release`. |
| Assembly metadata of a Debug build (`Morphir.TestSupport.Tests`, `-c Debug -p:BuildProjectReferences=false`) | The three paths are below `bin/Debug/net11.0`; the configuration is `Debug`. |
| Project dependencies in `obj/project.assets.json` | Only `Morphir.TestSupport/1.0.0`. No project from `src/`. |
| A file that names `Morphir.Tooling.Features.Generate.GenerationOptions` and `Morphir.Compilation.CompileRequest` | Does not compile: `error CS0234: The type or namespace name 'Tooling' does not exist in the namespace 'Morphir'`, and the same error for `Compilation`. |
| Tripwire, a Gherkin scenario and a framework test that write `~/.morphir/escaped-*.txt` (run with `HOME` set to a temporary folder) | The run fails with exit code 2: `Tests changed the real machine's Morphir locations; a test escaped its sandbox`, and the message has the path of each of the two files. |

**Not verified: the Debug build of the solution.** `src/Morphir.Toolchain.IO` does not compile in the Debug
configuration on `main` (commit `4d9e10a0`), with no change of this branch:
`WindowsProcess.fs(567,9): error FS3511: This state machine is not statically compilable`. So a Debug build of the
entry points fails before it reaches the test project. The Debug metadata is verified as the table says. The
repair of the Debug build is not in the scope of this change.

## The hooks (task 2.2)

Fixtures: in a feature with `@smoke`, the scenarios `Packages lane fixture` (`@lane @packages`),
`Unsupported capability fixture` (`@needs-self-contained-frontend`) and `Two tags fixture`
(`@needs-generation @needs-model-verification`). In a class with `[Category("smoke")]` that derives from
`BlackBoxTest`: `PackagesLaneFixture`, `UnsupportedCapabilityFixture`, `ManagedHostFixture`
(`[Category("host-managed")]`) and `PlainFixture`. Each runs `--version`.

Run on the development build, lane `none` (`dotnet exec Morphir.BlackBox.Tests.dll --output Detailed`):

```text
skipped UnsupportedCapabilityFixture      capability: SelfContainedFrontend on target dev, host native
skipped PackagesLaneFixture               lane: test packages, run none
skipped Packages lane fixture             lane: test packages, run none
skipped Unsupported capability fixture    capability: SelfContainedFrontend on target dev, host native
passed  ManagedHostFixture                dotnet .../src/Morphir.Managed/bin/Release/net11.0/morphir.dll --version
passed  PlainFixture                      dotnet .../src/Morphir/bin/Release/net11.0/morphir.dll --version
passed  The native host gives its version   dotnet ".../src/Morphir/bin/Release/net11.0/morphir.dll" --version
passed  The managed host gives its version  dotnet ".../src/Morphir.Managed/bin/Release/net11.0/morphir.dll" --version
passed  The tool host gives its version     dotnet ".../src/Morphir.Tool/bin/Release/net11.0/dotnet-morphir.dll" --version
passed  Two tags fixture
Skipped tests by reason: lane 2, host 0, capability 2
total: 10, failed: 0, succeeded: 6, skipped: 4
```

Run on one executable at a path (`MORPHIR_TEST_TARGET=executable`,
`MORPHIR_TEST_EXECUTABLE=.../src/Morphir/bin/Release/net11.0/morphir.dll`, `MORPHIR_TEST_HOST=native`,
`MORPHIR_TEST_LANE=published`):

```text
skipped PackagesLaneFixture                 lane: test packages, run published
skipped Packages lane fixture               lane: test packages, run published
skipped ManagedHostFixture                  host: managed is not in target executable
skipped The managed host gives its version  host: managed is not in target executable
skipped The tool host gives its version     host: tool is not in target executable
skipped UnsupportedCapabilityFixture        capability: SelfContainedFrontend on target executable, host native
skipped Unsupported capability fixture      capability: SelfContainedFrontend on target executable, host native
passed  PlainFixture
passed  Two tags fixture
passed  The native host gives its version
Skipped tests by reason: lane 2, host 3, capability 2
total: 10, failed: 0, succeeded: 3, skipped: 7
```

| Check of task 2.2 | Result |
| --- | --- |
| The three scenarios of `Smoke/Version.feature` pass, each on its own executable | Yes: three different files in the command lines above. |
| A scenario of the packages lane in a run with no lane | Skipped; the reason has both lanes: `lane: test packages, run none`. |
| A scenario that needs an unsupported capability | Skipped: `capability: SelfContainedFrontend on target dev, host native`. |
| A scenario that names a host that the target does not have | Skipped: `host: managed is not in target executable`. |
| A framework test with the same tags as a scenario | The same result and the same reason, in both runs. |
| The count of skips for each kind of reason | The line `Skipped tests by reason: lane 2, host 3, capability 2`. The default output of the runner also shows each skipped test with its reason. |
| `MORPHIR_TEST_KEEP=failed` | Unit tests in `BlackBoxSessionTests`: the workspace of a failed test stays, and the others are removed. |

After this verification, `Smoke/Version.feature` got a fourth scenario: the scenario with no host tag checks the
default host of the run, and the scenario of the native host has `@host-native`. So the feature also passes with
`MORPHIR_TEST_HOST=tool` (verified with `./build.sh --target Test --skip Restore Compile --test-group smoke`: 4 tests
pass with the default host and with the tool host).

After the review of the branch, the tripwire moved from the hook class into `BlackBoxSession`: `Start` records the
locations and `EndRun` checks them, so the black-box project has no code that reads the machine. Two unit tests in
`BlackBoxSessionTests` check it with a temporary folder as the watched location.

## An error of this verification

The first run of the filter checks selected the two tripwire fixtures without a temporary `HOME`. They wrote
`~/.morphir/escaped-framework.txt` and `~/.morphir/escaped-scenario.txt` (7 bytes each) on the developer machine.
The tripwire reported both files and failed the run, which is the behavior under test. Both files were removed
immediately; the four directories that were in `~/.morphir` before are unchanged.
