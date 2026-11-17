---
title: "morphir model verify"
linkTitle: "model verify"
weight: 10
description: "Validate a Morphir model, a single file or a document tree, against the official IR JSON schemas"
---

# morphir model verify

Validate a Morphir model against the official JSON schemas of IR format versions 1, 2, 3 and 4. The model is a
single IR JSON file, or a document tree: a directory with a manifest and a file for each module, type and value.

`model verify` is a command of the `model` group, which holds the commands that act on a Morphir model: the IR
together with its decorators, metadata and later additions. It replaces `morphir ir verify`. The `ir` group was
removed with no alias: `morphir ir verify` is now an unknown command.

## Synopsis

```text
morphir model verify <model-path> [--schema-version=<version>] [--json[=stream|document]] [--quiet]
```

## Description

The `morphir model verify` command validates a Morphir model against the schema of its format version. For a single
file, the command detects the schema version from the file, or you can give the version to use. For a document tree,
the version comes from the tree's manifest; see [Document trees](#document-trees).

An IR v4 file is also read with the v4 reader of Morphir after the schema check, as the files of a tree are. The
parent's v4 schema checks the types and values of a module only when the module is written as
`{ "access": ..., "value": {...} }`; the reader checks them in every spelling, such as the flattened
`{ "access": "Public", "types": {...}, "values": {...} }` that Morphir writes. An error of the reader has the reader's
`code`, such as `unknown_node`, and its JSON location in `Path`.

Use it to:
- **Catch structural errors** before other tools read a model
- **Validate generated IR** from Morphir compilers and from `morphir model convert`
- **Check models in CI/CD**
- **Debug IR problems** with detailed error messages

## Arguments

### `<model-path>`

**Required.** The path of the model to validate: a Morphir IR JSON file, or the directory of a document tree.

- Absolute and relative paths are supported
- A file must exist, be readable and contain valid JSON
- A directory is read as a document tree

**Examples:**
```bash
morphir model verify morphir-ir.json
morphir model verify ../output/morphir-ir.json
morphir model verify /absolute/path/to/morphir-ir.json
morphir model verify out/tree
```

## Options

### `--schema-version=<version>`

The schema version to validate against.

- **Valid values:** `1`, `2`, `3` or `4`. Another number is a usage error, with exit code 2.
- **Default:** detected from the file, or read from the manifest of a tree
- **Use when:** testing compatibility with one version, or overriding the detection of a single file
- **A tree:** the version must be the one that the manifest states. A version that contradicts it is refused with
  the diagnostic `verify.schema_version_conflict` and exit code 2, and nothing is checked. On a directory, a version
  other than 3 or 4 is refused the same way.

**Examples:**
```bash
# Validate against the v3 schema, whatever the file says
morphir model verify --schema-version=3 morphir-ir.json

# Test whether a v2 IR file is valid against the v3 schema
morphir model verify --schema-version=3 morphir-ir-v2.json

# Validate an IR v4 file against the v4 schema
morphir model verify --schema-version=4 morphir-ir-v4.json
```

### `--json[=stream|document]`

Write the result as JSON instead of text, by the convention of [Machine output](_index.md#machine-output).

- **Default:** text for a person
- **Use when:** reading the result in CI/CD, scripts or other tools
- **Output:** a bare `--json` writes one line, `{"event":"completed","report":{...}}`, whose `report` is the result.
  `--json=document` writes the result alone, as one JSON document.
- **Mode:** give it only after `=`. `morphir model verify --json morphir-ir.json` verifies `morphir-ir.json`.
- **Exit code:** the report states the exit code of the run in `exitCode`, next to `IsValid`. A model that is not
  valid has `exitCode` 1; a command line that does not parse, or a `--schema-version` that contradicts the manifest
  of a tree, has `exitCode` 2.
- **Usage errors:** in a JSON mode an invalid command line is a result with `IsValid` `false`, one error for each
  parser message, and exit code 2.

**JSON output format** (`--json=document`; with `--json` this is the `report` of the `completed` line):
```json
{
  "IsValid": true,
  "SchemaVersion": "3",
  "DetectionMethod": "auto",
  "FilePath": "/path/to/morphir-ir.json",
  "Errors": [],
  "Timestamp": "2025-12-15T10:30:00Z",
  "exitCode": 0,
  "layout": "single-file",
  "filesChecked": 1,
  "compactedFiles": []
}
```

| Member | Meaning |
| --- | --- |
| `IsValid` | `true` when the model is valid |
| `SchemaVersion` | The IR version checked: `"1"` to `"4"`, or `"unknown"` |
| `DetectionMethod` | `auto` (detected from a file), `manifest` (read from the manifest of a tree), `manual` (given with `--schema-version`) or `none` (a command line that does not parse) |
| `FilePath` | The full path of the model |
| `Errors` | Each error; see below |
| `exitCode` | The exit code of the run |
| `layout` | `single-file` or `document-tree`; absent when the command line does not parse |
| `filesChecked` | The number of files checked: 1 for a single file, each JSON or YAML file of a tree (a YAML file counts, and the store refuses it with `tree.unsupported_profile`), and 0 when none was read |
| `compactedFiles` | The files that were compacted; empty for a plain model |

Each error has `Path`, the JSON location of the error in its file as a JSON Pointer (`""` is the whole file, and
`$` an input that was not read), `Message`, and `Expected` and `Found` when the schema states them. An error in a
document tree also has `file`, the path of its file from the root of the tree, and an error of a rule of the tree, or
of the v4 reader, has its stable `code`, such as `missing_member` or `unknown_node`.

The result of a model with no `formatVersion`, with the file in the current directory:

```bash
$ morphir model verify model.json --json=document
{
  "IsValid": false,
  "SchemaVersion": "3",
  "DetectionMethod": "auto",
  "FilePath": "/work/model.json",
  "Errors": [
    {
      "Path": "",
      "Message": "Required properties [\u0022formatVersion\u0022] are not present",
      "Expected": "required property",
      "Found": "undefined (missing)",
      "Line": null,
      "Column": null
    }
  ],
  "Timestamp": "2025-12-15T10:30:00Z",
  "exitCode": 1,
  "layout": "single-file",
  "filesChecked": 1,
  "compactedFiles": []
}
```

**Examples:**
```bash
# Get the result as one JSON document
morphir model verify --json=document morphir-ir.json

# Use it in a script
morphir model verify --json=document morphir-ir.json | jq '.IsValid'
```

### `--quiet`

Write no text and no log: the exit code is the result.

- **Default:** the full text result
- **Use when:** a CI/CD step needs only the exit code
- **Exit code:** 0 for a valid model, 1 for one that is not valid
- **With `--json`:** the JSON result is still written. You asked for it, so `--quiet` does not hide it; it still
  hides the log, so standard error stays empty.

```bash
$ morphir model verify --quiet --json model.json
{"event":"completed","report":{"IsValid":false,"SchemaVersion":"3","DetectionMethod":"auto","FilePath":"/work/model.json","Errors":[{"Path":"","Message":"Required properties [\u0022formatVersion\u0022] are not present","Expected":"required property","Found":"undefined (missing)","Line":null,"Column":null}],"Timestamp":"2025-12-15T10:30:00Z","exitCode":1,"layout":"single-file","filesChecked":1,"compactedFiles":[]}}
```

**Examples:**
```bash
# Quiet mode: only the exit code
morphir model verify --quiet morphir-ir.json

# Use it in CI/CD
if morphir model verify --quiet morphir-ir.json; then
  echo "IR is valid"
else
  echo "IR validation failed"
  exit 1
fi
```

## Document trees

When `<model-path>` is a directory, `model verify` reads it as a document tree, as `morphir model convert` writes
it: `manifest.json`, a `module.json` for each module, and a `<name>.type.json` or `<name>.value.json` for each type
and value, below `pkg/<package>/` for the package and `deps/<package>/@/` for each dependency.

The version comes from the manifest: a v4 tree says `"formatVersion": 4`, and each file of a v3 tree says
`"formatVersion": "3.1.0"`. The command checks the whole tree and lists every error it finds:

- **IR v4:** each file against the parent's schema of its kind in
  [`morphir-ir-v4-document-tree-files.json`](https://morphir.finos.org/schemas/morphir-ir-v4-document-tree-files.json):
  `DistributionManifestFile` for `manifest.json`, `ModuleManifestFile` for `module.json`, `TypeDefinitionFile` for a
  type file and `ValueDefinitionFile` for a value file. Then every rule of a tree and every document, as
  `morphir model convert` reads the tree.
- **IR v3:** every rule of a tree and every document. The parent publishes no schema of the v3 tree files.

Each error names its file (`file`, from the root of the tree) and the JSON location in that file (`Path`). An error of
a rule of the tree has the diagnostic code of the parent's tree specification, such as `missing_member` for a value
that a module file lists and that has no file, at the path of the missing file. The report states the layout
`document-tree` and the number of files checked.

The result of a v4 tree whose module file lists the value `default-currency`, which has no file, with the tree in
the directory `model/`:

```bash
$ morphir model verify model/ --json=document
{
  "IsValid": false,
  "SchemaVersion": "4",
  "DetectionMethod": "manifest",
  "FilePath": "/work/model",
  "Errors": [
    {
      "Path": "",
      "Message": "The module file pkg/my-org/finance/pricing/models/module.json lists the value 'default-currency', and the tree has no file for it.",
      "Expected": null,
      "Found": null,
      "Line": null,
      "Column": null,
      "file": "pkg/my-org/finance/pricing/models/default-currency.value.json",
      "code": "missing_member"
    }
  ],
  "Timestamp": "2025-12-15T10:30:00Z",
  "exitCode": 1,
  "layout": "document-tree",
  "filesChecked": 8,
  "compactedFiles": []
}
```

The text of an error in one tree file names the file before the JSON location:

```text
Validation Result: ✗ INVALID
File: /work/model
Schema Version: v4 (manifest)
Layout: document-tree
Files Checked: 9
Timestamp: 2025-12-15 10:30:00 UTC

Found 1 validation error(s):

  File: pkg/my-org/finance/pricing/models/invoice.type.json
  Code: unknown_node
  Path: /def/Public/TypeAliasDefinition/typeExp
  Message: The object is not a node this position allows.
```

A `--schema-version` that the manifest contradicts, such as `--schema-version=3` on a v4 tree, is refused with exit
code 2 and one error, `verify.schema_version_conflict`, that names both versions:

```text
  File: manifest.json
  Code: verify.schema_version_conflict
  Path: /formatVersion
  Message: --schema-version=3 contradicts the manifest of the tree, which states the IR version 4.
  Expected: 4
  Found: 3
```

## Exit codes

| Code | Meaning | Description |
|------|---------|-------------|
| `0` | Valid | The model is valid against the schema |
| `1` | Not valid | The model failed validation, the file is not there, the file is not JSON, or a directory is not a readable tree, for example it has no `manifest.json` |
| `2` | Usage error | The command line does not parse, for example an unknown option or a `--schema-version` other than 1 to 4, or `--schema-version` contradicts the manifest of a tree |

## Output

### Text output (the default)

**A valid model:**
```text
Validation Result: ✓ VALID
File: /path/to/morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

No validation errors found.
```

**A model that is not valid** (the `formatVersion` of a v3 Library is the string `"3"`, and the Library has no
package name and no modules):
```text
Validation Result: ✗ INVALID
File: /path/to/morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

Found 4 validation error(s):

  Path: /formatVersion
  Message: Value is "string" but should be "integer"
  Expected: integer
  Found: string

  Path: /formatVersion
  Message: Expected "3"
  Expected: Expected "3"
  Found: see message for details

  Path: /distribution/1
  Message: Value should have at least 1 items
  Found: see message for details

  Path: /distribution/3
  Message: Required properties ["modules"] are not present
  Expected: required property
  Found: undefined (missing)
```

### JSON output

See the `--json` option above for the JSON output format.

## Schema version detection

The command reads the version of a single file from its `formatVersion` member:

- **v1**: `"formatVersion": 1`
- **v2**: `"formatVersion": 2`
- **v3**: `"formatVersion": 3` or `"3.1.0"`, and the default when the member is missing or states no version that the
  command knows
- **v4**: `"formatVersion": 4`, or a v4 release such as `"4.0.0"`

A document tree takes its version from its manifest: a `formatVersion` of major 3 is a v3 tree, and any other a v4
tree.

**Detection method in the output:**
- `auto`: the version was detected from a single file
- `manifest`: the version was read from the manifest of a tree
- `manual`: the version was given with `--schema-version`

## Examples

### Basic validation

Validate one model, with the detected version:

```bash
morphir model verify morphir-ir.json
```

### A given version

Validate against one schema version:

```bash
morphir model verify --schema-version=3 morphir-ir.json
```

### A document tree

Validate a tree that `morphir model convert` wrote:

```bash
morphir model convert --input morphir-ir.json --output out/tree --layout=document-tree
morphir model verify out/tree
```

### CI/CD integration

Validate a model in a GitHub Actions workflow:

```yaml
- name: Validate Morphir IR
  run: |
    dotnet morphir model verify --json=document morphir-ir.json > validation-result.json
    if [ $? -ne 0 ]; then
      cat validation-result.json
      exit 1
    fi
```

### A script with error handling

```bash
#!/bin/bash
set -e

echo "Validating Morphir IR files..."

for file in output/*.json; do
  echo "Validating $file..."
  if morphir model verify --quiet "$file"; then
    echo "✓ $file is valid"
  else
    echo "✗ $file is invalid"
    morphir model verify "$file"  # Show detailed errors
    exit 1
  fi
done

echo "All IR files are valid!"
```

### Reading the JSON output

Read the JSON output with `jq`:

```bash
# Check if valid
morphir model verify --json=document morphir-ir.json | jq '.IsValid'

# Get the exit code that the report states
morphir model verify --json=document morphir-ir.json | jq '.exitCode'

# Get error count
morphir model verify --json=document morphir-ir.json | jq '.Errors | length'

# Extract error messages
morphir model verify --json=document morphir-ir.json | jq '.Errors[].Message'

# List the files of a tree that have errors
morphir model verify --json=document out/tree | jq '[.Errors[].file] | unique'

# Get schema version used
morphir model verify --json=document morphir-ir.json | jq '.SchemaVersion'
```

## Common error messages

### File not found

```text
Path: $
Message: File does not exist: path/to/file.json
Expected: Existing file
Found: File not found
```

**Solution:** check the file path and make sure that the file exists.

### Malformed JSON

```text
Validation Result: ✗ INVALID

Found 1 validation error(s):

  Path: $
  Message: Malformed JSON: 'i' is an invalid start of a value. LineNumber: 6 | BytePositionInLine: 4.
  Expected: Valid JSON
  Found: Invalid JSON syntax
```

**Solution:** fix the JSON syntax error. The message gives the line and the byte position.

### Missing required member

```text
Path:
Message: Required properties ["formatVersion"] are not present
Expected: required property
Found: undefined (missing)
```

**Solution:** add the missing `formatVersion` member to the file. The empty path is the whole file.

### Type mismatch

```text
Path: /formatVersion
Message: Value is "string" but should be "integer"
Expected: integer
Found: string
```

**Solution:** change the value to the type that the schema requires (here `formatVersion` is a number, not a string).

### A tree has no manifest

```text
Code: tree.missing_manifest
Path:
Message: The tree has no manifest.json at its root.
```

**Solution:** give the root directory of the tree, the directory that holds `manifest.json`. A directory that cannot
be read at all gives the code `verify.unreadable_directory` instead.

### A file of a tree is missing

```text
File: pkg/my-org/finance/pricing/models/default-currency.value.json
Code: missing_member
Path:
Message: The module file pkg/my-org/finance/pricing/models/module.json lists the value 'default-currency', and the tree has no file for it.
```

**Solution:** write the missing file, or remove the name from the module file. A tree that `morphir model convert`
writes has a file for each name.

## Troubleshooting

### The schema version is not detected

If the detection fails or detects the wrong version:

1. **Check the `formatVersion` member** of the file
2. **Give `--schema-version`** to override the detection of a single file
3. **Check that the JSON structure** matches the schema version

### Large files

For large IR files (over 1 MB):

- Validation can take several seconds
- Use `--quiet` in CI/CD to write less output

### A model that you think is valid fails

If validation fails but you believe the IR is correct:

1. **Check the schema version**: make sure you validate against the right version
2. **Read the error messages**: they give the expected and the found values
3. **Read the schema documentation**: see [Schema Specifications](/docs/spec/schemas/)
4. **Report an issue** if you believe the schema or the validator has a bug

## Related commands

- [`morphir validate`](validate.md) runs the analyzers on a model, for example the type checker.

## See also

- [Schema Specifications](/docs/spec/schemas/) - The IR schema documentation
- [Troubleshooting Guide](../troubleshooting/) - Solutions to common problems
