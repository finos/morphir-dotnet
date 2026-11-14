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

`extension list` and `extension install` are available. `extension install` takes a
provider source, such as `github:finos/morphir-python@0.6.2` or `nuget:Acme.Morphir.Python@1.2.3`, or an explicit
local-developer declaration from a registered repository. Both need explicit
`--no-probe` consent. A positional archive URL is not accepted. Selection and removal
commands remain stubs. See [extension installation status](extension-installation.md).
The full installer is tracked by
[#558](https://github.com/finos/morphir-dotnet/issues/558).

An extension adds a capability to Morphir, such as a code generator. The plan is in the
[Morphir extension model PRD](../../contributing/design/prds/morphir-extension-model.md).

```bash
morphir extension list [--json] [--platform <rid>] [--local]
morphir extension runtimes [--json]
morphir extension repository add <name> --directory <absolute-path> --home <absolute-home> --local-developer [--dry-run]
morphir extension install github:<owner>/<repo>[@<version-or-tag>] --home <absolute-home> --no-probe [--dry-run]
morphir extension install nuget:<package-id>[@<version>] [--source <name|url|folder>] [--configfile <path>] [--interactive] --home <absolute-home> --no-probe [--dry-run]
morphir extension install <name> --repository <registered-name> --local-release <json> --local-archive <zip> --home <absolute-home> --local-developer --no-probe [--dry-run]
morphir extension use <name> <version> [--platform <rid>] [--local]             # not implemented yet
morphir extension remove <name> <version> [--platform <rid>] [--local]          # not implemented yet
morphir extension which <name> [--platform <rid>]                               # not implemented yet
```

`use`, `remove` and `which` fail with exit code 1 and a link to
[issue #492](https://github.com/finos/morphir-dotnet/issues/492). They arrive with step 3 of the PRD.

`morphir extension runtimes` reports whether this host can run WASM extensions, with its engine, profiles and bounds,
or the reason it cannot. See [morphir extension runtimes](../extension-runtimes/).

`morphir extension worker` is internal and experimental, and is not in this list or in the help: it serves one
WASM module over stdio for a conformance runner, and no product command uses it. See
[morphir extension worker](../extension-worker/).

### extension install

Install an extension package from a provider source, then run it by its id:

```bash
morphir extension install github:finos/morphir-python@0.6.2 --home "$HOME/.morphir" --no-probe
morphir compile --language python ... --extension-id <id>
```

The part before the colon names the provider. Each provider only finds the release and
downloads the artifact. The installer that checks the digest, unpacks the package and
writes the catalog and lock is the same for every provider.

| Provider | Source | What it reads |
| --- | --- | --- |
| `github` | `github:<owner>/<repo>[@<version-or-tag>]` | The `release.json` asset of a GitHub release, then the artifact asset for this platform |
| `nuget` | `nuget:<package-id>[@<version>]` | The `morphir/release.json` entry of a NuGet package, then the package subtree or WASM entry for this platform |
| local developer | `<extension-id> --repository <name> --local-release <json> --local-archive <archive> --local-developer` | A release declaration and archive in a registered local repository |

For `github:`:

- `@<version>` (SemVer) selects the release tagged `v<version>`, else the release tagged `<version>`.
  The descriptor `version` must equal `<version>`.
- `@<tag>` that is not SemVer selects the release with that exact tag.
- No `@` selects the latest release, as GitHub reports it: never a draft or a prerelease.
- The release must follow the
  [GitHub release convention](https://morphir.finos.org/docs/design/draft/extensions/distribution-and-acquisition#github-release-convention):
  one `release.json` asset (extension family `0.1.0-preview.3`), and each artifact `filename` equal to the name of
  an asset of the same release.
- The artifact bytes stream into a private stage in the Home, and their SHA-256 must equal the descriptor `sha256`
  before anything else reads them. A mismatch, a missing asset or no artifact for this platform installs nothing.
- The command uses the anonymous GitHub API. Set `GITHUB_TOKEN` or `GH_TOKEN` to raise the rate limit or to read a
  private repository. The token goes only to the API host. When the limit is reached, the command fails with
  `extension.github_rate_limited` and says when the limit resets.
- Naming a release is an explicit request, so `--local-developer` does not apply. `--no-probe` is required, as for
  a local declaration: the extension is not started during installation, and its claims stay unchecked.
- `--dry-run` reads the release metadata and `release.json`, and downloads no artifact.
- Only `extension install` uses the network. `compile` and `generate` run the installed copy and never download.

For `nuget:`:

```bash
# From the sources of nuget.config (nuget.org, a private V3 feed, a folder feed)
morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home "$HOME/.morphir" --no-probe

# From one source only: a configured source name, a V3 service index URL, or a folder
morphir extension install nuget:Acme.Morphir.Python --source ./artifacts/nupkg --home "$HOME/.morphir" --no-probe

# Plan only: reads the main package, selects the artifact, writes nothing to the Home
morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home "$HOME/.morphir" --no-probe --dry-run
```

- The package id follows the NuGet id rules and is compared ignoring case. The version is a NuGet version, compared
  in normalized form (`1.2` is `1.2.0`). A malformed id or version fails with `extension.invalid_source`.
- `@<version>` selects that exact version from the first applicable source that has it, also a prerelease or an
  unlisted version. No `@` selects the highest stable listed version across the applicable sources, as
  `dotnet tool install` without `--version` does: never a prerelease or an unlisted version. A folder feed has no
  listing, so all its stable versions count.
- Without `--source`, the sources are the enabled `packageSources` of the `nuget.config` hierarchy from the current
  directory, or of the file `--configfile` names. When `nuget.config` has `packageSourceMapping`, only the sources
  mapped to the package id apply. `--source` replaces that list with one source: a configured source name, a V3
  `index.json` URL, or a folder path. A configured source with the same URL keeps its credentials.
- A source is recorded in the lock, so it must be an `https` service index whose path ends in `/index.json`, or a
  folder. An `http` source, a V2 URL or a URL with userinfo, a query or a fragment fails with
  `extension.invalid_source`.
- Credentials come from the `packageSourceCredentials` of `nuget.config` and from NuGet credential provider plugins,
  found as NuGet finds them: the `NUGET_PLUGIN_PATHS` variable, else `nuget-plugin-*` executables on `PATH` and the
  plugins folder. The command is non-interactive; `--interactive` lets a plugin prompt for sign-in, with its prompts on
  standard error. NuGet sends a credential only to its own source. No credential is written to a log, a lock or a
  catalog. A refused credential fails with `extension.nuget_unauthorized`, which names the source.
- Package signatures are checked as the dotnet CLI checks them on the same operating system: by default on Windows
  and Linux, not on macOS. `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` or `false` changes the default on Linux and
  macOS. The client policy of `nuget.config` (`signatureValidationMode` `accept`, or `require` with
  `trustedSigners`) applies to each package the command reads; `require` checks signatures on every operating system.
  A refused package fails with `extension.nuget_signature_invalid`.
- The main package must hold `morphir/release.json`, its `.nuspec` id and version must equal the request, and the
  descriptor `version` must equal the package version. An ordinary NuGet package fails with
  `extension.release_descriptor_missing`; a different version fails with `extension.version_mismatch`.
- A process artifact has the package format `nupkg`. Its `filename` names the main package (one package, layout A)
  or a package whose id is the main id, a `.`, and more, at the same version and from the same source (one package
  per platform, layout B). Only the files under the artifact `root` are installed; `.nuspec`, `_rels/`,
  `[Content_Types].xml`, `package/`, `.signature.p7s` and `morphir/` outside the root are not. A package of another
  publisher fails with `extension.invalid_descriptor`; a missing artifact package fails with
  `extension.artifact_missing`.
- The installed tree is pinned by the descriptor `treeSha256`, not by the `.nupkg` bytes, because a feed such as
  nuget.org signs a package after it is packed. A changed file under the root fails with
  `package.tree_digest_mismatch` and installs nothing.
- A RID-specific .NET tool installs when its payload is self-contained or Native AOT. An `entryPoint` that ends in
  `.dll`, or a `<entry point>.runtimeconfig.json` that names a shared framework, fails with
  `extension.unsupported_package`, because such a tool needs an installed .NET runtime.
- A descriptor may offer `process` and `wasm` artifacts. The command selects the `process` artifact for this
  platform, else a `wasm` artifact. This host does not install a WASM artifact yet: it fails with
  `extension.unsupported_package`.
- No source with the package or version fails with `extension.nuget_not_found`; a source that cannot be read fails
  with `extension.nuget_unreachable`. Both messages name the package, the version and the sources tried.
- The command writes only to the Home: it downloads into a private stage in the Home and uses no NuGet HTTP cache,
  global packages folder or NuGet temporary folder. A credential provider plugin keeps its own cache.
- `--source`, `--configfile` and `--interactive` apply only to a `nuget:` source; with any other source they fail
  with `extension.invalid_source`.

Extension authors: see [Publish an extension as a NuGet package](../../guides/publish-extension-nuget/).

The catalog and lock record where the artifact came from as a provider-tagged source, for example
`{"kind": "github-release-asset", "repository": "finos/morphir-python", "tag": "v0.6.2", "asset": "…"}`, and the
release as `{"kind": "github-release", …, "revision": "<sha256 of release.json>"}`. A NuGet install records
`{"kind": "nuget-package-entry", "feed": "https://api.nuget.org/v3/index.json", "id": "acme.morphir.python.osx-arm64",
"version": "1.2.3", "path": "tools/net11.0/osx-arm64"}` and `{"kind": "nuget-package", "feed": …,
"id": "acme.morphir.python", "version": "1.2.3", "revision": "<sha256 of morphir/release.json>"}`.

### extension list

List the extensions that this host knows, and say which offering of morphir-dotnet is running and what it needs from
the machine. Today the extensions are the built-in F# frontend of `morphir compile`, the built-in generators of
`morphir generate` and the built-in analyzers of [`morphir validate`](validate.md).

```bash
$ morphir extension list
╭────────────────────────┬────────────────┬─────────┬─────────────────────┬──────────┬─────────────────────────╮
│ ID                     │ NAME           │ VERSION │ TYPES               │ DELIVERY │ HOSTS                   │
├────────────────────────┼────────────────┼─────────┼─────────────────────┼──────────┼─────────────────────────┤
│ morphir-dotnet.fsharp  │ F# frontend    │ 0.4.0   │ frontend (fsharp)   │ built-in │ morphir, dotnet-morphir │
│ morphir.csharp         │ C# generator   │ 0.4.0   │ backend             │ built-in │ morphir, dotnet-morphir │
│ morphir.fsharp         │ F# generator   │ 0.4.0   │ backend             │ built-in │ morphir, dotnet-morphir │
│ morphir.type-validator │ Type validator │ 0.4.0   │ validator (1, 2, 3) │ built-in │ morphir, dotnet-morphir │
╰────────────────────────┴────────────────┴─────────┴─────────────────────┴──────────┴─────────────────────────╯
```

The rows are sorted by id. The version of a built-in is the version of the host, including any build metadata.
The `TYPES` column shows each type. For a frontend, the languages that it reads follow in parentheses; for a validator,
the IR versions that it reads.

In a terminal the table has styled borders and headers and fits the terminal width. When stdout is redirected, or
`NO_COLOR` is set, the table has no ANSI codes. The table is for people: scripts use `--json`.

**Options:**
- `--json`: Write one JSON document on stdout.
- `--platform <rid>`, `--local`: Select installed extensions. There are none until step 3 of the PRD, so these
  options do not change the result now. Built-in extensions always appear.

With `--json`, from the Native AOT `morphir` for Windows ARM64:

```json
{
  "host": "morphir",
  "deployment": "native-aot",
  "execution": "native-aot",
  "platform": "win-arm64",
  "prerequisites": { "runtime": [], "sdk": [], "native": [] },
  "extensions": [
    {
      "id": "morphir-dotnet.fsharp",
      "name": "F# frontend",
      "version": "0.4.0",
      "description": "Compiles F# source files to a Morphir model in process.",
      "types": ["frontend"],
      "delivery": "built-in",
      "hosts": ["morphir", "dotnet-morphir"],
      "deployments": ["self-contained", "framework-dependent", "dotnet-tool"],
      "available": false,
      "reason": "The native-aot morphir has no in-process F# frontend; the self-contained and framework-dependent morphir and dotnet-morphir have one. Here, install the F# frontend process delivery morphir-fsharp-frontend and configure it with [frontend.fsharp].extension or --extension.",
      "prerequisites": { "runtime": [], "sdk": [], "native": [] },
      "capabilities": {
        "frontend": {
          "languages": [{ "id": "fsharp", "fileExtensions": [".fs", ".fsi"] }],
          "irVersions": ["1", "2", "3", "4"]
        }
      }
    },
    {
      "id": "morphir.csharp",
      "name": "C# generator",
      "version": "0.4.0",
      "description": "Generates C# source from a Morphir library model.",
      "types": ["backend"],
      "delivery": "built-in",
      "hosts": ["morphir", "dotnet-morphir"],
      "deployments": ["native-aot", "self-contained", "framework-dependent", "dotnet-tool"],
      "available": true,
      "prerequisites": { "runtime": [], "sdk": [], "native": [] },
      "capabilities": {
        "backend": { "targets": ["csharp"], "irVersions": ["1", "2", "3", "3.0.0", "3.1.0", "4", "4.0.0"], "generate": true }
      }
    }
  ]
}
```

The example leaves out the `morphir.fsharp` and `morphir.type-validator` entries. The four offerings report:

| Offering | `host` | `deployment` | `execution` | `platform` | Top-level `prerequisites` | `morphir-dotnet.fsharp` |
| --- | --- | --- | --- | --- | --- | --- |
| Native AOT `morphir` | `morphir` | `native-aot` | `native-aot` | the RID | none | not available, with the reason above |
| Self-contained `morphir` | `morphir` | `self-contained` | `jit` | the RID | none | available |
| Framework-dependent `morphir` | `morphir` | `framework-dependent` | `jit` | the RID | runtime `Microsoft.NETCore.App` `>=10.0.0` `<11.0.0` | available |
| `dotnet-morphir` (`Morphir.Tool`) | `dotnet-morphir` | `dotnet-tool` | `jit` | left out: the package is portable | that runtime, and SDK `Microsoft.NET.Sdk` `>=10.0.100` with `"phase": "install"` | available |

A build that no named release profile produced, such as `dotnet run`, a test host, or the legacy `morphir-<rid>`
single-file assets that releases attach today, has no qualified deployment: it leaves out `deployment`, `platform` and
the top-level `prerequisites`, and never reports one of the four names. It still reports `execution`.

| Field | Meaning |
| --- | --- |
| `host` | The command that wrote the list: `morphir` or `dotnet-morphir` (the dotnet tool). It is a command name, not the offering: the Native AOT, self-contained and framework-dependent offerings all report `morphir`. |
| `deployment` | The offering that the build's named release profile set at build time: `native-aot`, `self-contained`, `framework-dependent` or `dotnet-tool`. It is never inferred from how the process runs. Left out when no named profile built the executable. |
| `execution` | How this process runs: `native-aot` or `jit`. |
| `platform` | The .NET runtime identifier that the profile built for. Left out for the portable tool package and when no named profile built the executable. |
| `prerequisites` | What the offering needs from the machine: `runtime` (an installed .NET runtime, to run), `sdk` (a .NET SDK, with `phase` `install` or `run`) and `native` (none is declared yet). Left out when no named profile built the executable. |
| `deployments` | The offerings in which the extension is available. |
| `reason` | Why the extension is not available in the running offering. Present exactly when `available` is `false`. |
| extension `prerequisites` | What the extension needs from the machine beyond the host. Every built-in needs nothing. |
| `id` | The extension id. See [Extension ids](#extension-ids). For a built-in generator, it is also the `backend` value in `morphir generate --json` reports. |
| `version` | Canonical SemVer 2.0.0. |
| `description` | Left out when the extension has none. |
| `types` | MEP extension types: `frontend`, `backend`, `transform`, `validator` or `workspace`. They come from the capabilities. |
| `delivery` | How the extension is delivered: `built-in` (compiled into the host) or `wasm` (a WASM module that the host's WASM runtime runs). Every entry listed today is `built-in`: listing configured WASM modules is planned in [issue #565](https://github.com/finos/morphir-dotnet/issues/565). |
| `hosts` | The hosts that the extension runs in. |
| `available` | `true` when the extension runs in this host and offering. An extension for the other host, or for other offerings, is listed with `false` and a `reason`. A `wasm` extension in a host whose WASM runtime is unavailable is listed with `false`. |
| `capabilities.frontend` | The MEP frontend claim: the languages with their file extensions, and the IR versions that it produces. |
| `capabilities.backend` | The MEP backend claim: targets, IR versions in their declared spelling, and `generate`. |
| `capabilities.validator` | The IR versions that a validator reads, in the spelling of the model readers. This is a morphir-dotnet report member: MEP `0.2.0-draft.1` has no validator capability, so hosts do not write it into MEP messages. |

Logs and diagnostics go to stderr, so `morphir extension list --json | jq` works. If two extensions have the same id,
the command exits with code 2 and names each duplicate id (`extension.duplicate_id`). With `--json`, the document then
has an empty `extensions` array and a `diagnostics` array.

### Extension ids

An extension id is one or more segments joined by dots, at most 238 characters. Each segment starts with a lowercase
ASCII letter, contains only lowercase letters, digits and `-`, and ends with a letter or digit. Examples: `morphir.csharp`,
`acme.tools.lint-2`, `morphir-elm`.

A one-segment id follows the upstream Morphir rule. Dotted ids are a morphir-dotnet superset, proposed upstream in [finos/morphir#1137](https://github.com/finos/morphir/issues/1137).

Every id that starts with `morphir.` is reserved for the built-in frontends and backends of morphir-dotnet,
including built-ins that do not ship yet. `compile` and `generate` refuse an extension guest that reports such an id
(`extension.unavailable`). Ids such as `org.finos.morphir.fsharp-backend` are not reserved.

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
