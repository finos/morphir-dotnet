# cli-model-compilation Specification

## Purpose
Exposes source compilation through `morphir compile`. The command resolves settings from the CLI and project configuration, selects sources and a capable frontend, reports structured results and publishes IR safely. It can also continue directly into code generation.

## Requirements

### Requirement: Compile settings have deterministic precedence
Each setting SHALL come from the first source that supplies it:

1. a CLI flag;
2. the selected project configuration (`project.name`, `project.source_directory`, `project.exposed_modules`, `ir.format_version`, `frontend.language`, and `[frontend.<language>]` `project_file` and `extension`);
3. a documented default.

`--project` and `--config` SHALL select a project through the existing project discovery.

#### Scenario: CLI overrides configuration
- **WHEN** `morphir.toml` sets `ir.format_version = 3` and the command passes `--ir-version 4`
- **THEN** the compile produces IR format version 4, and the JSON report gives the CLI as the source of that setting

#### Scenario: Outside any project
- **WHEN** the command runs with `--language fsharp --input A.fs --package-name acme/demo` and no configuration exists
- **THEN** it compiles `A.fs` with IR format version 4 and exposes all modules

### Requirement: IR versions are full format versions
`--ir-version` and `ir.format_version` SHALL accept a Morphir format version: an integer classic version, or a SemVer version that can have pre-release and build parts. The requested version SHALL be kept exactly, never reduced to its major number. A version that no registered writer declares SHALL fail with exit code 2, listing the declared versions.

#### Scenario: Pre-release version
- **WHEN** `--ir-version 4.1.0-draft.1` is requested and no writer declares that exact version
- **THEN** the command fails before compilation with `compile.unsupported_ir_version`, listing the declared versions and keeping `4.1.0-draft.1` unchanged in the message

#### Scenario: Equivalent release spelling
- **WHEN** `--ir-version 4.0.0` is requested
- **THEN** it selects the same writer as `4`, and the report records `4.0.0` as requested

### Requirement: Sources are selected without language-specific rules
The command SHALL choose sources in this order:

1. `--project-file`: one project file;
2. `--input`: files in the given order, or one directory;
3. `[frontend.<language>].project_file` of the selected language;
4. the files of `project.source_directory`, in lexical path order.

Any other ordering SHALL come from a project file. The command SHALL NOT apply rules that belong to one language.

#### Scenario: Explicit file order
- **WHEN** `--input Rates.fs Pricing.fs` is given
- **THEN** the frontend receives `Rates.fs` then `Pricing.fs`

#### Scenario: Directory order
- **WHEN** the source directory holds `b/Two.fs` and `a/One.fs` and no project file is configured
- **THEN** the frontend receives `a/One.fs` then `b/Two.fs`

### Requirement: CLI source options override configuration
`--project-file` and `--input` SHALL NOT be combined. Either of them SHALL replace a configured `[frontend.<language>].project_file` for that run, and the configured project file SHALL NOT be evaluated.

#### Scenario: CLI input overrides configured project file
- **WHEN** `[frontend.fsharp] project_file` is configured and `--input Rates.fs Pricing.fs` is given
- **THEN** the project file is not evaluated, and the frontend receives `Rates.fs` then `Pricing.fs`

### Requirement: Source reading is bounded and safe
Source files SHALL be read through the existing bounded capture rules: byte, entry and path limits; link and special-file refusal; and cancellation. Sources outside the source root SHALL be accepted only when named by `--input`. Files SHALL be decoded as UTF-8, and invalid encodings SHALL be errors.

#### Scenario: Linked source refused
- **WHEN** a source file in the source directory is a symbolic link
- **THEN** the command fails before compilation with a diagnostic naming the file

### Requirement: Frontend selection uses advertised capabilities
The language SHALL come from `--language`, else `frontend.language`, else the extension of every source document. The command SHALL choose among registered frontends using only what each one advertises: language, file extensions, IR versions and multi-document support. Every document's extension SHALL be one the selected frontend claims. If no frontend matches, or more than one does, the command SHALL fail before compilation and name the frontend and the capability that blocked the request.

#### Scenario: IR version not served
- **WHEN** a configured frontend admits only IR version `3.0.0`, and the command requests `--ir-version 4`, which the host cannot derive from classic IR
- **THEN** the command fails before compilation, naming that frontend and its IR versions, with exit code 2

#### Scenario: F# frontend not in this build
- **WHEN** the AOT `morphir` executable compiles F# and no F# frontend extension is configured
- **THEN** the command fails with `compile.frontend_unavailable`. The message says that `dotnet-morphir` has a built-in F# frontend and that `[frontend.fsharp].extension` or `--extension` configures one.

### Requirement: The in-process F# frontend ships with the dotnet tool
The `dotnet-morphir` tool SHALL register the in-process F# frontend. Unless an extension is configured, it SHALL select that frontend for F#. The AOT `morphir` executable SHALL NOT contain or load the F# compiler service.

#### Scenario: Tool path without MEP
- **WHEN** `dotnet-morphir compile --project-file src/Acme.CarRental.fsproj --package-name acme/car-rental` runs and no extension is configured
- **THEN** the compile succeeds and starts no frontend process. Only the project-file provider starts a process.

### Requirement: Compile results are reported once
With `--json`, stdout SHALL hold exactly one JSON report. The report has `success`, `exitCode`, `language`, `frontend`, `irVersion`, `sources`, `modules`, `output`, `installed`, the generation fields when a target ran, and `diagnostics` in the frontend diagnostic shape. Logs and human-mode diagnostics SHALL go to stderr.

#### Scenario: Warnings from every stage in a successful report
- **WHEN** a JSON-mode compile has an `msbuild.ignored_package_reference` warning from its project file and an `fsharp.compiler` warning from the frontend, and no errors
- **THEN** the report has `success: true`, exit code 0 and both warnings, ordered by stage and then by document and position

#### Scenario: Unsupported construct in JSON mode
- **WHEN** a JSON-mode compile meets `let mutable`
- **THEN** stdout parses as one report with `success: false` and an `fsharp.unsupported_mutable` diagnostic with its range, and stderr holds no copy of that report

### Requirement: Diagnostics stream as stages complete
In human mode, the command SHALL write each stage's diagnostics to stderr as soon as that stage completes, before later stages run. The streamed diagnostics SHALL be exactly the diagnostics of the final result, with none added or omitted. The outcome and exit code SHALL be the same as without streaming. With `--json`, stdout SHALL still hold one report and no streamed lines.

#### Scenario: Project-file warning before compilation ends
- **WHEN** a human-mode compile has an `msbuild.ignored_package_reference` warning and a frontend that takes a long time
- **THEN** the warning is on stderr before the frontend completes, and the final summary lists the same diagnostics

#### Scenario: JSON mode is not streamed
- **WHEN** the same compile runs with `--json`
- **THEN** stdout is one parseable report holding both diagnostics, and nothing else is written to stdout

### Requirement: JSON Lines output streams compile events
With `--json-lines`, stdout SHALL hold only newline-delimited JSON. Each line SHALL be one compact UTF-8 JSON object with an `event` member, written and flushed when the event occurs. The events are `stageStarted`, `diagnostics`, `stageCompleted` and `completed`. Exactly one `completed` record SHALL be written, as the last line. It SHALL hold the full report that `--json` would write.

#### Scenario: Events in order
- **WHEN** a compile with `--json-lines` has a project-file warning and succeeds
- **THEN** stdout has a `stageStarted` record for `sources`, then a `diagnostics` record holding the warning, then a `stageCompleted` record for `sources`, then the records of later stages, and a last `completed` record whose report has `success: true` and the same warning

#### Scenario: Failure still ends with completed
- **WHEN** a compile with `--json-lines` fails on `fsharp.unsupported_mutable`
- **THEN** the last line is a `completed` record with `success: false` and exit code 1, and every line on stdout parses as JSON

### Requirement: JSON Lines records are a lossless stream of the report
The diagnostics of all `diagnostics` records, taken together, SHALL equal the diagnostics of the `completed` report, with none added or omitted. Consumers SHALL ignore event types and members they do not know. `--json` and `--json-lines` together SHALL fail with exit code 2. Parse errors and cancellation in `--json-lines` mode SHALL still write one `completed` record when the process can write output.

#### Scenario: Both JSON flags
- **WHEN** `--json` and `--json-lines` are both given
- **THEN** stdout is one `completed` record with an invocation failure, and the exit code is 2

#### Scenario: Cancellation
- **WHEN** a `--json-lines` compile is cancelled before publication
- **THEN** the last line is a `completed` record with failure kind `cancelled`, and the exit code is 130

### Requirement: Exit codes follow the generation convention
The command SHALL return:

- `0` on success;
- `1` when the frontend or an IR builder reports a source or model error;
- `2` for invocation, configuration, project-file, selection, protocol and I/O failures;
- `130` for cancellation before publication.

A run with only warnings SHALL succeed. With `--target`, a failure in the generation stages SHALL give the exit code that `morphir generate` gives for the same failure, and the compiled IR SHALL stay published.

#### Scenario: Source error
- **WHEN** compilation fails only because of `fsharp.*` errors
- **THEN** the exit code is 1, and no IR is published or installed

### Requirement: Output installation preserves foreign content
`-o/--output` SHALL install a copy of the canonical IR in the given directory after a successful compile. It SHALL replace only files that an earlier install from the same task wrote there. Content it did not write SHALL be preserved. A conflict with such content SHALL fail without changing the directory.

#### Scenario: Reinstall over own file
- **WHEN** a second successful compile installs to the same `-o` directory
- **THEN** `morphir-ir.json` is replaced and other files in that directory remain unchanged

### Requirement: Compile can continue into generation
With `--target <id>` and `--generate-output <dir>`, the command SHALL pass the compiled model in memory to the registered generation backend for that target. It SHALL use the same backend resolution, `--option` validation and owned publication as `morphir generate`. Generation SHALL NOT run when compilation fails. `--target` without `--generate-output`, or a repeated `--target`, SHALL fail with exit code 2.

#### Scenario: Equivalent to two commands
- **WHEN** `compile --target fsharp --generate-output gen` runs, and separately `compile` is followed by `generate --input <compiled IR> --target fsharp --output gen2`
- **THEN** `gen` and `gen2` contain the same paths and bytes, and both reports list the same backend

#### Scenario: Incompatible backend
- **WHEN** `--ir-version 1 --target csharp` is requested and the C# backend does not accept that version
- **THEN** the command fails before generation with the exit code of `generate` for that refusal, naming the backend and capability, and leaves the generation output unchanged, while the compile output remains published

### Requirement: Unimplemented options fail explicitly
These SHALL be rejected with exit code 2 and a diagnostic naming the option and the reason: `--types-only`, Ion or YAML `ir.format`, and `document-tree` layout. They SHALL NOT be ignored. This delivery has no option or key for Specs or Application output or for incremental or baseline compilation; an option or key for one of them that is added before the feature SHALL be rejected in the same way.

#### Scenario: Document tree requested
- **WHEN** the configuration sets `ir.layout = "document-tree"`
- **THEN** the command fails with `compile.unsupported_setting`, naming `ir.layout` and its source file
