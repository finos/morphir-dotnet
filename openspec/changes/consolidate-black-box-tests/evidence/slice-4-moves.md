# Slice 4: the package tests that moved to the black-box project (task group 8)

Date: 2026-11-12. The rows are the tests that the runner discovers, from the reports of the source projects before
the move: the TRX report of `Morphir.Myriad.Integration.Tests` and of the complete `Morphir.Build.Tests`, the test
lists of the Expecto projects (`--filter-test-list`), and the `PASS` lines of the two `package-tests.fsx` scripts
(a script is not a test project, so its checks are the lines that it prints). All ran on macOS arm64 in Release. After the move: the complete `./build.sh --target Test` passed (37 suites, 293 s; the
groups `integration` 66 s and `journey` 68 s), and `./build.sh --target TestBuild` after `PackAll` passed (460 build
tests, then 19 tests of the packages lane in 2 s).

## Counts

| Source | Discovered before | Passed / failed / skipped before | New tests | Passed / failed / skipped after |
| --- | ---: | ---: | --- | ---: |
| `Morphir.Myriad.Integration.Tests` (project removed) | 12 | 12 / 0 / 0 | 12 scenarios, `Journeys/MyriadCarRental.feature` | 12 / 0 / 0 |
| `tests/Morphir.Myriad.Tests/package-tests.fsx` (removed) | 15 checks | 15 / 0 / 0 | 15 scenarios, `Integration/MyriadConsumer.feature` | 15 / 0 / 0 |
| `tests/Morphir.Myriad.SystemTextJson.Tests/package-tests.fsx` (removed) | 7 checks | 7 / 0 / 0 | 5 scenarios of `Integration/SystemTextJsonConsumer.feature`, 2 of `EndToEnd/PackedPackages.feature` | 7 / 0 / 0 |
| `Morphir.Myriad.SystemTextJson.Tests/BuildTests.fs` (removed) | 5 | 5 / 0 / 0 | 6 scenarios of `Integration/SystemTextJsonConsumer.feature` (2 of them shared with the script), 1 build test | 7 / 0 / 0 |
| `Morphir.SDK.Tests/PackageMigrationTests.fs` with `Packaging/package-tests.ps1` (removed) | 2 | 2 / 0 / 0 | 2 scenarios, `Journeys/FSharpSdkPackages.feature`, 1 build test | 3 / 0 / 0 |
| `Morphir.Frontend.FSharp.Extension.Tests/PackageTests.fs` (removed) | 1 | 1 / 0 / 0 | 1 example of `EndToEnd/ToolInstallation.feature` | 1 / 0 / 0 |
| `Morphir.CodeGeneration.FSharp.Extension.Tests/PackageTests.fs` (removed) | 1 | 1 / 0 / 0 | 1 example of `EndToEnd/ToolInstallation.feature` | 1 / 0 / 0 |
| `Morphir.Frontend.FSharp.Extension.Tests/SelfContainedTests.fs` | 2 | 0 / 0 / 2 (macOS skips both) | None: it stays (see below) | 0 / 0 / 2 |
| The five package files of `Morphir.Build.Tests` (removed) | 15 | 15 / 0 / 0 | 12 scenarios of `EndToEnd/PackedPackages.feature`, 3 of `EndToEnd/ToolInstallation.feature` | 15 / 0 / 0 |

| Project | Before | After |
| --- | ---: | ---: |
| `Morphir.Build.Tests`, passed / failed / skipped | 472 / 0 / 0 | 460 / 0 / 0 (15 moved; 2 new build tests; `TestPlanTests` has 2 tests of the packages lane run and no test of the removed `RunsPackageConsumerScripts`) |
| `Morphir.BlackBox.Tests`, tests with no lane (integration, journey) | 0 of this slice | 40: integration 26, journey 14 |
| `Morphir.BlackBox.Tests`, tests of the packages lane | 0 | 19: e2e 19 |

58 old tests and checks become 61 new tests: two pairs of old tests had the same check and became one scenario
each, two old tests became a scenario outline with 5 and 2 examples, and two checks became build tests.

## The form of the checks

- A step reaches the product only through `IPackageDriver` and `IScenarioWorkspace`. The old tests started
  `dotnet` with `Process`, `ChildProcess` or PowerShell, wrote to `Path.GetTempPath()` and read the repository folder.
- A pack of a project of the development build is `dotnet pack --no-build --no-restore`, with its manifest
  (`NuspecOutputPath`) in the workspace. The SDK packs build in the workspace (`--artifacts-path`). So no test of this
  slice writes a file below the `bin` and `obj` folders of the repository (task 8.2, below).
- A consumer restores into the packages folder of its workspace. The old Myriad integration tests restored into the
  NuGet folder of the machine and deleted their package version after the run; `LocalInstallationTests` installed
  `Morphir.Tool` from the real NuGet folder. No package of a test goes there now.
- A check that a build fails is `When I build the consumer and it fails`: the build must exit with a code that is not
  0, as `build consumer false` and `WhenBuildFails` did. A check of the build output reads standard output and
  standard error (`the output contains`), as the old checks read both.
- The old tests of the packed packages found a package with a file pattern and took the newest file. The driver
  finds a package by the id in its manifest, and two packages with one id are an error. `PackAll` cleans its folder,
  so the folder has one. `AllPackages_HaveSameVersion` and `AllPackages_HaveReleaseNotes` passed when some of the four
  packages were missing; the new scenarios need each of the four packages.
- `ToolCommand_AvailableAfterInstall` checked that the output has a version (`\d+\.\d+\.\d+`). The new step
  `standard output is a version` also refuses text before or after the version. `ToolPackage_UninstallsSuccessfully`
  did not check the exit code of the install; the new scenario does.
- The describe reply of an extension tool is read by the step class, not with `ContentLengthFraming` of
  `Morphir.Mep`. The old backend test compared the extension id with `FSharpGuest.extension.Id`; the scenario checks
  the literal id, and the in-process test `GuestTests` of each extension test project keeps the comparison with the
  product. Each extension now has the checks of both old tests: the id, the claims version, the extension version
  and the tuples.

## `Morphir.Myriad.Integration.Tests/CarRental.feature` (12 scenarios)

New file: `Journeys/MyriadCarRental.feature`, `@journey @needs-development-packages`. Each scenario has the same
steps and checks. The package is packed for each scenario, not once for each run.

| Old scenario | New scenario |
| --- | --- |
| Call a generated daily rate through a locally packaged plugin | The same name |
| Execute the complete rental lifecycle from classic IR (1), (2), (3) | The same name and examples |
| Rebuild after changing the model's daily rate | The same name |
| Invalid input blocks a rebuild after successful generation (7 examples) | The same name and examples |

## `tests/Morphir.Myriad.Tests/package-tests.fsx` (15 checks)

New file: `Integration/MyriadConsumer.feature`, `@integration @needs-development-packages`. A model with modules
is built from `tests/Fixtures/MorphirIR/daily-rate-v3.json`, as the script did.

| Old check | New scenario |
| --- | --- |
| configured output directories accept trailing separators | A configured output directory accepts a separator at its end |
| repeat builds preserve source and manifest timestamps | Repeat builds keep the text and the time of the generated sources and manifests |
| model edits regenerate executable behavior | A change of the model changes what the program does |
| module addition and removal update compilation and clean only owned output | An added module compiles, and a removed module removes only its own generated output |
| invalid model after success fails instead of using stale source | A model that is not valid after a successful build fails the build and does not use the old source |
| a host failure leaves existing generated output unchanged | A failure of the Myriad host keeps the generated output |
| colliding module paths fail before the host starts | Module paths that collide fail the build before the host starts |
| a type mismatch fails the build with its rule | A value of the wrong type fails the build with its rule |
| missing model failure includes the input path | A model file that is not there fails the build with its path |
| models with the same basename have distinct output directories | Two models with the same file name have different output directories |
| explicit output directories support spaces | An output directory can have a space in its name |
| duplicate output ownership is rejected | Two models cannot own one output directory |
| symbolic-link output roots cannot redirect generation | An output directory that is a symbolic link cannot send the generation to another folder. On Windows the old check did nothing and passed; the scenario is skipped there with the reason (a symbolic link of a directory needs a privilege). |
| concurrent configurations use isolated generated outputs | Two configurations that build at the same time have their own generated output |
| clean removes default intermediate generated artifacts | A clean removes the generated output of the default directory. The old check looked for the one generated file; the new one checks that no file `Inventory.fs` is below `obj`, which is not weaker. |

The script used one packages folder for its 15 consumers. Each scenario now has its own packages folder.

## `tests/Morphir.Myriad.SystemTextJson.Tests/package-tests.fsx` (7 checks) and `BuildTests.fs` (5 tests)

| Old test | New test |
| --- | --- |
| script: the plugin package has the host, the plugin, the task and the build assets | `EndToEnd/PackedPackages.feature`: The System.Text.Json plugin package has the host, the plugin, the task and the build assets. Packages lane: it reads the package of `PackAll`, which is the package that a user gets, not a pack in the test. |
| script: the annotations package has only its library | `EndToEnd/PackedPackages.feature`: The annotations package has its library and its README (the same two entries). Packages lane. |
| script: a clean consumer with an empty package folder builds and runs a context | `Integration/SystemTextJsonConsumer.feature`: The same name |
| script: a repeat build does not rewrite the generated file; `BuildTests.fs`: a valid context builds, and a repeat build does not rewrite the generated file | A repeat build does not write the generated context again: one generated file `AppJson.g.fs`, and the text and the time of each generated F# file are the same after a second build |
| script: one project uses Morphir.Myriad and Morphir.Myriad.SystemTextJson together | One project uses the Myriad plugin and the System.Text.Json plugin together |
| script: wire defaults, extension data and migration work from packed packages | Wire defaults, extension data and migration work from the packed packages. The program is the fixture `Fixtures/SystemTextJson/WireRulesProgram.fs`, from the text of the script. |
| script: a context that the generator refuses blocks compilation; `BuildTests.fs`: the same name | A context that the generator refuses blocks compilation |
| `BuildTests.fs`: a type that the generator cannot encode is a build error at its line and column | The same name |
| `BuildTests.fs`: the compiler checks default provider methods and return types (one test, 5 cases in a loop) | The compiler checks the methods and the return types of a default value provider: a scenario outline with the 5 cases |
| `BuildTests.fs`: an IDE project load passes when the task assembly is not built | Build test `Morphir.Build.Tests/MyriadSystemTextJsonRepositoryTargetsTests.cs`: its subject is `repository/Morphir.Myriad.SystemTextJson.Repository.targets`, the targets of the projects of this repository (classification step 2). It restores the fixture only (`-p:RestoreRecursive=false`), and it checks that the restore files of the source projects stay. |

**A weaker check, with its reason.** The four other tests of `BuildTests.fs` built a fixture through the repository
targets, with references to the source projects. Their scenarios build through the packed packages, the form that a
user gets. The checks of the generator are the same, but the failures of the repository form are not checked any
more. The solution build compiles each JSON context of the repository through the repository targets, so a defect
of their success path still stops the build.

## `Morphir.SDK.Tests/PackageMigrationTests.fs` and `Packaging/package-tests.ps1` (2 tests)

New file: `Journeys/FSharpSdkPackages.feature`, `@journey @needs-development-packages`, on
`IPackageDriver.BuildAndPackAsync` (`--artifacts-path` in the workspace).

| Old test | New scenario |
| --- | --- |
| canonical, legacy and combined source and precompiled binary consumers | Source and precompiled consumers of each package graph run on the canonical runtime |
| a later package version retains source and genuine old binary compatibility | A later package version keeps the source and the old binary compatibility (version `0.4.1-sdkproof`, from a changelog in the workspace, as the script did) |

Each scenario has each check of the script, in steps: the legacy package has no assembly and depends only on the
canonical package at its own version; the hash of the precompiled baseline; for each of the three graphs, the one
runtime asset of the assets file, the allowed packages, the hash and the identity of the runtime in the output, the
old public declarations, and the precompiled consumer on the new runtime with the host trace; and the two generated
Ordering consumers. The script went on after a graph failed and reported all failures at the end; a scenario stops
at its first failed step.

The script checked that `src/Morphir.SDK/bin` and `obj` of the repository have no assembly. The scenario checks the
build of the test in the workspace (`the build of the package "Morphir.SDK" in the workspace made no assembly`), and
the build test `TheCompatibilityProjectOfTheSdkHasNoAssemblyInItsBuildOutput` keeps the check of the repository after
the solution build and the pack step. The script also set `DOTNET_PROCESSOR_COUNT=2` for its commands; that is not a
check, and the driver does not set it.

The files `Packaging/Baseline/`, `Packaging/Generated/Ordering.fs` and `Packaging/LegacyConsumer.fs` stay in
`Morphir.SDK.Tests` with `capture-baseline.ps1`, which makes the baseline. The black-box project links them as
fixtures. `Packaging/FSharpSdkPackaging.feature`, `MatchedRuntimeConsumer.fs` and `analyze-paired-coverage.py` are not
tests that a runner discovers (no project compiles or runs them), and they stay.

## The package tests of the two extensions (2 tests)

New file: `EndToEnd/ToolInstallation.feature`, `@e2e @lane @packages`, the scenario outline "An F# extension tool
answers describe with its claims and its package version", one example for each extension. The old tests packed the
project with no build in the test. A pack of a dotnet tool publishes into the `obj` and `bin` folders of its project
(`morphir-fsharp-frontend.deps.json`, `PublishOutputs.*.txt`, `bin/<configuration>/net11.0/publish/`), so the new
tests read the tool package of `PackAll` in the packages lane: it is also the package that a user gets. The pull
request still runs them on each operating system, in the step "Run build tests".

## `SelfContainedTests.fs` (2 tests): stays

The inventory had "Split: the run of the published self-contained process moves". From the discovered tests, it
stays a conformance test (classification step 1): `FixedPackage.load` reads the case `frontend-fsharp-0100` with
`Corpus.read` of `Morphir.Conformance`, the kit library, in the test process, and the comparison with the
framework-dependent guest reads the replies with `Morphir.Mep` types. The negative control is the control of the
environment of the second test, and it stays with it. This machine (macOS) skips both tests, so a split could be
checked only in CI. `inventory.md` has the changed row.

## The five package files of `Morphir.Build.Tests` (15 tests)

| Old test | New scenario (`@e2e @lane @packages`) |
| --- | --- |
| `AssemblyVersionTests.ToolPackage_MorphirAssemblies_HaveChangelogVersion` | Each Morphir assembly of the tool package has the assembly version of the changelog |
| `AssemblyVersionTests.ToolPackage_MorphirAssemblyReferences_HaveChangelogVersion` | Each reference between the Morphir assemblies of the tool package has the assembly version of the changelog |
| `PackageDependencyTests.PackedPackages_MorphirDependencies_ArePackedAtSameVersion` | Each Morphir dependency of a packed package is packed at its version |
| `PackageMetadataTests.AllPackages_HaveSameVersion` | The library packages and the tool package have one version |
| `PackageMetadataTests.PackageVersions_MatchChangelogVersion` | The version of the tool package is the version of the changelog |
| `PackageMetadataTests.ToolPackage_HasCorrectMetadata` | The tool package has the metadata of a dotnet tool of FINOS |
| `PackageMetadataTests.AllPackages_HaveReleaseNotes` | The library packages and the tool package have release notes with sections |
| `PackageStructureTests.ToolPackage_HasCorrectStructure` | The tool package has the layout of a dotnet tool |
| `PackageStructureTests.ToolPackage_HasTheManagedCompositionAndNotTheAotEntryPoint` | The tool package has the managed composition and not the AOT-compatible entry point |
| `PackageStructureTests.ToolPackage_HasCorrectToolSettings` | The tool settings name the command dotnet-morphir and its entry point |
| `PackageStructureTests.LibraryPackages_HaveCorrectStructure` | A library package has the layout of a library |
| `PackageStructureTests.Packages_DoNotContainUnnecessaryFiles` | The packages have no symbol file and no test file |
| `LocalInstallationTests.ToolPackage_InstallsFromLocalFolder` | `EndToEnd/ToolInstallation.feature`: The tool package installs from a local feed |
| `LocalInstallationTests.ToolCommand_AvailableAfterInstall` | The command of the installed tool runs |
| `LocalInstallationTests.ToolPackage_UninstallsSuccessfully` | The installed tool uninstalls |

The changelog version comes from `CHANGELOG.md`, which the project links as the fixture `Repository/CHANGELOG.md`.
The `PlatformSpecific` marks of these 15 tests are removed with their files: the black-box assembly has the marker.

## The other test files of `Morphir.Build.Tests` (task 8.1)

Each other class that the runner discovers, with its class from the classification steps. None moves.

| Class | Tests | Class of test | Why |
| --- | ---: | --- | --- |
| `ToolPackageInstallTests` | 7 | build | Its subject is the script `.github/scripts/install-tool-package.ps1`, with synthetic `Probe.Tool` packages. |
| `BuildProfileTargetsTests` | 13 | build | `src/Morphir.BuildProfile.targets`. |
| `CliDeliveryWorkflowTests` | 15 | build | The delivery workflows. |
| `DevelopmentWorkflowTests` | 11 | build | `development.yml` and `ci-request.yml`. |
| `EntryPointGuardTests` | 12 | build | The guards of the project files of the entry points. |
| `ExtensionPackageProducerTests` | 5 | build | The helper `ExtensionPackageProducer` of `build/Helpers`. |
| `ExtensionProcessLockTests` | 5 | build | The lock file path of `src/Directory.Build.props`. |
| `ExtensionProcessProducerTests` | 9 | build | The properties of the publish profile `SelfContainedProcess`. |
| `PublishLockRedirectTests` | 4 | build | The lock file redirect of `src/Directory.Build.props`. |
| `PublishingWorkflowTests` | 3 | build | `deployment.yml`. |
| `VersionSourceTests` | 6 | build | Where a project takes its package version. |
| `Helpers.MyriadPluginPackageTests` | 4 | build | The helper `PackageValidator.ValidateMyriadPluginPackage`. Two cases give it the packed packages of `PackAll` as input; the subject is the helper. |
| `Helpers.PackageValidatorTests` | 15 | build | The helper `PackageValidator`. Two cases give it a packed package as input. |
| `Helpers.PathHelperTests` | 14 | build | The helper `PathHelper`, with the package folder as input. |
| `Helpers.BlackBoxRulesTests`, `Helpers.TestManagerSkillTests` | 50, 11 | build | The guard tests of this change. |
| `Helpers.TestPlanTests`, `Helpers.CiPlanTests`, `Helpers.BuildMachineTests` | 64 (65 after), 49, 10 | build | The plan of the `Test` target, the CI plan, the build machine. |
| `Helpers.MorphirupReleaseTests`, `Helpers.ReleaseArchiveTests`, `Helpers.ReleaseAssetsTests`, `Helpers.ReleaseProfilesTests` | 13, 16, 11, 44 | build | The release helpers. |
| `Helpers.NativeCheckTests`, `Helpers.NativeNoticeDeclarationsTests`, `Helpers.ThirdPartyNoticesTests` | 15, 4, 9 | build | The native checks and notices of the build. |
| `Helpers.PackagedContentsTests`, `Helpers.PackagedEngineTests`, `Helpers.PublishedLayoutTests` | 10, 15, 11 | build | The helpers of the packaged and published layouts. |
| `Helpers.RepositoryTargetFrameworksTests` | 2 | build | The target frameworks of the repository. |
| `MyriadSystemTextJsonRepositoryTargetsTests` (new) | 2 | build | The repository targets of the System.Text.Json plugin, and the build output of `Morphir.SDK`. |

## Task 8.6: tutorial and documentation tests

The parent agent decided that pull request 3 moves all tutorial and documentation tests. `evidence/slice-3-moves.md`
is not on a branch yet (`git fetch origin` on 2026-11-12), and `evidence/inventory.md` gives no tutorial or
documentation test to pull request 4. So this pull request moves none. The car rental tutorial of the Myriad plugin
is a journey of this slice (`Journeys/MyriadCarRental.feature`), from `Morphir.Myriad.Integration.Tests`.

## Task 8.2: a solution build after the package driver

The check: `./build.sh --target Compile`, then the tests, then `./build.sh --target Compile` again, with no commit
between them. The count is the assemblies below `obj/` that were written after the first compile.

| Tests between the two compiles | Assemblies written by the tests | Assemblies written by the second compile |
| --- | ---: | ---: |
| The 40 scenarios with no lane of this slice, the 19 scenarios of the packages lane, `PackageDriverTests` (15), `MyriadSystemTextJsonRepositoryTargetsTests` (2) | 0 | 0 |
| Before this slice: `Morphir.Myriad.SystemTextJson.Tests` alone (with `BuildTests.fs`) | 0 | 58 |

The tests wrote no file below an `obj` folder outside the black-box project. The unit test
`PackageDriverTests.APackWithNoBuildAndAConsumerOfItChangeNoFileOfTheSolutionBuild` checks the same for each run: a
pack with no build and a consumer build change no file below the `bin` and `obj` folders of the packed projects.

**The cause of the defect of `evidence/slice-1-times.md`.** A fixture build in a `TestSandbox` restores its project
references recursively. The sandbox sets `NUGET_PACKAGES` with no separator at its end and a home with no user NuGet
configuration, so the restore of each referenced source project gets a new `packagesPath` and a new list of
configuration files. NuGet then writes `project.assets.json`, `*.nuget.g.props`, `*.nuget.g.targets` and
`*.nuget.dgspec.json` of those projects again, and the next compile builds them and each project that uses them.
`Morphir.Myriad.SystemTextJson.Tests/BuildTests.fs` had two such builds; its tests moved, and the build test that
stays restores the fixture only. **Open:** `Morphir.Mep.Tests` keeps the defect (`ChannelTests.fs:534` and
`InteropTests.fs:69` build a C# consumer of `src/Morphir.Mep` in a sandbox). Its tests are library tests that stay; a
repair is `-p:RestoreRecursive=false` on those two builds.
