---
name: test-manager
description: Classifies a test of morphir-dotnet and gives its place, its form, its tags and its parallel rule. Covers unit, property, conformance, build, integration, end-to-end, smoke and journey tests, the black-box project tests/Morphir.BlackBox.Tests, Gherkin scenarios, step classes, test drivers, lanes, hosts and capabilities. Use before you add, move, rename or delete a test, a feature file, a step, a driver or a test project; when you do not know in which project a test belongs; when a test starts a process, runs a build or reads a built artifact; or when you review a diff that changes tests. Triggers include "add a test", "new test", "where does this test go", "integration test", "end-to-end test", "e2e", "smoke test", "user journey", "black-box test", "feature file", "Gherkin", "step definition", "test driver", "PlatformSpecific", "test project", "classify a test", "review tests".
---

# Test manager

morphir-dotnet has many test projects. This skill says where a test goes and which form it has, so that each person and each agent makes the same decision.

Use it in two modes:

- **Author mode:** before you add, move, rename or delete a test, a feature file, a step, a driver or a test project.
- **Review mode:** on a diff that changes tests. See [Review mode](#review-mode).

The questions with examples from this repository are in [references/classification.md](references/classification.md). The feature, step and driver patterns are in [references/black-box-patterns.md](references/black-box-patterns.md). Read the file for your case before you write the test.

## Classify the test

Ask the questions in this order. Stop at the first answer that gives a class. The form of the product on which a test runs (the target) is never a question: it does not change the class.

| Step | Question | Answer | Class |
| --- | --- | --- | --- |
| 1 | Is it a feature of a kit suite (`conformance/suites/`), or does it need the kit library in the test process? | yes | **conformance**. Stop. |
| 2 | Is its subject the build of the repository: a project file, a build target, a workflow or a helper of the build? | yes | **build**. Stop. A test of a packed package or an installed tool is not a build test: continue. |
| 3 | Is it a loop over a corpus, a property test or a test with a time budget? | yes | The form is a framework test, not Gherkin. Continue with step 4 for the class. |
| 4 | Does it reach the product only through a built artifact (the command line of an executable, a package, a published folder, an archive)? | no | **unit**, or **property** if it states a law over generated inputs. Stop. |
| 5 | Does it prove the delivered form itself: installation, layout, start, or a property that the delivered form must have? | yes | **end-to-end** |
| 6 | Does it prove one user goal that needs two or more capabilities? | yes | **journey** |
| 7 | Does it prove only that an artifact starts and does one basic function (for example `--version`; for a library host: publish, run, compare)? | yes | **smoke** |
| 8 | Otherwise | | **integration** |

A test that needs both a product assembly and an artifact is two tests. The part that needs the assembly stays in the unit test project. The part that needs only the artifact is a black-box test. Example: `CSharpCliGenerationTests` compares the files of the CLI with `CSharpGenerator.Generate`, and `Integration/Generation.feature` checks the behavior that needs only the CLI.

Words of the steps:

- **Capability:** one command family of the product: for example verify a model (`ir verify`), generate (`generate`), compile, configuration, migration. It has a driver interface, or it gets one with its second scenario (rule 10). Three different commands are three capabilities. A command that runs two times is one capability.
- **Journey:** two or more capabilities for one goal of the user. Its last check is against that goal.
- **Smoke:** the test proves that the artifact starts, and no more: `--version`, `--help`. A test that checks the result of a command (its report, its text, its files) checks a capability, so it is integration.

## Place, form, tags and parallel rule

| Class | Place | Form | Tags or marker | Parallel rule |
| --- | --- | --- | --- | --- |
| unit | The test project of the library, `tests/<Library>.Tests` | A framework test: TUnit (C#) or Expecto (F#). Where the project has feature files for the same subject, follow the form of the tests beside it. | `PlatformSpecific` if it uses the operating system (see `docs/contributing/ci-os-matrix.md`) | Hermetic. Use `TestSandbox` and `ChildProcess` of `tests/Morphir.TestSupport` for a folder or a process. |
| property | The test project of the library | A property test of its framework | As unit | As unit |
| conformance | `conformance/suites/` with kit steps, or a test project that references `src/Morphir.Conformance` | Kit feature, or framework test | As unit | As unit. It gets its executable from the target of the testing library. |
| build | `tests/Morphir.Build.Tests` | TUnit | None | The `TestBuild` target runs the project after the pack step. |
| integration | `tests/Morphir.BlackBox.Tests/Integration/` | Gherkin scenario | `@integration` | Each scenario has its own workspace, so scenarios run at the same time. |
| end-to-end | `tests/Morphir.BlackBox.Tests/EndToEnd/` | Gherkin scenario | `@e2e` | As integration |
| smoke | `tests/Morphir.BlackBox.Tests/Smoke/` | Gherkin scenario | `@smoke` | As integration |
| journey | `tests/Morphir.BlackBox.Tests/Journeys/` | Gherkin scenario | `@journey` | As integration |

For a black-box test with the form of step 3 (corpus loop, property, time budget): write a TUnit class in the folder of its group that derives from `BlackBoxTest`, with the group as a category on the class (`[Category("integration")]`). The black-box project uses TUnit. A unit test with that form uses the framework of its project.

More rules for each test:

- A test that runs `dotnet build`, `pack` or `publish` on a project of the repository must not replace the assemblies of the solution build. Its suite has `WithSourceProjects()` in `build/Helpers/TestPlan.cs`.
- A suite that has tests with a time budget has `Alone()` in `build/Helpers/TestPlan.cs`.
- A new test project is in `Morphir.slnx` and in `TestPlan.Suites`. A group of the black-box project is in `TestPlan.Suites` from the change that adds its first test with no lane.

## Black-box tests

Rules for `tests/Morphir.BlackBox.Tests` (the reasons are in `tests/Morphir.BlackBox.Tests/README.md`):

1. **One group.** Each scenario and each framework test has exactly one of `@integration`, `@e2e`, `@smoke`, `@journey`, and its file is in the folder of that group.
2. **Drivers only.** A step or a test reaches the product, the file system and other processes only through a driver interface of `Morphir.TestSupport.Drivers`. It starts no process, makes no temporary directory and reads no environment variable. A file is a `WorkspaceFile` of the `IScenarioWorkspace` of the scenario.
3. **No product assembly.** The project references `tests/Morphir.TestSupport` only. It does not reference a project below `src/`, and it does not reference `src/Morphir.Conformance`.
4. **Reuse a step.** Find the step in the step table of `tests/Morphir.BlackBox.Tests/README.md` before you write a new one. Add a new step to the table in the same change. A step states an intent of the user, not a command line.
5. **A lane only for more than the development build.** A test with a lane has `@lane` and exactly one lane tag. Other tests have no lane.

   | The test needs | Lane | Who runs it |
   | --- | --- | --- |
   | The packages that the build packs | `@lane @packages` | A pull request, after the pack step |
   | To publish a program with Native AOT in the test (a library host or a probe: publish, run, compare) | `@lane @native-aot` | A pull request, on Linux |
   | An executable that a release publishes or packs (trimmed, untrimmed, Native AOT, a packaged offering), and it cannot run on the development build | `@lane @published` | The release and manual workflows |

   A test that must run on the Native AOT executable of a release has `@lane @published`: the executable is the target of the run. `@lane @native-aot` is only for a test that makes a Native AOT publish itself.
6. **A host tag only when the test needs one host.** The host is the entry point of the product, not its form: `@host-native` is `morphir` of `src/Morphir`, `@host-managed` is `morphir` of `src/Morphir.Managed`, `@host-tool` is `dotnet-morphir` of `src/Morphir.Tool`. A test with no host tag runs on the default host of the run. The form (development build, Native AOT executable, packaged offering) is the target of the run, and a test does not name it.
7. **A capability tag for each driver capability that the test needs.** The tags are `@needs-model-verification` (the `ir verify` command), `@needs-generation` (the `generate` command), `@needs-in-process-fsharp-frontend` (the F# frontend in the host process: the managed host and the tool host) `@needs-self-contained-frontend` (the F# frontend as a process of its own, which the native host starts), and one tag for each other command family: `@needs-compilation` (`compile`), `@needs-configuration` (`config`), `@needs-migration` (`project migrate`, `workspace migrate`), `@needs-extensions` (`extension list`, `extension runtimes`) and `@needs-model-validation` (`validate`). They are the cases of `DriverCapability` in `tests/Morphir.TestSupport/Drivers/TestEligibility.cs`. A run on a target or a host that does not support the capability skips the test with that reason. A capability with no case yet has no tag: add the case together with its driver interface (rule 10). A test that needs something that only one target has states it as a capability, not as the name of a target.
8. **No hidden test.** A test with no lane, or with the packages or the Native AOT lane, must run in a pull request: one pull request run has its host and supports all its capabilities together.
9. **A report check belongs to its command.** Use the report type of the command (`VerificationReport`, `GenerationReport`, `CompilationReport`, `ConfigurationReport`, `MigrationReport`, `ExtensionListReport`, `ExtensionRuntimesReport`, `ValidationReport`) for the status of a report, and `CommandResultAssertions` for the exit code, the streams and the written files.
10. **A new capability is a driver interface with its report type**, with tests in `tests/Morphir.TestSupport.Tests/Drivers`. Add the interface in the change that has the first two scenarios of the capability. Before that, the one scenario has a step class for the capability whose step states the intent and gives the arguments to `ICliDriver`. Do not put the command line in the feature file.

## Run and verify

```bash
./build.sh --target Test --skip Restore Compile --test-group integration   # one group of the black-box tests
./build.sh --target Test --skip Restore Compile                            # each suite
dotnet exec tests/Morphir.Build.Tests/bin/Release/net11.0/Morphir.Build.Tests.dll --treenode-filter "/*/*/BlackBoxRulesTests/*"   # the guard tests
```

A moved test keeps its coverage. When you move tests, make a table from the tests that the runner discovers (not from the list of files): each old test with its new test, or the reason and the place in which it stays. Compare the pass, fail and skip counts before and after.

## Review mode

Check a diff against these rules. Report each finding as `path:line: rule N: problem. fix.`, most severe first.

| Rule | Check | Guard test in `tests/Morphir.Build.Tests/Helpers/BlackBoxRulesTests.cs` |
| --- | --- | --- |
| R1 | Each new or changed test has the class that the classification steps give, and is in the place of that class. A test that starts the product, runs a build or reads a built artifact, and uses no product type, is not in a unit test project. | None: a reviewer must check it. |
| R2 | A black-box test has one group, and its file is in the folder of the group. | `EachTestOfTheBlackBoxProjectHasOneGroupInTheFolderOfItsGroup` |
| R3 | A test has at most one lane, with `@lane`. | `EachTestOfTheBlackBoxProjectHasAtMostOneLane` |
| R4 | No assembly from `src/` is visible to the black-box project. | `TheBlackBoxProjectMakesNoProductAssemblyVisible`, `TheRestoredDependenciesOfTheBlackBoxProjectHaveNoProductProject` |
| R5 | A step or a test of the black-box project uses the drivers. | `TheSourcesOfTheBlackBoxProjectUseTheDrivers`. **Partial:** it finds a fixed list of names (`System.Diagnostics`, `Process`, `ChildProcess`, `TestSandbox`, `File.`, `Directory.`, `Path.GetTempPath`, `Environment.`). A call through another type passes it, and an expression in an interpolated string is not read. Read each new step. |
| R6 | A new step is in the step table, and it does not repeat an old step with other words. | `TheStepTableOfTheReadmeIsCurrent` finds a step that is not in the table and two steps with the same pattern. **Partial:** it does not find two patterns with the same meaning. |
| R7 | Each test that a pull request must run has one run that supports it. | `EachPullRequestTestHasOneRunThatSupportsIt` |
| R8 | A group with a test is a suite of the plan, and a suite has a test. | `EachGroupWithATestIsASuiteOfThePlanAndEachSuiteHasATest` |
| R9 | A test that moved keeps its checks: the move table has each discovered test, and no check is weaker with no stated reason. | None: a reviewer must check it. |
| R10 | A test that needs a product assembly and an artifact is split. | None: a reviewer must check it. |
| R11 | A unit test that uses the operating system has the `PlatformSpecific` marker. A black-box test has no marker. | None for the unit test. |
| R12 | When the rules of this skill change, `docs/contributing/ci-os-matrix.md` and `tests/Morphir.BlackBox.Tests/README.md` change in the same change. | None: a reviewer must check it. |
