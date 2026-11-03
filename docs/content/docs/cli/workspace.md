---
title: "Workspace migration"
linkTitle: "Workspace"
weight: 50
description: "Planned bounded adoption of classic projects into a Morphir workspace"
---

# Workspace migration

Status: planned. `morphir workspace migrate` is not yet implemented in .NET. It will coordinate the shared [project migrator]({{< relref "/docs/cli/project" >}}) and workspace membership planning. Other hosts will use the same library services.

Use it when a repository contains several classic projects, including when the repository does not yet declare a Morphir workspace. For one project without workspace membership changes, use `morphir project migrate`.

## Synopsis

```text
morphir workspace migrate [<directory>] [--include <pattern>] [--exclude <pattern>] [--dry-run] [--json]
```

The supplied directory, or the current directory when omitted, bounds the inventory and resulting edits. The command does not walk ancestors to find more projects to adopt.

```bash
morphir workspace migrate ./repo --dry-run
morphir workspace migrate ./repo --include 'packages/*' --exclude 'packages/experimental-*' --dry-run --json
morphir workspace migrate ./repo
```

## Options

| Option | Purpose |
| --- | --- |
| `<directory>` | Root of the bounded inventory; defaults to the current directory |
| `--include <pattern>` | Refine candidates using a root-relative pattern; repeatable |
| `--exclude <pattern>` | Exclude candidate paths using a root-relative pattern; repeatable |
| `--dry-run` | Preview the complete workspace and project plan without writing |
| `--json` | Emit a structured plan/result and diagnostics |

Inventory stays within the selected root, skips discovered links, stops at nested declared workspaces, and excludes version-control, dependency, and build-output directories. Existing workspace exclusions remain effective.

## Membership and project plans

The preview lists candidates, exclusions, nested boundaries, already migrated members, project edits, workspace edits, blockers, and verification results in deterministic path order.

Propose explicit root-relative member paths while preserving existing membership, settings, and a valid default member. Already migrated members remain members without being rewritten. A root project retains its identity; a root without a project is a virtual workspace. Duplicate project names are diagnosed without merging their distinct paths.

Reuse the single-project migration contract for every selected classic project. One blocked project blocks the batch. Verify each classic-to-TOML conversion in the same proposed workspace context, excluding the converted classic source from the candidate and ambient overlays from both sides. Preserve source-path meaning and project settings independently of serialization.

The plan separately reports intended workspace adoption effects: the new topology, member/default selection, and shared output root. For example, standalone defaults under each member's `.morphir/out` can become `<workspace>/.morphir/out`. This declared adoption effect is not a conversion mismatch. Owning workspace context supplies topology and the output root; members do not inherit the root's entire `project` or `workspace` document subtrees. No existing generated files are moved by migration.

Check the resulting workspace as a whole against those declared effects. Do not relax loss checks or rely on classic manifests or ambient overrides to hide an unexplained setting change.

## Application and recovery

Validate and stage the entire plan before replacing any destination. Recheck relevant source and target preconditions, coordinate overlapping writers, and journal replacements so a commit failure can attempt rollback.

Multiple file replacements are not crash-atomic. Report restored and unresolved paths explicitly if rollback fails. Retain recovery data and block overlapping writes until recovery is resolved. Successful application and complete rollback clean their temporary artifacts. Every classic `morphir.json` remains unchanged.

An equivalent repeat reports unchanged files without rewriting them. A valid existing workspace needing no further conversion is a successful no-op. An undeclared root with no eligible projects reports that no adoption can be planned.

## Results and exit codes

Stdout contains results only; diagnostics go to stderr. JSON results include structured diagnostics and per-project outcomes, with sensitive values redacted.

| Code | Meaning |
| --- | --- |
| `0` | Verified success, applicable preview, or unchanged existing workspace |
| `2` | Invalid or blocked plan, failed verification/application, or recovery required |

## Related documentation

- [Single-project migration]({{< relref "/docs/cli/project" >}})
- [Workspace walkthrough]({{< relref "/docs/getting-started/configuration" >}})
- [Configuration guide]({{< relref "/guides/configuration" >}})
- [Layered configuration delivery plan]({{< relref "/docs/contributing/design/prds/layered-configuration#delivery-campaigns" >}})
