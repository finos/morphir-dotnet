# Spec Delta

## Purpose

Lets a user run the separately installed Morphir Compatibility Kit as `morphir mck`, the way `dotnet morphir` runs
`dotnet-morphir`, without the kit being part of the CLI under test.

## ADDED Requirements

### Requirement: morphir mck runs the standalone morphir-mck
`morphir mck <args>` SHALL start the executable `morphir-mck` with exactly the arguments that follow `mck`, in order and
unchanged. The arguments are not parsed, quoted or expanded by the CLI, `--help` and `--version` included. The CLI SHALL
link no compatibility kit engine of its own.

#### Scenario: Arguments pass through unchanged
- **WHEN** a user runs `morphir mck run --suite "frontend cases" --report ünïcode.json --help`
- **THEN** `morphir-mck` receives `run`, `--suite`, `frontend cases`, `--report`, `ünïcode.json` and `--help`, in that order

#### Scenario: The same result routed and standalone
- **WHEN** a user runs `morphir-mck <args>` and then `morphir mck <args>` in the same folder with the same input
- **THEN** both write the same standard output and standard error and exit with the same code

### Requirement: The tool runs in the caller's environment
The tool SHALL run in the CLI's working directory and environment, and SHALL use the CLI's standard input, output and
error directly, so that pipes, redirection and terminals behave as for the standalone tool. The CLI SHALL write
nothing of its own to standard output when it routes a command.

#### Scenario: Working directory and streams
- **WHEN** a user pipes text into `morphir mck` from a folder
- **THEN** the tool reads that text on its standard input, sees that folder as its working directory, and its standard output and standard error reach the user unchanged

### Requirement: The CLI exits with the tool's exit code
The CLI SHALL exit with the exit code of `morphir-mck`.

#### Scenario: A failing gate
- **WHEN** `morphir-mck` exits with code 3
- **THEN** `morphir mck` exits with code 3

### Requirement: Safe resolution of the tool
The CLI SHALL find `morphir-mck` only through `MORPHIR_MCK`, when set, which must be an absolute path to an existing
file, or else through the absolute folders of `PATH`, in order. It SHALL ignore relative and empty `PATH` entries and
SHALL never look in the current directory implicitly. On Windows only `morphir-mck.exe` is a candidate. Elsewhere the
candidate must be an executable file. The CLI SHALL use no shell and SHALL never install a tool.

#### Scenario: A tool in the current directory is not run
- **WHEN** the current directory holds a `morphir-mck` and `PATH` has no absolute folder with one
- **THEN** `morphir mck` reports that the tool was not found, and runs nothing

#### Scenario: An explicit path must be absolute
- **WHEN** `MORPHIR_MCK` is a relative path
- **THEN** `morphir mck` refuses it with a diagnostic and runs nothing

#### Scenario: A script is not run through a shell on Windows
- **WHEN** a `PATH` folder on Windows holds only `morphir-mck.cmd`
- **THEN** it is not a candidate, and `morphir mck` reports that the tool was not found

### Requirement: A clear diagnostic when the tool is missing
When no `morphir-mck` is found, the CLI SHALL write to standard error that the separately installed `morphir-mck` is
needed, where it looked, and how to point to it with `MORPHIR_MCK`, and SHALL exit with code 127. It SHALL write
nothing to standard output.

#### Scenario: No tool installed
- **WHEN** a user runs `morphir mck run` with no `morphir-mck` on `PATH` and no `MORPHIR_MCK`
- **THEN** standard output is empty, standard error names `morphir-mck` and `MORPHIR_MCK`, and the exit code is 127

### Requirement: Cancellation reaches the tool and leaves no process behind
An interrupt (Ctrl+C) SHALL reach the tool, which decides how to stop, and the CLI SHALL wait for it and exit with its
code. When the CLI itself is cancelled or terminated, the tool's whole process tree SHALL stop. On Windows this SHALL
hold even when the CLI process is killed.

#### Scenario: The CLI is killed on Windows
- **WHEN** the `morphir` process that runs `morphir mck` is killed while the tool and a child of the tool are running
- **THEN** the tool and its child stop as well

#### Scenario: The CLI is terminated on Linux or macOS
- **WHEN** the `morphir` process that runs `morphir mck` receives SIGTERM
- **THEN** the tool's process tree is stopped and the CLI exits

### Requirement: Every entry point routes the same way
The Native AOT `morphir`, the managed `morphir` and `dotnet-morphir` SHALL route `mck` with the same resolution,
arguments, streams, exit code and diagnostics. `morphir --help` SHALL list `mck`.

#### Scenario: dotnet-morphir routes mck
- **WHEN** a user runs `dotnet-morphir mck <args>` or `dotnet morphir mck <args>`
- **THEN** the result is the same as `morphir mck <args>`

### Requirement: mck is the only routed command
The CLI SHALL route only the `mck` command to an external executable. Any other unknown command SHALL remain a command
line error and SHALL never start an executable named after it.

#### Scenario: An unknown command is not executed
- **WHEN** a user runs `morphir lint` and an executable `morphir-lint` is on `PATH`
- **THEN** the CLI reports an unknown command and starts nothing
