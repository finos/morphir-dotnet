---
title: "morphir model convert"
linkTitle: "model convert"
weight: 11
description: "Store a Morphir model as a single file or as a document tree, keeping its IR version and its meaning"
---

# morphir model convert

Change how a Morphir model is stored without changing the model. A model is stored as a single IR JSON file or as a
document tree: a directory with a manifest and one file for each module, type and value. `model convert` reads either
layout and writes the other. It also reads compacted files and writes them plain (see [Encodings](#encodings)). The IR
version stays the same: a v3 model stays v3, and a v4 model stays v4.

`model convert` is a command of the `model` group, beside [`model verify`](model-verify.md).

## Synopsis

```text
morphir model convert --input <path> --output <path> [--layout=<layout>] [--compact[=<mode>]] [--json[=stream|document]]
```

## Try it

Here `morphir-ir.json` is a small IR v4 model: a `Library` of the package `car/rental` with one module, `inventory`,
which holds one value, `daily-rate`. Write it as a document tree, then write the tree back as a single file:

```bash
$ morphir model convert --input morphir-ir.json --output tree --layout=document-tree
tree
$ morphir model convert --input tree --output copy.json --layout=single-file
copy.json
```

Each run writes the path of its output to stdout. Its summary goes to stderr:

```text
model convert: wrote 3 file(s) to tree: IR 4, single-file to document-tree, plain; 0 notice(s)
model convert: wrote 1 file(s) to copy.json: IR 4, document-tree to single-file, plain; 0 notice(s)
```

The tree holds `manifest.json`, `pkg/car/rental/inventory/module.json` and
`pkg/car/rental/inventory/daily-rate.value.json`. With your own model, such as the `morphir-ir.json` that
`morphir compile` writes, the files follow your package and its modules. `copy.json` is the same model as `morphir-ir.json`. It is written
in the writer's canonical spelling, so its bytes can differ from a file that another tool wrote. To check the tree, run:

```bash
morphir model verify tree
```

## Options

| Option | Description |
| --- | --- |
| `-i`, `--input <path>` | The model to convert: a single-file model, or the directory of a document tree. Required. |
| `-o`, `--output <path>` | Where to write the converted model. Required. It must not exist. A tree can also go to an empty directory. A missing parent directory is created. |
| `--layout=<layout>` | The layout of the output: `document-tree` or `single-file`. The default is the layout of the input. The form `--layout <layout>`, with a space, is accepted too. |
| `--compact[=<mode>]` | Write compacted files. Writing compacted files is not implemented yet: the run is refused (see [Compaction](#compaction)). A compacted input is read without it. |
| `--json[=stream\|document]` | Write the report as JSON: a stream of events that ends with the report (`--json`), or the report alone (`--json=document`). See [JSON output](#json-output). |

Give the values of `--compact` and `--json` only after `=` (see [Machine output](_index.md#machine-output)). A bare `--compact` or `--json` never takes the next argument.

## Layouts

The input's layout is detected: a file is a single-file model, and a directory is a document tree. The output gets
the layout that `--layout` names. Without `--layout`, the output keeps the layout of the input.

- **`single-file`**: one IR JSON file, as `morphir compile` writes it. A v3 `Library` says `"formatVersion": 3`, and
  a v3 `Specs` says `"formatVersion": "3.1.0"`.
- **`document-tree`**: the layout of finos/morphir's document-tree specifications for
  [IR 4](https://github.com/finos/morphir/blob/main/docs/spec/ir/schemas/v4/document-tree-files.md) and [IR 3.1.0](https://github.com/finos/morphir/blob/main/docs/spec/ir/schemas/v3/document-tree-files.md), in the JSON
  profile. The tree holds `manifest.json`, a `module.json` for each module, and a `<name>.type.json` or
  `<name>.value.json` for each type and value. The package's own modules are below `pkg/<package>/`, and each
  dependency is below `deps/<package>/@/`. The version segment `@` is bare because the model carries no package
  version. Each file of a v3 tree says `"formatVersion": "3.1.0"`. Writing uses the path budget 4000, and a name
  whose path would be longer is cut and recorded in the module file's `fileNames`.
  [`model verify`](model-verify.md#document-trees) describes the tree in more detail.

The layout can also follow `--layout` after a space:

```bash
$ morphir model convert --input morphir-ir.json --output tree --layout document-tree
tree
```

A tree is read in the sorted order of its paths, so the order in which a disk lists files does not change the model.
Reading a tree drops the manifest's `layout` and `pathBudget`, which describe the tree, not the model. It also drops
a v4 manifest's `version` and `created`, which a single file cannot carry, and reports a notice for each one. Content
that a single file cannot carry is never dropped silently.

Only IR v3 and IR v4 models are converted, since document trees exist only for those versions. A v1 or v2 file is
refused with `model.unsupported_version`.

## Encodings

A file is plain, or compacted: wrapped in a name-compaction envelope of finos/morphir's `ir/name-compaction`
contract. The envelope's top-level `$schema` names its version, its `context` holds tables of names, paths and fully
qualified names, and its `document` is the Morphir document, in which a `{"$name": "<id>"}` stands for an entry of a
table. This version reads the envelope
`https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json`.

`model convert` reads compacted files and expands them: each reference is replaced by its entry at the positions
where the IR has a name, a path or a fully qualified name, and nowhere else, so a string that only looks like a
reference stays as written. A v3 document stays v3, with its names as classic word arrays, and a v4 document has them
as canonical strings. The output is plain. Writing compacted files is not implemented yet (see
[Compaction](#compaction)).

- A single file is compacted when it is in an envelope. A tree is compacted when any of its files is: the manifest, a
  module file, or a type or value file. The report states `inputEncoding: "compacted"`, and `"plain"` otherwise.
- Because the output is plain, a compacted input with no `--layout` is written as plain files in its own layout. It
  is not [nothing to do](#nothing-to-do).
- A file whose `$schema` is not an envelope that this version knows is refused with `context.undeclared_schema`. The
  report then states no `inputEncoding` when the only envelopes are undeclared; a tree that also has a file in a
  declared envelope is `compacted`.
- A context that breaks a rule of the contract is refused with the code of the rule, and the diagnostic names the
  file (see [Context refusals](#context-refusals)). Nothing is written.

Here `compacted.json` is a compacted IR v4 single file. Write it as a plain single file:

```bash
$ morphir model convert --input compacted.json --output plain.json --layout=single-file
plain.json
```

Its summary on stderr is `model convert: wrote 1 file(s) to plain.json: IR 4, single-file to single-file, plain; 0
notice(s)`. Here `model` is a compacted v4 tree, each file of which is in an envelope. Join it into a plain single
file:

```bash
$ morphir model convert --input model --output billing.json --layout=single-file --json=document
{
  "success": true,
  "exitCode": 0,
  "input": "model",
  "output": "billing.json",
  "inputLayout": "document-tree",
  "outputLayout": "single-file",
  "inputEncoding": "compacted",
  "outputEncoding": "plain",
  "irVersion": "4",
  "filesWritten": [
    "billing.json"
  ],
  "diagnostics": [],
  "notices": []
}
```

[`model verify`](model-verify.md#compacted-files) checks a compacted model after the same expansion.

## Compaction

`--compact` takes the optional mode `names`, and a bare `--compact` means `--compact=names`. An unknown mode, such
as `--compact=bogus`, exits 2 and lists `names`. `--compact names`, with a space, exits 2 and suggests
`--compact=names`.

**Writing compacted files is not implemented yet.** A run with `--compact` parses, reads the model, and is then
refused with `tree.unsupported_option` (exit code 1). Nothing is written. The report states `outputEncoding:
"compacted"` and `compactMode: "names"`. This page will change when compacted writing lands.

## Nothing to do

A run that changes neither the layout nor the encoding fails with exit code 2 and `model.nothing_to_do`: a plain input
with no `--layout` and no `--compact`. The message names the input's layout and encoding. The command reads only the
top level of the input (of a tree, the top level of each tree file, to find an envelope) and writes nothing:

```bash
$ morphir model convert --input morphir-ir.json --output other.json --json=document
{
  "success": false,
  "exitCode": 2,
  "input": "morphir-ir.json",
  "output": "other.json",
  "inputLayout": "single-file",
  "outputLayout": "single-file",
  "inputEncoding": "plain",
  "outputEncoding": "plain",
  "filesWritten": [],
  "diagnostics": [
    {
      "code": "model.nothing_to_do",
      "severity": "error",
      "message": "The input is a plain single file and the output would be the same. Give --layout=document-tree to change the layout, or --compact to compact it.",
      "location": "morphir-ir.json"
    }
  ],
  "notices": []
}
```

## A refused conversion leaves no output

The command checks `--output` before it reads the input. The output must not be the input, be inside it, or hold
it. It must not exist, except that a tree can go to an empty directory. The model is then read, converted, and
written into a staging place next to the output, which is moved into place only when every file is written. A
refused or failed run leaves nothing at `--output`, and a file that was already there stays unchanged.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The model was converted, and every file was written. |
| 1 | The conversion was refused or failed. The input is missing or not a valid model or tree, the envelope or the context of a compacted file is refused, the output is in the way, `--compact` asks for compacted output, or a file could not be read or written. |
| 2 | A usage error: arguments that do not parse, a bad `--layout` or `--compact` value, an `--input` or `--output` that is not a valid path, or nothing to do. |
| 130 | The run was cancelled, for example with Ctrl+C. The report is still written. |

## Diagnostics

Each diagnostic has a stable code. In text mode, each one is written to stderr when it occurs, as
`<location>: <severity> <code>: <message>`, with no `<location>:` when the diagnostic has no location. A summary
line follows. With `--json`, the diagnostics are in the report
and in the `diagnostics` events.

| Code | Exit code | Meaning |
| --- | --- | --- |
| `model.invalid_arguments` | 2 | The arguments do not parse, for example `--compact names` or `--layout=bogus`, or `--input` or `--output` is not a valid path, for example an empty one. |
| `model.nothing_to_do` | 2 | The run changes neither the layout nor the encoding. |
| `model.input_not_found` | 1 | No file or directory is at `--input`. |
| `model.output_not_empty` | 1 | `--output` exists and is not an empty directory for a tree: an existing file, a directory with an entry, or any directory when a single file is written. |
| `model.output_overlaps_input` | 1 | `--output` is the input, is inside it, or holds it. |
| `model.unsupported_version` | 1 | The input is IR v1 or v2, which has no document tree. |
| `context.undeclared_schema` | 1 | A file has a top-level `$schema` that this version does not know as a compaction envelope. |
| `context.unknown_alias`, `context.duplicate_entry`, `context.unknown_critical`, `context.too_many_entries`, `context.name_too_long`, `context.after_document`, `context.invalid_envelope`, `context.invalid_entry` | 1 | A compacted file breaks a rule of its envelope or its context; see [Context refusals](#context-refusals). |
| `depth_limit_exceeded` | 1 | A file is nested too deeply to read: a compacted single file deeper than 4,096 levels as written or once its references are expanded, a tree file deeper than 512, or a model deeper than a reader of its IR version holds. |
| `tree.unsupported_option` | 1 | `--compact` was given, and this version does not write compacted files yet. |
| `invalid_json` | 1 | A file of the input is not JSON. |
| `model.read_failed`, `model.write_failed` | 1 | The disk refused a read or a write. |
| `model.cancelled` | 130 | The run was cancelled. |

A tree that breaks a rule of finos/morphir's [document-tree specification](https://github.com/finos/morphir/blob/main/docs/spec/ir/schemas/v4/document-tree-files.md) is refused with that
specification's code, and the diagnostic names the file. Examples are `missing_member` (a module file lists a name
that has no file), `version_mismatch` (a v3 tree file that does not say `"3.1.0"`), `unknown_member`,
`tree.ambiguous_profile` (both `manifest.json` and `manifest.yaml`), `tree.unsupported_profile` (a YAML file: this
version reads the JSON profile only), `tree.over_budget`, `tree.outside_tree` and `tree.versioned_dependency`. A
refused read lists every error that it finds.

### Context refusals

A compacted file is refused, with the file as the location of the diagnostic, when its envelope or its context breaks
a rule of `ir/name-compaction`. The codes are the contract's:

| Code | The file |
| --- | --- |
| `context.undeclared_schema` | Has a `$schema` that this version does not declare. The message names the URL. |
| `context.unknown_alias` | Has a `{"$name": "<id>"}` whose id no table of its position defines. The message names the id. |
| `context.duplicate_entry` | Has a member twice in a table, in the context or in the envelope. |
| `context.unknown_critical` | Marks a member path `critical` that this version does not recognize. |
| `context.too_many_entries` | Has a table with more than 16,384 entries. |
| `context.name_too_long` | Has an entry longer than 1,024 UTF-8 bytes. |
| `context.after_document` | Has its `document` member before its `context` member: the context must come first. |
| `context.invalid_envelope` | Has no `context` or `document` object, or a `critical` that is not a list of strings. |
| `context.invalid_entry` | Has an id that is not ASCII letters and digits starting with a letter, an entry that is not the canonical spelling of its table's kind, or an entry that a classic v3 slot cannot hold. |

The bytes of the context count toward the size limit of a tree file, as they are part of it. A compacted single file
is read to the same depth of nesting as a plain one, as written and once expanded, and a file nested deeper is refused
with `depth_limit_exceeded`.

The message of a diagnostic of a compacted file that a reader of the model gives after expansion, such as
`decode`, locates the fault in the expanded document (`$.distribution...`), not in the file as written, whose document
is its envelope's `document` member.

Notices have the severity `info` and do not fail the run:

| Code | Meaning |
| --- | --- |
| `tree.dropped_manifest_member` | A v4 manifest's `version` or `created` has no place in a single file and was dropped. The message names the member. |
| `tree.ignored_file` | A package metadata file, `package.json` in a package directory of `pkg/` or `deps/`, is not part of the model and was ignored. Other files that are neither JSON nor YAML are ignored with no notice. |

## JSON output

`--json` writes JSON Lines to stdout. Each event is one line, written when it occurs, and the last line is one
`completed` record whose `report` holds the report. `--json=document` writes the report alone. Paths in the report
and the events are as the command line gave them. A file of a tree is the output joined with its path in the tree,
with `/`.

```bash
$ morphir model convert --input morphir-ir.json --output tree --layout=document-tree --json
{"event":"stageStarted","stage":"read"}
{"event":"stageCompleted","stage":"read","passed":true}
{"event":"stageStarted","stage":"convert"}
{"event":"stageCompleted","stage":"convert","passed":true}
{"event":"stageStarted","stage":"write"}
{"event":"fileWritten","path":"tree/manifest.json"}
{"event":"fileWritten","path":"tree/pkg/car/rental/inventory/daily-rate.value.json"}
{"event":"fileWritten","path":"tree/pkg/car/rental/inventory/module.json"}
{"event":"stageCompleted","stage":"write","passed":true}
{"event":"completed","report":{"success":true,"exitCode":0,"input":"morphir-ir.json","output":"tree","inputLayout":"single-file","outputLayout":"document-tree","inputEncoding":"plain","outputEncoding":"plain","irVersion":"4","filesWritten":["tree/manifest.json","tree/pkg/car/rental/inventory/daily-rate.value.json","tree/pkg/car/rental/inventory/module.json"],"diagnostics":[],"notices":[]}}
$ morphir model convert --input tree --output copy.json --layout=single-file --json=document
{
  "success": true,
  "exitCode": 0,
  "input": "tree",
  "output": "copy.json",
  "inputLayout": "document-tree",
  "outputLayout": "single-file",
  "inputEncoding": "plain",
  "outputEncoding": "plain",
  "irVersion": "4",
  "filesWritten": [
    "copy.json"
  ],
  "diagnostics": [],
  "notices": []
}
```

### Events

| Event | Members | When |
| --- | --- | --- |
| `stageStarted` | `stage` | A stage starts: `read`, `convert` or `write`. |
| `stageCompleted` | `stage`, `passed` | A stage ends. A stage that did not pass is the last one. |
| `diagnostics` | `stage` (absent for the checks of the request before the first stage), `diagnostics` | A stage, or a check of the request, found errors. |
| `notices` | `stage`, `notices` | A stage has notices. |
| `fileWritten` | `path` | One file of the output is in place. There is one for each file of `filesWritten`. |
| `completed` | `report` | The last line of every run, also of a refused or a cancelled one. |

### The report

| Member | Description |
| --- | --- |
| `success` | `true` when the model was converted. |
| `exitCode` | The exit code of the run. |
| `input`, `output` | The paths, as the command line gave them. |
| `inputLayout`, `outputLayout` | `single-file` or `document-tree`. |
| `inputEncoding`, `outputEncoding` | `plain` or `compacted`. |
| `irVersion` | `"3"` or `"4"`, the IR version of the input and of the output. Absent when the input was not read. |
| `compactMode` | The compaction mode, `names`. Present only with `--compact`. |
| `compactPolicy` | The policy that wrote compacted files. Present only when compacted files are written, which this version does not do yet. |
| `filesWritten` | Every file written, in order. Empty for a refused run. |
| `diagnostics` | The errors, each with `code`, `severity`, `message` and, when it has one, `location`. |
| `notices` | The notices, in the same form, with the severity `info`. |

A member whose value is unknown is left out: a report of arguments that do not parse has only `success`, `exitCode`, `filesWritten`, `diagnostics` and `notices`. `filesWritten`, `diagnostics` and `notices` are always lists.

## See also

- [morphir model verify](model-verify.md): check a single file or a document tree against the IR schemas
- [Machine output](_index.md#machine-output): the `--json` convention of every command
