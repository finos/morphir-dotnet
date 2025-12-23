---
title: "PRD: Layered Configuration"
linkTitle: "Layered Configuration"
weight: 40
description: "Product Requirements Document for layered Morphir configuration and workspace support"
---

# Product Requirements Document: Layered Configuration and Workspaces

**Status**: 📋 Draft  
**Created**: 2025-12-22  
**Last Updated**: 2025-12-22  
**Author**: Morphir .NET Team

## Overview

Introduce a layered configuration system for Morphir tooling with global and workspace-scoped TOML files, optional user and CI overlays, and standardized cache path resolution. Centralize configuration models in a new F# project (`Morphir.Configuration`) so all tools can share the same domain types. `Morphir.Tooling` will reference `Morphir.Configuration` and provide resolver and IO services.

## Problem Statement

Morphir tooling lacks a consistent configuration mechanism for workspace-scoped settings, user-specific overrides, and CI-specific behavior. This results in scattered, ad-hoc configuration approaches, inconsistent cache locations, and poor ergonomics for CLI usage in CI/CD environments.

## Goals

1. Provide layered configuration with deterministic precedence across global, workspace, user, and CI overlays.
2. Define workspace discovery rules and standard config file locations.
3. Centralize configuration domain models in `Morphir.Configuration` (F#).
4. Expose a resolver in `Morphir.Tooling` with a clear API for consumers.
5. Document configuration files, precedence, and CI activation behavior.

## Non-Goals

- Implementing cache read/write behavior (only path resolution and configuration).
- Introducing new CLI commands beyond config selection and CI activation flags.
- Complex schema validation beyond basic TOML parsing and sanity checks.
- Breaking compatibility with existing tooling workflows without migration guidance.

## User Stories

### Story 1: Workspace Configuration
**As a** developer  
**I want** workspace-level Morphir configuration in `.morphir/morphir.toml`  
**So that** I can keep project settings out of the repository root

### Story 2: Personal Overrides
**As a** developer  
**I want** a local override file (`.morphir/morphir.user.toml`)  
**So that** I can keep personal settings out of version control

### Story 3: CI Profiles
**As a** CI pipeline  
**I want** a CI overlay (`.morphir/morphir.ci.toml`)  
**So that** CI-specific settings apply only when needed

### Story 4: Global Defaults
**As a** developer  
**I want** global defaults in OS-standard config locations  
**So that** I can reuse defaults across repositories

## Detailed Requirements

### Functional Requirements

#### FR-1: Layered Precedence

Load configuration in the following order (lowest to highest precedence):
1. Global config (OS-standard path)
2. Workspace config: `.morphir/morphir.toml`
3. User override: `.morphir/morphir.user.toml` (optional)
4. CI override: `.morphir/morphir.ci.toml` (optional, conditional)

#### FR-2: Workspace Root Discovery

Workspace root is discovered by:
1. VCS root (Git) when available.
2. If no VCS root is found, the nearest `.morphir/` directory when walking up from the current directory.
3. If neither is found, treat as no workspace configuration.

Log selection decisions and conflicts (e.g., when `.morphir/` exists below VCS root).

#### FR-3: CI Overlay Activation

Support a CI activation flag with values:
- `on`: always apply `.morphir/morphir.ci.toml`
- `off`: never apply `.morphir/morphir.ci.toml`
- `auto` (default): apply if CI is detected

CI detection uses environment variables, minimum set:
`CI=true`, `GITHUB_ACTIONS`, `AZURE_HTTP_USER_AGENT`, `GITLAB_CI`, `BITBUCKET_BUILD_NUMBER`, `TEAMCITY_VERSION`.

#### FR-4: Global Config Locations

Global config path (OS-specific):
- Windows: `%APPDATA%\Morphir`
- Linux: `$XDG_CONFIG_HOME/morphir` or `~/.config/morphir`
- macOS: `~/Library/Application Support/morphir`

Global config file name: `morphir.toml`.

#### FR-5: Cache Paths (Resolution Only)

Expose resolved cache paths:
- Workspace cache: `.morphir/cache/` (overridable by config)
- Global cache: OS-standard cache dir (overridable by config)

No caching behavior is implemented in this phase.

#### FR-6: Shared Domain Models

Create a new F# project:
- `src/Morphir.Configuration/` containing domain models and pure configuration types
- `tests/Morphir.Configuration.Tests/` containing unit tests for models and parsing behavior

`Morphir.Tooling` references `Morphir.Configuration` and provides the resolver and IO boundary.

### Non-Functional Requirements

- Deterministic merging behavior with explicit precedence.
- Minimal dependencies; avoid heavy configuration frameworks.
- Respect CLI logging rules (stdout reserved for command output; diagnostics to stderr).
- Keep domain models immutable and free of IO.

## Proposed Architecture

### Projects

1. **Morphir.Configuration (F#)**
   - Config models (records, DU types)
   - Pure merge logic
   - CI activation options and detection helpers (pure, env injected)
2. **Morphir.Tooling (C# / F#)**
   - Config loader/resolver
   - Workspace discovery
   - TOML parsing and file IO

### Public API Sketch (Morphir.Configuration)

```fsharp
type CiProfileMode =
  | On
  | Off
  | Auto

type CachePaths =
  { WorkspaceCache: string option
    GlobalCache: string option }

type MorphirConfig =
  { Cache: CachePaths
    // Additional fields as needed
  }

type ConfigLayer =
  { Path: string
    Config: MorphirConfig }

type ConfigResolution =
  { Effective: MorphirConfig
    Layers: ConfigLayer list
    WorkspaceRoot: string option
    CiProfileApplied: bool }
```

## Testing Strategy

### Morphir.Configuration.Tests

- Merge precedence and overrides
- Optional fields and missing values
- CI activation mode handling (with injected env map)

### Morphir.Tooling.Tests

- Global path selection per OS (parameterized)
- Workspace discovery rules
- Layered load behavior with missing optional files
- CI activation flag (on/off/auto) and detection

## Documentation Requirements

- New documentation page describing config locations, precedence, and CI behavior.
- Update troubleshooting doc with config resolution guidance.
- Add `.morphir/morphir.user.toml` and cache paths to git-ignore guidance.

## Minimal TOML Schema (v1)

- `morphir.toml` supports optional `project`, `workspace`, and `morphir` sections.
- `morphir` is optional and contains `dist`, `tools`, and `extensions` subsections (defaults apply when omitted).
- `workspace.projects` accepts an array of project globs for monorepo layouts.
- `workspace.outputDir` defaults to `${WorkspaceHome}/out/`.
- `WorkspaceHome` defaults to the `.morphir/` folder at the workspace root and is overridable via config.
- `project` defaults to supporting the properties currently available in the Morphir project file (`morphir.json` in finos/morphir-elm).

## Feature Status Tracking

| Feature | Status | Notes |
|---------|--------|-------|
| Morphir.Configuration project + tests | ⏳ Planned | New F# domain project |
| Configuration model definitions | ⏳ Planned | Records/DU types + merge logic |
| Workspace discovery | ⏳ Planned | `.morphir/` and VCS root |
| Layered resolver in Morphir.Tooling | ⏳ Planned | IO boundary + merge |
| CI profile activation | ⏳ Planned | on/off/auto + env detection |
| Cache path resolution | ⏳ Planned | Expose effective paths |
| Documentation updates | ⏳ Planned | CLI and troubleshooting |

## Implementation Notes

_Add implementation notes here as decisions are made._

   - Start with a `morphir.toml` that supports optional `project` and `workspace` sections.
   - Add an optional `morphir` section containing `dist`, `tools`, and `extensions` subsections (defaults apply when omitted).
   - `workspace.projects` accepts an array of project globs for monorepo layouts.
   - `workspace.outputDir` defaults to `${WorkspaceHome}/out/`.
   - `WorkspaceHome` defaults to the `.morphir/` folder at the workspace root and is overridable via config.
   - `project` defaults to supporting the properties currently available in the Morphir project file (`morphir.json` in finos/morphir-elm).
