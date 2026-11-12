# Choosing the CI operating systems

The Development workflow (`.github/workflows/development.yml`) tests on Linux, Windows and macOS. Windows and macOS
runners cost much more than Linux runners. Use this page to run Linux only while a pull request is not ready to merge.

The Fallout target `CIPlan` makes the decision (`build/Build.CI.cs`, `build/Helpers/CiPlan.cs`). The workflow only
reads its outputs.

## What runs

Linux always runs: lint, OpenSpec and the Linux tests. Labels add the other platforms.

| Pull request | Operating systems | The `ci` check |
| --- | --- | --- |
| Draft, no platform label | Linux | Passes with a warning that Windows and macOS did not run |
| Ready for review, no platform label | Linux, Windows, macOS | Passes when all pass |
| `ci:windows` | Linux, Windows | Passes with a warning on a draft; fails on a ready pull request |
| `ci:macos` | Linux, macOS | Passes with a warning on a draft; fails on a ready pull request |
| `ci:windows` and `ci:macos`, or `ci:full` | Linux, Windows, macOS | Passes when all pass |
| `ci:linux-only` | Linux | Passes with a warning on a draft; fails on a ready pull request |
| Release call (`workflow_call`) | Linux, Windows, macOS, all run again | Passes when all pass |

When a pull request has a platform label, the labels give the exact set, on a draft and on a ready pull request.
Labels combine, and the larger set wins: `ci:linux-only` with `ci:windows` runs Linux and Windows.

A pull request that is ready for review needs every platform before it can merge. The `ci` check names the missing
platform and the label that adds it.

## Which tests run on Windows and macOS

Most tests are managed logic that gives the same result on each operating system. A pull request runs them once, on
Linux. On Windows and macOS it runs only the tests whose result can depend on the operating system. Those tests have
the marker `PlatformSpecific`:

| Framework | Marker |
| --- | --- |
| TUnit (C#) | `[Category("PlatformSpecific")]` on the test method or the test class |
| Reqnroll (`.feature`) | The tag `@PlatformSpecific` on the feature or the scenario |
| Expecto (F#) | `testLabel "PlatformSpecific"` on the test or the test list |

Mark a test that uses the real file system, paths, line endings, processes, the `dotnet` command, an operating
system API, native code or sockets, directly or through the code under test. A test with no marker has no Windows and
macOS coverage in a pull request. If you are not sure, mark the test.

The `Test` target has the parameter `--test-scope` (`build/Build.Testing.cs`, `build/Helpers/TestPlan.cs`):

| Scope | Tests | Used by |
| --- | --- | --- |
| `all` (default) | Every test of every test assembly | Linux in a pull request; every operating system in a manual run and in a call from another workflow |
| `platform` | Only the tests with the marker | Windows and macOS in a pull request |

The scope `platform` finds the test assemblies with a text search for `PlatformSpecific` in the sources below
`tests/<project>/`. A TUnit assembly that has the word but selects no test fails the run. The Myriad
package-consumer checks run in both scopes. To run every test on every operating system for a branch, use a manual
run with `os=full`.

```bash
./build.sh --target Test --test-scope platform   # only the marked tests
./build.sh --target Test --test-parallelism 1    # one test assembly at a time
```

## Parallel builds and tests

`Restore` and `Compile` use one MSBuild node for each processor. `--build-nodes 1` keeps a build small when more than
one build runs on the machine at the same time.

`Test` runs more than one test assembly at the same time: half the processors, from 1 to 4
(`--test-parallelism`). Each assembly writes its output as one block when it completes. The job summary and the log
show the time of each assembly.

All test assemblies share the `obj` and `bin` folders of the repository. A test that runs `dotnet build`, `pack` or
`publish` on a project of the repository must obey these rules:

- Do not replace the assemblies of the solution build. Pack with `--no-build`, build a copy of a fixture with
  `-p:BuildProjectReferences=false`, or send all outputs to a temporary folder with `--artifacts-path`.
- Set `WithSourceProjects()` on the test assembly in `build/Helpers/TestPlan.cs`. Only one such assembly runs at a
  time.
- Use a new temporary directory and a unique package version for each test, not a fixed name or a fixed port.

A test assembly that has tests with a time budget runs alone: set `Alone()` on it in `build/Helpers/TestPlan.cs`.
`Morphir.Toolchain.Tests` is such an assembly.

## How a test job uses its build

The `Build` step is the one restore and the one compile of a test job. The steps after it pass
`--skip Restore Compile`, so they use those outputs.

Do not pass a property such as `DebugType` or `PackageVersion` to a `dotnet pack`, `build` or `publish` of a
solution project between two compiles. A property on the command line also applies to the referenced projects, and
it can remove or replace their intermediate files. The next compile then builds them, and all projects that use
them, again. `PackTool` does this with `DebugType=none`, so run it after the steps that need the compile outputs.

## Where the selection shows

- The run page shows a "CI plan" notice, for example `Linux, Windows (label ci:windows). Runs: ...`.
- The job summary of the `plan` job shows the selection, why it was selected, and for each check: runs, passed in an
  earlier run (with a link to that job), or not selected (with the label that adds it).
- The job summary of the `ci` job shows the verdict and the result of each check.

## Jobs that already passed do not run again

The plan job looks up the pull_request runs of Development for the same head commit, with every attempt. A lint,
OpenSpec or test job that passed in one of them does not run again. Thus:

- When a draft becomes ready for review, only Windows and macOS run.
- When you add `ci:macos` to a pull request that ran Linux and Windows, only macOS runs.
- A new push changes the head commit, so every selected job runs again.

Manual runs (`workflow_dispatch`) are not evidence: GitHub does not count their checks for a pull request.

## Change the selection without a push

Change a `ci:*` label. The CI request workflow (`.github/workflows/ci-request.yml`) re-runs the Development run of
the pull request for its head commit. If that run has not completed, it is cancelled first. The plan job of the re-run
reads the labels and the draft state as they are now. A re-run keeps the `pull_request` event, so its `ci` check
counts for the pull request. Other labels start nothing.

"Re-run all jobs" on the run page does the same.

A label change on a pull request from a fork starts nothing. Push to the pull request to replan with its labels.

## Manual runs

Run the Development workflow by hand to test a branch on chosen platforms. Set `os` to `auto` (all), `linux`,
`full`, or a list of `windows` and `macos`:

```bash
gh workflow run development.yml --ref <branch> -f os=windows
```

A manual run never reuses jobs, and its checks do not count for a pull request.
