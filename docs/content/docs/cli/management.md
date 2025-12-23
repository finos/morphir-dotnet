---
title: "Management Commands"
linkTitle: "Management"
weight: 20
description: >
  Commands for managing Morphir distributions, tools, and extensions.
---

## Overview

The Morphir CLI provides commands to manage distributions, tools, and extensions with support for:
- **Global-first installation** (in OS config directory like `~/.config/morphir` or `~/.morphir`)
- **Local overrides** (per-project in `.morphir/` directory)
- **Platform-specific artifacts** using .NET Runtime Identifiers (e.g., `linux-x64`, `win-x64`, `osx-arm64`)

## Dist Commands

Manage core Morphir distributions.

### dist list

List installed distributions.

```bash
morphir dist list [options]
```

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: List local (project) installations only

**Example:**
```bash
# List all distributions for current platform
morphir dist list

# List distributions for a specific platform
morphir dist list --platform linux-x64

# List project-local distributions
morphir dist list --local
```

### dist install

Install a distribution from a URL.

```bash
morphir dist install <url> <version> [options]
```

**Arguments:**
- `<url>`: Source URL to download the distribution from
- `<version>`: Version string (e.g., `1.0.0`, `2.1.3-beta`)

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: Install locally (project-specific)

**Example:**
```bash
# Install globally
morphir dist install https://example.com/morphir-dist.tar.gz 1.0.0

# Install locally for project
morphir dist install https://example.com/morphir-dist.tar.gz 1.0.0 --local

# Install for specific platform
morphir dist install https://example.com/morphir-dist.tar.gz 1.0.0 --platform linux-x64
```

### dist use

Set the active distribution version.

```bash
morphir dist use <version> [options]
```

**Arguments:**
- `<version>`: Version to activate

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: Set in local (project) scope

**Example:**
```bash
# Set global active version
morphir dist use 1.0.0

# Set local active version for current project
morphir dist use 2.0.0 --local
```

**Resolution:** Local selection takes precedence over global when both are set.

### dist remove

Remove an installed distribution.

```bash
morphir dist remove <version> [options]
```

**Arguments:**
- `<version>`: Version to remove

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: Remove from local (project) scope

**Example:**
```bash
morphir dist remove 1.0.0
morphir dist remove 1.0.0 --local
```

### dist which

Show the currently active distribution.

```bash
morphir dist which [options]
```

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)

**Example:**
```bash
morphir dist which
# Output: Version 1.0.0 (global) at /home/user/.config/morphir/dist/linux-x64/1.0.0
```

## Tool Commands

Manage auxiliary CLI tools and utilities.

### tool list

List installed tools.

```bash
morphir tool list [options]
```

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: List local (project) installations only

### tool install

Install a tool from a URL.

```bash
morphir tool install <name> <url> <version> [options]
```

**Arguments:**
- `<name>`: Tool name
- `<url>`: Source URL
- `<version>`: Version string

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: Install locally (project-specific)

**Example:**
```bash
morphir tool install morphir-elm-codegen https://example.com/tool.tar.gz 1.0.0
morphir tool install my-formatter https://example.com/formatter.tar.gz 2.1.0 --local
```

### tool use

Set the active tool version.

```bash
morphir tool use <name> <version> [options]
```

**Arguments:**
- `<name>`: Tool name
- `<version>`: Version to activate

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)
- `--local`: Set in local (project) scope

### tool remove

Remove an installed tool.

```bash
morphir tool remove <name> <version> [options]
```

### tool which

Show the currently active tool version.

```bash
morphir tool which <name> [options]
```

**Options:**
- `--platform <rid>`: Platform Runtime Identifier (defaults to current platform)

## Extension Commands

Manage optional add-ons and plugins.

Extension commands follow the same pattern as tool commands:

```bash
morphir extension list [--platform <rid>] [--local]
morphir extension install <name> <url> <version> [--platform <rid>] [--local]
morphir extension use <name> <version> [--platform <rid>] [--local]
morphir extension remove <name> <version> [--platform <rid>] [--local]
morphir extension which <name> [--platform <rid>]
```

## Platform Identifiers (RIDs)

Morphir uses .NET Runtime Identifiers to manage platform-specific artifacts:

| Platform | RID |
|----------|-----|
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| Windows x64 | `win-x64` |
| Windows ARM64 | `win-arm64` |
| macOS x64 (Intel) | `osx-x64` |
| macOS ARM64 (Apple Silicon) | `osx-arm64` |

The CLI automatically detects the current platform when `--platform` is not specified.

## Resolution Rules

When looking for an active artifact, the CLI uses the following precedence:

1. **Project-local selection** (if `.morphir/` exists and selection is set locally)
2. **Global selection** (in OS config directory)
3. **Error** if no selection is found

Each artifact category (dist, tool, extension) resolves independently.

## Folder Layout

```
Global (preferred):
  ~/.config/morphir/           (Linux)
  ~/Library/Application Support/morphir/  (macOS)
  %APPDATA%\Morphir\           (Windows)
    dist/
      <platform>/
        <version>/
          bin/
          manifest.json
    tools/
      <platform>/
        <tool-name>/
          <version>/
            bin/
            manifest.json
    extensions/
      <platform>/
        <extension-name>/
          <version>/
            bin/
            manifest.json

Local (optional, per project):
  .morphir/
    dist/...
    tools/...
    extensions/...
```

## Manifest Format

Each installed artifact includes a `manifest.json` file (JSONC format with comments supported):

```json
{
  "name": "morphir-dist",
  "version": "1.0.0",
  "platform": "linux-x64",
  "sourceUrl": "https://example.com/artifact.tar.gz",
  "sha256": "abc123...",
  "description": "Core Morphir distribution",
  "installedAt": "2025-12-23T19:00:00Z",
  "metadata": {
    "key": "value"
  }
}
```

## See Also

- [Configuration Guide](/guides/configuration/) - Learn about layered configuration
- [Troubleshooting](/docs/cli/troubleshooting/) - Common issues and solutions
