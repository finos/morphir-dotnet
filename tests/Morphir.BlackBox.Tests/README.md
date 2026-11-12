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
| `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend`, `@needs-built-programs`, `@needs-development-packages` | A capability that the test needs from the drivers. `@needs-built-programs`: the build output of the programs of the repository that are not the CLI (library hosts, probes, morphirup); only the development build has them. `@needs-development-packages`: the test packs a project of the development build into the feed of its workspace (`IPackageDriver`). |
| `@needs-compilation`, `@needs-configuration`, `@needs-migration`, `@needs-extensions`, `@needs-model-validation`, `@needs-built-extensions`, `@needs-host-without-dynamic-code`, `@needs-all-hosts`, `@needs-unprofiled-build`, `@needs-tool-routing` | A command family that the test needs from the drivers (`compile`, `config`, the migrations, `extension`, `validate`), the extension guests of the development build, a host with the dynamic-code switch of the runtime off, each host of the target in one test, a host that no release profile built, or the routing of a command to an external tool. |
| `@needs-packaged-installation`, `@needs-native-aot-executable`, `@needs-known-form`, `@needs-wasm-guests` | A capability that the test needs from the drivers (see `tests/Morphir.TestSupport/README.md`): a packaged installation, a Native AOT executable, a target of one known form, or the WASM guests of the host. `@needs-wasm-guests` is not on Windows x64 (#609). |

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
| CLI | `Then standard output is the text {string}` |
| CLI | `Then standard error does not contain {string}` |
| CLI | `Then standard output does not contain {string}` |
| CLI | `Then standard output contains {string}` |
| Model | `Given the model fixture {string}` |
| Model | `Given the model tree fixture {string}` |
| Model | `Given no model file {string}` |
| Model | `When I replace {string} with {string} in the model` |
| Model | `When I remove the module {string} from the model` |
| Model | `Given the model has the problem {string}` |
| Model | `Given the model fixture {string} as {string}` |
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
| Generation | `Then generation exits with {int} and one JSON report` |
| Generation | `When I generate {string} from the model without the setting {string}` |
| Generation | `When I generate {string} from the model with the arguments {string}` |
| Generation | `When I generate {string} from the model with the input format {string} and the options` |
| Generation | `When I generate {string} from the model with the built F# backend extension` |
| Generation | `When I generate {string} from the model with the built F# backend extension and the option {string}` |
| Generation | `Given a project whose configuration selects the built F# backend extension for {string}` |
| Generation | `When I generate {string} from the model for the project` |
| Generation | `When I generate {string} from the model for the project with the extension {string}` |
| Generation | `Then the generation report names the external F# backend over MEP` |
| Generation | `Then the generation report has no member {string}` |
| Generation | `Then the generation report has these members in this order` |
| Generation | `Then the generation report has the diagnostic {string} that names {string}` |
| Generation | `Then the first diagnostic of the generation report has the code {string}` |
| Generation | `Then the generation report has the target {string}` |
| Generation | `Then the generation report has artifacts` |
| Generation | `Then the generation report has one C# artifact that contains {string} and {string}` |
| Generation | `Then the generation report lists only the artifact {string}` |
| Generation | `Then the output file {string} has the bytes of the fixture {string}` |
| Generation | `Given I remember the artifacts of the generation report` |
| Generation | `Then the generation report has the remembered artifacts with the same text in the same order` |
| Generation | `Then standard output lists only the artifacts of the output` |
| Generation | `When I generate {string} from the model on a host without dynamic code` |
| Generation | `When I generate {string} from the model as text on a host without dynamic code` |
| Generation | `When I generate {string} from the model on a host without dynamic code with the option {string}` |
| Generation | `Then the generation report has the formatting {string}` |
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
| Compilation | `Given an F# project whose morphir.toml names its project file` |
| Compilation | `Given an F# project without a configured project file` |
| Compilation | `Given the source {string} uses a mutable binding` |
| Compilation | `When I compile the project at the IR version {string}` |
| Compilation | `When I compile its input files at the IR version {string}` |
| Compilation | `When I compile the project with the project file {string}` |
| Compilation | `When I compile the project` |
| Compilation | `When I compile the project and install the IR to {string}` |
| Compilation | `When I generate F# for the project from its last compile` |
| Compilation | `When I compile the project from its {word} {word} with the target {string}` |
| Compilation | `When I compile the project from its {word} {word} and then generate {string} from the compiled IR` |
| Compilation | `When I compile its input files with the options {string}` |
| Compilation | `Given the F# source file {string} of the package {string}` |
| Compilation | `When I compile the F# source file` |
| Compilation | `Then the compilation report says that the compile succeeded with the frontend {string}` |
| Compilation | `Then compilation exits with {int} and one JSON report` |
| Compilation | `Then the compilation report has the IR version {string}` |
| Compilation | `Then the published IR declares format version {int}` |
| Compilation | `Then the command report has the diagnostic {string}` |
| Compilation | `Then the compilation report has the diagnostic {string} in {string}` |
| Compilation | `Then no IR is published` |
| Compilation | `Then the compilation report lists the source documents {string} in that order` |
| Compilation | `Then the generation input is the IR named by the compile record` |
| Compilation | `Then both reports name the same backend` |
| Compilation | `Then both generated outputs have the same paths and bytes` |
| Compilation | `Then the compile record is a tombstone that keeps the earlier install ledger` |
| Documentation | `Given the documentation page {string}` |
| Documentation | `Given the section {string} of the documentation page {string}` |
| Documentation | `Then the page documents these commands` |
| Documentation | `Given the source file {string}:` |
| Documentation | `When I run each documented command of the host on the model` |
| Documentation | `When I run each documented command of the host in the work area` |
| Documentation | `Then each documented command exits with {int}` |
| Documentation | `Then each documented command gives its documented output` |
| Documentation | `Given the morphir.toml of the section in the project directory {string}` |
| Documentation | `When I run the documented commands of the section in the project directory {string}` |
| Documentation | `Then the documented compile writes the path of the published IR of the project {string}` |
| Documentation | `Then the documented generate writes its documented files to {string} of the project {string}` |
| Tutorial | `Then each marked code block of the tutorial is the file of the example that it names` |
| Tutorial | `Then the first F# code block of the page {string} is the file {string} of the example` |
| Tutorial | `Given a copy of the example of the tutorial` |
| Tutorial | `Given the guarded discount of the tutorial in the example` |
| Tutorial | `When I compile the example as JSON` |
| Tutorial | `When I compile the example` |
| Tutorial | `Then the compile of the example succeeds with the modules {string} and no diagnostic` |
| Tutorial | `When I generate {string} from the last compile of the example to {string}` |
| Tutorial | `Then the example has the generated C# files {string} in {string}` |
| Tutorial | `When I compile the example at the IR version {string} and install the IR to {string}` |
| Tutorial | `When I validate the IR that the example installed in {string}` |
| Tutorial | `When I compile the example with the target {string} and the generated output {string}` |
| Model validation | `When I validate the model` |
| Model validation | `When I validate the model as JSON` |
| Model validation | `When I validate the model as JSON with the arguments {string}` |
| Model validation | `Then validation exits with {int} and one JSON report` |
| Model validation | `Then the validation text is a plain table with the error {string}` |
| Model validation | `When I validate the model as JSON on the native host and on the tool host` |
| Model validation | `Then both hosts write the same validation report apart from the host` |
| Model validation | `Then the validation report has {int} {string} diagnostic with the code {string} from {string}` |
| Model validation | `Then the validation report has {int} {string} diagnostic with the code {string}` |
| Model validation | `Then the validation report has no diagnostic` |
| Model validation | `Then the validation report lists the analyzer {string} as {string}` |
| Model validation | `Then the analyzers of the validation report are the validator entries of extension list` |
| Extensions | `When I list the extensions as JSON with the options {string}` |
| Extensions | `When I list the extensions as text` |
| Extensions | `When I report the extension runtimes as JSON` |
| Extensions | `When I report the extension runtimes as text` |
| Extensions | `Then the extension list report has the host {string} and the extensions {string}` |
| Extensions | `Then the extension list report states no deployment and the execution {string}` |
| Extensions | `Then the extension list report states no deployment` |
| Extensions | `Then the extension {string} is not available, and its reason names {string}` |
| Extensions | `Then the extension list text has a row for each extension` |
| Extensions | `Then the extension runtimes report has the host {string} and the platform of this machine` |
| Extensions | `Then the WASM runtime of the report has the engine version {string} and is available on this machine unless it is win-x64` |
| Extensions | `Then the runtimes text names the WASM runtime of this machine and the engine {string}` |
| Tool routing | `Given the stub morphir-mck is on the search path` |
| Tool routing | `Given no tool is on the search path` |
| Tool routing | `Given the stub morphir-mck is in the current directory, and the current directory is on the search path` |
| Tool routing | `Given the stub morphir-mck is in the current directory` |
| Tool routing | `Given the stub of a tool {string} is on the search path` |
| Tool routing | `Given MORPHIR_MCK is the relative name of the tool` |
| Tool routing | `Given MORPHIR_MCK is the absolute path of the stub` |
| Tool routing | `Given MORPHIR_MCK is the absolute path of a tool that is not there` |
| Tool routing | `Given the tool exits with {int}` |
| Tool routing | `Given the tool reads its standard input, and the input is {string}` |
| Tool routing | `When I run mck with the arguments` |
| Tool routing | `When I run the command {string} on the search path` |
| Tool routing | `When I run mck with {string} and the tool alone with the same arguments` |
| Tool routing | `When I run mck with the arguments and the tool alone with the same arguments` |
| Tool routing | `Then the tool received the arguments` |
| Tool routing | `Then the routed run writes and exits as the tool alone` |
| Tool routing | `Then the tool read the input {string} in the directory of the tool alone, the work area` |
| Tool routing | `Then standard output is not a report of the tool` |
| Tool routing | `Then the exit code is neither 0 nor {int}` |
| Tool routing | `Then the help lists the command mck` |
| Tool routing | `Then the tool alone with the arguments {string} exits with {int}` |
| Model verification | `When I verify the model with the schema version {int} as JSON` |
| Model verification | `Then the verification report has the detection method {string}` |
| Extension list | `When I list the extensions as JSON` |
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

The scenarios that pull request 3 of the change `consolidate-black-box-tests` moved keep the step texts of their old
features, so that each moved scenario has its old steps and its old checks. Some of those steps say one thing in the
words of their feature: for example `environment variable {word} is {string}` and `migration environment variable
{word} is {string}`, or `inspection diagnostics include {string}` and `workspace migration diagnostics include
{string}`. A later change can join them. A new scenario uses the general step where one exists.

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
