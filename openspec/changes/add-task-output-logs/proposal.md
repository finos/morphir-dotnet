# Proposal

## Why

When a compile, generate or validate run fails on a build agent, the only record of what it printed is the terminal of that run. Mill keeps a log of each task next to its result in `out/`. Morphir already follows Mill's out-directory layout (`<task>.json`, `<task>.dest/`, `<task>.lock`), but it keeps no output. A user who troubleshoots a run must run it again with the same settings to see the messages.

## What Changes

- Each task run that has an out root writes what it printed to stdout and stderr to `<task>.stdout.log` and `<task>.stderr.log`, beside `<task>.json`. Task ids are those of the out-directory layout: `compile`, `generate/<target>`, plus `validate`.
- The files are written for every run that resolves its out root: success, failure (next to the tombstone) and cancellation. Each run replaces the files of the run before.
- The files hold exactly the bytes the command wrote to each stream, including the `--json` report and the log lines. Each file is bounded; a file that reaches its limit ends with one line that says so.
- The files are on by default. A new boolean setting `workspace.task_logs` turns them off. It is read through the standard configuration layering: `morphir.toml`, `morphir.yaml` or `morphir.yml`, the user configuration, and the environment variable `MORPHIR_WORKSPACE__TASK_LOGS`.
- The pinned upstream out-directory specification has no log files. This change is a superset of it: readers that do not know the files ignore them.

No behavior that exists now changes. Stdout and stderr stay the same; the files are a copy.

## Capabilities

### New Capabilities

- `task-output-logs`: the stdout and stderr log files of each task in the out root, when they are written, what they hold, their size bound, and the `workspace.task_logs` setting.

### Modified Capabilities

None. `compile-task-records` keeps its requirements; the log files sit beside its records and do not change them.

## Tracking

- Follows `add-fsharp-compile` (#499), which added the out root, task records and `generate` from a prior compile.
- An upstream request to add the log files to the shared out-directory specification is a follow-up in `finos/morphir`.

## Impact

- `src/Morphir.Tooling`: the task commands (`compile`, `generate`, `validate`) capture their two output streams and write the files at the end of the run, under the task lock. `TaskPaths` gets the log file paths.
- `src/Morphir.Configuration`: the schema gets `workspace.task_logs` (boolean, default `true`), as an amendment of the pinned upstream schema in `scripts/configuration/generate-schema.py`.
- Docs: the `compile`, `generate` and `validate` reference pages, the out-directory notes, and the configuration reference.
- No new dependency. Trim-safe and AOT-safe.
