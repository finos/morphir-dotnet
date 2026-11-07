# Spec Delta

## Purpose

Shows the Morphir refusals of an F# project during a normal MSBuild build, in the error list of any IDE and in the CLI, through a check-only compile that publishes nothing.

## ADDED Requirements

### Requirement: Compile has a check-only mode
`morphir compile --check` SHALL run every frontend stage of a normal compile and report the same diagnostics and exit code. It SHALL NOT write IR, an output directory, a task record or generated code. `--check` SHALL NOT be combined with `-o/--output`, `--target` or `--generate-output`.

#### Scenario: Check succeeds
- **WHEN** `morphir compile --check --input Rates.fs Pricing.fs --package-name acme/demo` runs on sources in the supported subset
- **THEN** the command exits with code 0 and writes no file

#### Scenario: Check fails on a refusal
- **WHEN** the same command runs and `Pricing.fs` has `let mutable total = 0`
- **THEN** the command exits with code 1, reports `fsharp.unsupported_mutable`, and writes no file

#### Scenario: Check with output
- **WHEN** `morphir compile --check -o out` runs
- **THEN** the command fails with exit code 2 and a diagnostic that names `--check` and `--output`

### Requirement: The check does not change the frontend protocol
A check-only compile SHALL send the same frontend request as a normal compile. It SHALL use no protocol member that MEP 0.2.0-draft.1 does not define. Only the host SHALL skip publication.

#### Scenario: MEP frontend check
- **WHEN** `morphir compile --check` runs with a configured MEP frontend extension
- **THEN** the guest receives a standard `compileModel` request, and the host discards the returned model

### Requirement: Diagnostics have an MSBuild format
With `--diagnostic-format msbuild`, human-mode diagnostics SHALL be lines in the MSBuild canonical format on stderr: `<uri>(<line>,<column>,<endLine>,<endColumn>): <error|warning> <code>: <message>`. Lines and columns SHALL be one-based: the zero-based UTF-16 range plus one. Information and hint diagnostics SHALL be lines without a category.

#### Scenario: Refusal line
- **WHEN** `morphir compile --check --diagnostic-format msbuild` reports `fsharp.unsupported_mutable` at zero-based line 2, characters 8 to 15 of `src/Pricing.fs`
- **THEN** stderr holds `src/Pricing.fs(3,9,3,16): error fsharp.unsupported_mutable: ` followed by the message

#### Scenario: JSON is unchanged
- **WHEN** `--diagnostic-format msbuild` and `--json` are both given
- **THEN** stdout holds the normal JSON report, and the diagnostics in it keep zero-based ranges

### Requirement: The build package checks a project after compilation
A project that references the Morphir F# build package and sets `MorphirCheck` to `true` SHALL run a Morphir check after `CoreCompile`. The check SHALL use the project's `Compile` items in their MSBuild order. Each refusal SHALL be an MSBuild error at its file, line and column, and a refusal SHALL fail the build.

#### Scenario: Refusal in the build
- **WHEN** `dotnet build` runs on such a project whose `Pricing.fs` has `let mutable total = 0`
- **THEN** the build fails, and its error list holds `fsharp.unsupported_mutable` at `Pricing.fs`, line 3, column 9

#### Scenario: Check is off
- **WHEN** the project references the package and does not set `MorphirCheck`
- **THEN** the build runs no Morphir check

#### Scenario: No change since the last check
- **WHEN** a second `dotnet build` runs and no `Compile` item, `morphir.toml` or check setting changed
- **THEN** the Morphir check does not run again

### Requirement: A missing tool is a clear build error
When the check is on and the `dotnet-morphir` command (package `Morphir.Tool`) is not available to the project, the build SHALL fail with one MSBuild error that names the command and how to install it as a local tool.

#### Scenario: Tool not installed
- **WHEN** `MorphirCheck` is `true` and no tool manifest lists `Morphir.Tool`
- **THEN** the build fails with one error that says `dotnet tool install Morphir.Tool`
