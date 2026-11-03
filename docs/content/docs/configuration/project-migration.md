---
title: "Classic project migration APIs"
weight: 50
description: "Pure planning, independent equivalence and guarded application without a CLI dependency"
---

# Classic project migration APIs

`Morphir.Configuration.ProjectMigration` owns the pure conversion policy.
`Morphir.Configuration.IO.ProjectMigrationService` supplies bounded discovery,
source-preserving TOML rendering and guarded application. Neither library depends
on System.CommandLine or the CLI host. The
[command reference]({{< relref "/docs/cli/project-migrate" >}}) covers options,
before/after files and exit codes.

## Read-only planning

```csharp
var tree = new InMemoryProjectTree("/repo", new Dictionary<string, string>
{
    ["/repo/morphir.json"] =
        "{name:'Orders',sourceDirectory:'elm-src',exposedModules:['Order']}"
});
var service = new ProjectMigrationService(tree);
var preview = service.Plan("/repo", outputPath: ".config/morphir/config.toml");
var safePlan = preview.Presentation;
// safePlan includes redacted candidate, edits, raw-field ledger and conflicts.
// preview.Verification includes redacted snapshots, differences and origin changes.
```

A parsed-only tree needs no write adapter. It receives the complete logical plan
and a TOML representability check, without writer coordination or artifacts.
`CanApply` means the logical conversion is verified and applicable; that host
still needs a storage adapter to create a fresh plan and apply it. The canonical
`Plan.Candidate` and `Plan.Edits` support programmatic application and may contain
secrets. Present `Presentation`, `Verification` and `Diagnostics` to users.
Secret references stay opaque and are never evaluated.

## Pure conversion and verification

`ProjectMigration.plan` accepts an original `SourceInput`, a TOML target identity
and the target's raw typed value. `planWithContext` also accepts declared base
resolution context. Raw classic input is authoritative for loss accounting,
including compatibility-reader fields that ordinary loading ignores.

The ledger accounts for each original value as mapped, equivalently retained or
blocking. Derived entries record materialized defaults. Canonical dependencies
objects and supported extra settings survive; unsupported nonempty classic
shapes block. Null, out-of-range TOML integers and invalid Unicode in values or any key segment block even if
another layer would hide them. Classic/canonical collisions in one file block
regardless of equality. Equal target entries remain unchanged; missing leaves
become edits; differing values produce redacted source/target conflicts.

`ProjectMigration.verify` accepts the actual parsed candidate TOML raw tree.
It independently resolves before and after snapshots, excluding the converted
classic source and old target from the candidate. Both sides retain only root
and selected-member primary sources. Machine, selected-file, personal,
environment, explicit CLI and activated-profile contributions are removed.
Declared profile controls are compared separately using their raw merge semantics;
activation stays disabled. Missing or changed declarations fail verification. Declared base
workspace context remains, with its output directory recomputed from root base
sources rather than a previously resolved personal override.

The comparison checks typed values before redaction, including complete arrays,
map shape, signed zero and secret references. It also checks resolved project
source-directory and workspace output paths. The project location remains the
anchor for every primary layout. Expected origin changes appear separately.
An unrelated default `ir.format_version` is never written into the candidate.
For a member, base workspace pipeline values that override legacy low-priority
checkpoint defaults are preserved when those defaults are materialized.

## Explicit application

```csharp
var root = ProjectPath.normalize(Path.GetFullPath("orders"));
var service = new ProjectMigrationService(
    new FileSystemProjectTree(root), new NativeConfigurationTransactionHost());
var preview = service.Plan(root);
if (preview.CanApply)
{
    var result = service.Apply(preview);
    // Inspect Status, Code and Artifacts. RecoveryRequired needs explicit recovery.
}
```

Only the creating service can apply its immutable, verified, adapter-backed
preview. Exact source and target bytes plus discovery observations, missing
alternate primaries and relevant workspace evidence become transaction
preconditions. Stale source, target or workspace evidence blocks installation.
The converted source is never a write target and remains byte-identical.

Application reuses the editor's transaction boundary. It stages bytes beside the
target, coordinates cooperating writers, rechecks preconditions and replaces one
file atomically. An identical repeat preserves bytes and timestamp. A write
failure reports rollback or retained recovery artifacts with transaction ownership.
Preserve journals, stages and backups when recovery is incomplete; do not remove
a subsequently edited target to force a rollback. No multi-file crash atomicity
or distributed-writer guarantee is claimed.

Native trees reject linked ancestors. On macOS use physical `/private/tmp` paths
for temporary roots. A host authorizes each tree boundary explicitly; migration
never borrows access from machine roots or modifies workspace membership.
