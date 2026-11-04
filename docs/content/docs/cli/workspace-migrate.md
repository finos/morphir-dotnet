---
title: "workspace migrate command"
linkTitle: "workspace migrate"
weight: 51
description: "Preview and apply bounded workspace adoption with per-project loss checks and recoverable writes"
---

# workspace migrate

`morphir workspace migrate` adopts eligible projects within one selected directory.
It uses the reusable inventory, aggregate planner/verifier, and transaction APIs.
The same operation is available to embedded hosts without invoking this command.

## Synopsis

```text
morphir workspace migrate [<directory>]
    [--include <pattern>]... [--exclude <pattern>]...
    [--dry-run] [--json]
```

`<directory>` defaults to the current directory. It must name an existing directory.
That directory bounds inventory, discovery, and all proposed destinations, including
when it contains no workspace declaration. Migration does not search ancestors for
projects to adopt. Select the intended repository root explicitly when invoking
from a member or source directory.

## Arguments and options

| Control | Meaning |
| --- | --- |
| `<directory>` | Bounded root; omitted means current directory |
| `--include <pattern>` | Include candidate project directories matching a root-relative pattern; repeatable |
| `--exclude <pattern>` | Exclude candidate project directories; repeatable |
| `--dry-run` | Return the complete verified plan and write nothing |
| `--json` | Write one redacted JSON result to stdout; diagnostics remain on stderr |
| `--help` | Show usage without discovery or migration |

Patterns are relative to the selected root. `*` and `?` match within a path
segment; `**` matches across directory segments. Absolute patterns and `..`
segments are invalid. Exclusions prune matching subtrees. Shell-quote wildcards
so the shell does not expand them before the CLI receives them. Prefix a literal
leading-dash pattern with `./`, for example `--include './--experimental'`, so it
cannot be mistaken for a missing value followed by an option. Existing `workspace.exclude`
settings remain effective alongside the supplied exclusions. Explicit filters do
not override nested workspace, link, version-control, dependency, or build-output
boundaries. Invalid controls block before application.

```bash
morphir workspace migrate . --dry-run
morphir workspace migrate ./repo --dry-run --json
morphir workspace migrate . --include 'packages/*' --include 'services/*' \
  --exclude 'packages/experimental-*' --exclude 'services/retired' --dry-run --json
```

## Inventory and membership

Inventory reports project candidates, excluded paths, skipped links, nested declared
workspace boundaries, and already migrated projects in deterministic path order.
It remains bounded even without an existing workspace. It skips generated output,
version-control, and dependency directories and does not follow discovered links.

The plan uses explicit root-relative paths for new members. Existing membership,
exclusions, settings, and valid `workspace.default_member` remain in place. Already
migrated members are retained without rewriting their primaries. A root project
keeps its identity. A root without a project becomes a virtual workspace. Duplicate
project names produce diagnostics while their distinct paths remain separate.

For example, a new virtual workspace containing `packages/orders` and
`packages/payments` receives:

```toml
[workspace]
members = ["packages/orders", "packages/payments"]
```

Each classic project receives its own supported TOML primary. Hidden primary
layouts keep the owning project directory as the source-path anchor.

## Conversion and verification

Every selected classic project uses the [single-project migration
contract]({{< relref "/docs/cli/project-migrate" >}}), including the complete raw
loss ledger, unsupported-field blockers, conflict checks, and materialized legacy
defaults. A blocked member blocks the aggregate before any destination replacement.

Migration compares each classic source and its TOML conversion inside the same
proposed base workspace context. It excludes converted classic sources from the
candidate and ambient personal, machine, environment, explicit CLI, and activated
profile overlays from both sides. Declared profile controls are still checked.
The comparison preserves typed values and source-path meaning and checks the
resulting workspace discovery, membership, and selection.

Adoption effects appear separately in the plan: ownership, membership, default
selection, and shared output roots. Standalone projects may acquire a shared
`<workspace>/.morphir/out` output context. That effect cannot excuse unrelated
setting or source-path changes. Migration moves no generated artifacts and keeps
every classic source byte-identical.

## Output

Human output names the outcome, inventory entries, member paths, project edits,
workspace edits, adoption effects, and verification. Diagnostics go to stderr.

JSON follows the [workspace migration result
schema](https://github.com/finos/morphir-dotnet/blob/main/docs/spec/schemas/workspace-migration-result.schema.json).
Its fields include:

| Field | Content |
| --- | --- |
| `operation`, `outcome`, `exitCode` | Operation identity, result state, and exit code |
| `rootPath`, `rootIsProject` | Bounded root and root versus virtual identity |
| `changed`, `dryRun`, `verified` | Planned changes, preview mode, and aggregate verification |
| `inventory` | Path, relative path, kind, reason, and project name |
| `projects` | Per-project redacted ledger, edits, conflicts, and independent verification |
| `members`, `workspaceTarget`, `workspaceEdits` | Reviewed membership and workspace primary edits |
| `adoptionEffects` | Intentional ownership, membership, default-selection, and output-root effects |
| `diagnostics` | Structured warnings and blockers also sent to stderr |
| `recoveryArtifacts`, `restoredPaths`, `unresolvedPaths` | Retained owned evidence and recovery state |
| `uncertainDirectories`, `preservedDirectories` | Directory paths whose ownership is uncertain, or that explicit recovery was instructed to preserve |
| `valueTypes` | Typed encoding metadata for nonfinite and temporal values in workspace edits |

Each nested project report has its own `valueTypes` array. Its JSON pointers are
relative to that project report. Sensitive settings and opaque secret references
are redacted throughout the reports; reference targets are never resolved.

```bash
morphir workspace migrate . --dry-run --json > workspace-preview.json
morphir workspace migrate . --json > workspace-result.json
morphir workspace migrate . --json # unchanged after successful adoption
```

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Applicable preview, successful application, or valid existing-workspace no-op |
| `2` | Invalid input, blocked conversion, failed verification, stale evidence, failed application, or recovery required |

An undeclared directory with no eligible projects reports no adoption plan and
exits 2. A valid existing workspace requiring no conversions succeeds unchanged.
Equivalent repeats preserve target bytes and timestamps.

## Application and recovery

Application validates and stages the entire aggregate before any destination is
replaced. It coordinates overlapping cooperating writers, rechecks source,
target, listing, and topology preconditions, and records durable recovery evidence.
A later replacement failure triggers rollback attempts. The result identifies
restored and unresolved paths.

Per-file atomic replacement does not make the batch crash-atomic. Incomplete
rollback or interrupted state retains journals, stages, and backups and produces
`recovery-required`. A read-only preview also reports pending recovery with exit
2 and retains the journal metadata. Overlapping configuration editing and
project/workspace migration writes remain blocked until explicit recovery resolves
that state.
Successful application and proven complete rollback remove only owned transaction
artifacts. User changes and unresolved evidence are preserved. If interruption
occurs after directory creation but before its ownership receipt returns,
`uncertainDirectories` names those paths explicitly. Recovery does not claim or
delete them. The shared recovery API can explicitly preserve reviewed uncertain
directories and reports them in `preservedDirectories`.

Use the shared [inspection and recovery APIs]({{< relref
"/docs/configuration/workspace-migration" >}}). There is no automatic cleanup of
uncertain recovery state. Do not delete a journal or backup to force a repeat
migration through. Classic sources are never removed by migration or recovery.

## Troubleshooting and automation

For a blocker, inspect `projects[*].ledger`, `projects[*].conflicts`, and
`diagnostics` before changing source or target settings. A fresh preview is needed
after source, target, relevant listing, or topology changes. A shared-output-root
adoption effect does not permit a different source directory.

CI should inspect the exit code and retain a failed JSON result as an artifact:

```bash
morphir workspace migrate "$REPOSITORY" --dry-run --json > workspace-plan.json
```

A preview still requires independent review before choosing to apply it. No
release, deployment, source deletion, or generated-output publication is part of
this operation. Migration is owned by `project` and `workspace`; there is no
`config migrate` alias.

See the [walkthrough]({{< relref "/docs/getting-started/configuration#adopt-a-repository-of-classic-projects" >}}),
[workspace overview]({{< relref "/docs/cli/workspace" >}}),
and [troubleshooting]({{< relref "/docs/cli/troubleshooting#workspace-migration-blocks-or-needs-recovery" >}}).
