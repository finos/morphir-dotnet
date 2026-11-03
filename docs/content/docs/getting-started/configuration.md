---
title: "Configure a workspace"
linkTitle: "Configuration"
weight: 40
description: "Workspace discovery through shared libraries, with planned inspection and migration commands"
---

# Configure a workspace

The shared libraries read these files and expose bounded workspace/member discovery and source loading. Campaign 2 is implemented and locally qualified, with parent review/landing/cleanup pending. The `morphir config`, editing, and project/workspace migration commands shown later remain planned. Use the [PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}}) and [delivery journal]({{< relref "/docs/contributing/design/prds/project-workspace-discovery-delivery" >}}) to check implementation and validation status.

## Declare the workspace

Create `morphir.toml` at the repository root:

```toml
[workspace]
members = ["packages/*"]
exclude = ["packages/experimental-*"]
default_member = "packages/orders"
out_dir = ".morphir/out"

[ir]
format_version = 4

[ui]
theme = "light"
```

Create `packages/orders/morphir.toml` for the member project:

```toml
[project]
name = "Acme.Orders"
source_directory = "src"
exposed_modules = ["Orders.Api"]
```

Create its `src` directory. When invoked from `packages/orders/src`, discovery remembers the member and continues upward to the declared workspace. The root has no `project` section, so it is a virtual workspace rather than an extra project.

## Discover through a host library

For a native repository, supply an absolute boundary and start path:

```csharp
using Morphir.Configuration.IO;

var tree = new FileSystemProjectTree("/repo");
var result = ProjectSystem.Discover(tree, "/repo/packages/orders/src");
// Discovery.Location: /repo
// Selection.Projects: /repo/packages/orders
```

A mounted host can supply the same contents with `InMemoryProjectTree`. Both adapters use the same pure discovery and selection policy. The result retains the complete member inventory separately from the selected project. Pass `"packages/orders"` as the optional selector to choose that canonical member path, even if another member has the same display name.

The boundary includes `/repo` and excludes its parents. An input file starts discovery in its containing directory. Only the nearest declared workspace participates; discovery does not merge configuration from every ancestor. If no workspace is found, the nearest primary location is a standalone project. With no primary, the start directory remains the location. Neither `.git` nor an empty `.morphir` directory declares a workspace through this API.

Keep missing and invalid members visible in diagnostics. A nonexistent literal member produces a warning; a literal directory with no configuration or an invalid document requires attention. Excluded members are absent from the selected membership. Read source reports to distinguish an optional miss from a read failure.

Resolve that member independently through the shared loader:

```csharp
var selected = result.Selection.Projects.Single();
var effective = ProjectSystem.Load(tree, result.Discovery, selected,
    new ProjectLoadOptions { Isolated = true });
```

Isolation retains declared primaries and supplied values while omitting machine and personal files. For active profiles, supply the desired profile selection through `ProjectLoadOptions.Input`. To load the root instead, omit the `ProjectInfo` argument. The owning workspace context carries an absolute output directory separately from the member settings; the member does not inherit the root's `project` or `workspace` document subtrees.

Machine files require both explicit `MachineLocationInput` and a separately authorized `MachineTree`. No ambient machine access is implied by this example. A relative `SelectedPath` resolves from the invocation directory, while the project selector resolves from the effective workspace root.

Existing artifact-management commands still use their legacy Git-first `WorkspaceDiscovery`. They do not switch root policies when these libraries are installed.

## Inspect the effective values

From the repository root, these planned commands inspect settings and discovery:

```bash
morphir config get ir.format_version --show-origin
morphir config path
morphir config explain ui.theme
```

The format version comes from the root TOML file. `path` reports consulted sources and their status; `explain` identifies every contributing layer for the theme and the winner.

## Add and remove a personal override

These planned commands run from the same directory:

```bash
morphir config set ui.theme dark --user
morphir config get ui.theme --show-origin
morphir config unset ui.theme --user
morphir config get ui.theme --show-origin
```

The edit targets root-adjacent `morphir.user.toml`. Without a higher override, the first query returns `dark` from that file. After `unset`, it returns `light` from the workspace primary. Removal reveals the inherited value rather than deleting the setting from every source.

Add `morphir.user.*`, applicable member override paths, and `.morphir/out/` to your ignore policy. The configuration system does not change ignore files automatically.

## Retain an existing JSON project

A member may retain its morphir-elm `morphir.json`:

```json
{
  "name": "Acme.Orders",
  "sourceDirectory": "elm-src",
  "exposedModules": ["Orders.Api"]
}
```

If a TOML primary also sets `project.source_directory = "src"`, that value wins while the JSON-only fields survive. A scoped edit creates or updates TOML and leaves JSON unchanged. Removing that TOML key reveals `elm-src` again. A legacy field and its canonical spelling in the same JSON file are an error.

## Migrate the classic project

This command remains planned.

For this example, start with a classic project whose only primary is the JSON file above. If the earlier TOML example is also present, its different source directory is a conflict that migration must report before writing.

Preview the conversion, then apply it to that member:

```bash
morphir project migrate packages/orders/morphir.json --dry-run
morphir project migrate packages/orders/morphir.json
```

The planned `packages/orders/morphir.toml` includes the mapped fields and the defaults that previously came from recognizing a classic Elm manifest:

```toml
[project]
name = "Acme.Orders"
source_directory = "elm-src"
exposed_modules = ["Orders.Api"]

[frontend]
language = "elm"

[pipeline]
backend = "checkpoint"
checkpoint_format = "morphir-json"
```

Migration checks that effective project values and source-path meaning are unchanged when the classic source is excluded. The comparison excludes machine, personal, environment, CLI, and activated-profile overlays while retaining declared base workspace context. It does not pin a new IR version or copy those overrides into the project file. The result reports verification and any fields requiring manual decisions.

The source JSON stays unchanged so older morphir-elm consumers can continue using it. Repeating migration with the same inputs makes no changes. A conflicting TOML value, unsupported dependency/decorations shape, or any unmapped setting blocks application rather than silently dropping data. Remove the old manifest separately only after those consumers have moved to the new format.

See the [migration command reference]({{< relref "/docs/cli/project#migration-behavior" >}}) for output selection and structured dry-run results.

## Adopt a repository of classic projects

This command remains planned.

If the repository has several classic projects and no new workspace yet, preview the bounded workspace migration from its root:

```bash
morphir workspace migrate . --include 'packages/*' --exclude 'packages/experimental-*' --dry-run
morphir workspace migrate . --include 'packages/*' --exclude 'packages/experimental-*'
```

The plan lists projects, exclusions, nested workspace boundaries, already migrated members, TOML edits, proposed membership, and intended member/default-selection and shared-output-root changes. It checks each classic-to-TOML conversion in the same proposed workspace context and verifies the resulting workspace before applying the batch. One blocked selected project blocks the whole plan. Existing membership/settings and every classic manifest are preserved; an equivalent repeat makes no changes.

Adopting a shared workspace can change standalone output defaults to `<workspace>/.morphir/out`. The preview reports that change separately from serialization equivalence; it does not move previously generated files.

Read the [workspace migration reference]({{< relref "/docs/cli/workspace" >}}) for batch recovery and supported controls.

## Inspect configuration in CI

Create `morphir.profile.ci.toml` beside the root primary:

```toml
[ui]
color = false
```

The shared loader accepts explicitly selected profiles. The planned command will select the profile and capture JSON output:

```bash
morphir config show --isolated --discovery-boundary . --profiles ci --json > effective-config.json
```

This excludes machine configuration and personal overrides while retaining the selected profile. Environment values still participate. Stdout contains JSON; diagnostics go to stderr. Merely setting `CI=true` does not activate a named profile through the shared loader. Legacy cache callers retain their separate `CiProfileMode` behavior.

Read the [configuration guide]({{< relref "/guides/configuration" >}}) for all layouts and precedence rules, the [command reference]({{< relref "/docs/cli/config" >}}) for options, and [troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration-planned" >}}) for unexpected results.
