---
title: "Configuration System"
linkTitle: "Configuration"
weight: 40
description: >
  Layered configuration system for Morphir tooling
---

# Configuration System

Morphir .NET provides a layered configuration system that allows you to customize tooling behavior at multiple levels: global, workspace, user-specific, and CI-specific.

## Configuration Files

Morphir uses TOML format for all configuration files. Configuration is hierarchical with clear precedence rules.

### File Locations

#### Global Configuration
- **Windows**: `%APPDATA%\Morphir\morphir.toml`
- **Linux**: `$XDG_CONFIG_HOME/morphir/morphir.toml` or `~/.config/morphir/morphir.toml`
- **macOS**: `~/Library/Application Support/morphir/morphir.toml`

#### Workspace Configuration
All workspace configuration files live under the `.morphir/` directory at the workspace root:

- **Base config**: `.morphir/morphir.toml` (committed to version control)
- **User overrides**: `.morphir/morphir.user.toml` (git-ignored, developer-specific)
- **CI overrides**: `.morphir/morphir.ci.toml` (committed, applied in CI environments)

## Workspace Discovery

Morphir automatically discovers your workspace root using the following rules:

1. **Prefer VCS root**: If a `.git` directory is found, use its parent as the workspace root
2. **Fallback to .morphir/**: If no VCS root is found, walk up from the current directory to find `.morphir/`
3. **Log conflicts**: If both exist at different levels, VCS root takes precedence and a warning is logged

## Configuration Precedence

Configuration layers are merged with the following precedence (lowest to highest):

1. **Global config** (`morphir.toml` in OS config directory)
2. **Workspace config** (`.morphir/morphir.toml`)
3. **User override** (`.morphir/morphir.user.toml`)
4. **CI override** (`.morphir/morphir.ci.toml`, when active)

Higher precedence values override lower precedence values for the same setting.

## CI Profile Activation

The CI profile (`.morphir/morphir.ci.toml`) can be activated in three modes:

### Auto Mode (Default)
Automatically detects CI environments based on well-known environment variables:
- `CI=true`
- `GITHUB_ACTIONS`
- `AZURE_HTTP_USER_AGENT`
- `GITLAB_CI`
- `BITBUCKET_BUILD_NUMBER`
- `TEAMCITY_VERSION`
- `CIRCLECI`
- `TRAVIS`
- `JENKINS_URL`
- `BUILDKITE`
- `CODEBUILD_BUILD_ID`
- `TF_BUILD`

### On Mode
Always applies the CI profile, regardless of environment.

### Off Mode
Never applies the CI profile, even in CI environments.

## Configuration Schema

### Minimal Schema (v1)

The initial configuration schema supports cache path customization:

```toml
[morphir.cache]
workspace = "/custom/workspace/cache"
global = "/custom/global/cache"
```

Or alternatively:

```toml
[cache]
workspace = "/custom/workspace/cache"
global = "/custom/global/cache"
```

### Default Values

If not specified in configuration:

- **Workspace cache**: `.morphir/cache/` at workspace root
- **Global cache**: OS-standard cache directory
  - Windows: `%LOCALAPPDATA%\Morphir\Cache`
  - Linux: `$XDG_CACHE_HOME/morphir` or `~/.cache/morphir`
  - macOS: `~/Library/Caches/morphir`

## Git Ignore Recommendations

Add the following to your `.gitignore`:

```gitignore
# Morphir user-specific configuration (keep out of version control)
.morphir/morphir.user.toml

# Morphir caches
.morphir/cache/

# Morphir generated output
.morphir/out/
```

## Example Configurations

### Global Configuration

Create a global default for all your Morphir projects:

**`~/.config/morphir/morphir.toml`** (Linux):
```toml
[morphir.cache]
global = "/home/user/.cache/morphir-shared"
```

### Workspace Configuration

Set project-specific defaults for your team:

**`.morphir/morphir.toml`**:
```toml
[morphir.cache]
workspace = ".morphir/cache"
```

### User Override

Customize settings for your local development:

**`.morphir/morphir.user.toml`**:
```toml
[morphir.cache]
workspace = "/mnt/fast-disk/morphir-cache"
```

### CI Configuration

Optimize for CI environments:

**`.morphir/morphir.ci.toml`**:
```toml
[morphir.cache]
workspace = "/tmp/morphir-ci-cache"
global = "/tmp/morphir-ci-global"
```

## Programmatic Access

### Using ConfigResolver (C#)

```csharp
using Morphir.Configuration;
using Morphir.Tooling.Configuration;
using Microsoft.Extensions.Logging;

// Create resolver
var logger = loggerFactory.CreateLogger<ConfigResolver>();
var workspaceDiscovery = new WorkspaceDiscovery(
    loggerFactory.CreateLogger<WorkspaceDiscovery>());
var resolver = new ConfigResolver(logger, workspaceDiscovery);

// Resolve configuration
var config = await resolver.ResolveConfigAsync(
    ciMode: CiProfileMode.Auto,
    startPath: "/path/to/project");

// Access effective configuration
var effectiveCache = config.Effective.Cache;
if (Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(effectiveCache.WorkspaceCache))
{
    var workspaceCachePath = effectiveCache.WorkspaceCache.Value;
    // Use workspace cache path
}

// Check which layers were loaded
foreach (var layer in config.Layers)
{
    Console.WriteLine($"Loaded config from: {layer.Path}");
}

// Check if CI profile was applied
if (config.CiProfileApplied)
{
    Console.WriteLine("CI profile is active");
}
```

### Using Domain Models (F#)

```fsharp
open Morphir.Configuration

// Merge configurations manually
let globalConfig = { Cache = { WorkspaceCache = Some "/global"; GlobalCache = None } }
let workspaceConfig = { Cache = { WorkspaceCache = Some "/workspace"; GlobalCache = Some "/ws-global" } }

let merged = Merge.mergeConfigs globalConfig workspaceConfig
// merged.Cache.WorkspaceCache = Some "/workspace" (workspace overrides global)
// merged.Cache.GlobalCache = Some "/ws-global" (from workspace)

// Check CI environment
let envVars = 
    Environment.GetEnvironmentVariables()
    |> Seq.cast<System.Collections.DictionaryEntry>
    |> Seq.map (fun e -> (string e.Key, string e.Value))
    |> Map.ofSeq

let isCI = CiDetection.isCiEnvironment envVars
let shouldApply = CiDetection.shouldApplyCiOverlay CiProfileMode.Auto envVars
```

## Troubleshooting

### Configuration Not Loading

1. Check file permissions - ensure config files are readable
2. Verify TOML syntax - use a TOML validator
3. Enable debug logging to see which files are being loaded:
   ```bash
   # Set log level in your tool invocation
   MORPHIR_LOG_LEVEL=Debug morphir <command>
   ```

### Workspace Root Not Found

- Ensure you have either a `.git` directory or `.morphir/` directory
- Run from within the project directory
- Check logs for workspace discovery warnings

### CI Profile Not Applied

- Verify `CiProfileMode` is set to `Auto` or `On`
- Check that CI environment variables are set
- Confirm `.morphir/morphir.ci.toml` exists and is readable

## Future Extensions

The configuration system is designed to be extensible. Future versions may support:

- Project metadata (name, version, dependencies)
- Workspace project globs for monorepo layouts
- Output directory customization
- Tool-specific settings
- Extension configuration

See the [Layered Configuration PRD]({{< ref "/docs/contributing/design/prds/layered-configuration" >}}) for planned enhancements.
