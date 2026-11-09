---
title: Compile
description: Compile source code to Morphir IR
---

# Compile

`compile` reads source documents, compiles them with a frontend, writes the IR in the requested format
version, and publishes it under the out root. With `--target`, it then generates code from the IR that it
wrote, as `generate` does. The dotnet tool `dotnet-morphir` has a built-in F# frontend.
The AOT executable `morphir` has no built-in frontend.

Both executables can also run a frontend as a MEP 0.2.0-draft.1 extension: a guest process that the
command starts and that compiles the documents. See [Frontend extensions](#frontend-extensions).

## Synopsis

```text
dotnet-morphir compile [--language <id>] [--input <file>... | --input <directory> | --project-file <file>]
  [--package-name <name>] [--ir-version <version>] [--project <path-or-name>] [--config <file>]
  [--extension <id-or-path>] [-o <directory>] [--out-dir <directory>]
  [--target <id> --generate-output <directory> [--option <key>=<value>]...] [--json | --json-lines]
```

## Options

| Option | Meaning |
| --- | --- |
| `--language`, `-l` | Source language, for example `fsharp`. Overrides `frontend.language`. |
| `--input`, `-i` | Source files in compilation order, or exactly one directory. |
| `--project-file` | One build-tool project file that names the ordered sources. See [project-file sources]({{< relref "/docs/configuration/project-file-sources" >}}). |
| `--package-name` | Package name, for example `acme/car-rental`. Overrides `project.name`. |
| `--ir-version` | IR format version: `1`, `2`, `3`, `4`, or a SemVer version such as `4.0.0`. Overrides `ir.format_version`. |
| `--project` | A project directory, a workspace member path, or a project name. |
| `--config` | An explicit configuration file. |
| `--extension` | A frontend extension: a command name, a path, or a `.dll`. Overrides `[frontend.<language>].extension`. |
| `--output`, `-o` | Install a copy of the IR in this directory after a successful compile. See [Output](#output). |
| `--out-dir` | The out root. Overrides `MORPHIR_OUT_DIR` and `workspace.out_dir`. |
| `--target` | A generation target, for example `fsharp` or `csharp`. Generate code from the compiled IR. See [Compile and generate](#compile-and-generate). |
| `--generate-output` | The owned output directory of the generation. Necessary with `--target`. |
| `--option` | A backend option `KEY=VALUE` for `--target`, as in `generate`. You can give it more than once. |
| `--json` | Write one JSON report to stdout. |
| `--json-lines` | Write the compile events to stdout as JSON Lines, and end with the report. |
| `--types-only` | Not implemented. The command refuses it with exit code 2. |

Do not use `--json` and `--json-lines` together. Do not use `--project-file` and `--input` together. Give
`--target` one time only, and always with `--generate-output`. `--generate-output` and `--option` need
`--target`. The command refuses these errors with exit code 2 before it compiles.

## Settings

Each setting comes from the first source that supplies it:

1. a command-line option;
2. the selected project configuration: `project.name`, `project.source_directory`,
   `project.exposed_modules`, `ir.format_version`, `frontend.language`, and the
   `[frontend.<language>]` keys `project_file` and `extension`;
3. a default: IR version `4`, source directory `src`, all public modules exposed.

`--project` and `--config` select the project through the usual project discovery. Outside a project, only
the command line and the defaults apply. The report gives the origin of each setting in `origins`.

```toml
[project]
name = "acme/car-rental"
exposed_modules = ["Pricing"]

[ir]
format_version = 4            # or "4.0.0"; a declared pre-release such as "4.1.0-draft.1" is also valid

[frontend]
language = "fsharp"

[frontend.fsharp]
project_file = "src/Acme.CarRental.fsproj"   # the same key in every [frontend.<language>] table
```

The upstream default of `frontend.language` is not a selection. If you do not set the language, the
extensions of the documents select it.

## IR versions

The command keeps the requested version exactly. It never reduces a version to its major number. `4` and
`4.0.0` select the same writer, and the report records the version as you wrote it. A pre-release, for
example `4.1.0-draft.1`, matches only a writer that declares that exact version. This build declares `1`,
`2`, `3` and `4`. A version that no writer declares fails before compilation with
`compile.unsupported_ir_version` and exit code 2.

## Sources

The command selects sources in this order:

1. `--project-file`: one project file;
2. `--input`: files in the given order, or one directory;
3. `[frontend.<language>].project_file` of the selected language;
4. the files of `project.source_directory`.

A directory gives its files in lexical order of their relative paths, with `/` separators. It gives only
files whose extension a registered frontend claims: `.fs` and `.fsi` for the in-process F# frontend, and `.fs` only for the F# MEP guest. The command does not search
for project files, and it does not apply rules that belong to one language. F# compiles files in order, so
use a project file or `--input` files when a file uses a module of another file.

The command reads each document as strict UTF-8. It refuses links and special files, and it names each
refused document.

## Frontends

The command selects a frontend by what each registered frontend advertises: the language (or the
extensions of every document), the file extension of each document, the output IR version, and support for
several documents. If no frontend matches, or more than one matches, the command fails before compilation
and names the frontend and the capability.

- `dotnet-morphir` registers the in-process F# frontend `morphir-dotnet.fsharp`.
- `morphir` has no F# frontend. A compile of F# fails with `compile.frontend_unavailable`.
- A configured extension replaces the registered frontends for the run. See [Frontend extensions](#frontend-extensions).

## Frontend extensions

A frontend extension is a guest process that speaks the Morphir Extension Protocol (MEP) `0.2.0-draft.1`
over stdio and serves `morphir.frontend.compileModel`. The F# frontend is available as the guest
`morphir-fsharp-frontend`. Install it as a dotnet tool. It needs the .NET runtime:

```sh
dotnet tool install --global Morphir.Frontend.FSharp.Extension
```

Select the extension with `--extension`, or with `[frontend.<language>].extension` in the project
configuration. The option overrides the key.

```sh
morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --extension morphir-fsharp-frontend
```

```toml
[frontend.fsharp]
extension = "morphir-fsharp-frontend"
```

The command reads the value in this way:

- A name without a directory is a command on `PATH`.
- A relative path is relative to the invocation directory.
- A path that ends in `.dll` runs on the `dotnet` host (`DOTNET_HOST_PATH` when it is set).

The command starts the guest with explicit arguments and no shell, and passes the guest's stderr to its own
stderr. It agrees the session with `initialize`, and it admits only the model tuples that both sides
support. The F# guest claims the language `fsharp` with the file extension `.fs`, several documents in one
request, and these tuples: profile `morphir-ir`, codec `morphir-json`, envelope `0.1.0-draft.1`, IR
`4.0.0` and `3.0.0`. IR versions 1 and 2 have no tuple. For them, the command compiles at `3.0.0` and
writes the classic model at the requested version. Before it sends a request, the command checks that the
session admits the language, the file extension of each document, the number of documents and the IR
version. The guest does not claim `.fsi` signature files.

The command checks the model envelope of the result (base64, length and SHA-256) before it reads the IR.
It always ends the session with `shutdown` and `exit`, or stops the process tree after a deadline. The
report names the frontend with its transport, for example
`org.finos.morphir.fsharp-frontend (mep)`. For the same documents, the guest gives the same IR and the same
diagnostics as the in-process frontend. A `.fsi` signature file is the exception: no frontend in the
session claims it, so the command refuses the compile before it sends a request. Without `--language`, the
refusal is `compile.language_required`.

An extension failure is exit code 2 with one of these codes:

| Code | Meaning |
| --- | --- |
| `extension.unavailable` | The guest did not start, or the session did not initialize. |
| `extension.not_admitted` | The agreed session does not admit the request, for example its IR version. |
| `extension.tuple_unavailable` | The host and the guest agreed no `compileModel` tuple, so no request can be sent. |
| `extension.protocol_error` | The guest broke the protocol, for example it wrote a log line to stdout. |
| `extension.timeout` | The guest did not answer before the deadline. |

Upstream qualification: the MEP guest kit of `finos/morphir` still needs a legacy `0.1` stage, so the guest
is not qualified with it yet. The guest is checked against the pinned `0.2.0-draft.1` contract and the
conformance corpus. See the [F# frontend]({{< relref "/docs/frontends/fsharp" >}}).

## Output

A compile writes its IR to `<out root>/[<member path>/]compile.dest/morphir-ir.json` and its task record to
`compile.json` beside that directory. These files use the out-directory contract of the Morphir ecosystem.
The out root is the first of:

1. `--out-dir`;
2. `MORPHIR_OUT_DIR`, relative to the invocation directory;
3. the workspace `workspace.out_dir`, relative to the workspace root;
4. `.morphir/out` under the workspace root.

Without a workspace, the project root is the workspace root, or the invocation directory outside a project.
The member path is the directory of a workspace member relative to the workspace root, for example
`packages/orders`.

The record has schema `1`, task `compile`, the `language`, the project file in `inputs` (relative to the
workspace root), `value`, the `ir` descriptor (`path`, `layout`, `format` and `version`, for example `v4`),
the `installed` ledger and `completedAt`. `morphir generate` without `--input` reads the IR through this
descriptor. See [Generate]({{< relref "/docs/cli/generate" >}}).

When a compile starts, it clears `compile.dest` and replaces an earlier record with a tombstone. A
successful compile then writes the full record. A failed compile publishes no IR and leaves the tombstone,
which keeps the `installed` ledger. The compile holds the exclusive lock `compile.lock` until its record and
its install are written. Another run that waits for the lock writes one line to stderr:

```text
waiting for another Morphir run to finish this task (<out root>/compile.lock)
```

`-o <directory>` installs a copy of the IR in the directory after a successful compile. It replaces only
the files that an earlier install of this task wrote there, and it keeps all other files. If a file that it
must write is already there and an earlier install did not write it, the command fails with
`install.foreign_content` and exit code 2, and the directory does not change. The compile output in
`compile.dest` stays published.

## Compile and generate

With `--target <id> --generate-output <directory>`, a successful compile continues into generation in the
same run. The command gives the exact IR bytes that it wrote to the model loader of `generate --input`, and
then runs the generation stages of `generate`: backend selection, `--option` validation, generation and
owned publication. The result is the same as these two commands:

```text
dotnet-morphir compile ...
dotnet-morphir generate --input <out root>/compile.dest/morphir-ir.json --target <id> --output <directory>
```

Both give the same files with the same bytes, and both reports name the same backend. Classic IR versions
(`1`, `2` and `3`) give a classic model, and the backend uses its classic path.

- Generation does not run when the compile fails.
- When generation fails, the compiled IR stays published. The report has `success: false`, the `output`
  of the IR, the `modules`, and a `generation` member that names the target and, after backend selection,
  the backend. A backend that does not accept the IR version fails before generation, names the backend
  and the versions that it declares, and does not change the generation output.
- A generation failure has the exit code of `generate`: `1` for a model that the backend cannot use, `2`
  for an invocation, option or publication failure, `130` for cancellation.
- `-o` installs the IR. It is not the generation output. A run with more than one target is not part of
  this command.

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --target fsharp --generate-output generated --json
{
  "success": true,
  "exitCode": 0,
  "language": "fsharp",
  "frontend": "morphir-dotnet.fsharp (in-process)",
  "irVersion": "4",
  "sources": {
    "projectFile": null,
    "root": "src",
    "documents": [
      "Rates.fs",
      "Totals.fs"
    ]
  },
  "modules": [
    "Rates",
    "Totals"
  ],
  "output": "/work/.morphir/out/compile.dest/morphir-ir.json",
  "installed": [],
  "diagnostics": [],
  "failureKind": null,
  "origins": {
    "language": {
      "kind": "cli",
      "flag": "--language"
    },
    "packageName": {
      "kind": "cli",
      "flag": "--package-name"
    },
    "irVersion": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  },
  "generation": {
    "target": "fsharp",
    "backend": "morphir.fsharp",
    "output": "/work/generated",
    "sourceVersion": "4",
    "family": "V4",
    "distributionKind": "Library",
    "representation": "Json",
    "artifacts": [
      "Rates.fs",
      "Totals.fs"
    ],
    "written": [
      "Rates.fs",
      "Totals.fs"
    ],
    "unchanged": [],
    "removed": []
  }
}
```

In human mode, the summary on stderr also names the backend, the number of generated artifacts and the
generation output. Stdout still holds only the path of the published IR.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success. Warnings do not change the exit code. |
| `1` | The frontend, an IR builder or the writer reported a source or model error. |
| `2` | An invocation, configuration, project-file, selection, extension or I/O failure. |
| `130` | The command was cancelled before publication. |

## Human output

Without `--json` or `--json-lines`, the command writes each stage's diagnostics to stderr when that stage
completes, before later stages start. A project-file warning is therefore on stderr before a slow compile
ends. The last stderr line is a summary that lists the same diagnostics. On success, stdout holds only the
path of the published IR.

```text
Pricing.fs:3:9: error fsharp.unsupported_mutable: Mutable bindings and fields are not supported; Morphir values are immutable. Use a new let binding, or a record update, instead of changing a value.
compile failed (source, exit code 1); 1 error(s): error fsharp.unsupported_mutable
```

## JSON report

With `--json`, stdout holds exactly one JSON report. Logs go to stderr. The examples on this page use the
sources of the MCK corpus cases `frontend-fsharp-0116` (`src/Rates.fs` and `src/Totals.fs`) and
`frontend-fsharp-0001` (`broken/Pricing.fs`). `output` and `sources.root` show example paths.

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --json
{
  "success": true,
  "exitCode": 0,
  "language": "fsharp",
  "frontend": "morphir-dotnet.fsharp (in-process)",
  "irVersion": "4",
  "sources": {
    "projectFile": null,
    "root": "src",
    "documents": [
      "Rates.fs",
      "Totals.fs"
    ]
  },
  "modules": [
    "Rates",
    "Totals"
  ],
  "output": "/work/.morphir/out/compile.dest/morphir-ir.json",
  "installed": [],
  "diagnostics": [],
  "failureKind": null,
  "origins": {
    "language": {
      "kind": "cli",
      "flag": "--language"
    },
    "packageName": {
      "kind": "cli",
      "flag": "--package-name"
    },
    "irVersion": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

The report members are:

| Member | Meaning |
| --- | --- |
| `success`, `exitCode` | The outcome. |
| `language`, `frontend` | The language and the selected frontend, when known. |
| `irVersion` | The requested IR version, exactly as written. |
| `sources` | The project file (or null), the root, and the documents in compilation order. The root and the project file are relative to the invocation directory when they are in it, else absolute. Each document is relative to the root, with `/` separators. |
| `modules` | The compiled modules, in compilation order. |
| `output` | The published IR path, or null. |
| `installed` | The absolute files that the `-o` install of this run holds. Empty without `-o`. |
| `diagnostics` | Every diagnostic of the run, ordered by stage, then document, then position. |
| `failureKind` | `source`, `invocation`, `configuration`, `operational` or `cancelled`, or null. |
| `origins` | Where each setting came from: `cli` with its `flag`, `configuration` with its `file` and `key`, or `default`. |
| `generation` | Only with `--target`: `target`, `backend` (null when selection failed), `output`, and the `generate` report members `sourceVersion`, `family`, `distributionKind`, `representation`, `artifacts`, `written`, `unchanged` and `removed`. |

Diagnostics have the frontend (MEP) shape: `severity`, `code`, `message`, and optional `location` (a
document `uri` and a zero-based `range` in UTF-16 code units), `related` and `data`. A location `uri` is
relative to `sources.root`, as the documents are.

```bash
$ dotnet-morphir compile --language fsharp --input broken/Pricing.fs --package-name acme/demo --json
{
  "success": false,
  "exitCode": 1,
  "language": "fsharp",
  "frontend": "morphir-dotnet.fsharp (in-process)",
  "irVersion": "4",
  "sources": {
    "projectFile": null,
    "root": "broken",
    "documents": [
      "Pricing.fs"
    ]
  },
  "modules": [],
  "output": null,
  "installed": [],
  "diagnostics": [
    {
      "severity": "error",
      "code": "fsharp.unsupported_mutable",
      "message": "Mutable bindings and fields are not supported; Morphir values are immutable. Use a new let binding, or a record update, instead of changing a value.",
      "location": {
        "uri": "Pricing.fs",
        "range": {
          "start": {
            "line": 2,
            "character": 8
          },
          "end": {
            "line": 2,
            "character": 15
          }
        }
      },
      "data": {
        "construct": "mutable binding or field"
      }
    }
  ],
  "failureKind": "source",
  "origins": {
    "language": {
      "kind": "cli",
      "flag": "--language"
    },
    "packageName": {
      "kind": "cli",
      "flag": "--package-name"
    },
    "irVersion": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

A pre-release that no writer declares fails before compilation:

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --ir-version 4.1.0-draft.1 --json
{
  "success": false,
  "exitCode": 2,
  "language": "fsharp",
  "frontend": null,
  "irVersion": "4.1.0-draft.1",
  "sources": null,
  "modules": [],
  "output": null,
  "installed": [],
  "diagnostics": [
    {
      "severity": "error",
      "code": "compile.unsupported_ir_version",
      "message": "No IR writer declares format version 4.1.0-draft.1. Declared versions: 1, 2, 3, 4."
    }
  ],
  "failureKind": "configuration",
  "origins": {
    "language": {
      "kind": "cli",
      "flag": "--language"
    },
    "packageName": {
      "kind": "cli",
      "flag": "--package-name"
    },
    "irVersion": {
      "kind": "cli",
      "flag": "--ir-version"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

The AOT executable has no F# frontend:

```bash
$ morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --json
{
  "success": false,
  "exitCode": 2,
  "language": "fsharp",
  "frontend": null,
  "irVersion": "4",
  "sources": null,
  "modules": [],
  "output": null,
  "installed": [],
  "diagnostics": [
    {
      "severity": "error",
      "code": "compile.frontend_unavailable",
      "message": "No registered frontend reads the language 'fsharp'. This build registers no frontend. The dotnet tool dotnet-morphir has a built-in F# frontend. Set [frontend.fsharp].extension in morphir.toml, or use --extension, to configure a frontend extension."
    }
  ],
  "failureKind": "configuration",
  "origins": {
    "language": {
      "kind": "cli",
      "flag": "--language"
    },
    "packageName": {
      "kind": "cli",
      "flag": "--package-name"
    },
    "irVersion": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

## JSON Lines

With `--json-lines`, stdout holds only newline-delimited JSON. Each line is one compact UTF-8 JSON object
with an `event` member. The command writes and flushes each line when its event occurs. Logs stay on
stderr.

| `event` | Other members | Meaning |
| --- | --- | --- |
| `stageStarted` | `stage` | A stage starts. |
| `diagnostics` | `stage`, optional `document`, `diagnostics` | The diagnostics of a stage for one document, or for no document. |
| `stageCompleted` | `stage`, `passed` | A stage completes. |
| `completed` | `report` | The last line, exactly once. `report` is the report that `--json` writes. |

The stages are `settings`, `sources`, `capture`, `frontend` (which builds the requested IR family),
`write` and `publish`, and with `--target` also `generate` (model loading, backend selection, option
validation and generation) and `generatePublish` (owned publication of the generated files). A stage that
does not run has no records. All `diagnostics` records together hold
the same diagnostics as the report. The records keep emission order; the report keeps stage, document and
position order. A parse error, the `--json` and `--json-lines` conflict, and cancellation also end with
one `completed` record. Ignore event types and members that you do not know: new ones can be added
without a new format version.

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --ir-version 3 --json-lines
{"event":"stageStarted","stage":"settings"}
{"event":"stageCompleted","stage":"settings","passed":true}
{"event":"stageStarted","stage":"sources"}
{"event":"stageCompleted","stage":"sources","passed":true}
{"event":"stageStarted","stage":"capture"}
{"event":"stageCompleted","stage":"capture","passed":true}
{"event":"stageStarted","stage":"frontend"}
{"event":"stageCompleted","stage":"frontend","passed":true}
{"event":"stageStarted","stage":"write"}
{"event":"stageCompleted","stage":"write","passed":true}
{"event":"stageStarted","stage":"publish"}
{"event":"stageCompleted","stage":"publish","passed":true}
{"event":"completed","report":{"success":true,"exitCode":0,"language":"fsharp","frontend":"morphir-dotnet.fsharp (in-process)","irVersion":"3","sources":{"projectFile":null,"root":"src","documents":["Rates.fs","Totals.fs"]},"modules":["Rates","Totals"],"output":"/work/.morphir/out/compile.dest/morphir-ir.json","installed":[],"diagnostics":[],"failureKind":null,"origins":{"language":{"kind":"cli","flag":"--language"},"packageName":{"kind":"cli","flag":"--package-name"},"irVersion":{"kind":"cli","flag":"--ir-version"},"input":{"kind":"cli","flag":"--input"}}}}
```

### Difference from the Rust CLI

The Rust `morphir` CLI (finos/morphir) writes one compact report line for `--json-lines`. This CLI writes
the event stream, one record per line, as the events occur. Until upstream aligns, the two CLIs give
different `--json-lines` output. The last line of this CLI is a `completed` record that holds the full
report, so a consumer that reads only the last line and its `report` member works with this CLI. An
upstream issue proposes this record format as the shared meaning.

## Unimplemented options

These fail with exit code 2 and `compile.unsupported_setting`, which names the option or the key and its
file: `--types-only`, `ir.format = "yaml"` or `"ion"`, and `ir.layout = "document-tree"`.

## See also

- [Project-file sources]({{< relref "/docs/configuration/project-file-sources" >}})
- [Generate]({{< relref "/docs/cli/generate" >}})
- [Troubleshooting]({{< relref "/docs/cli/troubleshooting" >}})
