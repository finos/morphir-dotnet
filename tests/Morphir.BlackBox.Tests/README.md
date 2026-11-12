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
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend`, `@needs-development-packages` | A capability that the test needs from the drivers. `@needs-development-packages`: the test packs a project of the development build into the feed of its workspace (`IPackageDriver`). |

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
| CLI | `Then the output does not contain {string}` |
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
| Packages | `Given the package {string} packed from the development build` |
| Packages | `Given the packages {string} packed from the development build` |
| Packages | `Given the packed package {string}` |
| Packages | `Given the packed package {string} in the feed of the workspace` |
| Packages | `Then the manifest of the package has the id {string}` |
| Packages | `Then the authors of the package include {string}` |
| Packages | `Then the package has a license` |
| Packages | `Then the project URL of the package contains {string}` |
| Packages | `Then the package type of the package is {string}` |
| Packages | `Then the version of the package is the version of the changelog` |
| Packages | `Then the packed packages {string} have one version` |
| Packages | `Then each of the packed packages {string} has release notes that contain {string}` |
| Packages | `Then each Morphir dependency of each packed package is packed at its version` |
| Packages | `Then the package has the entries:` |
| Packages | `Then the package has an entry below {string}` |
| Packages | `Then the package has no entry below {string}` |
| Packages | `Then the package has an entry that contains {string}` |
| Packages | `Then the package has no entry with the file name {string}` |
| Packages | `Then the entry {string} of the package contains the text:` |
| Packages | `Then the packed packages {string} have no entry that ends with {string}` |
| Packages | `Then the packed packages {string} have no entry that contains {string}` |
| Packages | `Then each Morphir assembly of the package below {string} has the assembly version of the changelog` |
| Packages | `Then each reference between the Morphir assemblies of the package below {string} has the assembly version of the changelog` |
| Packages | `Then the package has no assembly` |
| Packages | `Then the package depends only on {string} at exactly its own version` |
| Package consumer | `When I build the consumer` |
| Package consumer | `When I build the consumer with no restore` |
| Package consumer | `When I build the consumer and it fails` |
| Package consumer | `When I build the consumer with no restore and it fails` |
| Package consumer | `When I build the consumer in the configurations {string} and {string} at the same time with no restore` |
| Package consumer | `When I run the consumer` |
| Package consumer | `When I build and run the consumer` |
| Package consumer | `When I build and run the consumer with no restore` |
| Package consumer | `When I clean the consumer` |
| Package consumer | `Given the consumer program is:` |
| Package consumer | `Given the consumer program is the fixture {string}` |
| Package consumer | `Given the consumer types are:` |
| Package consumer | `Given the consumer types are the fixture {string}` |
| Package consumer | `Given the consumer has the property {string} with the value {string}` |
| Package consumer | `Then the consumer prints {string}` |
| Package consumer | `Then the consumer prints:` |
| Package consumer | `Then the first line that the consumer prints is:` |
| Package consumer | `Then the consumer prints {int} lines` |
| Package consumer | `Then the consumer reports a complete rental costing {int} cents` |
| Package consumer | `Then the consumer rejects invalid rental operations` |
| Package consumer | `Then the consumer has the file {string}` |
| Package consumer | `Then the consumer has no file {string}` |
| Package consumer | `Then the consumer has no directory {string}` |
| Package consumer | `Then the consumer directory {string} has no file` |
| Package consumer | `Then the consumer has {int} generated file(s) {string}` |
| Package consumer | `Then the consumer has no generated file {string} in a folder {string}` |
| Package consumer | `Given I remember the generated files of the consumer` |
| Package consumer | `Then the generated files of the consumer are the same as I remembered` |
| Package consumer | `Given I remember the text of the generated file {string}` |
| Package consumer | `Then the text of the generated file {string} is the same as I remembered` |
| Package consumer | `Given I put the file {string} with the text {string} beside the generated file {string}` |
| Package consumer | `Then the file {string} beside the generated files has the text {string}` |
| Package consumer | `Given I wait {int} milliseconds` |
| Myriad plugin | `Given a Myriad consumer of the model fixture {string}` |
| Myriad plugin | `Given a Myriad consumer of a model with the modules:` |
| Myriad plugin | `Given a Myriad consumer of the models:` |
| Myriad plugin | `Given a Myriad consumer with no model file` |
| Myriad plugin | `Given the consumer program is the car rental example` |
| Myriad plugin | `Given the consumer program prints the daily rate of {string}` |
| Myriad plugin | `When the consumer program prints the daily rate of {string}` |
| Myriad plugin | `Given the consumer also uses the Myriad plugin with the model fixture {string}` |
| Myriad plugin | `Given the consumer program also prints the daily rate of {string}` |
| Myriad plugin | `Given the model {string} of the consumer has the output directory {string}` |
| Myriad plugin | `Given the model {string} of the consumer has the output directory {string} with the separator of the operating system at its end` |
| Myriad plugin | `When the model {string} of the consumer has the full path of the output directory {string} with the separator of the operating system at its end` |
| Myriad plugin | `Given the consumer folder {string} is a symbolic link to its folder {string}` |
| Myriad plugin | `When the model of the consumer changes to the modules:` |
| Myriad plugin | `When the model of the consumer is the fixture {string}` |
| Myriad plugin | `Given the daily rate of the module {string} of the model is the string {string}` |
| Myriad plugin | `When the daily rate of the car rental model changes to {int} cents` |
| Myriad plugin | `When the model of the consumer changes to the invalid input {string}` |
| System.Text.Json plugin | `Given a System.Text.Json consumer` |
| System.Text.Json plugin | `Given the context of the consumer has the name {string}` |
| System.Text.Json plugin | `Given the context of the consumer has the metadata:` |
| F# SDK packages | `Given the SDK packages built and packed in the workspace with the version of the changelog` |
| F# SDK packages | `Given the SDK packages built and packed in the workspace at the version {string}` |
| F# SDK packages | `Then the build of the package {string} in the workspace made no assembly` |
| F# SDK packages | `Then the precompiled legacy consumer has its recorded SHA-256` |
| F# SDK packages | `When a consumer of the package graph {string} restores, builds and runs` |
| F# SDK packages | `Then the consumer resolves one runtime assembly, from the package {string}` |
| F# SDK packages | `Then the consumer resolves only the packages of its graph` |
| F# SDK packages | `Then the runtime assembly in the output of the consumer is the one of the package {string}, with its identity` |
| F# SDK packages | `Then the consumer keeps each public declaration of the legacy runtime` |
| F# SDK packages | `Then the precompiled legacy consumer ran unchanged on the runtime of the package {string}` |
| F# SDK packages | `When a consumer of the generated Ordering source of {string} builds and runs against the package {string}` |
| F# SDK packages | `Then the runtime assembly in the output of the consumer is the runtime of the package {string}` |
| Tools | `When I install the tool {string} from the feed into a tool folder` |
| Tools | `When I uninstall the tool {string} from the tool folder` |
| Tools | `When I run the installed command {string} with {string}` |
| Tools | `Then the tool folder has the command {string}` |
| Tools | `Then the tool folder has no command {string}` |
| Tools | `When I send a describe request to the installed command {string}` |
| Tools | `Then the describe reply has the extension id {string}` |
| Tools | `Then the describe reply has the claims version {string}` |
| Tools | `Then the extension version of the describe reply is the version of the package {string}` |
| Tools | `Then the describe reply claims the model transport tuples of {string} for the IR versions {string}` |

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

The packages lane has the tests that read the packages of the pack step (`@lane @packages`). The `TestBuild`
target runs it after the build tests, with the output folder of `PackAll`:

```bash
./build.sh --target PackAll --skip Restore Compile
./build.sh --target TestBuild --skip Restore Compile

# The test assembly, with the filter of the lane and the folder of the packed packages.
MORPHIR_TEST_LANE=packages MORPHIR_TEST_PACKAGES="$PWD/artifacts/packages" dotnet exec tests/Morphir.BlackBox.Tests/bin/Release/net11.0/Morphir.BlackBox.Tests.dll --treenode-filter "/*/*/*/*[Category=packages]"
```

`PackAll` packs the tool and the extensions with no symbol files, so it builds them again. Run
`./build.sh --target Compile` after it, before a `Test` run: the package driver packs the projects with no build.

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
