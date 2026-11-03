---
title: "Configuration"
linkTitle: "Configuration"
weight: 40
description: "Morphir configuration formats, discovery, precedence, and shared host APIs"
---

# Configuration

Morphir configuration describes projects, workspace membership, and tooling settings. Shared libraries provide typed documents, source resolution, and project discovery to native and embedded hosts. Shared queries, CLI inspection and an opt-in Microsoft configuration adapter are available. Shared TOML editing and classic project migration are available; [bounded workspace migration]({{< relref "/docs/cli/workspace-migrate" >}}) is available.

## Implementation status

The shared configuration libraries read TOML, YAML, JSON/JSON5, normalize classic manifests, resolve typed settings, and retain origins and diagnostics. Named profiles use explicit activation. Campaign 1 landed in [PR #461](https://github.com/finos/morphir-dotnet/pull/461).

Campaign 2 landed bounded document-aware discovery in [PR #467](https://github.com/finos/morphir-dotnet/pull/467).
Campaign 3 landed [shared queries]({{< relref "/docs/configuration/inspection" >}}),
`morphir config get/list/show/path/explain` and the
[Microsoft adapter]({{< relref "/docs/configuration/microsoft" >}}).
See the [inspection journal]({{< relref "/docs/contributing/design/prds/config-inspection-delivery" >}}) for its verified validation and landing/cleanup receipt.
Campaign 4 adds [shared editing]({{< relref "/docs/configuration/editing" >}}) and `morphir config set/unset`, including dry-run, typed inputs, effective explanations, optimistic conflicts and recovery. Its [delivery journal]({{< relref "/docs/contributing/design/prds/config-editing-delivery" >}}) records local qualification and parent delivery gates. [Classic project migration]({{< relref "/docs/cli/project-migrate" >}}) is available; [bounded workspace migration]({{< relref "/docs/cli/workspace-migrate" >}}) is available.

Existing cache-only tooling retains Git-first discovery and its separate CI overlay. It does not opt into the new policy automatically. Follow the [layered configuration PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}}), the [walkthrough]({{< relref "/docs/getting-started/configuration" >}}), and the [CLI reference]({{< relref "/docs/cli/config" >}}) for their respective status.

### Legacy cache configuration

For the existing resolver, place cache overrides in `.morphir/morphir.toml` at the discovered workspace root:

```toml
[morphir.cache]
workspace = "/custom/workspace/cache"
global = "/custom/global/cache"
```

The parser also accepts these keys under `[cache]`. An adjacent `.morphir/morphir.user.toml` overrides the base file. `.morphir/morphir.ci.toml` applies after the user file when `CiProfileMode` is `On`, or when it is `Auto` and CI is detected; `Off` disables it. Without a workspace, resolution uses global configuration and model defaults only.

The existing global file is `morphir.toml` under `%APPDATA%\Morphir` on Windows, `$XDG_CONFIG_HOME/morphir` or `$HOME/.config/morphir` on Linux, and `$HOME/Library/Application Support/morphir` on macOS. These paths belong to the compatibility API. The shared project system uses the machine-location rules below.

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

Each stem accepts `.json`, `.json5`, `.yaml`, `.yml`, or `.toml`. Both JSON extensions accept JSON5 syntax, including comments, unquoted keys, single-quoted strings, and trailing commas. Duplicate JSON/JSON5 keys are rejected.

YAML follows the pinned MoonBit Moon-Lander Default profile, including nulls, expanded anchors/aliases, empty streams, and custom scalar tags. Raw documents retain nulls for profile-control validation; ordinary null object fields do not erase lower values, and null array elements remain values. The adapters use CsToml 1.8.6 and YamlDotNet 16.3.0. See the [configuration API reference]({{< relref "/docs/configuration/resolution" >}}) and the library's `src/Morphir.Configuration.IO/README.md` for the pinned compatibility evidence.

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

A discovery boundary includes its own directory and limits the upward walk, file reads, and member enumeration. Mounted filesystems and explicit host boundaries must not be crossed, including through `..` segments or discovered links. With no primary location, use the host's starting location and applicable defaults. A bare `.git` or `.morphir` directory does not declare a workspace through the new API.

Starting in `/repo/app/src`, a project at `/repo/app/morphir.json` must still find a workspace at `/repo/morphir.toml`. If `/repo/app/morphir.toml` itself declares a workspace, that nearer workspace wins and `/repo/morphir.toml` is not read. Configuration from every ancestor is not merged into the result.

### Members and project selection

Use `workspace.members` to discover member directories, then apply `workspace.exclude`. Resolve `workspace.root` relative to the declaration location and keep it within the supplied boundary. Resolve member and exclusion paths from that effective workspace root. Glob syntax uses `*` and `?` within one path segment and `**` across segments. Each member uses the same configuration layouts and format rules as the root.

Traversal prunes paths that cannot match a member pattern and branches fully excluded by the patterns before requesting directory evidence. Reserved `.morphir` and `.config` directories do not become glob members, including when they appear in a glob's starting path.

Membership is an inventory operation, separate from project selection. Member paths are canonicalized and ordered deterministically. Exclusions remove matching members before selection. Missing literal members produce warnings. Existing literal directories without a primary and members with invalid documents remain identifiable through structured diagnostics; an ambiguous member configuration is an error. Hidden configuration directories do not become projects.

A root with only a `workspace` section is a virtual workspace. It does not become its own member through `*` or `**`. A root that also declares `project` is a root project.

For project selection, resolve a relative explicit path from the effective workspace root and match it before trying a project name. A duplicate name produces a diagnostic, but each canonical path remains selectable. An unknown or ambiguous explicit selector is an error. Without an explicit selector, select the deepest member containing the invocation directory, then `workspace.default_member`, then the workspace's projects. Defaults use the same path-before-name matching. An invalid default is an error. A future edit operation that would target multiple projects must request a single selector.

`workspace.out_dir` defaults to `.morphir/out` relative to the workspace root. A member cannot relocate that root; attempted member output overrides produce diagnostics. The owning workspace is returned separately from the member document. Its `Root` is the normalized effective `workspace.root`, and `OutputDirectory` is the normalized absolute output path. Root `project` and `workspace` subtrees do not become member settings. `workspace.output_dir` is a removed alias; `workspace.projects` and camel-case `outputDir` are not the canonical field names.

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

Selecting an already discovered primary with `ProjectLoadOptions.SelectedPath`, or the `--config` option, moves its ordinary values to the explicit-file layer; it is not applied twice. It does not replace all other discovered settings. A relative selected configuration path resolves from the invocation directory, or the containing directory when discovery starts with an input file. Relative project selectors instead resolve from the effective workspace root.

An explicitly selected eligible personal file keeps its workspace or member ownership restrictions, including in isolated loading. Selecting a member's personal file cannot relocate workspace output. Selecting a workspace personal file does not pass its project identity into a member, while its permitted workspace output setting still supplies the shared owning context.

Maps merge recursively. Arrays replace the entire earlier array, including when the replacement is empty. Scalars and incompatible value shapes replace earlier values. Secret references are indivisible values. Preserve the winning source for each leaf and array, together with source reports for inspection and diagnostics.

System/global files provide tooling defaults and exclude the entire `project` and `workspace` subtrees. Their contents cannot declare the root or workspace membership. A selected member receives applicable tooling settings without inheriting the root's `project` or `workspace` document subtrees. The owning workspace context supplies topology and its output root separately. Profile files cannot change workspace roots or member selection.

### Machine configuration locations

System configuration uses `/etc/morphir` on Unix-like systems and `%PROGRAMDATA%\morphir` on Windows, with `C:\ProgramData\morphir` as the Windows fallback. Files use the `morphir` stem and supported extensions.

Global user configuration uses the `morphir` stem under:

- Linux: an absolute `$XDG_CONFIG_HOME/morphir`, otherwise `$HOME/.config/morphir`.
- macOS: an absolute `$XDG_CONFIG_HOME/morphir`, otherwise `$HOME/Library/Application Support/morphir`.
- Windows: the supplied Roaming AppData directory under `morphir`, falling back to `<home>/AppData/Roaming/morphir`.
- The alternate Morphir home, `$HOME/.morphir` or `%USERPROFILE%\.morphir`. A nonempty `MORPHIR_HOME` replaces this home location without replacing the platform config directory.

Apply format layering at these sources as well. Multiple alternatives within one format group are ambiguous, including global candidates split across the platform and Morphir home locations. A TOML file in the platform directory and a YAML file in Morphir home layer normally. An empty `MORPHIR_HOME` retains the default home alternative. Relative `XDG_CONFIG_HOME` is ignored. The host supplies `MachineLocationInput` and its platform directories/environment explicitly. A relative nonempty `MORPHIR_HOME` resolves from the invocation directory. Machine loading also requires a separately authorized `MachineTree`; neither discovery nor loading implicitly reads the process environment or ambient filesystem. Embedded hosts supply only the mounts they permit.

### Environment values

Double underscores separate key segments; single underscores stay in the field name. For example, `MORPHIR_IR__FORMAT_VERSION=4` sets `ir.format_version`, while `MORPHIR_IR_FORMAT_VERSION` identifies a different, single-segment key.

Interpret booleans, integers, and JSON arrays/objects as typed values; otherwise retain strings. Operational variables such as `MORPHIR_HOME` and the profile selector `MORPHIR_PROFILES` are not ordinary configuration keys.

### Profiles and personal overrides

Select profiles from explicit `--profiles` or `--no-profiles`, then a present `MORPHIR_PROFILES`, then base `profiles.active`, then base `profiles.default`. No implicit profile is active. A present empty profile environment selector disables profiles.

Active files use `morphir.profile.<name>.<ext>` or the corresponding hidden/dot-config stem. For each active name, apply workspace files before member files. Later active names win. Includes and groups follow MoonBit's profile rules; member files and user overrides cannot set activation controls. Inactive files are not read.

Personal overrides are adjacent to their eligible primaries and use the matching stem in the layout table. A dot-config primary permits `config.user.<ext>` and `config.profile.<name>.<ext>` in the same directory, rather than root-level `morphir.user.<ext>`. Apply the same format groups and ambiguity checks to eligible overrides and each active profile. Profiles and personal files do not identify roots. Workspace personal overrides precede member personal overrides. Add applicable `*.user.*` paths, `.morphir/cache/`, and `.morphir/out/` to the repository's ignore policy. Discovery and editing do not change ignore files automatically.

Isolated loading omits system/global sources and personal override files before reading them. It retains declared primaries, an explicitly selected configuration file, eligible active profiles, and supplied environment/explicit values. Only requested active profile files are read.

The existing `.morphir/morphir.ci.toml` and `CiProfileMode` behavior belong to the compatibility loader. The shared loader uses named profiles; `CI=true` does not activate one. Existing callers keep their behavior until they explicitly migrate to the shared APIs.

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

Compatibility loading and migration are separate features. The [project migration command]({{< relref "/docs/cli/project" >}}) adopts one classic project by converting `morphir.json` into canonical TOML through shared library services. Its path can identify a project directory or its manifest. It migrates the project system, not Morphir IR or its version.

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

The [config read commands]({{< relref "/docs/cli/config" >}}) expose effective values, source paths, contributing layers and source-scoped inspection. Reads default to effective configuration. Arrays remain whole values and quoted path segments address literal dots. Stdout carries results; diagnostics go to stderr. JSON reports contain structured diagnostics and conform to the published schema.

`config set/unset` target one TOML file and report the effective value and origin, including higher overrides. Use `--dry-run` to preview without writes. See the [shared editing API]({{< relref "/docs/configuration/editing" >}}) and [CLI reference]({{< relref "/docs/cli/config" >}}).

TOML is the written format. Preserve existing TOML comments and formatting. When a target has only YAML/JSON primaries, create its corresponding TOML primary and leave the lower-format files unchanged. Write complete replacement values for arrays and objects being set. Removing a TOML setting may reveal a lower-format or inherited value; it does not delete that value from other sources.

Validate edits before writing, install changes atomically, and reject concurrent changes. Inspection redacts sensitive values and does not resolve secret references or execute configuration code.

## Sharing configuration between hosts

The architecture separates these responsibilities:

- `Morphir.Configuration` contains immutable models and pure rules for typed documents, merging, validation, profiles, membership, selection, and provenance. Pure inspection, edit planning, migration planning and independent equivalence services are available.
- `Morphir.Configuration.IO` parses documents and supplies discovery/loading adapters. Native hosts use filesystem access; other hosts supply mounted or in-memory trees. Reusable TOML editing, project migration coordination and preconditioned transaction services are available.
- `Morphir.Configuration.Microsoft` exposes one resolved result through opt-in `AddMorphirConfiguration(...)`. Its [consumer example]({{< relref "/docs/configuration/microsoft" >}}) uses generated or explicit binding without a CLI dependency.

The CLI translates arguments into requests and formats results and exit codes. Other hosts use the same library services without referencing the CLI or constructing a command-line application. Editing is an explicit library operation, not a write through `IConfiguration`.

The typed Morphir document remains authoritative. Standard providers flatten values into string keys and do not by themselves enforce whole-array replacement, case-sensitive document keys, or per-value provenance. Preserve those semantics before projecting settings for .NET consumers. Native AOT support requires explicit or source-generated binding.

### Discovery library API

`ProjectSystem.Discover` in `Morphir.Configuration.IO` accepts an `IProjectTree`, an absolute start path, and an optional project selector. The result keeps `Discovery` and `Selection` separate. Discovery includes the root location, owning workspace context, complete member inventory, source reports, and diagnostics. Selection identifies the applicable projects and any selector errors.

```csharp
using Morphir.Configuration.IO;

var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
{
    ["/repo/morphir.toml"] = "[workspace]\nmembers = ['app']",
    ["/repo/app/morphir.json"] = "{name:'App'}",
    ["/repo/app/src/input.elm"] = "module Input exposing (..)"
});
var result = ProjectSystem.Discover(tree, "/repo/app/src/input.elm");
// result.Discovery.Location is /repo.
// result.Selection.Projects identifies /repo/app.
```

A native host supplies `new FileSystemProjectTree(boundary)` instead. A standalone project uses the same entry point and has no owning workspace. Project selection does not copy ancestor project identity into a member.

`DiscoveryPolicy.evaluate` is the pure F# boundary. It accepts a `DiscoveryRequest` and `ProjectSnapshot`, then returns either `DiscoveryStep.Requests` for additional evidence or `DiscoveryStep.Complete`. `DiscoveryPolicy.select` can select again from the resulting inventory without filesystem access. No CLI or `System.CommandLine` dependency is required.

Root and member inventories carry `RootPrimary` and `MemberPrimary` source roles, including embedded documents and reports. A host can supply root and member `SourceInput` values directly to `Resolver.resolve` with `ProjectScope.Member` and the discovered owning workspace; it does not need to repair their identities first.

### Loading one root or member

`ProjectSystem.Load(IProjectTree tree, ProjectDiscovery discovery, ProjectInfo? project = null, ProjectLoadOptions? options = null)` returns a `DocumentResolution` with effective values, winning origins, ordered source reports, active profiles, owning workspace context, and diagnostics. Omit `project` to resolve the discovered root. Pass one selected `ProjectInfo` to resolve a member. Load each selected project independently so project identities and member settings remain separate.

```csharp
var project = result.Selection.Projects.Single();
var resolution = ProjectSystem.Load(tree, result.Discovery, project,
    new ProjectLoadOptions { Isolated = true });
```

`ProjectLoadOptions` exposes these host controls:

| Property | Meaning |
| --- | --- |
| `Input` | Supplied `ResolutionInput` for sources, environment values, explicit values, profile selection, and warning suppression; defaults to `DocumentDefaults.resolutionInput` |
| `Isolated` | Omit machine and personal files before reading; retain primaries, selected configuration, active profiles, and supplied values |
| `SelectedPath` | Optional required configuration file, resolved from the invocation directory when relative |
| `Machine` | Optional explicit `MachineLocationInput` for platform candidates |
| `MachineTree` | Separate `IProjectTree` authorizing machine-file access; required when non-isolated machine loading is requested |

Requesting non-isolated machine loading with `Machine` but no `MachineTree` produces `discovery.machine_host_required`. A host can omit both to use only the project tree and supplied values. Each tree enforces its own boundary, and isolation avoids machine-tree reads entirely. Invalid or unrelated `ProjectInfo` records are diagnosed rather than treated as healthy selected members.

### Host boundary and evidence obligations

An `IProjectTree` exposes its `Boundary` and `Observe(path)`. Use canonical absolute paths with `/` separators in shared evidence, including Windows drive paths. A mounted host must return only permitted directory children and must reject paths outside its mount. It must distinguish missing entries, directories, parsed files, links, and access failures. A failure to read a candidate is an error, not evidence that it was absent.

Never follow a discovered symbolic link to read configuration or enumerate members. Native adapters must check linked ancestors as well as the final candidate; use a physical boundary path such as `/private/tmp/...` on macOS. Lexical prefix checks alone cannot enforce a filesystem boundary. A host that supplies snapshot entries directly has the same obligation. Keep the tree stable during one discovery operation, or repeat discovery after a change.

Source reports distinguish loaded, missing, skipped, and failed inputs. Optional missing candidates remain visible without becoming required-file errors. Invalid documents and inaccessible candidates retain their paths and diagnostics. Inactive profiles and isolated machine/personal inputs are not read. Equivalent native and mounted evidence must produce the same roots, member selection, source ordering, and diagnostics.

### Legacy cache API

`Morphir.Tooling.Configuration.ConfigResolver.ResolveConfigAsync` continues to load the cache-only `MorphirConfig`. `ConfigResolution` exposes `Effective`, `Layers`, `WorkspaceRoot`, and `CiProfileApplied`; `Merge.mergeConfigs` merges cache paths. `WorkspaceDiscovery`, `ManagementScenario`, and existing artifact callers retain Git-first root selection. New consumers opt into `ProjectSystem` explicitly.

See [troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration" >}}) for wrong roots, ambiguous files, and settings that remain overridden.

The discovery result records primary topology. Loading recalculates the owning
output directory from permitted workspace overlays, including active profiles
and workspace personal files. Root and member loads therefore share the same
output context. Member profiles and personal files cannot relocate it. Invalid
workspace output overlays remain diagnostics even when loading a member.


Machine discovery uses separate authorized filesystem hosts. The native CLI
plans one host per machine drive or UNC share, so a project on `D:` can read
system settings from `C:/ProgramData` and redirected roaming settings. Embedded
hosts can provide `ProjectLoadOptions.MachineTrees` for multiple mounted roots,
or retain `MachineTree` for one root. `ProjectSystem.MachineBoundaries` plans
those roots without reading files. The project discovery boundary still applies
only to project files, and isolation omits all machine reads.

## Classic project adoption

Use [project migrate]({{< relref "/docs/cli/project-migrate" >}}) for one classic manifest. The [shared migration APIs]({{< relref "/docs/configuration/project-migration" >}}) support read-only hosts and guarded application without the CLI. The [workspace API]({{< relref "/docs/configuration/workspace-migration" >}}) composes the same conversion checks with bounded inventory, aggregate verification and explicit recovery.
