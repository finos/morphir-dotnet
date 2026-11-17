---
title: "CLI Reference"
linkTitle: "CLI"
weight: 30
description: "Command-line interface reference for Morphir .NET tooling"
---

# Morphir .NET CLI Reference

The Morphir .NET CLI provides powerful command-line tools for working with Morphir IR files, validating schemas, and managing Morphir projects.

## Available Commands

- [morphir compile](./compile/) - Compile C# or F# sources to Morphir IR (every host has the C# frontend; `dotnet-morphir` also has the F# frontend built in)
- [morphir generate](./generate/) - Generate F# or C# from classic or v4 JSON/Ion libraries
- [morphir validate](./validate/) - Check a model with the built-in analyzers, such as the type validator
- [morphir extension list](./management/#extension-list) - List the built-in generators and analyzers of this host
- [morphir extension runtimes](./extension-runtimes/) - Report whether this host can run WASM extensions, or why not
- [WASM host runner adapter](./mck-wasm-host/) - Development source awaiting the dedicated `morphir-mck` entry point
- [morphir extension worker](./extension-worker/) - _Experimental and internal:_ serve one WASM extension module over stdio for a conformance runner
- [morphir mck](./mck/) - Run the separately installed Morphir Compatibility Kit, `morphir-mck`, with the same arguments

### Toolchain manager

- [morphirup](./morphirup/) - The standalone toolchain manager. This build makes plan-only setups and checks a project's request and lock; it acquires and installs nothing yet

### Model commands

The `model` group holds the commands that act on a Morphir model. It replaces the `ir` group, which was removed with
no alias.

- **[morphir model verify](./model-verify/)** - Validate a Morphir model, a single IR file (v1-v4) or a document tree, against the official IR JSON schemas

### Project Management

- [morphir project migrate]({{< relref "/docs/cli/project" >}}) converts one classic project to the newer project system.
- [morphir workspace migrate]({{< relref "/docs/cli/workspace" >}}) inventories and adopts projects within a bounded repository directory.

## Installation

The Morphir .NET CLI is distributed as a .NET tool. Install it globally with:

```bash
dotnet tool install -g Morphir.CLI
```

Or locally in a project:

```bash
dotnet tool install Morphir.CLI
```

## Getting Help

For help with any command, use the `--help` flag:

```bash
morphir --help
morphir model --help
morphir model verify --help
```

## Common Workflows

### Validating IR Files

The most common workflow is validating Morphir IR JSON files to ensure they conform to the expected schema:

```bash
# Validate a single file with auto-detection
morphir model verify path/to/morphir-ir.json

# Validate with explicit schema version
morphir model verify --schema-version 3 path/to/morphir-ir.json

# Get one JSON report for CI/CD (see Machine output below)
morphir model verify --json=document path/to/morphir-ir.json

# Quiet mode (only the exit code)
morphir model verify --quiet path/to/morphir-ir.json
```

See the [morphir model verify](./model-verify/) documentation for complete details.

## Machine output

Every command with machine output takes one option, `--json[=stream|document]`:

- A bare `--json` (the same as `--json=stream`) writes JSON Lines to stdout. Each line is one compact JSON object with
  an `event` member, written when its event occurs. The last line is the only `completed` record, and its `report`
  member holds the command's report. A command with no events writes only that line, which is also one JSON document.
- `--json=document` writes the report alone, as one JSON document. It is the `report` of the `completed` record of the
  same run.
- Without `--json`, the command writes its output for people.

For a valid model `model.json` in the current directory, the stream is one line:

```bash
$ morphir validate --input model.json --json
{"event":"completed","report":{"host":"morphir","success":true,"exitCode":0,"input":"/work/model.json","family":"Classic","formatVersion":"3","analyzers":[{"id":"morphir.type-validator","status":"ran"}],"diagnostics":[]}}
```

The document is the `report` of that line:

```bash
$ morphir validate --input model.json --json=document
{
  "host": "morphir",
  "success": true,
  "exitCode": 0,
  "input": "/work/model.json",
  "family": "Classic",
  "formatVersion": "3",
  "analyzers": [
    {
      "id": "morphir.type-validator",
      "status": "ran"
    }
  ],
  "diagnostics": []
}
```

Give the mode only after `=`. A bare `--json` never takes the next argument, so `morphir model verify --json model.json`
verifies `model.json`. A command without a positional argument refuses `--json document` with exit code 2 and
suggests `--json=document`. A mode that is not `stream` or `document` fails with exit code 2 and names both. A parse
or usage error in a JSON mode still ends with one `completed` record (or the one document) whose report states the
error. Logs and human-readable diagnostics go to stderr in every mode, so stdout holds only the JSON:
`morphir validate --input model.json --json=document | jq .exitCode` prints `0` for the model above. A consumer
ignores event types and members that it does not know. When a page names a member of a command's JSON output, it is
a member of the report: of the document, and of the `report` of the `completed` record.

The removed `--json-lines` option fails as an unknown option with exit code 2: use `--json`.

Output that a cross-host contract defines keeps that contract's form. `morphir extension worker` follows the pinned
WASM worker contract: `--help --json` writes one `morphir/command-result` with no `completed` wrapper, and its event
lines stay `morphir/command-event` (see [extension worker](./extension-worker/)).

## Exit Codes

The commands share a general split of exit codes:

- **0**: Success.
- **1**: The command ran, and its input failed: a model that is not valid, or a source or model error.
- **2**: A usage, configuration or I/O error: a command line that does not parse, a bad option value, or an input or
  configuration that cannot be read.
- **130**: The command was cancelled, in a command that supports cancellation (for example `compile`, `generate` and
  `validate`).

The exact codes differ by command, so read each command's page for them. For example,
[`model verify`](./model-verify/#exit-codes) reports a file that is missing or not JSON as a model that is not valid,
with exit code 1, where `validate` gives 2 for an input that it cannot read, and a `config` key that is not found
exits 1. An unknown command, such as `morphir ir`, exits 1.

## Configuration

The [configuration guide]({{< relref "/guides/configuration" >}}) distinguishes the compatibility TOML cache loader from the additive shared configuration and project system.

The [morphir config read commands]({{< relref "/docs/cli/config" >}}) inspect effective/scoped values, source reports and redacted explanations. JSON output follows the published schema; success, missing keys and usage/loading failures use exit codes 0/1/2. Shared `config set/unset` editing, [project migrate]({{< relref "/docs/cli/project-migrate" >}}) and bounded [workspace migrate]({{< relref "/docs/cli/workspace-migrate" >}}) are available. Start with the [workspace configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}}).

## Troubleshooting

See the [Troubleshooting Guide](./troubleshooting/) for solutions to common issues.
