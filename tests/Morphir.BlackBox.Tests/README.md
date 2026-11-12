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
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend` | A capability that the test needs from the drivers. |
| `@needs-compilation`, `@needs-configuration`, `@needs-migration`, `@needs-extensions`, `@needs-model-validation` | A command family that the test needs from the drivers: `compile`, `config`, the migrations, `extension`, `validate`. |

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
| Configuration editing | `Given a configuration editing project` |
| Configuration editing | `Given the editing project has mixed primary layouts and a personal override` |
| Configuration editing | `Given the editing project is a workspace with two members` |
| Configuration editing | `Then editing uses the existing personal target` |
| Configuration editing | `Then the personal edit is a member output of the personal file` |
| Configuration editing | `Given an editing environment winner` |
| Configuration editing | `Given the editing target has an opaque reference` |
| Configuration editing | `Then editing streams redact opaque reference inputs` |
| Configuration editing | `Then editing preserves unrelated comments` |
| Configuration editing | `When I edit configuration with {string}` |
| Configuration editing | `Given the editing project is a member below an outer workspace` |
| Configuration editing | `Given the editing home has a global configuration` |
| Configuration editing | `Given editing environment variable {word} is {string}` |
| Configuration editing | `When I edit configuration without a boundary flag with {string}` |
| Configuration editing | `Then the editing plan consults no project source outside {string}` |
| Configuration editing | `Then the editing plan consults no machine or personal sources` |
| Configuration editing | `When I set a complete typed object` |
| Configuration editing | `When I set an opaque typed reference` |
| Configuration editing | `When I edit a literal dotted key` |
| Configuration editing | `Then the persisted editing value has type {string}` |
| Configuration editing | `Given a classic JSON value below the editing target` |
| Configuration editing | `Then the lower JSON bytes and origin are retained` |
| Configuration editing | `Then system preview respects the native no-follow policy` |
| Configuration editing | `Then editing exits {int} with outcome {string}` |
| Configuration editing | `Then the editing target retains its original bytes` |
| Configuration editing | `Then the editing effective value is {string}` |
| Configuration | `Given an inspection workspace with a member and a personal override` |
| Configuration | `Given the inspection workspace contains sibling YAML` |
| Configuration | `Given the inspection workspace has an invalid default member` |
| Configuration | `Given the inspection member configuration is malformed` |
| Configuration | `Given the invocation directory contains malformed host appsettings` |
| Configuration | `Given environment profiles contain an empty name` |
| Configuration | `Given the member contains a TOML table with {int} path segments` |
| Configuration | `Then the nested table at {int} path segments retains its values` |
| Configuration | `Given the member contains nonfinite and temporal values` |
| Configuration | `Given environment variable {word} is {string}` |
| Configuration | `When I inspect without a boundary flag with {string}` |
| Configuration | `Then inspection consults no project source outside {string}` |
| Configuration | `Then inspection consults the workspace primary` |
| Configuration | `Then inspection consults no sources` |
| Configuration | `Then machine and personal sources are not consulted` |
| Configuration | `Then machine and personal sources are consulted` |
| Configuration | `Then the reported boundary is {string} from {string}` |
| Configuration | `Then the reported isolation is {word} from {string}` |
| Configuration | `Then the inspection diagnostic names {string}` |
| Configuration | `Given a home directory with a global Morphir file and a bare project below it` |
| Configuration | `Given MORPHIR_HOME is the native path of the global Morphir home` |
| Configuration | `When I inspect the bare project with {string}` |
| Configuration | `Then the global Morphir file is consulted only as a global source` |
| Configuration | `When I inspect with {string}` |
| Configuration | `When I inspect a key containing a literal dot` |
| Configuration | `When I request human inspection output` |
| Configuration | `When I request inspection help` |
| Configuration | `Then inspection exits with {int} and one JSON result` |
| Configuration | `Then special values preserve their canonical kinds` |
| Configuration | `Then nonfinite explanation metadata identifies its value and contribution` |
| Configuration | `Then the failure is a usage diagnostic without a source read` |
| Configuration | `Then the inspection result operation is {string}` |
| Configuration | `Then the inspection value is true` |
| Configuration | `Then the inspection value is {string}` |
| Configuration | `Then the inspection value is an empty array` |
| Configuration | `Then the queried key is absent` |
| Configuration | `Then inspection diagnostics include {string}` |
| Configuration | `Then inspection diagnostics exclude {string}` |
| Configuration | `Then the get entry identifies its member origin` |
| Configuration | `Then human inspection succeeds without exposing sensitive values` |
| Configuration | `Then the explanation identifies workspace and member sources` |
| Configuration | `Then structured warnings appear in JSON and human diagnostics only on stderr` |
| Configuration | `Then the mixed format warning is suppressed` |
| Configuration | `Then neither stream exposes secret values or executes the secret command` |
| Configuration | `Then inspection help succeeds without configuration diagnostics` |
| Project migration | `Given a classic migration project` |
| Project migration | `Given malformed migration host appsettings` |
| Project migration | `Given migration fixture {string}` |
| Project migration | `Given the migration target contains equivalent values and comments` |
| Project migration | `Given a conflicting migration target and ambient overrides` |
| Project migration | `Given the migration project is one workspace member` |
| Project migration | `Given an unlisted migration project invoked from {string}` |
| Project migration | `Then the unlisted migration leaves every project unchanged` |
| Project migration | `When I migrate the project with {string}` |
| Project migration | `Given migration environment variable {word} is {string}` |
| Project migration | `Given a personal override masks the migration source directory` |
| Project migration | `When I migrate the project without a boundary flag with {string}` |
| Project migration | `Then the migration plan reads no workspace` |
| Project migration | `Then migration verification keeps the classic source directory` |
| Project migration | `When I migrate the project with human output` |
| Project migration | `Then migration exits {int} with outcome {string}` |
| Project migration | `Then the migration source remains byte identical` |
| Project migration | `Then the migration report verifies independent equivalence` |
| Project migration | `Then no migration target exists` |
| Project migration | `Then migration writes {string} with the legacy defaults` |
| Project migration | `Then migration preserves the existing equivalent text` |
| Project migration | `Then the repeated migration preserves the target timestamp` |
| Project migration | `Then migration reports blocking diagnostics on stderr` |
| Project migration | `Then migration reports both conflicting values and origins` |
| Project migration | `Then migration preserves the conflicting target` |
| Project migration | `Then migration changes only the selected member target` |
| Project migration | `Then migration streams redact secret inputs` |
| Project migration | `Then migration human output reports a verified preview` |
| Workspace migration | `Given an empty workspace migration repository` |
| Workspace migration | `Given a workspace migration repository` |
| Workspace migration | `Given workspace migration project {string}` |
| Workspace migration | `Given workspace migration skipped and nested projects` |
| Workspace migration | `Given an existing workspace migration member and default` |
| Workspace migration | `Given workspace migration adoption fixture {string}` |
| Workspace migration | `Then workspace migration excludes root conversion` |
| Workspace migration | `Then workspace migration reports the new member original context` |
| Workspace migration | `Given a hidden workspace migration member primary` |
| Workspace migration | `Given a declared empty migration workspace` |
| Workspace migration | `Given a workspace migration root project` |
| Workspace migration | `Given workspace migration invoked from a bounded child` |
| Workspace migration | `Given workspace migration fixture {string}` |
| Workspace migration | `Given malformed workspace migration appsettings` |
| Workspace migration | `Given workspace migration environment variable {word} is {string}` |
| Workspace migration | `When I migrate the workspace with boundary {string} and {string}` |
| Workspace migration | `Then workspace migration diagnostics include {string}` |
| Workspace migration | `Then workspace migration diagnostics name {string}` |
| Workspace migration | `When I migrate the workspace with {string}` |
| Workspace migration | `When I migrate the workspace with human output` |
| Workspace migration | `When I request the configuration migration alias` |
| Workspace migration | `Then workspace migration exits {int} with outcome {string}` |
| Workspace migration | `Then workspace migration retains every classic source` |
| Workspace migration | `Then workspace migration verifies the aggregate and every conversion` |
| Workspace migration | `Then workspace migration reports members {string}` |
| Workspace migration | `Then workspace migration reports excluded {string}` |
| Workspace migration | `Then workspace migration reports boundary {string}` |
| Workspace migration | `Then workspace migration writes no destinations` |
| Workspace migration | `Then workspace migration retains the existing member and default` |
| Workspace migration | `Then workspace migration retains root project identity` |
| Workspace migration | `Then workspace migration keeps the hidden member anchor` |
| Workspace migration | `Then workspace migration preserves every target byte and timestamp` |
| Workspace migration | `Then workspace migration reports diagnostics on stderr` |
| Workspace migration | `Then workspace migration accounts for unsupported raw fields` |
| Workspace migration | `Then workspace migration streams redact secret values` |
| Workspace migration | `Then workspace migration human output reports verified preview` |
| Workspace migration | `Then configuration migration remains unavailable` |

The suites of the compatibility kit in `conformance/suites/` have their own step vocabulary and their own runner.
A kit feature does not use a step of this project.

## How to run

Build first (`./build.sh --target Compile`), or let the build of the project build the three entry points.

```bash
# Each suite of the Test target, with the groups of this project.
./build.sh --target Test --skip Restore Compile

# One group, and no other suite or check.
./build.sh --target Test --skip Restore Compile --test-group smoke

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
