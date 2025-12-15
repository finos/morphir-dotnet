---
title: "Validating IR Files"
linkTitle: "Validating IR"
weight: 20
description: "Quick start guide for validating Morphir IR JSON files"
---

# Validating Morphir IR Files

This guide will help you get started with validating Morphir IR JSON files using the `morphir ir verify` command.

## Prerequisites

- Morphir .NET CLI installed (see [Installation](./installation/))
- A Morphir IR JSON file to validate

## Quick Start

### 1. Validate Your First IR File

The simplest way to validate an IR file is to run:

```bash
morphir ir verify morphir-ir.json
```

If the IR is valid, you'll see:

```
Validation Result: ✓ VALID
File: morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

No validation errors found.
```

### 2. Understanding the Output

The validation output includes:

- **Validation Result**: ✓ VALID or ✗ INVALID
- **File**: The path to the validated file
- **Schema Version**: Which schema version was used (and how it was detected)
- **Timestamp**: When the validation was performed
- **Errors**: Detailed error messages (if validation failed)

### 3. Handling Validation Errors

If validation fails, you'll see detailed error messages:

```
Validation Result: ✗ INVALID
File: morphir-ir.json
Schema Version: v3 (auto)
Timestamp: 2025-12-15 10:30:00 UTC

Found 1 validation error(s):

  Path: $.distribution
  Message: Required properties ["formatVersion"] are not present
  Expected: required property
  Found: undefined (missing)
```

Each error includes:
- **Path**: JSON path to the error location
- **Message**: Description of what's wrong
- **Expected**: What the schema expects
- **Found**: What was actually found

## Common Scenarios

### Validating Multiple Files

To validate all IR files in a directory:

```bash
# Validate all JSON files
for file in *.json; do
  echo "Validating $file..."
  morphir ir verify "$file" || exit 1
done
```

Or in parallel:

```bash
find . -name "morphir-ir.json" -exec morphir ir verify {} \;
```

### CI/CD Integration

Add validation to your CI/CD pipeline:

**GitHub Actions:**

```yaml
steps:
  - name: Install Morphir CLI
    run: dotnet tool install -g Morphir.CLI

  - name: Validate IR
    run: morphir ir verify output/morphir-ir.json
```

**GitLab CI:**

```yaml
validate-ir:
  stage: test
  script:
    - dotnet tool install -g Morphir.CLI
    - morphir ir verify output/morphir-ir.json
```

### Using Different Output Formats

**JSON Output (for parsing in scripts):**

```bash
morphir ir verify --json morphir-ir.json > validation-result.json

# Check if valid
cat validation-result.json | jq '.IsValid'
```

**Quiet Mode (for CI/CD):**

```bash
# Only shows output if validation fails
morphir ir verify --quiet morphir-ir.json

# Check exit code
if morphir ir verify --quiet morphir-ir.json; then
  echo "IR is valid"
fi
```

### Specifying Schema Version

If you need to validate against a specific schema version:

```bash
# Validate against v3 schema
morphir ir verify --schema-version 3 morphir-ir.json

# Test if v2 IR is compatible with v3
morphir ir verify --schema-version 3 morphir-ir-v2.json
```

## Understanding Schema Versions

Morphir IR has three schema versions:

| Version | Format Version Field | Detection |
|---------|---------------------|-----------|
| v1 | None | Legacy format, no `formatVersion` field |
| v2 | `"formatVersion": 2` | Explicit field in JSON |
| v3 | `"formatVersion": 3` | Explicit field in JSON |

The CLI automatically detects the version by examining the JSON structure.

## Next Steps

### Learn More

- **[CLI Reference](../../cli/)** - Complete command documentation
- **[morphir ir verify](../../cli/ir-verify/)** - Detailed command reference
- **[Troubleshooting](../../cli/troubleshooting/)** - Solutions to common issues

### Advanced Topics

- **[Schema Specifications](../../spec/schemas/)** - Understand the IR schema structure
- **[Error Messages](../../cli/ir-verify/#common-error-messages)** - Detailed error reference
- **Batch Validation** _(Phase 2 - coming soon)_
- **Custom Validation Rules** _(Phase 3 - planned)_

## Common Pitfalls

### 1. JSON Syntax Errors

**Problem:** Malformed JSON will fail validation

**Solution:** Validate JSON syntax first:
```bash
cat morphir-ir.json | jq . > /dev/null && echo "Valid JSON"
```

### 2. Wrong formatVersion Value

**Problem:** File has `"formatVersion": "3"` (string) instead of `"formatVersion": 3` (number)

**Solution:** Ensure `formatVersion` is a number:
```json
// Incorrect
{
  "formatVersion": "3",
  ...
}

// Correct
{
  "formatVersion": 3,
  ...
}
```

### 3. Case Sensitivity in Tags

**Problem:** Tags must use correct capitalization

**Solution:** Use `"Public"` not `"public"`, `"Private"` not `"private"`

```json
// Incorrect
"accessControl": "public"

// Correct
"accessControl": "Public"
```

## Example Workflow

Here's a complete workflow for validating IR in your project:

```bash
#!/bin/bash
set -e

echo "=== Morphir IR Validation Workflow ==="

# 1. Generate IR (example using hypothetical compiler)
echo "Step 1: Generating IR..."
morphir-elm make --output output/morphir-ir.json

# 2. Validate IR
echo "Step 2: Validating IR..."
if morphir ir verify output/morphir-ir.json; then
  echo "✓ IR is valid"
else
  echo "✗ IR validation failed"
  exit 1
fi

# 3. Generate detailed report
echo "Step 3: Generating validation report..."
morphir ir verify --json output/morphir-ir.json > validation-report.json

# 4. Check for any warnings (future feature)
echo "Step 4: Checking for warnings..."
# (Future: morphir ir lint output/morphir-ir.json)

echo "=== Workflow Complete ==="
```

## Getting Help

If you run into issues:

1. Check the **[Troubleshooting Guide](../../cli/troubleshooting/)**
2. Review **[Common Error Messages](../../cli/ir-verify/#common-error-messages)**
3. Ask in **[GitHub Discussions](https://github.com/finos/morphir-dotnet/discussions)**
4. Report bugs in **[GitHub Issues](https://github.com/finos/morphir-dotnet/issues)**
