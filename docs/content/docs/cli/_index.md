---
title: "CLI Reference"
linkTitle: "CLI"
weight: 30
description: "Command-line interface reference for Morphir .NET tooling"
---

# Morphir .NET CLI Reference

The Morphir .NET CLI provides powerful command-line tools for working with Morphir IR files, validating schemas, and managing Morphir projects.

## Available Commands

- [morphir compile](./compile/) - Compile F# sources to Morphir IR (built into `dotnet-morphir`)
- [morphir generate](./generate/) - Generate F# from classic or v4 JSON/Ion libraries
- [morphir validate](./validate/) - Check a model with the built-in analyzers, such as the type validator
- [morphir extension list](./management/#extension-list) - List the built-in generators and analyzers of this host
- [morphir mck wasm-host](./mck-wasm-host/) - _Experimental and internal:_ run the crafted WASM host cases against a host's worker mode
- [morphir extension worker](./extension-worker/) - _Experimental and internal:_ serve one WASM extension module over stdio for a conformance runner

### IR Management

- **[morphir ir verify](./ir-verify/)** - Validate Morphir IR JSON files against official schemas
- **morphir ir detect-version** _(coming in Phase 2)_ - Detect the schema version of an IR file

### Project Management

- Planned [morphir project migrate]({{< relref "/docs/cli/project" >}}) converts one classic project to the newer project system.
- Planned [morphir workspace migrate]({{< relref "/docs/cli/workspace" >}}) inventories and adopts projects within a bounded repository directory.

These migration commands are not yet implemented.

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

The [configuration guide]({{< relref "/guides/configuration" >}}) distinguishes the compatibility TOML cache loader from the additive shared configuration and project system.

The [morphir config read commands]({{< relref "/docs/cli/config" >}}) inspect effective/scoped values, source reports and redacted explanations. JSON output follows the published schema; success, missing keys and usage/loading failures use exit codes 0/1/2. Shared `config set/unset` editing, [project migrate]({{< relref "/docs/cli/project-migrate" >}}) and bounded [workspace migrate]({{< relref "/docs/cli/workspace-migrate" >}}) are available. Start with the [workspace configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}}).

## Troubleshooting

See the [Troubleshooting Guide](./troubleshooting/) for solutions to common issues.
