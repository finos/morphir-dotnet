---
title: "morphir ir verify"
linkTitle: "ir verify"
weight: 10
description: "Validate Morphir IR JSON files against official schemas"
---

# morphir ir verify

Validate Morphir IR JSON files against the official JSON schemas for format versions 1, 2, and 3.

## Synopsis

```bash
morphir ir verify <file-path> [options]
```

## Description

The `morphir ir verify` command validates a Morphir IR JSON file against the appropriate schema specification. The command automatically detects the schema version from the file content, or you can explicitly specify the version to use.

This command is useful for:
- **Catching structural errors** before IR files are used by other tools
- **Validating generated IR** from Morphir compilers
- **CI/CD integration** to ensure IR quality
- **Debugging IR issues** with detailed error messages

## Arguments

### `<file-path>`

**Required.** Path to the Morphir IR JSON file to validate.

- Supports absolute and relative paths
- File must exist and be readable
- File must contain valid JSON

**Examples:**
```bash
morphir ir verify morphir-ir.json
morphir ir verify ../output/morphir-ir.json
morphir ir verify /absolute/path/to/morphir-ir.json
```

## Options

### `--schema-version <version>`

Explicitly specify the schema version to validate against.

- **Valid values:** `1`, `2`, or `3`
- **Default:** Auto-detected from file content
- **Use when:** Testing version-specific compatibility or overriding auto-detection

**Examples:**
```bash
# Validate against v3 schema regardless of file content
morphir ir verify --schema-version 3 morphir-ir.json

# Test if a v2 IR file is compatible with v3 schema
morphir ir verify --schema-version 3 morphir-ir-v2.json
```

### `--json`

Output validation results in JSON format instead of human-readable text.

- **Default:** Human-readable output
- **Use when:** Parsing results in CI/CD, scripts, or other tools
- **Output:** Structured JSON with validation details

**JSON Output Format:**
```json
{
  "IsValid": true,
  "SchemaVersion": "3",
  "DetectionMethod": "auto",
  "FilePath": "/path/to/morphir-ir.json",
  "Errors": [],
  "Timestamp": "2025-12-15T10:30:00Z"
}
```

For invalid IR:
```json
{
  "IsValid": false,
  "SchemaVersion": "3",
  "DetectionMethod": "auto",
  "FilePath": "/path/to/morphir-ir.json",
  "Errors": [
    {
      "Path": "$.distribution[3]",
      "Message": "Required properties [\"formatVersion\"] are not present",
      "Expected": "required property",
      "Found": "undefined (missing)",
      "Line": null,
      "Column": null
    }
  ],
  "Timestamp": "2025-12-15T10:30:00Z"
}
```

**Example:**
```bash
# Get JSON output for parsing
morphir ir verify --json morphir-ir.json

# Use in scripts
RESULT=$(morphir ir verify --json morphir-ir.json)
echo $RESULT | jq '.IsValid'
```

### `--quiet`

Suppress all output except for errors.

- **Default:** Show detailed output
- **Use when:** Running in CI/CD pipelines where you only care about failures
- **Exit code:** Still returns 0 (success) or 1 (failure)

**Examples:**
```bash
# Quiet mode - only shows output if validation fails
morphir ir verify --quiet morphir-ir.json

# Use in CI/CD
if morphir ir verify --quiet morphir-ir.json; then
  echo "IR is valid"
else
  echo "IR validation failed"
  exit 1
fi
```

## Exit Codes

| Code | Meaning | Description |
|------|---------|-------------|
| `0` | Success | IR file is valid according to the schema |
| `1` | Validation failure | IR file failed schema validation (see error output) |
| `2` | Operational error | File not found, malformed JSON, or other operational issue |

## Output

### Human-Readable Output (Default)

**Valid IR:**
```
Validation Result: ✓ VALID
File: /path/to/morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

No validation errors found.
```

**Invalid IR:**
```
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

### JSON Output

See the `--json` option above for the JSON output format specification.

## Schema Version Detection

The command automatically detects the schema version by analyzing the IR file structure:

- **v1**: Detected by presence of v1-specific structure
- **v2**: Detected by `"formatVersion": 2` field
- **v3**: Detected by `"formatVersion": 3` field

**Detection Method in Output:**
- `auto`: Version was automatically detected
- `manual`: Version was explicitly specified via `--schema-version`

## Examples

### Basic Validation

Validate a single IR file with auto-detection:

```bash
morphir ir verify morphir-ir.json
```

### Explicit Version

Validate against a specific schema version:

```bash
morphir ir verify --schema-version 3 morphir-ir.json
```

### CI/CD Integration

Validate IR in a GitHub Actions workflow:

```yaml
- name: Validate Morphir IR
  run: |
    dotnet morphir ir verify --json morphir-ir.json > validation-result.json
    if [ $? -ne 0 ]; then
      cat validation-result.json
      exit 1
    fi
```

### Script with Error Handling

```bash
#!/bin/bash
set -e

echo "Validating Morphir IR files..."

for file in output/*.json; do
  echo "Validating $file..."
  if morphir ir verify --quiet "$file"; then
    echo "✓ $file is valid"
  else
    echo "✗ $file is invalid"
    morphir ir verify "$file"  # Show detailed errors
    exit 1
  fi
done

echo "All IR files are valid!"
```

### JSON Output Parsing

Parse JSON output with `jq`:

```bash
# Check if valid
morphir ir verify --json morphir-ir.json | jq '.IsValid'

# Get error count
morphir ir verify --json morphir-ir.json | jq '.Errors | length'

# Extract error messages
morphir ir verify --json morphir-ir.json | jq '.Errors[].Message'

# Get schema version used
morphir ir verify --json morphir-ir.json | jq '.SchemaVersion'
```

## Common Error Messages

### File Not Found

```
Error: File does not exist: path/to/file.json
```

**Solution:** Check the file path and ensure the file exists.

### Malformed JSON

```
Validation Result: ✗ INVALID

Found 1 validation error(s):

  Path: $
  Message: Malformed JSON: 'i' is an invalid start of a value. LineNumber: 6 | BytePositionInLine: 4.
  Expected: Valid JSON
  Found: Invalid JSON syntax
```

**Solution:** Fix the JSON syntax error. The error message includes the line and byte position.

### Missing Required Field

```
Path: $.distribution
Message: Required properties ["formatVersion"] are not present
Expected: required property
Found: undefined (missing)
```

**Solution:** Add the missing `formatVersion` field to your IR file.

### Type Mismatch

```
Path: $.distribution[3].modules[0].name
Message: Value is "string" but should be "array"
Expected: array
Found: string
```

**Solution:** Change the field type to match the schema requirement (in this case, `name` should be an array, not a string).

## Troubleshooting

### Schema Version Not Detected

If auto-detection fails or detects the wrong version:

1. **Check the `formatVersion` field** in your JSON (for v2/v3)
2. **Use `--schema-version` explicitly** to override auto-detection
3. **Verify JSON structure** matches the expected schema version

### Performance Issues

For large IR files (>1MB):

- Validation may take several seconds
- Consider using `--quiet` mode in CI/CD to reduce output overhead
- _(Phase 2 will include performance optimizations for batch processing)_

### False Positives

If validation fails but you believe the IR is correct:

1. **Check schema version** - ensure you're validating against the correct version
2. **Review error messages** - they include expected vs. found values
3. **Consult schema documentation** - see [Schema Specifications](/docs/spec/schemas/)
4. **Report an issue** - if you believe the schema or validator has a bug

## Related Commands

- **morphir ir detect-version** _(Phase 2)_ - Detect schema version without validation
- **morphir ir migrate** _(Phase 3)_ - Migrate IR between schema versions

## See Also

- [Schema Specifications](/docs/spec/schemas/) - Complete IR schema documentation
- [Troubleshooting Guide](../troubleshooting/) - Solutions to common issues
- [CI/CD Integration Guide](../../guides/ci-cd-integration/) _(coming soon)_
