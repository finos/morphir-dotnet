# Tasks

GitHub issue: [finos/morphir-dotnet#589](https://github.com/finos/morphir-dotnet/issues/589). The parent's work is
[finos/morphir#1153](https://github.com/finos/morphir/issues/1153): `ecosystem/morphir-dotnet`, its workflows and docs,
the extraction of `morphir-mck` under modified Decision 16A, and the removal of the Rust product CLI. That work is not
tracked here. The Beads link is pending, because the shared Beads create rolled back on an existing Dolt conflict; do
not invent a Beads id.

Dependencies: group 1 comes first. Group 2 needs the stub from 1.1. Group 3 is independent of groups 1 and 2. Group 4
integrates them and needs the full-build slot from the root thread.

Commands follow the repository's build settings: `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false
-nodeReuse:false`, and TUnit assemblies run with `dotnet exec <dll> --treenode-filter "<filter>"`. Set
`DOTNET_PROCESSOR_COUNT=2` and `MORPHIR_TEST_MAX_PROCESSES=2`.

## 1. Resolution and the stub tool

- [x] 1.1 Add `tests/Morphir.Mck.Stub`, an executable named `morphir-mck` that reports its arguments, working directory
  and standard input as JSON, writes to standard error, exits with a requested code and can start a grandchild. Verify
  that building it produces the `morphir-mck` apphost.
- [x] 1.2 Write failing resolution tests in `tests/Morphir.Tooling.Tests` for `MORPHIR_MCK` (absolute, relative,
  missing), absolute and relative `PATH` entries, the Windows `.exe`-only rule and the Unix execute bit. Then
  implement the resolver in `src/Morphir.Tooling/Features/Mck` and verify the tests pass.

## 2. Routing in every entry point

- [x] 2.1 Write failing subprocess tests in `tests/Morphir.Tests` that start the produced `morphir` and `dotnet-morphir`
  apphosts with the stub on `PATH`. Cover pass-through of arguments (spaces, non-ASCII text, `--help`), working
  directory, standard input, the separation of standard output and standard error, exit code 3, standalone against
  routed output, the missing tool (exit 127, empty standard output), a relative `MORPHIR_MCK`, a tool in the current
  directory only, and that `morphir lint` starts nothing. Verify they fail before 2.2.
- [x] 2.2 Implement the routing in `MorphirApplication.Run` before the command tree, with inherited streams, the exit
  code, the 127 diagnostic, and an `mck` command so that `morphir --help` lists it. Verify the 2.1 tests pass.
- [x] 2.3 Add the Windows job object (kill on close), Ctrl+C handling, and SIGTERM or SIGHUP stopping the tool's tree on Unix.
  Verify with a subprocess test that killing the CLI on Windows stops the stub and its grandchild, and on Unix that
  SIGTERM stops the tree.
  - The stated Windows-kill and Unix-SIGTERM checks passed at `51be09f25107145f10c3046da333cc781dc2724a`,
    [CI run 42898613660](https://github.com/finos/morphir-dotnet/actions/runs/42898613660). Both strict SIGTERM cases,
    `morphir` and `dotnet-morphir`, passed on Linux and macOS; Windows passed its kill tests. The local Windows
    mutation check fails without the job object. Task completion records those checks, not platform qualification.
    The earlier Linux grandchild timeout at `8c15df59`, run 42882875643, remains unexplained. Later passing runs do
    not establish its cause or prove it cannot recur. Ctrl+C, SIGHUP and managed `morphir` SIGTERM on Unix remain
    untested by automation.
- [x] 2.4 Add a subprocess test that the managed `morphir` routes `mck` the same way, in `tests/Morphir.Managed.Tests`,
  and verify it passes.
- [x] 2.5 Document `morphir mck` in `docs/content/docs/cli/mck.md` and the CLI index, and add a CHANGELOG entry. Verify
  the page states the resolution rules, exit code 127, and that the tool is installed separately from finos/morphir.

## 3. CLI publication and the parent build interface

- [x] 3.1 Gate the CLI jobs and steps of `.github/workflows/deployment.yml` on
  `vars.MORPHIR_DOTNET_CLI_PUBLICATION == 'enabled'` and keep `PublishLibs` unconditional. Verify with a test in
  `tests/Morphir.Build.Tests` that reads the workflow and with `actionlint`.
- [x] 3.2 Add `.github/workflows/cli-offering.yml` (`workflow_call` with `repository`, `ref`, `deployment`, `rid`, `os`,
  `configuration`) that builds one offering with `PackRelease`, starts the produced executable or the installed
  `dotnet-morphir` with `--version`, and uploads the archive, `.build-launch.json`, `.build-contents.json` and `.sha256`.
  Verify with a test that pins its inputs and offerings and with `actionlint`.
  - `.github/scripts/install-tool-package.ps1` is the parent's shared helper for checking the built tool package.
    Keep its default tool-path install, `-Local` manifest install and success-only `-ResultFile` evidence interface.
    The seven `ToolPackageInstallTests` cover feed/cache conflicts, byte checks, both install modes and result files;
    all passed on Linux, macOS and Windows in run 42898613660. The reusable workflow itself has not run on GitHub.
- [x] 3.3 Document the build interface for finos/morphir (targets, inputs, outputs, qualification of produced
  executables, the version source, the publication switch) in `docs/contributing`. Verify the documented commands match
  the workflow.

## 4. Integration

- [x] 4.1 With the full-build slot from the root thread, compile the solution and run the affected test projects.
  Verify no failures beyond the known intermittent config-edit tests.
  - Done on win-arm64, 10 November 2026: the Release solution build has 0 errors and none of its warnings come from changed files.
    Morphir.Tests 49 passed (2 Unix skips), Morphir.Managed.Tests 12 passed, Morphir.Tooling.Tests 1001 passed (1 Unix skip).
    The Native AOT win-arm64 pack has no IL or AOT warning in changed files. Its produced morphir.exe routes mck,
    exits with 127 without a tool, and its job object stops the tool and the tool's child. These local runs predate the
    Linux, macOS and Windows CI evidence at `51be09f2` recorded under 2.3.
- [x] 4.2 Run `openspec validate add-parent-cli-delivery-and-mck-dispatch --strict` and `mise run openspec:verify`, and
  verify both pass.

## Refresh against main, 11 November 2026

- Merged `origin/main` at `017e05b11ea2cb75a1a4a1c60df792f8247b5a9e` normally. Both CHANGELOG sides are retained.
  The two test lock-file auto-merges keep main's `Morphir.Compilation` dependency and this change's MCK stub.
  Offering identity, setup `--dry-run`, contract-corpus checks, reserved Morphir IDs, frontend schema object members,
  the cancellation-observation test fix and the documented macOS minimum are retained from main.
- All 13 checks and the independent no-blocker review apply to the old exact head `51be09f2`. The refreshed head
  needs its own CI and review. No local .NET build, test or publish is run for this merge/docs refresh because the
  root thread has reserved the heavy CPU slot. The earlier 4.1 evidence is not a build of the refreshed head.
- Parent contracts and package planning are merged: finos/morphir#1147, #1152, #1150 and #1151. Their ownership and
  remaining package-version timing are recorded in the proposal and design. No package or installation work is
  added here. The parent contract-test pin `f89dda45` and its CLI delivery source pin have separate purposes.
- The parent's CLI delivery candidate remains held in finos/morphir#1155. This refresh changes no source pin, parent
  checkout, contract fixture or version. Root owns refreshed-head review, CI monitoring and merge approval.

## Workflow follow-up

- Follow AGENTS.md for review, archive, tracker closure and release approval.
- After the parent has published a release, remove the variable-gated CLI jobs in a separate change.
