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
- [ ] 2.3 Add the Windows job object (kill on close), Ctrl+C handling, and SIGTERM or SIGHUP stopping the tool's tree on Unix.
  Verify with a subprocess test that killing the CLI on Windows stops the stub and its grandchild, and on Unix that
  SIGTERM stops the tree.
  - Windows half verified locally, with a mutation check (the kill test fails without the job object). The Unix SIGTERM
    test is written but skips on Windows; it is not ticked until it passes on Linux and macOS CI.
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
  `dotnet-morphir` with `--version`, and uploads the archive, `.build-launch.json` and `.sha256`. Verify with a test
  that pins its inputs and offerings and with `actionlint`.
- [x] 3.3 Document the build interface for finos/morphir (targets, inputs, outputs, qualification of produced
  executables, the version source, the publication switch) in `docs/contributing`. Verify the documented commands match
  the workflow.

## 4. Integration

- [ ] 4.1 With the full-build slot from the root thread, compile the solution and run the affected test projects.
  Verify no failures beyond the known intermittent config-edit tests.
- [ ] 4.2 Run `openspec validate add-parent-cli-delivery-and-mck-dispatch --strict` and `mise run openspec:verify`, and
  verify both pass.

## Workflow follow-up

- Follow AGENTS.md for review, archive, tracker closure and release approval.
- After the parent has published a release, remove the variable-gated CLI jobs in a separate change.
