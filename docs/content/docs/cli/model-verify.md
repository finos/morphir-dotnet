---
title: "morphir model verify"
linkTitle: "model verify"
weight: 10
description: "Validate a Morphir model file against the official IR JSON schemas"
---

# morphir model verify

Validate a Morphir model file against the official JSON schemas of IR format versions 1, 2 and 3.

`model verify` is a command of the `model` group, which holds the commands that act on a Morphir model: the IR
together with its decorators, metadata and later additions. It replaces `morphir ir verify`. The `ir` group was
removed with no alias: `morphir ir verify` is now an unknown command.

## Synopsis

```text
morphir model verify <file-path> [--schema-version <version>] [--json[=stream|document]] [--quiet]
```

## Description

The `morphir model verify` command validates a Morphir IR JSON file against the schema of its format version. The
command detects the schema version from the file, or you can give the version to use.

Use it to:
- **Catch structural errors** before other tools read a model
- **Validate generated IR** from Morphir compilers
- **Check models in CI/CD**
- **Debug IR problems** with detailed error messages

## Arguments

### `<file-path>`

**Required.** The path of the Morphir IR JSON file to validate.

- Absolute and relative paths are supported
- The file must exist and be readable
- The file must contain valid JSON

**Examples:**
```bash
morphir model verify morphir-ir.json
morphir model verify ../output/morphir-ir.json
morphir model verify /absolute/path/to/morphir-ir.json
```

## Options

### `--schema-version <version>`

The schema version to validate against.

- **Valid values:** `1`, `2` or `3`
- **Default:** detected from the file
- **Use when:** testing compatibility with one version, or overriding the detection

**Examples:**
```bash
# Validate against the v3 schema, whatever the file says
morphir model verify --schema-version 3 morphir-ir.json

# Test whether a v2 IR file is valid against the v3 schema
morphir model verify --schema-version 3 morphir-ir-v2.json
```

### `--json[=stream|document]`

Write the result as JSON instead of text, by the convention of [Machine output](_index.md#machine-output).

- **Default:** text for a person
- **Use when:** reading the result in CI/CD, scripts or other tools
- **Output:** a bare `--json` writes one line, `{"event":"completed","report":{...}}`, whose `report` is the result.
  `--json=document` writes the result alone, as one JSON document.
- **Mode:** give it only after `=`. `morphir model verify --json morphir-ir.json` verifies `morphir-ir.json`.
- **Exit code:** the report states the exit code of the run in `exitCode`, next to `IsValid`. A model that is not
  valid has `exitCode` 1; a command line that does not parse has `exitCode` 2.
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
  "exitCode": 0
}
```

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
      "Message": "Required properties [\"formatVersion\"] are not present",
      "Expected": "required property",
      "Found": "undefined (missing)",
      "Line": null,
      "Column": null
    }
  ],
  "Timestamp": "2025-12-15T10:30:00Z",
  "exitCode": 1
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
  hides the log.

```bash
$ morphir model verify --quiet --json model.json
{"event":"completed","report":{"IsValid":false,"SchemaVersion":"3","DetectionMethod":"auto","FilePath":"/work/model.json","Errors":[{"Path":"","Message":"Required properties [\"formatVersion\"] are not present","Expected":"required property","Found":"undefined (missing)","Line":null,"Column":null}],"Timestamp":"2025-12-15T10:30:00Z","exitCode":1}}
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

## Exit codes

| Code | Meaning | Description |
|------|---------|-------------|
| `0` | Valid | The model is valid against the schema |
| `1` | Not valid | The model failed schema validation, the file is not there, or the file is not JSON |
| `2` | Usage error | The command line does not parse, for example an unknown option |

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

**A model that is not valid:**
```text
Validation Result: ✗ INVALID
File: /path/to/morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

Found 2 validation error(s):

  Path: $.distribution[3]
  Message: Required properties ["formatVersion"] are not present
  Expected: required property
  Found: undefined (missing)

  Path: $.distribution[3].modules[0].name
  Message: Value is "string" but should be "array"
  Expected: array
  Found: string
```

### JSON output

See the `--json` option above for the JSON output format.

## Schema version detection

The command reads the version from the `formatVersion` member of the file:

- **v1**: `"formatVersion": 1`
- **v2**: `"formatVersion": 2`
- **v3**: `"formatVersion": 3`, and the default when the member is missing or is not 1, 2 or 3

**Detection method in the output:**
- `auto`: the version was detected
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
morphir model verify --schema-version 3 morphir-ir.json
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
Path: $.distribution
Message: Required properties ["formatVersion"] are not present
Expected: required property
Found: undefined (missing)
```

**Solution:** add the missing `formatVersion` member to the file.

### Type mismatch

```text
Path: $.distribution[3].modules[0].name
Message: Value is "string" but should be "array"
Expected: array
Found: string
```

**Solution:** change the value to the type that the schema requires (here `name` is an array, not a string).

## Troubleshooting

### The schema version is not detected

If the detection fails or detects the wrong version:

1. **Check the `formatVersion` member** of the file
2. **Give `--schema-version`** to override the detection
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
