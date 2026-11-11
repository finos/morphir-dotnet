---
title: Generate
description: Generate target artifacts from a Morphir model
---

# Generate

Generate target files from a decoded Morphir library. Targets `fsharp` and
`csharp` support classic and v4 models. Target and output are explicit. The
input is an explicit `--input`, or else the IR of the most recent
`morphir compile` of the project.

## Synopsis

```text
morphir generate [--input <file-or-directory>] --target <fsharp|csharp> --output <directory>
  [--input-format auto|json|ion] [--option KEY=VALUE ...] [--json]
  [--extension <path-or-command>] [--project <path-or-name>] [--config <file>] [--out-dir <directory>]
```

## Options

| Option | Meaning |
| --- | --- |
| `--input` | JSON/Ion model file or Ion tree directory. Without it, the IR of the most recent compile |
| `--target` | Required exact registered target ID, `fsharp` or `csharp` |
| `--output` | Required output directory with owned generated files |
| `--input-format` | `auto` by default; `json` or `ion` constrains the reader |
| `--option` | Repeatable backend option, described below |
| `--json` | Write one structured report to stdout |
| `--extension` | A backend extension: a command name, a path, or a `.dll`. Overrides `[codegen.<target>].extension`. See [Backend extensions](#backend-extensions). |
| `--project` | The project directory, workspace member path or project name: its compile record without `--input`, and its configuration |
| `--config` | An explicit configuration file of the project |
| `--out-dir` | Without `--input`: the out root. Overrides `MORPHIR_OUT_DIR` and `workspace.out_dir` |

Paths resolve against the invocation directory. With `--input`, no project
configuration is required. Without `--extension`, `generate` reads the project
configuration only for `[codegen.<target>].extension`. With `--project` or `--config`, a configuration
error fails the command with exit code 2, as it does for `compile`. With neither, a discovered configuration
that does not load is a warning on stderr, and the built-in backend runs. Repeated option keys use the last value. Values parse
as JSON first, so `optimize=false` is Boolean while `optimize='"false"'` is a
string and is refused by this backend.

F# accepts Boolean `optimize`, default `true`. C# accepts numeric JSON
`language-version=14` or `language-version=15`, default `14`, and optional string
`namespace=Pricing.Generated`. Plain namespace text becomes a JSON string through
the shared option parser. Numeric strings, `preview`, `latest`, unsupported
numbers, unknown keys and wrong JSON kinds are invocation errors.

C# namespaces are dotted literal identifiers in Unicode Normalization Form C.
An identifier can start with a letter or `_` and continue with letters, decimal
digits, connector punctuation, combining marks or formatting characters.
Reserved keywords require `@`, for example `@class.Models`. Literal BMP Unicode
is supported, such as `Café.模型`; source `\u`/`\U` escapes and surrogate
characters are refused. The core validates this same vocabulary for direct callers.
An override changes the input package's emitted namespace and its qualified
references. Relative source paths retain the original package identity.

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

YAML model files, JSON/YAML trees and MEP envelopes are unsupported. MEP
admission is a separate change.
Generation produces source files and their ownership manifest. It does not
create an F# project or compile and execute the output.

The C# target declares the same exact Library versions. It uses the immutable
`Morphir.SDK.CSharp` runtime and the admitted
[C# generation contract]({{< relref "/docs/backends/csharp/diagnostics" >}}). Its own semantics and
located refusals remain those of the reusable C# library. Generation needs no
compiler installation. A separate consumer chooses the matching numeric compiler
profile and references the runtime package. The production Roslyn adapter remains
deferred.

## Backend extensions

A backend extension is a guest process that speaks the Morphir Extension Protocol (MEP) `0.2.0-draft.1`
over stdio and serves `morphir.backend.generateModel`. Select one with `--extension`, or with
`[codegen.<target>].extension` in the project configuration. The option overrides the key. `compile --target`
does not read the key: it runs the built-in backend and prints a note on stderr when the key is set. Without either,
the built-in backend of the target runs, as before. The F# generator is available as the guest
`morphir-fsharp-backend`; see [F# generation]({{< relref "/docs/backends/fsharp" >}}).

```sh
morphir generate --input model.json --target fsharp --output gen --extension morphir-fsharp-backend
```

```toml
[codegen.fsharp]
extension = "morphir-fsharp-backend"
```

The table is named by the target id. A target id can contain a dot; then the id is one quoted TOML key,
`[codegen."acme.codegen"]`. Without the quotes, `[codegen.acme.codegen]` is a table `codegen` inside a table
`acme`, which no target reads.

The command reads the value as `compile` reads a frontend extension:

- A name without a directory is searched only in the absolute directories of `PATH`, in order, and the
  command starts that absolute path. It is never taken from the invocation directory or from the directory of
  `morphir` itself. Starting a bare name directly would look there first: CreateProcess searches the
  application's directory and the current directory before `PATH` on Windows, and .NET does the same on Unix.
  On Windows the name matches a `.exe` or `.com` file, in `PATHEXT` order, and batch files are not started. On
  Unix the file must be executable. A name that is not on `PATH` fails with `extension.unavailable`; to run a
  file in the current directory, give a path such as `./guest`.
- A relative path is relative to the invocation directory.
- A path that ends in `.dll` runs on the `dotnet` host: `DOTNET_HOST_PATH` when it is set, else `dotnet`
  searched on `PATH` in the same way.

**What configuration can run.** `[codegen.<target>].extension` names a program that `morphir generate` starts with your user's rights.
The project configuration is the `morphir.toml` found from the working directory upward, so running
`morphir generate` inside a checkout that you do not trust can start a program that the checkout chooses: a file
inside the checkout, or any command on `PATH`. `MORPHIR_ISOLATED=1` leaves out only the machine and personal
configuration files, not the project's own file, and `MORPHIR_DISCOVERY_BOUNDARY` only limits how far up the
search goes. Read the `extension` keys of a project before you run `morphir generate` in it, or choose the file
with `--config`.

`generate` loads the model first, then starts the guest with explicit arguments and no shell, and passes the
guest's stderr to its own stderr. It agrees the session with `initialize` and offers JSON IR in the tuples
profile `morphir-ir`, codec `morphir-json`, envelope `0.1.0-draft.1`, IR `4.0.0` and `3.0.0`. A v4 model
is sent in the `4.0.0` tuple and a classic version 3 model in the `3.0.0` tuple, as the JSON bytes of the
input file. Other versions and Ion input are not sent to an extension. The command checks every artifact
path that the guest returns, keeps the guest's artifact order, and publishes the files as for a built-in
backend. When the run ends, it sends `shutdown` and then `exit`. A guest that does not answer `shutdown`, or does not take `exit`, within the deadline (for example a guest that is still busy or no longer reads its input) has its whole process tree stopped. So a request that times out ends within about two deadlines: one for the request and one for the end of the session.

The report names the backend that ran by its own extension id, and adds `backendTransport` (`mep`) and
`backendVersion`, the version that the extension gives. A report of a built-in backend has neither member.

An extension failure is exit code 2 with one of these codes, or exit code 1 when the guest rejects the model
with error diagnostics:

| Code | Meaning |
| --- | --- |
| `extension.unavailable` | The command is not on `PATH`, the guest did not start, the session did not initialize, the guest does not claim the target, or the guest gave an id reserved for built-ins: every id that starts with `morphir.` (such as `morphir.fsharp`), and the id of any registered built-in backend. |
| `extension.tuple_unavailable` | The host and the guest agreed no `generateModel` tuple, or the model needs a tuple that the session did not agree. The message names the tuples. |
| `extension.not_admitted` | The guest refused the request, for example an option, or the host did not send the model because it is over the limits that the session agreed (by default 45 MiB decoded, 60 MiB of base64, depth 4096, 50,000,000 nodes). |
| `extension.unsafe_artifact` | The guest returned an artifact path that is not a plain relative path; a segment that ends in a period or a space, or is a reserved Windows device name (`CON`, `NUL`, `COM1` and the like, also with an extension); two paths that differ only in case; or a path that is also the directory of another path. Nothing is published. |
| `extension.protocol_error` | The guest broke the protocol, for example a result that is not a generate result, more than 10,000 artifacts in one result, malformed describe claims, or a `-32800` answer that the host did not ask for. |
| `extension.timeout` | The guest did not answer before the deadline. |
| `invalid-extension` | The value of `--extension` or of the key is empty. |

Selection by an installed extension id is not available: the value is a command, a path or a `.dll`, as for
`compile`.

## Input from a prior compile

Without `--input`, `generate` reads the IR of the most recent successful
`morphir compile` of the project. It finds the project and the out root in the
same way as [compile]({{< relref "/docs/cli/compile" >}}): `--project` and
`--config` select the project, and the out root is `--out-dir`, else
`MORPHIR_OUT_DIR`, else the workspace `workspace.out_dir`, else `.morphir/out`
under the workspace root. `generate` then reads the compile record
`compile.json` and loads the IR that its `ir` descriptor names. The report gives
that IR file as `input` and the recorded IR version as `sourceVersion`. An
explicit `--input` never reads a record.

If there is no compile record, or the most recent compile failed and left a
tombstone, `generate` fails with `compile.record_missing` and exit code 2, tells
you to run `morphir compile` first, and publishes nothing. If the IR file does
not declare the version that the record names, it fails with
`compile.record_version_mismatch`. An `--input-format` that is not the format of
the compiled IR fails with `generate.input_format_conflict`.

`generate` holds the shared lock `compile.lock` from when it reads the record
until it has loaded the IR. While a compile of the same project runs, `generate`
writes one waiting line to stderr, waits, and then reads the IR of that compile.
A compile that starts while `generate` loads the IR waits for `generate`.

With this `morphir.toml` in the project directory, and the sources `src/Rates.fs`
and `src/Totals.fs` of the MCK corpus case `frontend-fsharp-0116`:

```toml
[project]
name = "acme/demo"

[frontend]
language = "fsharp"
```

compile the project, then generate F# from the compiled IR. Run both commands in
the project directory. The compile writes the path of the published IR to stdout,
and `generate` writes the generated files:

```bash
$ dotnet-morphir compile
/work/demo/.morphir/out/compile.dest/morphir-ir.json
$ dotnet-morphir generate --target fsharp --output gen
Rates.fs
Totals.fs
```

## Examples

Run these from a repository checkout, using its shared fixtures:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v3.json --target fsharp --output artifacts/generated-fsharp
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target fsharp --output artifacts/generated-fsharp --option optimize=false --json
morphir generate --input tests/Fixtures/MorphirIon/pairs/v3-daily-rate.ion --target fsharp --output artifacts/generated-fsharp --input-format ion --json
morphir generate --input tests/Fixtures/MorphirIon/trees/v3-daily-rate --target fsharp --output artifacts/generated-fsharp --json
```

For C#:

```powershell
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v3.json --target csharp --output artifacts/generated-csharp --json
morphir generate --input tests/Fixtures/MorphirIR/daily-rate-v4.json --target csharp --output artifacts/generated-csharp --option language-version=14 --option namespace=Pricing.Generated --json
morphir generate --input tests/Fixtures/MorphirIon/pairs/v3-daily-rate.ion --target csharp --output artifacts/generated-csharp --input-format ion --json
morphir generate --input tests/Fixtures/MorphirIon/trees/v3-daily-rate --target csharp --output artifacts/generated-csharp --json
```

`--option language-version=15 --option language-version=14` selects `14`.
Selecting `15` explicitly generates the C# 15 profile independently of the SDK
installed on the generation machine. Compile it separately using the matching
.NET 11 compiler. See [the executable C# walkthrough]({{< relref "/docs/getting-started/morphir-to-csharp" >}}).

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
`written`, `unchanged`, `removed`, `diagnostics` and `failureKind`, and for a selected
backend extension `backendTransport` and `backendVersion`. Logs use stderr.
Failed reports contain no successful artifact list. Parsed JSON-mode invocation
errors also produce a report.

Structured diagnostics remain fields in that JSON report. JSON failures need
not duplicate them as human-readable stderr text. Human-mode diagnostics and
any emitted log text use stderr. C# reports identify backend `morphir.csharp`.

| Exit | Meaning |
| --- | --- |
| 0 | Generation and publication succeeded |
| 1 | Model or generation semantics are unsupported or invalid |
| 2 | Invalid invocation, unavailable provider or operational failure |
| 130 | Cancelled before publication |

For a single-module fixture, `artifacts` contains `["Inventory.fs"]`. Its order
is the F# compilation order, including on repeat runs when files are unchanged.
For the C# daily-rate fixture it contains
`["p_Car.Rental/m_InventoryModule.g.cs"]`. Both direct generation and the CLI
use those same package-derived paths and source bytes.

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

Missing settings require an explicit `--target` and `--output`. Without
`--input`, `generate` needs a prior `morphir compile` of the project.
An unknown target lists registered target IDs. Invalid F# options require
`optimize=true` or `optimize=false`. Codec diagnostics identify malformed models
and tree members. Limit diagnostics describe the exceeded byte, node, depth,
entry or path budget. See [CLI troubleshooting](../troubleshooting/).
