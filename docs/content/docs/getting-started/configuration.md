---
title: "Configure a workspace"
linkTitle: "Configuration"
weight: 40
description: "Workspace discovery, inspection, safe TOML editing and .NET consumption"
---

# Configure a workspace

The shared libraries read configuration and expose bounded workspace/member discovery, source loading and pure inspection. The `morphir config get/list/show/path/explain` commands expose the same queries. `config set/unset` plan and apply one TOML edit. `project migrate` adopts one classic project through loss-checked shared services. `workspace migrate` adopts a bounded repository through aggregate verification and recoverable application. See the [workspace delivery journal]({{< relref "/docs/contributing/design/prds/workspace-migration-delivery" >}}) for local validation and parent delivery gates.

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

From the repository root, these commands inspect settings and discovery:

```bash
morphir config get ir.format_version --show-origin
morphir config path
morphir config explain ui.theme
```

The format version comes from the root TOML file. `path` reports consulted sources and their status; `explain` identifies every contributing layer for the theme and the winner.

For a member-only value, select its canonical workspace path:

```bash
morphir config get project.name --project packages/orders --json
morphir config get ui.theme --workspace --show-origin --show-scope
morphir config list --isolated --json
```

A scoped query does not fill absent settings from other scopes. `--file path`
inspects only that file; `--config path` adds a selected-file layer during effective
resolution. Empty strings and arrays succeed; a missing key exits 1. Usage or
load failures exit 2. See the [query API]({{< relref "/docs/configuration/inspection" >}})
and [Microsoft host example]({{< relref "/docs/configuration/microsoft" >}}) to use
these results without the CLI. Existing IR commands do not adopt all settings
merely because inspection can show them.

## Add and remove a personal override

Preview and apply a personal override from the same directory:

```bash
morphir config set ui.theme dark --user --dry-run --json
morphir config set ui.theme dark --user
morphir config get ui.theme --show-origin
morphir config unset ui.theme --user
morphir config get ui.theme --show-origin
```

The workspace declares `packages/orders` as its default member, so `--user` targets `packages/orders/morphir.user.toml`. Without a selected member, a virtual workspace root uses root-adjacent `morphir.user.toml`. Without a higher override, the first query returns `dark` from that file. After `unset`, it returns inherited `light` from the workspace primary. Removal reveals the inherited value rather than deleting the setting from every source.

Add `morphir.user.*`, applicable member override paths, and `.morphir/out/` to your ignore policy. The configuration system does not change ignore files automatically.

Preview reports the target, redacted before/after values and the effective winner without writing. If an environment variable or profile still wins, a successful file edit still exits 0. For example:

```bash
MORPHIR_UI__THEME=light morphir config set ui.theme dark --user --json
morphir config set ui.color false --type bool --workspace --dry-run
```

Repeat an identical set to receive `unchanged` without rewriting the file. A missing unset exits 1. Validation, access failures and conflicts exit 2. After a conflict, inspect the new source contents and replan. If a report names recovery artifacts, preserve its journal/backups and reconcile the target before retrying. The [shared editing API]({{< relref "/docs/configuration/editing" >}}) provides the same preview/apply boundary without the CLI.

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

The command is available through the CLI and shared migration APIs.

For this example, start with a classic project whose only primary is the JSON file above. If the earlier TOML example is also present, its different source directory is a conflict that migration must report before writing.

Preview the conversion, then apply it to that member:

```bash
morphir project migrate packages/orders/morphir.json --dry-run --json > migration-plan.json
morphir project migrate packages/orders/morphir.json
morphir project migrate packages/orders/morphir.json --json # unchanged repeat
```

The resulting `packages/orders/morphir.toml` includes the mapped fields and the defaults that previously came from recognizing a classic Elm manifest:

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

See the [migration command reference]({{< relref "/docs/cli/project-migrate" >}}) for output selection and structured dry-run results.

## Adopt a repository of classic projects

If the repository has several classic projects and no new workspace yet, preview the bounded workspace migration from its root:

```bash
morphir workspace migrate . --include 'packages/*' --exclude 'packages/experimental-*' --dry-run
morphir workspace migrate . --include 'packages/*' --exclude 'packages/experimental-*'
```

The plan lists projects, exclusions, nested workspace boundaries, already migrated members, TOML edits, proposed membership, and intended member/default-selection and shared-output-root changes. It checks each classic-to-TOML conversion in the same proposed workspace context and verifies the resulting workspace before applying the batch. One blocked selected project blocks the whole plan. Existing membership/settings and every classic manifest are preserved; an equivalent repeat makes no changes.

Adopting a shared workspace can change standalone output defaults to `<workspace>/.morphir/out`. The preview reports that change separately from serialization equivalence; it does not move previously generated files.

For two eligible projects under `packages/orders` and `packages/payments`, a new
virtual workspace contains explicit member paths:

```toml
[workspace]
members = ["packages/orders", "packages/payments"]
```

Each converted member receives its own TOML primary with the same loss checks as
single-project migration. If the repository root itself has a project, migration
keeps it as the root project rather than adding `"."` to the member list. Existing
membership patterns, exclusions, valid default selection, comments, and settings
remain in place. Preview the operation from the directory you intend to adopt;
invoking from a child deliberately limits the operation to that child.

Use JSON in automation and retain the result when application fails:

```bash
morphir workspace migrate . --dry-run --json > workspace-preview.json
morphir workspace migrate . --json > workspace-result.json
morphir workspace migrate . --json # unchanged repeat
```

Exit 2 means blocked, invalid, failed, or recovery required. A recovery-required
result identifies the transaction artifacts and unresolved paths. Stop overlapping
writes and use the shared explicit inspection/recovery APIs described in the
[recovery guide]({{< relref "/docs/configuration/workspace-migration" >}}).
Do not delete journals or backups to make a rerun pass.

Read the [workspace migration reference]({{< relref "/docs/cli/workspace" >}}) for batch recovery and supported controls.

## Inspect configuration in CI

Create `morphir.profile.ci.toml` beside the root primary:

```toml
[ui]
color = false
```

The shared loader accepts explicitly selected profiles. This command selects the profile and captures JSON output:

```bash
morphir config show --isolated --discovery-boundary . --profiles ci --json > effective-config.json
```

This excludes machine configuration and personal overrides while retaining the selected profile. Environment values still participate. Stdout contains JSON; diagnostics go to stderr. Merely setting `CI=true` does not activate a named profile through the shared loader. Legacy cache callers retain their separate `CiProfileMode` behavior.

Read the [configuration guide]({{< relref "/guides/configuration" >}}) for all layouts and precedence rules, the [command reference]({{< relref "/docs/cli/config" >}}) for options, and [troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration" >}}) for unexpected results.
