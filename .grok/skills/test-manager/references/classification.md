# Classification, with examples from this repository

Use the steps of [SKILL.md](../SKILL.md) in order. This file has an example for each answer, and the cases that are hard.

## Step 1: conformance

A feature below `conformance/suites/` uses the closed step vocabulary of the compatibility kit. The kit belongs to finos/morphir, so its vocabulary stays apart from the steps of the black-box project.

A test that needs the kit library (`src/Morphir.Conformance`) in the test process is also a conformance test. Example: `tests/Morphir.Frontend.FSharp.Tests/CliAdapterTests.fs` judges the output of an executable with kit types. It stays in a project that can reference the kit, because the kit library references `Morphir.Compilation` and `Morphir.Mep`, and a black-box test must not see those assemblies.

A check of the kit through the CLI (`morphir mck ...`) needs only the artifact. It is a black-box test: continue with step 2.

## Step 2: build

The subject is the build of the repository. Examples in `tests/Morphir.Build.Tests`: `Helpers/TestPlanTests.cs` (a helper of the build), `DevelopmentWorkflowTests.cs` (a workflow file), `EntryPointGuardTests.cs` (a guard of a project file).

A test of a packed package or of an installed tool is not a build test. Its subject is what the user gets. Example: a test that reads the layout of a packed package is an end-to-end test with the packages lane.

## Step 3: the form

Gherkin is the default form of a black-box test. Three forms do not fit Gherkin:

| Form | Example |
| --- | --- |
| A loop over a corpus | Each case of a corpus against an executable |
| A property test | A law over generated inputs |
| A test with a time budget | A deadline with a margin |

Such a test is a framework test. Step 4 says if it is a black-box test (a TUnit class in the black-box project) or a unit test (the framework of its project: TUnit or Expecto).

## Step 4: black-box or not

A black-box test reaches the product only through a built artifact. Ask: does the test compile if the test project has no reference to a project below `src/`?

| Test | Answer | Why |
| --- | --- | --- |
| Starts `morphir ir verify` and reads the exit code and the output | Black-box | Only the command line |
| Calls `CSharpGenerator.Generate` and compares the result | Unit | A product type in the test process |
| Compiles F# in memory with the frontend library | Unit | A product type in the test process, also when it is slow |
| Reads a fixture file and parses it with a product parser | Unit | A fixture is not an artifact of the build |
| Reads a built assembly with `PEReader` and checks its metadata | Black-box | A built artifact, and no product type |
| Starts a helper process to test a library in the test process | Unit | The subject is the library. Use `ChildProcess` and `TestSandbox`. |
| Runs the CLI and compares its files with a product function | Two tests | Split it: see the rule in SKILL.md |

A unit test that uses the real file system, paths, line endings, processes or an operating system API has the `PlatformSpecific` marker. A black-box test has no marker: the assembly has it.

## Steps 5 to 8: the purpose

| Question | Example | Class |
| --- | --- | --- |
| The delivered form itself? | The layout of a packed package. A tool that installs and starts. A built assembly that must be invariant-globalization. | end-to-end |
| One user goal with two or more capabilities? | Verify a model, then generate C# from it (`Journeys/ModelToCSharp.feature`). | journey |
| Only that an artifact starts and does one basic function? | `--version` on each host (`Smoke/Version.feature`). | smoke |
| Otherwise | `ir verify` with each option (`Integration/ModelVerification.feature`). `generate` with a rerun (`Integration/Generation.feature`). | integration |

Hard cases:

- **A command that runs two times** is one capability, so it is integration. Example: generate, change the model, generate again.
- **A journey on a published executable** is a journey. The target is where the test runs. It does not change the class. A journey with no lane runs on the development build in a pull request and on each published target in the release and manual workflows. If the journey cannot run on the development build, it gets the lane `@lane @published`, also when the published executable is the Native AOT executable.
- **`--help` of one command** is smoke if it proves only that the artifact starts. A check of the options that the help text shows is integration.
- **One command on one input.** `ir verify` on one model, with a check of the exit code and the text, checks the capability. It is integration, not smoke.
- **Three different commands for one goal** are three capabilities, so the test is a journey.
- **An end-to-end test on the development build.** A property that the delivered form must have can be a check of the build output (for example the runtime configuration file of an executable). It is end-to-end with no lane, and a pull request runs it on each operating system.

## After the class: the lane, the host and the capabilities

| Need | Tag |
| --- | --- |
| Only the development build | None |
| The packages that the build packs | `@lane @packages` |
| To publish a program with Native AOT in the test (a library host or a probe) | `@lane @native-aot` |
| An executable that a release publishes or packs, also its Native AOT executable, when the test cannot run on the development build | `@lane @published` |
| One entry point (`morphir` of `src/Morphir`, `morphir` of `src/Morphir.Managed`, `dotnet-morphir` of `src/Morphir.Tool`) | `@host-native`, `@host-managed` or `@host-tool` |
| A driver capability | The tag of the capability: `@needs-model-verification`, `@needs-generation`, `@needs-in-process-fsharp-frontend`, `@needs-self-contained-frontend`, `@needs-compilation`, `@needs-configuration`, `@needs-migration`, `@needs-extensions`, `@needs-model-validation`, `@needs-built-extensions`, `@needs-host-without-dynamic-code` |

A test with a lane does not run in the run of its group. The lane decides where it runs. Do not give a test a lane that it does not need: a test with no lane runs in a pull request on each operating system, and also on each published target in the release and manual workflows.
