---
title: Validate
description: Check a Morphir model with the built-in analyzers
---

# Validate

`morphir validate` reads a Morphir model and runs the enabled analyzers on it. An analyzer reads the model and
reports diagnostics. It does not change the model or write files. The command reads the model in the same way as
[`morphir generate`](generate.md).

## Synopsis

```text
morphir validate --input <file-or-directory> [--input-format auto|json|ion] [--json]
```

## Options

| Option | Meaning |
| --- | --- |
| `--input` | Required. A JSON or Ion model file, or an Ion tree directory. The path resolves against the invocation directory. |
| `--input-format` | `auto` (the default), `json` or `ion`. `json` and `ion` select the reader. |
| `--json` | Write one JSON report to stdout. |

The input rules are those of `morphir generate`: the same readers, the same detection, the same limits and the same
refusals (for example YAML input, or a MEP model envelope instead of bare IR).

## Analyzers

Both hosts, `morphir` and `dotnet-morphir`, include the same built-in analyzers. `morphir extension list` shows them
with the type `validator`.

| Id | Reads | Reports |
| --- | --- | --- |
| `morphir.type-validator` | Classic models, format versions 1, 2 and 3 | Definite type mismatches in value definitions, with the code `type-mismatch` |

The type validator reports a mismatch only when it is sure: for example, a `String` literal where the definition
declares `Int`. Types that it does not know stay silent. An IR node has no source position, so the diagnostic has no
location, and the message starts with the module and value name.

The run rule:

- Every enabled analyzer whose IR versions include the model's format version runs. The diagnostics merge into one
  result.
- The result is in a stable order: by extension id, then in the order in which each analyzer reported.
- When no enabled analyzer reads the model, the result has one `info` diagnostic with the code `no-analyzer` that names
  the family and the format version. The command does not convert the model to another family. Today this is the
  result for every V4 model.
- When an analyzer fails, the result names it with the status `failed` and still holds the diagnostics of the other
  analyzers.

## Output

Without `--json`, the command writes a table of the diagnostics to stdout, then one line for each analyzer that did not
run, then a summary line:

```bash
$ morphir validate --input model.json
╭──────────┬───────────────┬──────────┬───────────────────────────────────────────────────────────────────────────────────────────────┬────────────────────────╮
│ SEVERITY │ CODE          │ LOCATION │ MESSAGE                                                                                       │ EXTENSION              │
├──────────┼───────────────┼──────────┼───────────────────────────────────────────────────────────────────────────────────────────────┼────────────────────────┤
│ error    │ type-mismatch │          │ Inventory.dailyRate: Value has type Morphir.SDK.String.String, declared                       │ morphir.type-validator │
│          │               │          │ Morphir.SDK.Basics.Int.                                                                       │                        │
╰──────────┴───────────────┴──────────┴───────────────────────────────────────────────────────────────────────────────────────────────┴────────────────────────╯
1 error, 0 warnings, 0 info, 0 hints. Analyzers: 1 ran, 0 not run, 0 failed.
```

In a terminal the table has styled borders and headers and fits the terminal width. When stdout is redirected, or
`NO_COLOR` is set, the table has no ANSI codes. A location is written as `uri:line:character`, one-based. When there is
no diagnostic, only the summary line is written.

When the command stops before any analyzer runs (an invalid option, or an input that cannot be read), it writes the
diagnostics to stderr and nothing to stdout.

With `--json`, the command writes one report:

```bash
$ morphir validate --input model.json --json
{
  "host": "morphir",
  "success": false,
  "exitCode": 1,
  "input": "/work/model.json",
  "family": "Classic",
  "formatVersion": "3",
  "analyzers": [
    {
      "id": "morphir.type-validator",
      "status": "ran"
    }
  ],
  "diagnostics": [
    {
      "severity": "error",
      "code": "type-mismatch",
      "message": "Inventory.dailyRate: Value has type Morphir.SDK.String.String, declared Morphir.SDK.Basics.Int.",
      "extension": "morphir.type-validator"
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `host` | The host that wrote the report: `morphir` or `dotnet-morphir`. The two hosts write the same report apart from this field. |
| `success` | `true` when the exit code is 0. |
| `exitCode` | The exit code of the command. |
| `input` | The full path of the input. |
| `family`, `formatVersion` | The model's family (`Classic` or `V4`) and its declared format version. Left out when the input could not be read. |
| `analyzers` | Each analyzer, ordered by id, with its `status`: `ran`, `failed` (with a `detail`) or `not-run` (with a `reason`: `turned-off` or `unsupported-model`). |
| `diagnostics` | The diagnostics, in the MEP `Diagnostic` shape: `severity` (`error`, `warning`, `info` or `hint`), `code`, `message`, `location` (`uri` and a zero-based `range`), `related` and `data`, plus `extension`, the id of the analyzer that reported it. Absent members are left out. A diagnostic of the command itself has no `extension`. |

Logs go to stderr only, so `morphir validate --input model.json --json | jq .exitCode` works.

The command does not change the model file or the working directory.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | No diagnostic has the severity `error`. Warnings and information are in the result. |
| 1 | At least one diagnostic has the severity `error`. |
| 2 | An invalid invocation, an input or configuration that cannot be read, or an analyzer that failed. |
| 130 | The command was cancelled. |

## Configuration

The command reads the layered configuration of the invocation directory, with the same discovery as
[`morphir config`](config.md). It reads only `extensions."<id>".enabled`. The value `false` turns off the analyzer with
that id:

```toml
[extensions."morphir.type-validator"]
enabled = false
```

With this `morphir.toml` in the project directory, the same command gives:

```text
╭──────────┬─────────────┬──────────┬───────────────────────────────────────────────────────────────────┬───────────╮
│ SEVERITY │ CODE        │ LOCATION │ MESSAGE                                                           │ EXTENSION │
├──────────┼─────────────┼──────────┼───────────────────────────────────────────────────────────────────┼───────────┤
│ info     │ no-analyzer │          │ No enabled analyzer reads the Classic family at format version 3. │           │
╰──────────┴─────────────┴──────────┴───────────────────────────────────────────────────────────────────┴───────────╯
morphir.type-validator: not run (turned-off)
0 errors, 0 warnings, 1 info, 0 hints. Analyzers: 0 ran, 1 not run, 0 failed.
```

A configuration with an error stops the command with exit code 2 before any analyzer runs.

## validate and ir verify

- [`morphir ir verify`](ir-verify.md) checks that a JSON file has the shape of the Morphir IR JSON schema. Use it to
  check a file that another tool wrote.
- `morphir validate` decodes the model and checks what it means, for example the types of its values. Use it before
  you generate code from a model.

A model that passes `ir verify` can still have a type mismatch. A model that `validate` cannot read gives exit code 2.

## External analyzers

Only built-in analyzers run now. External analyzers, delivered as WASM extensions, come in step 2a of the
[Morphir extension model PRD](../../contributing/design/prds/morphir-extension-model.md). They will report the same
diagnostic shape and follow the same run rule.
