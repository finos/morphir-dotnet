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
| `@needs-validation`, `@needs-native-and-tool-hosts`, `@needs-packaged-installation`, `@needs-native-aot-executable`, `@needs-known-form` | A capability that the test needs from the drivers (see `tests/Morphir.TestSupport/README.md`). |

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
| CLI | `Then standard output contains {string}` |
| CLI | `Then standard error does not contain {string}` |
| Model verification | `When I verify the model with the schema version {int} as JSON` |
| Model verification | `Then the verification report has the detection method {string}` |
| Validation | `When I validate the model` |
| Validation | `When I validate the model as text` |
| Validation | `When I validate the model on the other host` |
| Validation | `Then the validation report has the exit code {int}` |
| Validation | `Then the validation report has no diagnostic` |
| Validation | `Then the validation report has {int} {string} diagnostic with the code {string} from {string}` |
| Validation | `Then the validation report has {int} {string} diagnostic with the code {string}` |
| Validation | `Then the validation report lists {string} as {string}` |
| Validation | `Then the analyzers of the validation report are the validator entries of extension list` |
| Validation | `Then the validation reports of both hosts are equal apart from the host` |
| Extension list | `When I list the extensions as JSON` |
| Extension list | `When I list the extensions as text` |
| Extension list | `When I list the extensions as JSON on the other host` |
| Extension list | `Then the extension list names the host of the run` |
| Extension list | `Then the extension list has the ids {string}` |
| Extension list | `Then each listed extension except {string} is available in the host` |
| Extension list | `Then the in-process F# frontend is available exactly when the host has it` |
| Extension list | `Then the extension list names the deployment of the target` |
| Extension list | `Then the extension {string} has the types {string}` |
| Extension list | `Then the extension {string} has the delivery {string} and the hosts {string}` |
| Extension list | `Then the extension {string} claims the validator IR versions {string}` |
| Extension list | `Then the extensions that are available in both hosts are equal in both lists` |
| Extension worker | `When I ask the worker for its help as JSON` |
| Extension worker | `When the worker serves {string} with the frames {string} and the arguments {string}` |
| Extension worker | `When the worker serves {string} with the frames {string}` |
| Extension worker | `When the worker is started with another build identity for a module file that is not there` |
| Extension worker | `When the worker serves a text file named {string}` |
| Extension worker | `When the worker serves a module file that is not there` |
| Extension worker | `When the worker is started with the extra argument {string}` |
| Extension worker | `When the guest has made its private workspace` |
| Extension worker | `When I interrupt the worker` |
| Extension worker | `Then the worker exits with status {int}` |
| Extension worker | `Then the worker exits with status {int} within one second of the interrupt` |
| Extension worker | `Then standard output of the worker is one command result whose build identity is 64 hexadecimal digits` |
| Extension worker | `Then the help in the command result contains {string}` |
| Extension worker | `Then standard output of the worker holds only these frames:` |
| Extension worker | `Then standard output of the worker holds only {int} frames that are each {}` |
| Extension worker | `Then standard output of the worker is empty` |
| Extension worker | `Then the guest output contains {string}` |
| Extension worker | `Then standard error of the worker has the events {string}` |
| Extension worker | `Then standard error of the worker has exactly one {string} event with the code {string}` |
| Extension worker | `Then standard error of the worker has no {string} event` |
| Extension worker | `Then the fault has the kind {string} and a message that contains {string}` |
| Extension worker | `Then each output event line of the worker is at most {int} bytes` |
| Extension worker | `Then the output events state that more than {int} MiB of guest output were dropped` |
| Extension worker | `Then standard error of the worker holds less than {int} KiB` |
| Extension worker | `Then the peak working set of the worker is less than {int} MiB above that of an echo session` |
| Extension worker | `Then the temporary directory of the worker is empty` |
| Extension runtimes | `Given a copy of the host without its engine library` |
| Extension runtimes | `When I ask for the runtimes of the host as JSON` |
| Extension runtimes | `When I ask for the runtimes of the copy as JSON` |
| Extension runtimes | `When I run {string} with the copy` |
| Extension runtimes | `When the copy serves {string} with the frames {string}` |
| Extension runtimes | `Then the runtime report names the host of the run` |
| Extension runtimes | `Then the WASM runtime is available exactly when the platform can run guests` |
| Extension runtimes | `Then the WASM runtime has the engine of the pinned Wasmtime package, the profile {string} and {long} bytes of memory` |
| Extension runtimes | `Then the WASM runtime is not available, with the reason of a missing engine on this platform` |
| Extension runtimes | `Then the WASM runtime has the engine version of the pinned Wasmtime package` |
| Extension runtimes | `Then the worker is refused with {string} and the reason of a missing engine on this platform` |
| Extension runtimes | `Then the command of the copy wrote its result with no word of the engine` |
| Packaged artifact | `Given the packaged artifact of the run is installed into a new directory whose path contains a space` |
| Packaged artifact | `Then the archive matches the SHA-256 in its launch metadata` |
| Packaged artifact | `Then the launch path of the launch metadata is in the installed directory` |
| Packaged artifact | `Then the installed layout is what the offering claims` |
| Packaged artifact | `When I run the installed artifact with {string} from a new empty directory` |
| Packaged artifact | `When I run the installed artifact with {string} with no .NET runtime reachable` |
| Packaged artifact | `When I run the installed artifact with {string} with the .NET runtime of the machine reachable` |
| Packaged artifact | `When I compile an F# source file with the installed artifact` |
| Packaged artifact | `When I validate the model fixture {string} with the installed artifact` |
| Packaged artifact | `Then the installed artifact exits with code {int}` |
| Packaged artifact | `Then the installed artifact prints the release of its launch metadata` |
| Packaged artifact | `Then the installed artifact lists the extensions of its host` |
| Packaged artifact | `Then the F# compile follows the in-process F# frontend of the launch metadata` |
| Packaged artifact | `Then the installed artifact runs without a runtime as its launch metadata says` |
| Packaged artifact | `Then the installed artifact uses the runtime that its launch metadata declares` |
| Packaged artifact | `Then the engine libraries in the installed artifact are exactly the separate libraries that its build contents record` |
| Packaged artifact | `Then the installed artifact reports its WASM runtime availability with the pinned engine on this platform` |
| Packaged artifact | `When the installed artifact serves the pinned module {string} with the frames {string} in a worker session` |
| Packaged artifact | `Given a second installation of the artifact of the run, made read-only before its first run` |
| Packaged artifact | `Then the read-only installation holds the same files as before its first run` |
| Host file | `Then the folder of the host has no file {string}` |

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

The release and manual workflows run each group against a published or packaged target with `TestE2E`
(`scripts/run-e2e-tests.cs`). It publishes or packs a target that has no artifact, and runs each group with
`MORPHIR_TEST_LANE=published`: the tests with no lane and the tests of the published lane.

```bash
# Each group on the Native AOT executable; the script publishes it first when it is not there.
./build.sh --target TestE2E --executable-type aot

# Each group on the four packaged offerings.
./build.sh --target TestE2E --executable-type offerings
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
