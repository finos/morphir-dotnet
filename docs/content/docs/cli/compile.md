---
title: Compile
description: Compile source code to Morphir IR
---

# Compile

`compile` reads source documents, compiles them with a frontend, writes the IR in the requested format
version, and publishes it under the out root. With `--target`, it then generates code from the IR that it
wrote, as `generate` does. Every offering has the built-in [C# frontend](../frontends/csharp/), the Native AOT
`morphir` included. The dotnet tool `dotnet-morphir` also has a built-in F# frontend.
The Native AOT executable `morphir` has no built-in F# frontend. A managed `morphir` built from `src/Morphir.Managed` has the same in-process F# frontend as `dotnet-morphir`, but it is not published yet.

Both executables can also run a frontend as a MEP 0.2.0-draft.1 extension: a guest process that the
command starts and that compiles the documents. See [Frontend extensions](#frontend-extensions).

## Synopsis

```text
dotnet-morphir compile [--language <id>] [--input <file>... | --input <directory> | --project-file <file>]
  [--package-name <name>] [--ir-version <version>] [--ir-layout <layout>] [--ir-format json|yaml|ion] [--project <path-or-name>] [--config <file>]
  [--extension <id-or-path>] [-o <directory>] [--out-dir <directory>]
  [--target <id> --generate-output <directory> [--option <key>=<value>]...] [--json[=stream|document]]
```

## Options

| Option | Meaning |
| --- | --- |
| `--language`, `-l` | Source language, for example `fsharp`. Overrides `frontend.language`. |
| `--input`, `-i` | Source files in compilation order, or exactly one directory. |
| `--project-file` | One build-tool project file that names the ordered sources. See [project-file sources]({{< relref "/docs/configuration/project-file-sources" >}}). |
| `--package-name` | Package name, for example `acme/car-rental`. Overrides `project.name`. |
| `--ir-version` | IR format version: `1`, `2`, `3`, `4`, or a SemVer version such as `4.0.0`. Overrides `ir.format_version`. |
| `--ir-layout` | IR layout: `single-file` or `document-tree`. Overrides `ir.layout` and `ir.mode`. See [IR layouts](#ir-layouts). |
| `--ir-format` | The serialization of the written IR: `json`, `yaml` or `ion`. Overrides `ir.format`. See [IR formats](#ir-formats). |
| `--project` | A project directory, a workspace member path, or a project name. |
| `--config` | An explicit configuration file. |
| `--extension` | A frontend extension: a command name, a path, or a `.dll`. Overrides `[frontend.<language>].extension`. |
| `--output`, `-o` | Install a copy of the IR in this directory after a successful compile. See [Output](#output). |
| `--out-dir` | The out root. Overrides `MORPHIR_OUT_DIR` and `workspace.out_dir`. |
| `--target` | A generation target, for example `fsharp` or `csharp`. Generate code from the compiled IR. See [Compile and generate](#compile-and-generate). |
| `--generate-output` | The owned output directory of the generation. Necessary with `--target`. |
| `--option` | A backend option `KEY=VALUE` for `--target`, as in `generate`. You can give it more than once. |
| `--json[=stream\|document]` | Write JSON to stdout. A bare `--json` streams the compile events as JSON Lines and ends with the report. `--json=document` writes the report alone. See [JSON stream](#json-stream) and [JSON report](#json-report). |
| `--types-only` | Not implemented. The command refuses it with exit code 2. |

Give the mode of `--json` only after `=`: the command refuses `--json document` and suggests `--json=document`.
Do not use `--project-file` and `--input` together. Give
`--target` one time only, and always with `--generate-output`. `--generate-output` and `--option` need
`--target`. The command refuses these errors with exit code 2 before it compiles.

## Settings

Each setting comes from the first source that supplies it:

1. a command-line option;
2. the selected project configuration: `project.name`, `project.source_directory`,
   `project.exposed_modules`, `ir.format_version`, `ir.layout` (or the deprecated `ir.mode`), `ir.format`,
   `frontend.language`, and the `[frontend.<language>]` keys `project_file` and `extension`;
3. a default: IR version `4`, layout `single-file`, IR format `json`, source directory `src`, all public modules exposed.

`--project` and `--config` select the project through the usual project discovery. Outside a project, only
the command line and the defaults apply. The report gives the origin of each setting in `origins`.

```toml
[project]
name = "acme/car-rental"
exposed_modules = ["Pricing"]

[ir]
format_version = 4            # or "4.0.0"; a declared pre-release such as "4.1.0-draft.1" is also valid
layout = "document-tree"      # or "single-file", the default
format = "json"               # or "yaml" or "ion"

[frontend]
language = "fsharp"

[frontend.fsharp]
project_file = "src/Acme.CarRental.fsproj"   # the same key in every [frontend.<language>] table
```

The other keys of the `[frontend.<language>]` table of the selected language are options of its frontend. The
command gives each key to the frontend as an extra member of the MEP compile options, with `-` for `_`, so
`loop_lowering` is `loop-lowering`. Only a scalar value (a string, a boolean or a number) goes to the frontend. The
C# frontend has the key `loop_lowering` (`traversal` or `recursion`), and `[frontend.csharp]` is a closed table: a key
that the C# frontend does not know, or a value that is not one of its words, fails when the configuration loads, with
exit code 2. See [C# handling options](../backends/csharp/options.md).

A language id can contain a dot; then the id is one quoted TOML key, `[frontend."acme.lang"]`. Without
the quotes, `[frontend.acme.lang]` is a table `lang` inside a table `acme`, which no language reads.

The upstream default of `frontend.language` is not a selection. If you do not set the language, the
extensions of the documents select it.

## IR versions

The command keeps the requested version exactly. It never reduces a version to its major number. `4` and
`4.0.0` select the same writer, and the report records the version as you wrote it. A pre-release, for
example `4.1.0-draft.1`, matches only a writer that declares that exact version. This build declares `1`,
`2`, `3` and `4`. A version that no writer declares fails before compilation with
`compile.unsupported_ir_version` and exit code 2.

## IR layouts

`ir.layout`, or `--ir-layout`, selects how the compile stores its IR:

- `single-file` (the default) writes one file, `compile.dest/morphir-ir.json` (or `.yaml` / `.ion`).
- `document-tree` writes a document tree, the directory `compile.dest/morphir-ir`: for `json` or `yaml`,
  `manifest.{json,yaml}`, one `module.{json,yaml}` for each module, and one `<stem>.type.{json,yaml}` or
  `<stem>.value.{json,yaml}` for each type and value, below `pkg/<package>/` and `deps/<package>/@/`; for `ion`,
  `manifest.ion`, `module.ion`, `<stem>.type.ion`, and `<stem>.value.ion`. The tree holds the same model as the single file. It has no
  module order, so a tree that joins back into one file lists the modules by name.

Document trees exist only for IR 3 and IR 4. A `document-tree` layout with IR version `1` or `2` fails before
compilation with `compile.unsupported_setting` and exit code 2. For `json` and `yaml`, an IR 4 tree says `"formatVersion": 4` in every
file, and an IR 3 tree says `"formatVersion": "3.1.0"` in every file, as the parent's v3 document-tree
specification requires, and the record names its version `v3.1.0`.

With `--ir-format json` or `yaml`, request an IR 3 tree with `--ir-version 3`, not `3.1.0`. The classic writer declares `3`, and `3.1.0` is
another version (VER-1), so `--ir-version 3.1.0` with JSON or YAML fails before compilation with
`compile.unsupported_ir_version` and exit code 2. The JSON/YAML tree that `--ir-version 3` writes still says `3.1.0`
in every file. With `--ir-format ion`, both `--ir-version 3` (which writes `3.0.0`) and `--ir-version 3.1.0` (which writes `3.1.0`) are supported. The report keeps `irVersion` as you wrote it (`3` or `3.1.0`).

`ir.mode` is the deprecated name of the layout: `classic` is `single-file`, and `vfs` is `document-tree`. A
configured `ir.mode` always gives the warning `compile.deprecated_setting`. When `--ir-layout` or `ir.layout`
is also set, that layout is used and `ir.mode` is ignored. The selected value (the first of `--ir-layout`,
`ir.layout` and `ir.mode` that is set) fails with one `compile.invalid_setting` error and exit code 2 when it is
not one of these words or not a string. An overridden value that is not valid gives the warning
`compile.invalid_setting` and is ignored. The report gives the origin of the layout in `origins.irLayout`.

```text
dotnet-morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --ir-layout document-tree
```

In human mode, standard output is the path of the tree, for example `/work/.morphir/out/compile.dest/morphir-ir`.

## IR formats

`--ir-format` or `ir.format` selects how the command writes the IR. Every format holds the same IR value.

| Format | Single file | Tree files | Content |
| --- | --- | --- | --- |
| `json` | `morphir-ir.json` | `manifest.json`, `module.json`, `*.type.json`, `*.value.json` | The IR JSON that the writer of the IR version writes (or split tree). This is the default. |
| `yaml` | `morphir-ir.yaml` | `manifest.yaml`, `module.yaml`, `*.type.yaml`, `*.value.yaml` | The canonical YAML of that JSON, as the [YAML profile](https://github.com/finos/morphir/blob/main/docs/spec/ir/schemas/v4/yaml-profile.md) defines it. A YAML reader of the profile reads back the same value. |
| `ion` | `morphir-ir.ion` | `manifest.ion`, `module.ion`, `*.type.ion`, `*.value.ion` | The canonical Ion 1.0 text of the [Ion IR format](https://github.com/finos/morphir/blob/main/docs/design/draft/ir/ion.md), draft `0.1.0-draft.1`: one datagram ending with `morphir_footer::{}` for single-file, or split tree files without footer. |

The Ion format carries the IR versions `3`, `3.0.0`, `3.1.0`, `4` and `4.0.0` only. Ion at `3.1.0` writes the v3 library
that the IR 3 writer gives and states `formatVersion: "3.1.0"`; the task record names `v3.1.0`. JSON and YAML have no
writer for a v3 library at `3.1.0`, so `--ir-version 3.1.0` with them fails with `compile.unsupported_ir_version`. Ion
holds a v3 doc as a string, so a v3 Ion compile writes a member that has no documentation with the empty doc. Ion with any other version, such as
`1`, `2` or `4.1.0-draft.1`, fails before compilation with `compile.unsupported_setting` and exit code 2. A value that is
not `json`, `yaml` or `ion` fails with exit code 2: in a configuration file the configuration check reports it once,
with `compile.configuration`, and `--ir-format` reports it with `compile.invalid_setting`. If the command cannot write the IR in the selected format, the write stage
fails with `compile.ir_format_failed`. The task record names the format in `ir.format`, and `-o` installs the same
file or tree directory.

`morphir generate --input` reads JSON, YAML, or Ion single files and document trees. `morphir generate` without `--input`
reads a compile record whose IR is JSON, YAML, or Ion (`single-file` or `document-tree`).

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

- Every offering registers the in-process C# frontend `morphir-dotnet.csharp`. It reads `.cs` documents and writes IR version 4.
- `dotnet-morphir` registers the in-process F# frontend `morphir-dotnet.fsharp`.
- The Native AOT `morphir` has no F# frontend. A compile of F# fails with `compile.frontend_unavailable`.
- The managed `morphir` built from `src/Morphir.Managed` registers the same in-process F# frontend as `dotnet-morphir`. It is not published yet.
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

**What configuration can run.** `[frontend.<language>].extension` names a program that `morphir compile` starts with your user's rights.
The project configuration is the `morphir.toml` found from the working directory upward, so running
`morphir compile` inside a checkout that you do not trust can start a program that the checkout chooses: a file
inside the checkout, or any command on `PATH`. `MORPHIR_ISOLATED=1` leaves out only the machine and personal
configuration files, not the project's own file, and `MORPHIR_DISCOVERY_BOUNDARY` only limits how far up the
search goes. Read the `extension` keys of a project before you run `morphir compile` in it, or choose the file
with `--config`.

The command starts the guest with explicit arguments and no shell, and passes the guest's stderr to its own
stderr. It agrees the session with `initialize`, and it admits only the model tuples that both sides
support. The F# guest claims the language `fsharp` with the file extension `.fs`, several documents in one
request, and these tuples: profile `morphir-ir`, codec `morphir-json`, envelope `0.1.0-draft.1`, IR
`4.0.0` and `3.0.0`. IR versions 1 and 2 have no tuple. For them, the command compiles at `3.0.0` and
writes the classic model at the requested version. Before it sends a request, the command checks that the
session admits the language, the file extension of each document, the number of documents and the IR
version. The guest does not claim `.fsi` signature files.

The command checks the model envelope of the result (base64, length and SHA-256) before it reads the IR.
When the run ends, it sends `shutdown` and then `exit`. A guest that does not answer `shutdown`, or does not take `exit`, within the deadline (for example a guest that is still busy or no longer reads its input) has its whole process tree stopped. So a request that times out ends within about two deadlines: one for the request and one for the end of the session. The
report names the frontend with its transport, for example
`org.finos.morphir.fsharp-frontend (mep)`. For the same documents, the guest gives the same IR and the same
diagnostics as the in-process frontend. A `.fsi` signature file is the exception: no frontend in the
session claims it, so the command refuses the compile before it sends a request. Without `--language`, the
refusal is `compile.language_required`.

An extension failure is exit code 2 with one of these codes:

| Code | Meaning |
| --- | --- |
| `extension.unavailable` | The guest did not start, the session did not initialize, or the guest gave an id reserved for built-ins (every id that starts with `morphir.`). |
| `extension.not_admitted` | The agreed session does not admit the request, for example its IR version. |
| `extension.tuple_unavailable` | The host and the guest agreed no `compileModel` tuple, so no request can be sent. |
| `extension.protocol_error` | The guest broke the protocol, for example it wrote a log line to stdout. |
| `extension.timeout` | The guest did not answer before the deadline. |

Upstream qualification: the MEP guest kit of `finos/morphir` still needs a legacy `0.1` stage, so the guest
is not qualified with it yet. The guest is checked against the pinned `0.2.0-draft.1` contract and the
conformance corpus. See the [F# frontend]({{< relref "/docs/frontends/fsharp" >}}).

## Output

A compile writes its IR to `<out root>/[<member path>/]compile.dest/morphir-ir.json` (`morphir-ir.yaml` or
`morphir-ir.ion` for those [IR formats](#ir-formats)), or for a
[document tree](#ir-layouts) to the directory `compile.dest/morphir-ir`, and its task record to
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
descriptor. See [Generate]({{< relref "/docs/cli/generate" >}}). The record of a document-tree compile has:

```json
"value": ["morphir-ir"],
"ir": { "path": "morphir-ir", "layout": "document-tree", "format": "json", "version": "v4" }
```

An IR 3 tree has `"version": "v3.1.0"`. Only a backend extension reads a recorded document tree, in a
bounded session under MEP `0.2.0-draft.2`: give `morphir generate` the extension and no `--input`.

```text
dotnet-morphir compile --ir-layout document-tree
dotnet-morphir generate --target text --extension ./orders-generator --output generated
```

`orders-generator` is a backend extension that advertises the `text` target. See
[Bounded generation from a compile record]({{< relref "/docs/cli/generate#bounded-generation-from-a-compile-record" >}}).
A built-in backend, for example `generate --target fsharp` with no extension, does not read a JSON document
tree. It fails with `unsupported-format` and exit code 1, and writes nothing.

When a compile starts, it clears `compile.dest` and replaces an earlier record with a tombstone. A
successful compile then writes the full record. A failed compile publishes no IR. After an earlier successful
compile it leaves the tombstone, which keeps the `installed` ledger; a first compile that fails leaves no
`compile.json`. A write failure, a cancellation or a record failure removes the staged and moved IR from
`compile.dest`. While another program (a virus scanner, for example) holds a file, each removal tries again for
about 2.6 seconds. When a removal is still refused, the run keeps its exit code (130 for a cancellation) and writes
a warning on stderr that names each path that remains and the file system's reason. The warning comes after every removal of the cleanup, the staged
record `.compile.json.<id>.tmp` included, and says what `compile.json` holds then: a tombstone, a completed record,
a file that cannot be read, or nothing (on a first compile, or after an earlier record that could not be read). The
next compile clears `compile.dest` and removes the staged records that earlier runs left. A compile
that cannot clear `compile.dest` when it starts fails with `compile.prepare_failed` and exit code 2; one that is
cancelled while it tries exits with 130. Both release `compile.lock`. The compile holds the exclusive lock `compile.lock` until its record and
its install are written. Another run that waits for the lock writes one line to stderr:

```text
waiting for another Morphir run to finish this task (<out root>/compile.lock)
```

`-o <directory>` installs a copy of the IR in the directory after a successful compile: the file
`morphir-ir.json`, or the tree directory `morphir-ir`. It replaces only
the files that an earlier install of this task wrote there, and it keeps all other files. If a file that it
must write is already there and an earlier install did not write it, the command fails with
`install.foreign_content` and exit code 2, and the directory does not change. The compile output in
`compile.dest` stays published.

## Compile and generate

`compile` writes its task output and record, and can install IR or generated files.
It currently has no `--dry-run` option; `--option format=false` controls formatting
of generated F# only.

With `--target <id> --generate-output <directory>`, a successful compile continues into generation in the
same run. The command gives the exact IR bytes that it wrote to the model loader of `generate --input` (for the
`yaml` format, the JSON that the YAML was written from, which holds the same model), and
then runs the generation stages of `generate`: backend selection, `--option` validation, generation and
owned publication. The result is the same as these two commands:

```text
dotnet-morphir compile ...
dotnet-morphir generate --input <out root>/compile.dest/morphir-ir.json --target <id> --output <directory>
```

With `ir.layout = "document-tree"`, the backend gets the single file that the writer wrote, named
`morphir-ir.json`. The published tree is the split of that file, so the backend gets the same model, and the
generated files are the same as for a single-file compile. `generate --input` does not read a JSON document
tree; give it the `--input` of a single-file compile.

Both give the same files with the same bytes, and both reports name the same backend, as long as the project
configuration selects no backend extension. `compile --target` always runs the built-in backend of the target
and does not read `[codegen.<target>].extension`; when that key is set, the command prints a note on stderr,
and only `morphir generate` runs the extension. The other keys of the table are options of the backend, as in
`generate`, for example `float_equality = "ieee"` in `[codegen.csharp]`. Classic IR versions
(`1`, `2` and `3`) give a classic model. The F# backend uses its classic path, and the C# backend gets the model
after the upgrade to the current IR. As in `generate`, the
`generation` member says whether the built-in backend formatted the files (`formatting`: `applied`, `off` or
`unavailable`), and a host that cannot run the formatter (the Native AOT `morphir`) writes a warning on stderr,
never on stdout or in the JSON output. Pass `--option format=false` for the unformatted files without the warning.

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
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --target fsharp --generate-output generated --json=document
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
    "irLayout": {
      "kind": "default"
    },
    "irFormat": {
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
    "removed": [],
    "formatting": "applied"
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

Without `--json`, the command writes each stage's diagnostics to stderr when that stage
completes, before later stages start. A project-file warning is therefore on stderr before a slow compile
ends. The last stderr line is a summary that lists the same diagnostics. On success, stdout holds only the
path of the published IR.

```text
Pricing.fs:3:9: error fsharp.unsupported_mutable: Mutable bindings and fields are not supported; Morphir values are immutable. Use a new let binding, or a record update, instead of changing a value.
compile failed (source, exit code 1); 1 error(s): error fsharp.unsupported_mutable
```

## JSON report

With `--json=document`, stdout holds exactly one JSON report. Logs go to stderr. The examples on this page use the
sources of the MCK corpus cases `frontend-fsharp-0116` (`src/Rates.fs` and `src/Totals.fs`) and
`frontend-fsharp-0001` (`broken/Pricing.fs`). `output` and `sources.root` show example paths.

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --json=document
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
    "irLayout": {
      "kind": "default"
    },
    "irFormat": {
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
| `output` | The published IR path, or null: the file `morphir-ir.json`, or the directory `morphir-ir` of a document tree. |
| `installed` | The absolute entries that the `-o` install of this run holds: the IR file, or the tree directory. Empty without `-o`. |
| `diagnostics` | Every diagnostic of the run, ordered by stage, then document, then position. |
| `failureKind` | `source`, `invocation`, `configuration`, `operational` or `cancelled`, or null. |
| `origins` | Where each setting came from: `cli` with its `flag`, `configuration` with its `file` and `key`, or `default`. |
| `passes` | Only when a pass ran after the frontend: the ids that ran, the application list (`rule`, `before` and `after` node addresses, and `origin` when the node has a source location) and the reads of other modules. See [the middle end](../pipeline/middle-end.md). |
| `generation` | Only with `--target`: `target`, `backend` (null when selection failed), `output`, and the `generate` report members `sourceVersion`, `family`, `distributionKind`, `representation`, `artifacts`, `written`, `unchanged` and `removed`, `formatting` when the backend reports it, and `passes` when a pass ran before the backend. The application list of `generation.passes` continues the list of `passes`: each `before` names the node in the model that the frontend wrote, and a node that a pass after the frontend and a pass before the backend replaced has one record. The diagnostics of the passes before the backend are in `diagnostics`, in the stage `generate`. |

Diagnostics have the frontend (MEP) shape: `severity`, `code`, `message`, and optional `location` (a
document `uri` and a zero-based `range` in UTF-16 code units), `related` and `data`. A location `uri` is
relative to `sources.root`, as the documents are.

```bash
$ dotnet-morphir compile --language fsharp --input broken/Pricing.fs --package-name acme/demo --json=document
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
    "irLayout": {
      "kind": "default"
    },
    "irFormat": {
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
$ dotnet-morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --ir-version 4.1.0-draft.1 --json=document
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
    "irLayout": {
      "kind": "default"
    },
    "irFormat": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

The Native AOT executable has no F# frontend:

```bash
$ morphir compile --language fsharp --input src/Rates.fs --package-name acme/demo --json=document
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
      "message": "No registered frontend reads the language 'fsharp'. Registered frontends: morphir-dotnet.csharp. The self-contained and framework-dependent morphir and dotnet-morphir have a built-in F# frontend; this offering uses the F# frontend process delivery morphir-fsharp-frontend. Set [frontend.fsharp].extension in morphir.toml, or use --extension, to configure a frontend extension."
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
    "irLayout": {
      "kind": "default"
    },
    "irFormat": {
      "kind": "default"
    },
    "input": {
      "kind": "cli",
      "flag": "--input"
    }
  }
}
```

## JSON stream

With `--json` (the same as `--json=stream`), stdout holds only newline-delimited JSON. Each line is one compact UTF-8 JSON object
with an `event` member. The command writes and flushes each line when its event occurs. Logs stay on
stderr.

| `event` | Other members | Meaning |
| --- | --- | --- |
| `stageStarted` | `stage` | A stage starts. |
| `diagnostics` | `stage`, optional `document`, `diagnostics` | The diagnostics of a stage for one document, or for no document. |
| `stageCompleted` | `stage`, `passed` | A stage completes. |
| `completed` | `report` | The last line, exactly once. `report` is the report that `--json=document` writes. |

The stages are `settings`, `sources`, `capture`, `frontend` (which builds the requested IR family),
`transform` (the passes of the stage after a frontend; it starts only when a pass has that stage), `write` and `publish`, and with `--target` also `generate` (model loading, backend selection, option
validation and generation) and `generatePublish` (owned publication of the generated files). A stage that
does not run has no records. All `diagnostics` records together hold
the same diagnostics as the report. The records keep emission order; the report keeps stage, document and
position order. A parse error and cancellation also end with one `completed` record. Ignore event types and members that you do not know: new ones can be added
without a new format version.

```bash
$ dotnet-morphir compile --language fsharp --input src/Rates.fs src/Totals.fs --package-name acme/demo --ir-version 3 --json
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
{"event":"completed","report":{"success":true,"exitCode":0,"language":"fsharp","frontend":"morphir-dotnet.fsharp (in-process)","irVersion":"3","sources":{"projectFile":null,"root":"src","documents":["Rates.fs","Totals.fs"]},"modules":["Rates","Totals"],"output":"/work/.morphir/out/compile.dest/morphir-ir.json","installed":[],"diagnostics":[],"failureKind":null,"origins":{"language":{"kind":"cli","flag":"--language"},"packageName":{"kind":"cli","flag":"--package-name"},"irVersion":{"kind":"cli","flag":"--ir-version"},"irLayout":{"kind":"default"},"irFormat":{"kind":"default"},"input":{"kind":"cli","flag":"--input"}}}}
```

A consumer that needs only the outcome reads the last line and its `report` member. The command has no
`--json-lines` option: it fails as an unknown option with exit code 2. Use `--json`.

## Unimplemented options

This fails with exit code 2 and `compile.unsupported_setting`, which names the option: `--types-only`.

## See also

- [Project-file sources]({{< relref "/docs/configuration/project-file-sources" >}})
- [Generate]({{< relref "/docs/cli/generate" >}})
- [Troubleshooting]({{< relref "/docs/cli/troubleshooting" >}})
