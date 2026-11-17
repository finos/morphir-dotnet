# Spec Delta

## Purpose

Defines one machine-output convention for every `morphir` command: a stream of JSON Lines by default, so a tool sees each event as it happens, and a single JSON document on request, both carrying the same report.

```console
$ morphir compile --language fsharp --input src/Rates.fs --json
{"event":"stageStarted","stage":"sources"}
{"event":"stageCompleted","stage":"sources","passed":true}
{"event":"completed","report":{"success":true,"exitCode":0}}

$ morphir compile --language fsharp --input src/Rates.fs --json=document
{"success":true,"exitCode":0}
```

## ADDED Requirements

### Requirement: JSON output is chosen by one option with an optional mode
A command with machine output SHALL accept `--json[=stream|document]`. A bare `--json` SHALL select `stream`. Without `--json`, the command SHALL write its human output. An unknown mode SHALL fail with exit code 2 and list `stream` and `document`.

#### Scenario: A bare flag streams
- **WHEN** a user runs a command with a bare `--json`
- **THEN** stdout holds JSON Lines that end with one `completed` record

#### Scenario: Unknown mode
- **WHEN** a user runs a command with `--json=lines`
- **THEN** the command exits 2 and names `stream` and `document` as the known modes

### Requirement: The stream is JSON Lines ending with the report
In `stream` mode, stdout SHALL hold only JSON Lines: each line one compact UTF-8 JSON object with an `event` member, written and flushed when its event occurs. Exactly one `completed` record SHALL be written, as the last line, with the command's full report in its `report` member. Parse errors, usage errors and cancellation SHALL still end with one `completed` record when the process can write output.

#### Scenario: Every line parses
- **WHEN** a streaming command succeeds after several events
- **THEN** every stdout line parses as a JSON object with an `event` member, and the last is the only `completed` record

#### Scenario: A usage error still ends with completed
- **WHEN** a streaming command is given an unknown option
- **THEN** stdout is one `completed` record whose report states the usage failure, and the exit code is 2

#### Scenario: A command with nothing to stream
- **WHEN** a command that has no events runs with `--json`
- **THEN** stdout is exactly one `completed` line, which is also one valid JSON document

### Requirement: Events are written as they occur
A streaming command SHALL write each event's record before it starts the work that follows the event. The records' diagnostics, taken together, SHALL equal the report's diagnostics, with none added or omitted.

#### Scenario: An early warning is visible before a slow stage ends
- **WHEN** a streaming command reports a warning in its first stage and its second stage takes a long time
- **THEN** the warning's record is on stdout before the second stage completes, and the final report holds the same warning

### Requirement: The document mode writes the report alone
In `document` mode, stdout SHALL hold exactly one JSON document: the command's report, equal to the `report` member that `stream` mode's `completed` record holds for the same run.

#### Scenario: The two modes carry one report
- **WHEN** the same run is made with `--json` and with `--json=document`
- **THEN** the document equals the `completed` record's `report`

#### Scenario: A document is pipeable
- **WHEN** a user runs `morphir validate --input model.json --json=document | jq .exitCode`
- **THEN** `jq` prints the report's exit code

### Requirement: An option value is given only with an equals sign
A mode SHALL be given only as `--json=<mode>`. A bare `--json` SHALL never take the next argument, so the next argument keeps its meaning for the command. Where the next argument cannot belong to the command, the command SHALL fail with exit code 2 and suggest `--json=<that argument>`.

#### Scenario: The next argument stays the command's
- **WHEN** a command that takes a positional input runs as `--json model.json`
- **THEN** `model.json` is the input and the output mode is `stream`

#### Scenario: A separated mode is refused with a hint
- **WHEN** a command without positional arguments runs as `--json document`
- **THEN** the command exits 2 and the message suggests `--json=document`

### Requirement: Stdout holds only machine output in JSON mode
With `--json` in either mode, stdout SHALL hold nothing but the JSON the mode defines. Logs, progress and human-readable diagnostics SHALL go to stderr, which SHALL stay human-readable in every mode.

#### Scenario: A log line goes to stderr
- **WHEN** a command writes a log line during a `--json` run
- **THEN** the line is on stderr and stdout still parses as the mode requires

### Requirement: Report members are named relative to the report
Where a capability names members of a command's JSON output, those SHALL be members of the command's report: of the document in `document` mode, and of the `completed` record's `report` in `stream` mode. Consumers SHALL ignore event types and members they do not know.

#### Scenario: A named member in both modes
- **WHEN** a spec says the JSON output of `extension list` has a top-level `host` member
- **THEN** that member is at the top of the document in `document` mode and at the top of `report` in `stream` mode

### Requirement: The former streaming flag is removed
The CLI SHALL NOT accept `--json-lines` on any command. It SHALL fail as an unknown option with exit code 2.

#### Scenario: The old flag is unknown
- **WHEN** a user runs `morphir compile --json-lines`
- **THEN** the command exits 2 and names `--json-lines` as unknown
