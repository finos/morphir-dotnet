# Spec Delta

## MODIFIED Requirements

### Requirement: Compile results are reported once
The compile report SHALL have `success`, `exitCode`, `language`, `frontend`, `irVersion`, `sources`, `modules`, `output`, `installed`, the generation fields when a target ran, and `diagnostics` in the frontend diagnostic shape. With `--json=document`, stdout SHALL hold exactly that report as one JSON document; with `--json`, the report SHALL be the `report` of the last record. Logs and human-mode diagnostics SHALL go to stderr.

#### Scenario: Warnings from every stage in a successful report
- **WHEN** a JSON-mode compile has an `msbuild.ignored_package_reference` warning from its project file and an `fsharp.compiler` warning from the frontend, and no errors
- **THEN** the report has `success: true`, exit code 0 and both warnings, ordered by stage and then by document and position

#### Scenario: Unsupported construct in JSON mode
- **WHEN** a compile with `--json=document` meets `let mutable`
- **THEN** stdout parses as one report with `success: false` and an `fsharp.unsupported_mutable` diagnostic with its range, and stderr holds no copy of that report

### Requirement: Diagnostics stream as stages complete
In human mode, the command SHALL write each stage's diagnostics to stderr as soon as that stage completes, before later stages run. The streamed diagnostics SHALL be exactly the diagnostics of the final result, with none added or omitted. The outcome and exit code SHALL be the same as without streaming. With `--json=document`, stdout SHALL still hold one report and no streamed lines.

#### Scenario: Project-file warning before compilation ends
- **WHEN** a human-mode compile has an `msbuild.ignored_package_reference` warning and a frontend that takes a long time
- **THEN** the warning is on stderr before the frontend completes, and the final summary lists the same diagnostics

#### Scenario: JSON mode is not streamed
- **WHEN** the same compile runs with `--json=document`
- **THEN** stdout is one parseable report holding both diagnostics, and nothing else is written to stdout

### Requirement: JSON Lines output streams compile events
With `--json`, stdout SHALL hold only newline-delimited JSON. Each line SHALL be one compact UTF-8 JSON object with an `event` member, written and flushed when the event occurs. The events are `stageStarted`, `diagnostics`, `stageCompleted` and `completed`. Exactly one `completed` record SHALL be written, as the last line. It SHALL hold the full report that `--json=document` would write.

#### Scenario: Events in order
- **WHEN** a compile with `--json` has a project-file warning and succeeds
- **THEN** stdout has a `stageStarted` record for `sources`, then a `diagnostics` record holding the warning, then a `stageCompleted` record for `sources`, then the records of later stages, and a last `completed` record whose report has `success: true` and the same warning

#### Scenario: Failure still ends with completed
- **WHEN** a compile with `--json` fails on `fsharp.unsupported_mutable`
- **THEN** the last line is a `completed` record with `success: false` and exit code 1, and every line on stdout parses as JSON

### Requirement: JSON Lines records are a lossless stream of the report
The diagnostics of all `diagnostics` records, taken together, SHALL equal the diagnostics of the `completed` report, with none added or omitted. Consumers SHALL ignore event types and members they do not know. Parse errors and cancellation with `--json` SHALL still write one `completed` record when the process can write output.

#### Scenario: Both JSON flags
- **WHEN** `--json` and the removed `--json-lines` are both given
- **THEN** stdout is one `completed` record with an invocation failure that names `--json-lines` as unknown, and the exit code is 2

#### Scenario: Cancellation
- **WHEN** a compile with `--json` is cancelled before publication
- **THEN** the last line is a `completed` record with failure kind `cancelled`, and the exit code is 130
