# Tasks

Tracking: follows [#499](https://github.com/finos/morphir-dotnet/issues/499). Open a tracking issue for this change before task 1.1 and link it here.

## 1. Capture and write for compile

- [ ] 1.1 Add the bounded tee writers and install them in `MorphirCli` before logging is configured (design CAP-1). Verify with unit tests: the original stream receives every byte; the buffer keeps the first 16 MiB and counts the rest; the `--json` report on stdout is unchanged (the existing `CompileCommandTests` pass).
- [ ] 1.2 Add the `TaskOutputLogs` service: a task reports its location, and the service writes both files by temporary file and rename at the end of the run, also after a failure or a cancellation, and writes nothing when no location was reported (design CAP-2, FMT-1). Verify with unit tests for success, failure, cancellation, no location, and the truncation line.
- [ ] 1.3 Report the compile task location from the settings stage, and write the files before `compile.lock` is released (design CAP-3). Verify with the spec scenarios "Successful compile", "JSON report", "Workspace member", "Source error" (the tombstone and the diagnostic line in `compile.stderr.log`), "Invalid option" and "Second run", as acceptance tests that run the real `dotnet-morphir`.
- [ ] 1.4 Document the files in `docs/content/docs/cli/compile.md` (section Output) and in the troubleshooting page, with one example of reading `compile.stderr.log` after a failed run. Verify that `CompileDocumentationTests` still pass.

## 2. Setting

- [ ] 2.1 Add `workspace.task_logs` (boolean, default `true`) to the schema amendments in `scripts/configuration/generate-schema.py`, and regenerate the schema (design CFG-1). Verify that the schema test passes and that the pinned upstream schema stays byte-identical.
- [ ] 2.2 Read the setting in the compile settings stage, with its origin; `false` writes no files and keeps earlier files; a value that is not a boolean fails with exit code 2 and names the key and its source (design CFG-1, CFG-2). Verify with the spec scenarios "Off in the project file", "Off in the environment" (`MORPHIR_WORKSPACE__TASK_LOGS=false`) and "Not a boolean", and with `morphir.yaml` and `morphir.yml` variants of the first one.
- [ ] 2.3 Document `workspace.task_logs` in the configuration reference and in the `compile` settings section. Verify that the docs tests pass.

## 3. Generate and validate

- [ ] 3.1 Report the `generate/<target>` location and write the files under `generate/<target>.lock` (design CAP-3). Verify with the spec scenario "Generate for a target", with and without `--input`, and "Off in the environment" for generate.
- [ ] 3.2 Resolve the out root for `validate` by the same rules as `compile`, read `workspace.task_logs`, and write `validate.stdout.log` and `validate.stderr.log` under `validate.lock`. Verify with acceptance tests for a clean model, a model with a type mismatch (the diagnostic in `validate.stdout.log` in JSON mode) and `task_logs = false`.
- [ ] 3.3 Document the files in `docs/content/docs/cli/generate.md` and `docs/content/docs/cli/validate.md`. Verify that the docs tests pass.

## 4. Integration

- [ ] 4.1 Check the spec scenario "Ecosystem reader": a record written with the files equals the record written with `task_logs = false`, and both match the pinned upstream example record. Verify with a test in the task-record tests.
- [ ] 4.2 Run the trimmed single-file and Native AOT `morphir` with the files on, and run the E2E tests against the single-file build. Verify that both write the files and that no new trim or AOT warning appears.
- [ ] 4.3 Run `./build.sh --target Test`, `./build.sh --target Lint` and `openspec validate add-task-output-logs --strict`. Verify that they pass and that the coverage of the changed files is at least 80%.

## Workflow follow-up

- Open an upstream request in `finos/morphir` to add `<task>.stdout.log` and `<task>.stderr.log` to the shared out-directory specification.
- Follow AGENTS.md for review, archive, tracker closure and release approval.
