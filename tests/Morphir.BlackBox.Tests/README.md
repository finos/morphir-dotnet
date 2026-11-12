# Morphir.BlackBox.Tests

The black-box tests of the product: the tests that reach it only through a built artifact (the command line of an
executable, a package, a published folder or an archive).

This project references `Morphir.TestSupport` only. No assembly from `src/` is visible to its code. A step or a
test starts no process, makes no temporary directory and reads no environment variable: it calls a driver
interface of `Morphir.TestSupport.Drivers` (see [the README of the testing library](../Morphir.TestSupport/README.md)).

Before you add, move, rename or delete a test here, use the `test-manager` skill
(`.agents/skills/test-manager/SKILL.md`). It has the classification steps and the patterns.

## The four groups

The group of a test states what the test proves. The form of the product on which a test runs is the target, and
the target never changes the group.

| Group | Tag | Folder | What it proves |
| --- | --- | --- | --- |
| Integration | `@integration` | `Integration/` | The components behind one capability work together. A command can run more than once. |
| End-to-end | `@e2e` | `EndToEnd/` | The delivered form itself: installation, layout, start, and a property that the delivered form must have. |
| Smoke | `@smoke` | `Smoke/` | An artifact starts and does one basic function. |
| Journey | `@journey` | `Journeys/` | One user goal that needs two or more capabilities. The last check is against the goal. |

Each test has exactly one group, and its file is in the folder of that group. The step classes are in `Steps/`,
one for each capability, shared by the four groups.

## The form of a test

A black-box test is a Gherkin scenario. Three forms are framework tests (TUnit) instead: a loop over a corpus, a
property test, and a test with a time budget. A framework test derives from `BlackBoxTest`, has the category of its
group on its class (`[Category("smoke")]`), and gets its drivers from the base class.

A test that needs a product assembly in the test process is not a black-box test. It stays in the test project of
its library. A test that needs both an artifact and a product assembly is two tests.

## Tags

| Tag | Meaning |
| --- | --- |
| `@integration`, `@e2e`, `@smoke`, `@journey` | The group. One for each test, on the feature or on the scenario. |
| `@lane` with one of `@packages`, `@native-aot`, `@published` | The test needs more than the development build: the packed packages, a Native AOT publish, or a published executable. A group run does not select it. |
| `@host-native`, `@host-managed`, `@host-tool` | The test runs on that entry point. With no host tag, a test runs on the default host of the run. |
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend`, `@needs-built-programs` | A capability that the test needs from the drivers. `@needs-built-programs`: the build output of the programs of the repository that are not the CLI (library hosts, probes, morphirup); only the development build has them. |

Before the first step, one decision says if the test runs: the lane, then the host, then the capabilities. A test
that the run does not execute ends as skipped with one reason, for example `lane: test packages, run none`.

## Steps

Reuse a step before you write a new one. This table has each step of `Steps/`. A guard test
(`BlackBoxRulesTests.TheStepTableOfTheReadmeIsCurrent`) fails when the table and the step classes differ, and when
two steps have the same pattern.

| Capability | Step |
| --- | --- |
| CLI | `When I run the command {string}` |
| CLI | `Then the exit code is {int}` |
| CLI | `Then the output contains {string}` |
| CLI | `Then the output contains {string} or {string}` |
| CLI | `Then the output is empty` |
| CLI | `Then standard output is empty` |
| CLI | `Then standard error contains {string}` |
| CLI | `Then standard output is one JSON document` |
| CLI | `Then standard output is a version` |
| CLI | `Then the command ran on the {string} host` |
| Model | `Given the model fixture {string}` |
| Model | `Given the model tree fixture {string}` |
| Model | `Given no model file {string}` |
| Model | `When I replace {string} with {string} in the model` |
| Model | `When I remove the module {string} from the model` |
| Model verification | `When I verify the model` |
| Model verification | `When I verify the model with the schema version {int}` |
| Model verification | `When I verify the model as JSON` |
| Model verification | `When I verify the model in quiet mode` |
| Model verification | `Then the verification report says that the model is valid` |
| Model verification | `Then the verification report says that the model is not valid` |
| Model verification | `Then the verification report has the schema version {string}` |
| Model verification | `Then the verification report has errors` |
| Generation | `When I generate {string} from the model` |
| Generation | `When I generate {string} from the model as text` |
| Generation | `When I generate {string} from the model with the option {string}` |
| Generation | `When I generate {string} from the model with the options` |
| Generation | `Then the generation report says that the generation succeeded` |
| Generation | `Then the generation report says that the generation failed with the kind {string}` |
| Generation | `Then the generation report has the backend {string}` |
| Generation | `Then the generation report has the source version {string}` |
| Generation | `Then the generation report has diagnostics` |
| Generation | `Then the first diagnostic of the generation report has the code {string} and a location` |
| Generation | `Then the generation report has no artifact` |
| Generation | `Then the generation report has no written file and has unchanged files` |
| Generation | `Then the generation report says that it removed only {string}` |
| Generation | `Then each artifact of the generation report is a file of the output` |
| Generation | `Then the first artifact of the generation report contains {string} and not {string}` |
| Generation | `Then the output has one C# file for the module {string} of the package {string}` |
| Generation | `Given I remember the files of the output` |
| Generation | `Then the files of the output are the same as I remembered` |
| Generation | `Then the output directory is not there` |
| Generation | `Given the output has the file {string} with the text {string}` |
| Generation | `Then the output file {string} has the text {string}` |
| Generation | `Then the output has no file {string}` |
| Generation | `Given each file of the output has the time of the year 2000` |
| Generation | `Then each file of the output still has the time of the year 2000` |
| CLI | `Then the JSON document on standard output has the field {string} with the value true` |
| Library hosts | `When I run the program {string}` |
| Library hosts | `When I plan the morphirup setup of the probe with no ICU` |
| Library hosts | `When I publish the program with Native AOT` |
| Library hosts | `When I run the published program` |
| Library hosts | `Then the published program writes the same output as the built program` |
| Library hosts | `Then the publish has no trim or AOT warning except the summary warnings of {string}` |
| Build output | `Given the build output of the program {string}` |
| Build output | `Then the runtime configuration {string} sets {string} to true` |
| Build output | `Then the assembly {string} references no member {string} of {string}` |
| Build output | `Then the assembly {string} references no assembly {string} and no type of its namespace` |
| Build output | `Then the assembly {string} uses no type of the namespace {string}` |

The suites of the compatibility kit in `conformance/suites/` have their own step vocabulary and their own runner.
A kit feature does not use a step of this project.

## How to run

Build first (`./build.sh --target Compile`), or let the build of the project build the three entry points.

```bash
# Each suite of the Test target, with the groups of this project.
./build.sh --target Test --skip Restore Compile

# One group, and no other suite or check.
./build.sh --target Test --skip Restore Compile --test-group smoke

# The Native AOT lane: publish each probe, each library host and morphirup with Native AOT, and compare.
./build.sh --target TestAot --skip Restore Compile

# The test assembly, with the filter of a group.
dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "/*/*/*/*[(Category=smoke)&(Category!=lane)]"
```

The default target is the development build (`dev`), with the native host as the default host. To run on another
host or target, set the variables of the testing library:

```bash
# The tests with no host tag run on the dotnet-morphir tool.
MORPHIR_TEST_HOST=tool ./build.sh --target Test --skip Restore Compile --test-group smoke

# Keep the workspace of a failed test. The log gives its path.
MORPHIR_TEST_KEEP=failed ./build.sh --target Test --skip Restore Compile --test-group smoke
```

## The rules that a program checks

`tests/Morphir.Build.Tests/Helpers/BlackBoxRulesTests.cs` checks these rules; the `TestBuild` target runs it.

| Rule | Check |
| --- | --- |
| `one-group` | Each scenario and each framework test has one group, and its file is in the folder of the group. |
| `one-lane` | A test with `@lane` has exactly one lane tag, and a test with a lane tag has `@lane`. |
| `no-product-assembly` | A reference below `src/` has `ReferenceOutputAssembly="false"`, and the restored dependencies have no project from `src/`. |
| `use-drivers` | The sources of this project have none of the names that go past a driver. This check is partial: it finds the listed names only. |
| `step-table` | The step table of this document is current, and no two steps have the same pattern. |
| `pull-request-run` | Each test that a pull request must run has one run that has its host and supports all its capabilities. |
| `group-suite` | A group that has a test is a suite of `build/Helpers/TestPlan.cs`, and a suite selects a test. |
