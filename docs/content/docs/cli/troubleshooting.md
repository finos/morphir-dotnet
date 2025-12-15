---
title: "Troubleshooting"
linkTitle: "Troubleshooting"
weight: 100
description: "Solutions to common issues with Morphir .NET CLI"
---

# Troubleshooting Guide

This guide covers common issues you may encounter when using the Morphir .NET CLI and how to resolve them.

## Installation Issues

### Tool Not Found After Installation

**Problem:** After running `dotnet tool install`, the `morphir` command is not found.

**Solution:**

1. **Check if tools are in PATH:**
   ```bash
   echo $PATH | grep .dotnet/tools
   ```

2. **Add to PATH if missing** (Linux/macOS):
   ```bash
   export PATH="$PATH:$HOME/.dotnet/tools"
   ```

3. **Add to PATH if missing** (Windows):
   ```powershell
   $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
   ```

4. **Restart your terminal** after modifying PATH

### Installation Fails with "Unable to find package"

**Problem:** `dotnet tool install Morphir.CLI` fails to find the package.

**Solution:**

1. **Check NuGet sources:**
   ```bash
   dotnet nuget list source
   ```

2. **Add nuget.org if missing:**
   ```bash
   dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
   ```

3. **Verify package name** - ensure you're using the correct package name

## Validation Issues

### "File does not exist" Error

**Problem:**
```
Error: File does not exist: morphir-ir.json
```

**Solutions:**

1. **Check file path:**
   ```bash
   ls -l morphir-ir.json
   ```

2. **Use absolute path:**
   ```bash
   morphir ir verify /full/path/to/morphir-ir.json
   ```

3. **Check current directory:**
   ```bash
   pwd
   cd /path/to/ir/files
   morphir ir verify morphir-ir.json
   ```

### Malformed JSON Errors

**Problem:**
```
Message: Malformed JSON: 'i' is an invalid start of a value. LineNumber: 6
```

**Solutions:**

1. **Validate JSON syntax** with a JSON validator:
   ```bash
   cat morphir-ir.json | jq .
   ```

2. **Check for common issues:**
   - Missing commas between array/object elements
   - Trailing commas (not allowed in JSON)
   - Unquoted keys or values
   - Invalid escape sequences

3. **Use a JSON formatter:**
   ```bash
   cat morphir-ir.json | jq . > morphir-ir-formatted.json
   ```

### Schema Version Detection Issues

**Problem:** Auto-detection selects the wrong schema version.

**Solutions:**

1. **Explicitly specify version:**
   ```bash
   morphir ir verify --schema-version 3 morphir-ir.json
   ```

2. **Check formatVersion field** (for v2/v3):
   ```bash
   cat morphir-ir.json | jq '.formatVersion'
   ```

3. **Verify IR structure:**
   - **v1**: No `formatVersion` field
   - **v2**: `"formatVersion": 2`
   - **v3**: `"formatVersion": 3`

### Validation Fails but IR Appears Correct

**Problem:** Validation reports errors, but you believe the IR is valid.

**Investigation Steps:**

1. **Review error messages carefully** - they include Expected vs. Found values
2. **Check schema documentation** - [Schema Specifications](/docs/spec/schemas/)
3. **Validate against different versions:**
   ```bash
   morphir ir verify --schema-version 1 morphir-ir.json
   morphir ir verify --schema-version 2 morphir-ir.json
   morphir ir verify --schema-version 3 morphir-ir.json
   ```

4. **Compare with known-good IR:**
   ```bash
   # Validate a reference file
   morphir ir verify reference-morphir-ir.json
   ```

5. **Check for common issues:**
   - Incorrect tag capitalization (e.g., `"Public"` vs `"public"`)
   - Missing required fields
   - Incorrect array structure
   - Type mismatches

## Performance Issues

### Validation Takes Too Long

**Problem:** Validation of large IR files (>1MB) takes several seconds.

**Current Limitations:**
- This is expected behavior for large files in Phase 1
- Schema validation is inherently slower for complex, deeply-nested JSON

**Workarounds:**

1. **Use `--quiet` mode** to reduce output overhead:
   ```bash
   morphir ir verify --quiet morphir-ir.json
   ```

2. **Validate in parallel** (for multiple files):
   ```bash
   find . -name "*.json" | xargs -P 4 -I {} morphir ir verify --quiet {}
   ```

3. **Phase 2 improvements** (coming soon):
   - Performance optimizations for batch processing
   - Caching and incremental validation

### High Memory Usage

**Problem:** Validation consumes excessive memory for very large IR files.

**Solutions:**

1. **Check file size:**
   ```bash
   ls -lh morphir-ir.json
   ```

2. **For files >10MB:**
   - Consider splitting into smaller modules
   - Report the issue with file size details

3. **Increase available memory** (if using Docker):
   ```bash
   docker run --memory 2g morphir-cli ir verify morphir-ir.json
   ```

## CI/CD Integration Issues

### CI Pipeline Doesn't Fail on Invalid IR

**Problem:** Pipeline continues even when validation fails.

**Solution:** Check exit codes explicitly:

```yaml
# GitHub Actions
- name: Validate IR
  run: |
    morphir ir verify morphir-ir.json
    if [ $? -ne 0 ]; then
      echo "Validation failed"
      exit 1
    fi
```

Or use `set -e` in bash scripts:

```bash
#!/bin/bash
set -e  # Exit on any error

morphir ir verify morphir-ir.json
echo "Validation succeeded!"
```

### JSON Output Not Parsed Correctly

**Problem:** CI tools can't parse JSON output.

**Solutions:**

1. **Verify JSON format:**
   ```bash
   morphir ir verify --json morphir-ir.json | jq .
   ```

2. **Save to file:**
   ```bash
   morphir ir verify --json morphir-ir.json > validation-result.json
   ```

3. **Check for extra output:**
   - Use `--quiet` with `--json` to suppress non-JSON output
   - Some logging may appear on stderr

### Permission Denied in Docker

**Problem:**
```
Permission denied: /app/morphir-ir.json
```

**Solution:** Fix file permissions or use volume mounts:

```dockerfile
# In Dockerfile
RUN chmod +r /app/*.json

# Or when running
docker run -v $(pwd):/app:ro morphir-cli ir verify /app/morphir-ir.json
```

## Error Message Reference

### Common Validation Errors

#### Missing Required Field

```
Path: $.distribution
Message: Required properties ["formatVersion"] are not present
Expected: required property
Found: undefined (missing)
```

**Fix:** Add the missing field to your JSON:
```json
{
  "formatVersion": 3,
  "distribution": [ ... ]
}
```

#### Type Mismatch

```
Path: $.modules[0].name
Message: Value is "string" but should be "array"
Expected: array
Found: string
```

**Fix:** Change the field type:
```json
// Incorrect
"name": "MyModule"

// Correct
"name": ["my", "module"]
```

#### Invalid Tag Value

```
Path: $.modules[0].accessControl
Message: Value must be one of: ["Public", "Private"]
Expected: "Public" or "Private"
Found: "public"
```

**Fix:** Use correct capitalization:
```json
// Incorrect
"accessControl": "public"

// Correct
"accessControl": "Public"
```

#### Array Structure Error

```
Path: $.distribution[2]
Message: Expected array to have exactly 4 elements
Expected: array of length 4
Found: array of length 3
```

**Fix:** Ensure array has the correct number of elements per schema.

## Reporting Issues

If you encounter an issue not covered here:

1. **Check existing issues:** [GitHub Issues](https://github.com/finos/morphir-dotnet/issues)

2. **Gather information:**
   - Morphir .NET CLI version: `morphir --version`
   - .NET SDK version: `dotnet --version`
   - Operating system and version
   - Complete error message
   - Minimal reproduction steps

3. **Create a new issue** with:
   - Clear title describing the problem
   - Steps to reproduce
   - Expected vs. actual behavior
   - Environment information
   - Sample IR file (if possible) or minimal example

## Getting Help

- **Documentation:** [Morphir .NET Docs](/)
- **GitHub Issues:** [finos/morphir-dotnet/issues](https://github.com/finos/morphir-dotnet/issues)
- **Community:** [FINOS Morphir](https://github.com/finos/morphir)

## See Also

- [CLI Reference](../cli/) - Complete command documentation
- [morphir ir verify](./ir-verify/) - Detailed verification command reference
- [Schema Specifications](/docs/spec/schemas/) - IR schema documentation
