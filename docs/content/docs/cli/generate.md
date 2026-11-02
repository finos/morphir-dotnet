---
title: Generate
description: Generate target artifacts from a Morphir model
---

# Generate

Generate target files from a decoded Morphir library. The delivered target is
`fsharp`, including classic and v4 models. Input, target and output are explicit.

## Synopsis

```text
morphir generate --input <file-or-directory> --target fsharp --output <directory>
  [--input-format auto|json|ion] [--option KEY=VALUE ...] [--json]
```

## Options

| Option | Meaning |
| --- | --- |
| `--input` | Required JSON/Ion model file or Ion tree directory |
| `--target` | Required exact registered target ID, currently `fsharp` |
| `--output` | Required output directory with owned generated files |
| `--input-format` | `auto` by default; `json` or `ion` constrains the reader |
| `--option` | Repeatable backend option; F# accepts Boolean `optimize`, default `true` |
| `--json` | Write one structured report to stdout |

Paths resolve against the invocation directory. No project configuration is
required or discovered. Repeated option keys use the last value. Values parse
as JSON first, so `optimize=false` is Boolean while `optimize='"false"'` is a
string and is refused by this backend.

## Inputs and target profile

JSON accepts classic integer versions 1, 2 and 3, their supported semantic
release declarations, and v4. Ion text, binary and trees support releases
3.0.0, 3.1.0 and 4.0.0. Automatic detection recognizes the binary marker even
without an Ion suffix. An Ion tree has `manifest.ion` at its root.

The F# target supports Library implementations at exact versions 1, 2, 3,
3.0.0, 3.1.0, 4 and 4.0.0. It emits types, values and mapped SDK operations.
Specs/Application, document literals, open records, holes, incomplete/native/
external bodies, nonempty constraints and unknown required semantics are refused.
Integers use Int32, floats finite doubles, decimals exact .NET decimal values
with retained scale up to 28, and characters one BMP scalar. Unsupported or
lossy representations fail before publication.

YAML model files, JSON/YAML trees and MEP envelopes are unsupported. Future
`morphir.toml`/`morphir.yml` discovery and MEP admission are separate changes.
Generation produces source files and their ownership manifest. It does not
create an F# project or compile and execute the output.

## Examples

Run these from a repository checkout, using its shared fixtures:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v3.json --target fsharp --output artifacts/generated-fsharp
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target fsharp --output artifacts/generated-fsharp --option optimize=false --json
morphir generate --input tests/Fixtures/MorphirIon/pairs/v3-daily-rate.ion --target fsharp --output artifacts/generated-fsharp --input-format ion --json
morphir generate --input tests/Fixtures/MorphirIon/trees/v3-daily-rate --target fsharp --output artifacts/generated-fsharp --json
```

For CI, retain the JSON report and propagate a failure:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target fsharp --output artifacts/generated-fsharp --json |
    Set-Content artifacts/generation-report.json
if ($LASTEXITCODE -ne 0) { throw "Morphir generation failed" }
```

## Output formats and exit codes

Human output lists relative artifact paths on stdout and diagnostics on stderr.
JSON mode writes one document containing `success`, `exitCode`, target/backend,
input/output, source version/family/kind/representation, ordered `artifacts`,
`written`, `unchanged`, `removed`, `diagnostics` and `failureKind`. Logs use stderr.
Failed reports contain no successful artifact list. Parsed JSON-mode invocation
errors also produce a report.

| Exit | Meaning |
| --- | --- |
| 0 | Generation and publication succeeded |
| 1 | Model or generation semantics are unsupported or invalid |
| 2 | Invalid invocation, unavailable provider or operational failure |
| 130 | Cancelled before publication |

For a single-module fixture, `artifacts` contains `["Inventory.fs"]`. Its order
is the F# compilation order, including on repeat runs when files are unchanged.

## Publication and ownership

Generated files are published through the shared tree publisher. The output
directory contains `.morphir-generated.json`, an ownership manifest listing
artifact paths in declared compilation order. Repeat generation leaves
byte-identical files and the manifest untouched. Reports distinguish written,
unchanged and removed paths.

Files listed by the previous manifest are removed when no longer generated.
Files outside that ownership list remain untouched. Keep the manifest alongside
the output so later runs can identify stale owned files.

Input and output paths must be separate. Rooted artifact paths, traversal, case
collisions and links are refused before publication. Generation errors preserve
previous output. A failure while staging also preserves previous files and the
ownership manifest.

Publication does not provide directory-wide rollback. A failure while committing
staged files can leave a mix of old and new files. Such failures return an
operational error; inspect the output and rerun generation after fixing the
reported filesystem problem.

## Common errors

Missing settings require an explicit `--input`, `--target` and `--output`.
An unknown target lists registered target IDs. Invalid F# options require
`optimize=true` or `optimize=false`. Codec diagnostics identify malformed models
and tree members. Limit diagnostics describe the exceeded byte, node, depth,
entry or path budget. See [CLI troubleshooting](../troubleshooting/).
