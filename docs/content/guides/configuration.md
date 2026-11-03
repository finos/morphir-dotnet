---
title: "Configuration"
linkTitle: "Configuration"
weight: 40
description: "Morphir configuration formats, discovery, precedence, and shared host APIs"
---

# Configuration

Morphir configuration describes projects, workspace membership, and tooling settings. The target design shares discovery, resolution, inspection, and editing across the CLI and other hosts.

## Implementation status

The current .NET library supports cache settings from TOML, Git-root-first workspace discovery, adjacent user overrides, and an optional CI overlay. It does not yet implement the multi-format project system or the `morphir config` commands described below.

The remaining sections describe the agreed target behavior. Follow the [layered configuration PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}}) for implementation status, the [walkthrough]({{< relref "/docs/getting-started/configuration" >}}) for examples, and the [planned CLI reference]({{< relref "/docs/cli/config" >}}) for commands.

### Current cache configuration

For the existing resolver, place cache overrides in `.morphir/morphir.toml` at the discovered workspace root:

```toml
[morphir.cache]
workspace = "/custom/workspace/cache"
global = "/custom/global/cache"
```

The parser also accepts these keys under `[cache]`. An adjacent `.morphir/morphir.user.toml` overrides the base file. `.morphir/morphir.ci.toml` applies after the user file when `CiProfileMode` is `On`, or when it is `Auto` and CI is detected; `Off` disables it. Without a workspace, resolution uses global configuration and model defaults only.

The existing global file is `morphir.toml` under `%APPDATA%\Morphir` on Windows, `$XDG_CONFIG_HOME/morphir` or `$HOME/.config/morphir` on Linux, and `$HOME/Library/Application Support/morphir` on macOS. These are the current paths, before the planned machine-location rules below.

## Compatibility target

Use the newer [Morphir MoonBit configuration specification](https://github.com/finos/morphir-moonbit/blob/main/openspec/changes/adopt-moon-lander/specs/application/configuration/spec.md) and its [adoption decision](https://github.com/finos/morphir-moonbit/blob/main/docs/adr/0004-adopt-moon-lander.md) for discovery and format layering.

The [finos/morphir configuration model](https://github.com/finos/morphir/blob/main/docs/spec/morphir-toml/morphir-toml-specification.md) supplies the shared field vocabulary. Its older discovery rules reject sibling TOML and YAML files. For .NET, MoonBit's format layering supersedes that conflict rule, including implicit `.yml` and `.json5` discovery.

## File names and layouts

At each project or workspace directory, inspect these primary layouts:

| Layout | Primary stem | Adjacent user override stem |
| --- | --- | --- |
| Root | `morphir` | `morphir.user` |
| Hidden | `.morphir/morphir` | `.morphir/morphir.user` |
| Dot-config | `.config/morphir/config` | `.config/morphir/config.user` |

Each stem accepts `.json`, `.json5`, `.yaml`, `.yml`, or `.toml`. Both JSON extensions accept JSON5 syntax, including comments, unquoted keys, single-quoted strings, and trailing commas.

The enclosing directory is the project location. For example, `/repo/.config/morphir/config.toml` identifies `/repo`, not `/repo/.config/morphir`.

`morphir-workspace.json` is an additional root-layout primary. It uses canonical configuration keys and participates below the ordinary JSON/JSON5 primary in the JSON layer. It can coexist with `morphir.json` or `morphir.json5`.

### Format order and ambiguity

Within one directory, merge primary files from lowest to highest precedence:

1. JSON/JSON5, with `morphir-workspace.json` first.
2. YAML/YML.
3. TOML.

Keep keys supplied only by a lower layer. Use the same format order for member primaries, eligible user overrides, and active profile files.

Two primary candidates in the same format group are ambiguous, even across different layouts. Report every conflicting path instead of choosing a winner.

| Files at one project location | Result |
| --- | --- |
| `morphir.json`, `morphir.yaml`, `morphir.toml` | Merge all three; TOML wins overlapping keys |
| `morphir.yaml`, `morphir.yml` | Ambiguous YAML group |
| `morphir.json`, `morphir.json5` | Ambiguous JSON group |
| `morphir.toml`, `.morphir/morphir.toml` | Ambiguous TOML group |
| `morphir-workspace.json`, `morphir.json` | Merge both; `morphir.json` wins overlapping keys |

YAML beside TOML produces `config.yaml_beside_toml`. JSON values that survive below TOML produce `config.json_keys_below_toml`. These are warnings, not ambiguity errors. Suppress them with `--suppress-warning <code>`, `MORPHIR_DIAGNOSTICS__SUPPRESS`, or system/global `diagnostics.suppress`. Project-supplied settings cannot suppress these warnings.

## Finding a workspace or project

1. Start in the invocation directory, or the containing directory of an input file.
2. Discover, parse, and merge the primary format layers at that directory.
3. Remember the nearest primary location, but continue upward if it does not declare a `workspace` section.
4. Select the first directory whose merged primary documents contain a `workspace` section. Stop searching above it.
5. If no declared workspace exists before the discovery boundary, use the nearest project primary location.

A discovery boundary limits the upward walk. Mounted browser filesystems and explicit host boundaries must not be crossed. With no primary location, use the host's starting location and applicable defaults. A bare `.git` or `.morphir` directory does not declare a workspace under the target rules.

Starting in `/repo/app/src`, a project at `/repo/app/morphir.json` must still find a workspace at `/repo/morphir.toml`. If `/repo/app/morphir.toml` itself declares a workspace, that nearer workspace wins and `/repo/morphir.toml` is not read. Configuration from every ancestor is not merged into the result.

### Members and project selection

Use `workspace.members` to discover member directories, then apply `workspace.exclude`. Resolve membership paths from the workspace root, including `workspace.root` when configured. Each member uses the same configuration layouts and format rules as the root.

A root with only a `workspace` section is a virtual workspace. It does not become its own member through `*` or `**`. A root that also declares `project` is a root project.

For project selection, an explicit path or unique name takes priority. Otherwise, select the deepest member containing the invocation directory, then `workspace.default_member`, then the workspace's projects. An edit that would target multiple projects must request a single selector.

`workspace.out_dir` defaults to `.morphir/out` relative to the workspace root. A member cannot relocate that root. `workspace.output_dir` is a removed alias; `workspace.projects` and camel-case `outputDir` are not the canonical field names.

## Configuration precedence

Apply sources from lowest to highest precedence:

1. Built-in defaults.
2. System configuration.
3. Global user configuration.
4. Workspace or standalone-project primaries, in format order.
5. An explicitly selected configuration file.
6. Selected member primaries, in format order.
7. Active profile files.
8. Adjacent user overrides, workspace before member, each in format order.
9. `MORPHIR_` environment values.
10. Explicit command-line setting values.

Selecting an already discovered primary through `--config` moves its ordinary values to the explicit-file layer; it is not applied twice. It does not replace all other discovered settings.

Maps merge recursively. Arrays replace the entire earlier array, including when the replacement is empty. Scalars and incompatible value shapes replace earlier values. Secret references are indivisible values. Preserve the winning source for each leaf and array, together with source reports for inspection and diagnostics.

System/global files provide tooling defaults and exclude the entire `project` and `workspace` subtrees. A selected member receives applicable tooling settings without inheriting the root's `project` or `workspace` document subtrees. The owning workspace context supplies topology and its output root separately. Profile files cannot change workspace roots or member selection.

### Machine configuration locations

System configuration uses `/etc/morphir` on Unix-like systems and `%PROGRAMDATA%\morphir` on Windows, with `C:\ProgramData\morphir` as the Windows fallback. Files use the `morphir` stem and supported extensions.

Global user configuration uses the `morphir` stem under:

- Linux: an absolute `$XDG_CONFIG_HOME/morphir`, otherwise `$HOME/.config/morphir`.
- macOS: an absolute `$XDG_CONFIG_HOME/morphir`, otherwise `$HOME/Library/Application Support/morphir`.
- Windows: the user's Roaming AppData directory under `morphir`.
- The alternate Morphir home, `$HOME/.morphir` or `%USERPROFILE%\.morphir`. A nonempty `MORPHIR_HOME` replaces this home location without replacing the platform config directory.

Apply format layering at these sources as well. Multiple alternatives within one format group are ambiguous, including global candidates split across the platform and Morphir home locations.

### Environment values

Double underscores separate key segments; single underscores stay in the field name. For example, `MORPHIR_IR__FORMAT_VERSION=4` sets `ir.format_version`, while `MORPHIR_IR_FORMAT_VERSION` identifies a different, single-segment key.

Interpret booleans, integers, and JSON arrays/objects as typed values; otherwise retain strings. Operational variables such as `MORPHIR_HOME` and the profile selector `MORPHIR_PROFILES` are not ordinary configuration keys.

### Profiles and personal overrides

Select profiles from explicit `--profiles` or `--no-profiles`, then a present `MORPHIR_PROFILES`, then base `profiles.active`, then base `profiles.default`. No implicit profile is active. A present empty profile environment selector disables profiles.

Active files use `morphir.profile.<name>.<ext>` or the corresponding hidden/dot-config stem. For each active name, apply workspace files before member files. Later active names win. Includes and groups follow MoonBit's profile rules; member files and user overrides cannot set activation controls. Inactive files are not read.

Personal overrides are adjacent to their eligible primaries and use the matching stem in the layout table. Add applicable `*.user.*` paths, `.morphir/cache/`, and `.morphir/out/` to the repository's ignore policy. Discovery and editing do not change ignore files automatically.

The existing `.morphir/morphir.ci.toml` and `CiProfileMode` behavior belong to the current loader. The new design uses named profiles rather than implicitly treating CI detection as activation. Migration of that API remains an implementation task.

## Equivalent project files

These documents express the same canonical settings. They may be used separately; if they coexist, format order applies.

```toml
[project]
name = "Acme.Orders"
source_directory = "src"
exposed_modules = ["Orders.Api"]

[ir]
format_version = 4
```

```yaml
project:
  name: Acme.Orders
  source_directory: src
  exposed_modules: [Orders.Api]
ir:
  format_version: 4
```

```json
{
  "project": {
    "name": "Acme.Orders",
    "source_directory": "src",
    "exposed_modules": ["Orders.Api"]
  },
  "ir": { "format_version": 4 }
}
```

Legacy morphir-elm JSON fields map to canonical keys: `name` to `project.name`, `sourceDirectory` to `project.source_directory`, and `exposedModules` to `project.exposed_modules`. Reject a file that sets both spellings for the same field. Legacy manifests also select the Elm frontend and checkpoint defaults. Unsupported legacy fields produce diagnostics rather than silently claiming support.

## Migrating a classic project

Compatibility loading and migration are separate features. The planned [project migration command]({{< relref "/docs/cli/project" >}}) adopts one classic project by converting `morphir.json` into canonical TOML through shared library services. Its path can identify a project directory or its manifest. It migrates the project system, not Morphir IR or its version.

```bash
morphir project migrate morphir.json --dry-run
morphir project migrate morphir.json
```

The default output is adjacent `morphir.toml`. Map legacy project fields to `project.*`, preserve supported extra settings and project-relative paths, and explicitly write legacy-derived defaults needed to retain Elm frontend and checkpoint behavior when the JSON source is no longer present. An optional output can choose another supported primary layout within the same project location.

Dry-run reports mappings, edits, conflicts, and equivalence checks without writing. Existing matching TOML values are unchanged; missing nonconflicting values can be added while preserving comments and unrelated settings. Conflicting values or fields that cannot be mapped without loss block migration and identify the affected keys. In particular, do not silently drop legacy dependency or decoration settings just because compatibility loading does not consume them.

Verify the candidate without relying on the classic source. Compare project-scoped values and resolved path meaning, excluding ambient machine, personal, environment, CLI, and activated-profile overlays that could hide a mismatch. Retain declared workspace context for a member project. Report the expected change in provenance from JSON to TOML.

Applying a verified plan writes TOML atomically, detects concurrent source/target edits, and leaves `morphir.json` unchanged. Repeating the migration is a no-op once its values are present. Keeping the JSON file allows older consumers to continue using it; remove it separately only after checking those consumers. See the [migration walkthrough]({{< relref "/docs/getting-started/configuration#migrate-the-classic-project" >}}) and [command options]({{< relref "/docs/cli/project#migration-behavior" >}}).

For several projects, use the planned [workspace migration command]({{< relref "/docs/cli/workspace" >}}). It inventories only the selected directory, respects exclusions and nested workspace boundaries, reuses each project migration plan, and proposes workspace membership. It works before a workspace exists and preserves already migrated members.

The workspace plan reports intended topology, member/default selection, and shared-output-root changes. Standalone member output defaults can become `<workspace>/.morphir/out`; no generated files are moved. Verify each classic-to-TOML conversion in the same proposed workspace context, preserving source-path meaning and other project settings, so a declared adoption effect is not confused with data loss. Verify and stage the complete batch before destination replacement. Unresolved recovery is reported with retained recovery data; multi-file application does not promise crash atomicity.

## Inspecting and editing

The planned [config command]({{< relref "/docs/cli/config" >}}) exposes effective values, source paths, contributing layers, and scoped edits. Reads default to effective configuration. Writes target one file and report whether another layer still overrides the written value.

TOML is the written format. Preserve existing TOML comments and formatting. When a target has only YAML/JSON primaries, create its corresponding TOML primary and leave the lower-format files unchanged. Write complete replacement values for arrays and objects being set. Removing a TOML setting may reveal a lower-format or inherited value; it does not delete that value from other sources.

Validate edits before writing, install changes atomically, and reject concurrent changes. Inspection redacts sensitive values and does not resolve secret references or execute configuration code.

## Sharing configuration between hosts

The planned architecture has three reusable parts:

- Core models and pure rules for typed documents, merging, validation, profiles, membership, selection, provenance, queries, edit/migration planning, and equivalence checks.
- Shared discovery/loading services with file, directory, environment, and write access supplied by host adapters. Native hosts can use filesystem adapters; other hosts can supply mounted or in-memory documents.
- A `Microsoft.Extensions.Configuration` adapter exposing the resolved result through the proposed `AddMorphirConfiguration(...)` integration.

The CLI translates arguments into requests and formats results and exit codes. Other hosts use the same library services without referencing the CLI or constructing a command-line application. Editing is an explicit library operation, not a write through `IConfiguration`.

The typed Morphir document remains authoritative. Standard providers flatten values into string keys and do not by themselves enforce whole-array replacement, case-sensitive document keys, or per-value provenance. Preserve those semantics before projecting settings for .NET consumers. Native AOT support requires explicit or source-generated binding.

### Current library API

Until the new APIs are implemented, `Morphir.Tooling.Configuration.ConfigResolver.ResolveConfigAsync` loads the existing cache-only model in `Morphir.Configuration`. `ConfigResolution` exposes `Effective`, `Layers`, `WorkspaceRoot`, and `CiProfileApplied`. `Merge.mergeConfigs` merges cache paths, and `CiDetection` accepts injected environment values. These APIs do not yet provide the target project system, editing, or Microsoft configuration adapter.

See [troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration-planned" >}}) for wrong roots, ambiguous files, and settings that remain overridden.
