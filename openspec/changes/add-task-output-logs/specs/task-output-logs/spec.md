# Spec Delta

## Purpose
Keeps a copy of what each task run printed to stdout and stderr in the out root, beside the task's record, so that a failed or surprising run can be examined after it ends without running it again.

## ADDED Requirements

### Requirement: Task runs write their output streams to log files
A task run that resolves its out root SHALL write two files beside its task record: `<task>.stdout.log` with every byte the command wrote to stdout, and `<task>.stderr.log` with every byte it wrote to stderr, in the order written. The task ids SHALL be those of the out-directory layout: `compile` and `generate/<target>`, under the member path for a workspace member, and `validate` for `morphir validate`. The commands SHALL write the same bytes to their streams as without the files.

#### Scenario: Successful compile
- **WHEN** `compile` succeeds in a project
- **THEN** `<out>/compile.stdout.log` holds the path of the IR that the command printed, `<out>/compile.stderr.log` holds the summary line and any diagnostics that it printed, and both streams of the command are unchanged

#### Scenario: JSON report
- **WHEN** `compile --json` runs
- **THEN** `<out>/compile.stdout.log` holds the same JSON report as stdout, byte for byte

#### Scenario: Generate for a target
- **WHEN** `generate --target fsharp` runs from a project
- **THEN** its output is in `<out>/generate/fsharp.stdout.log` and `<out>/generate/fsharp.stderr.log`

#### Scenario: Workspace member
- **WHEN** `compile` runs for the member `packages/orders`
- **THEN** the files are `<out>/packages/orders/compile.stdout.log` and `<out>/packages/orders/compile.stderr.log`

### Requirement: Failed and cancelled runs keep their log files
The files SHALL be written for a run that fails or is cancelled after it resolved its out root, as for a successful run. A run that fails before it resolves its out root, for example on an invalid option, SHALL write no files and change no earlier files.

#### Scenario: Source error
- **WHEN** `compile` fails with `fsharp.unsupported_pattern_guard`
- **THEN** `compile.json` is a tombstone, and `compile.stderr.log` holds the diagnostic line and the failure summary

#### Scenario: Invalid option
- **WHEN** `compile --json --json-lines` fails before settings resolve
- **THEN** no log file is written, and the files of the earlier run are unchanged

### Requirement: Each run replaces the log files of the run before
A run SHALL replace both files of its task as a whole. A reader SHALL never see a file that mixes two runs. Runs of one task SHALL write the files while they hold the task's exclusive lock, so that two runs of the same task cannot write the files at the same time.

#### Scenario: Second run
- **WHEN** a second `compile` succeeds after a failed one
- **THEN** `compile.stderr.log` holds only the output of the second run

### Requirement: Log files are bounded
Each file SHALL hold at most 16 MiB of a run's output. When a stream writes more, the file SHALL keep the first part of that stream and end with one line that says how many bytes were left out. The stream itself SHALL still receive all bytes.

#### Scenario: Large output
- **WHEN** a run writes 20 MiB to stderr
- **THEN** stderr receives 20 MiB, and `<task>.stderr.log` holds the first part and ends with a line that says how many bytes were left out

### Requirement: The task_logs setting turns the files off
The boolean setting `workspace.task_logs` SHALL control the files, with the default `true`. It SHALL be read through the standard configuration layering: `morphir.toml`, `morphir.yaml`, `morphir.yml`, the user configuration and the environment variable `MORPHIR_WORKSPACE__TASK_LOGS`. When the value is `false`, a run SHALL write no log files and SHALL leave the files of earlier runs as they are.

#### Scenario: Off in the project file
- **WHEN** `morphir.toml` sets `task_logs = false` in `[workspace]` and `compile` runs
- **THEN** no log file is written

#### Scenario: Off in the environment
- **WHEN** `MORPHIR_WORKSPACE__TASK_LOGS=false` is set and `generate --target csharp` runs
- **THEN** no log file is written

### Requirement: An invalid task_logs value fails the settings
A `workspace.task_logs` value that is not a boolean SHALL fail the run's settings stage with exit code 2 and a diagnostic that names the key and its source.

#### Scenario: Not a boolean
- **WHEN** `morphir.toml` sets `task_logs = "sometimes"`
- **THEN** the command fails with exit code 2 and a diagnostic that names `workspace.task_logs` and `morphir.toml`

### Requirement: The out root stays readable by ecosystem tools
The log files SHALL be the only new entries in the out root. They SHALL NOT change the task record, the `.dest` directory, the lock files or the install ledger. A tool that reads the out root by the upstream out-directory specification SHALL read it as before.

#### Scenario: Ecosystem reader
- **WHEN** a record written with log files is read against the pinned upstream out-directory specification and example record
- **THEN** the record is equal to the record that the same run writes with `task_logs = false`
