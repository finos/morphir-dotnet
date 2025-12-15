---
title: "CLI Reference"
linkTitle: "CLI"
weight: 30
description: "Command-line interface reference for Morphir .NET tooling"
---

# Morphir .NET CLI Reference

The Morphir .NET CLI provides powerful command-line tools for working with Morphir IR files, validating schemas, and managing Morphir projects.

## Available Commands

### IR Management

- **[morphir ir verify](./ir-verify/)** - Validate Morphir IR JSON files against official schemas
- **morphir ir detect-version** _(coming in Phase 2)_ - Detect the schema version of an IR file

### Project Management

_(Future commands will be documented here)_

## Installation

The Morphir .NET CLI is distributed as a .NET tool. Install it globally with:

```bash
dotnet tool install -g Morphir.CLI
```

Or locally in a project:

```bash
dotnet tool install Morphir.CLI
```

## Getting Help

For help with any command, use the `--help` flag:

```bash
morphir --help
morphir ir --help
morphir ir verify --help
```

## Common Workflows

### Validating IR Files

The most common workflow is validating Morphir IR JSON files to ensure they conform to the expected schema:

```bash
# Validate a single file with auto-detection
morphir ir verify path/to/morphir-ir.json

# Validate with explicit schema version
morphir ir verify --schema-version 3 path/to/morphir-ir.json

# Get JSON output for CI/CD
morphir ir verify --json path/to/morphir-ir.json

# Quiet mode (only errors)
morphir ir verify --quiet path/to/morphir-ir.json
```

See the [morphir ir verify](./ir-verify/) documentation for complete details.

## Exit Codes

All Morphir CLI commands use consistent exit codes:

- **0**: Success - operation completed successfully
- **1**: Validation failure - IR file failed schema validation
- **2**: Operational error - file not found, invalid JSON, missing dependencies, etc.

These exit codes are designed for CI/CD integration and scripting.

## Configuration

_(Future: Configuration file format and options will be documented here)_

## Troubleshooting

See the [Troubleshooting Guide](./troubleshooting/) for solutions to common issues.
