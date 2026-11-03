---
title: "Workspace migration API and inventory"
linkTitle: "Workspace migration"
weight: 65
---

# Adopt a bounded repository

`Morphir.Configuration.IO.WorkspaceMigrationService` inventories, plans and applies
workspace adoption without constructing the CLI. It reuses `ProjectMigration`'s
mapping, raw loss ledger and independent equivalence verification. It does not
compile sources, move generated outputs, remove classic manifests or invoke
publication services.

```csharp
var root = ProjectPath.normalize(Path.GetFullPath(directory));
var service = new WorkspaceMigrationService(
    new FileSystemProjectTree(root), new NativeConfigurationTransactionHost());
var preview = service.Plan(root, includes: ["apps/**"], excludes: ["**/experimental"]);
if (preview.CanApply)
{
    var outcome = service.Apply(preview);
    // Inspect outcome.Status, Artifacts, RestoredPaths and UnresolvedPaths.
}
```

Omit storage for a read-only host. Supply `InMemoryProjectTree` or implement
`IProjectTree` for a mounted tree. Both use canonical absolute paths with `/`
separators. A mounted directory returns only immediate child directories or
links. A child outside that directory or the selected operation root blocks the
plan. Application requires storage-backed evidence and the service that created
the verified preview. Read-only and foreign-service plans cannot be applied.

## Inventory rules

The absolute directory passed to `Plan` is the operation boundary even if the
host's authorization boundary is larger. No ancestor is searched for projects
or workspace adoption. Inventory orders paths ordinally. It inspects shared
primary layouts and formats, including supplemental workspace JSON, without
reading personal, machine, environment or active profile overlays.

Include and exclude filters are repeated root-relative segment globs. `*` and
`?` match within one segment; `**` matches zero or more segments. Absolute paths,
empty filters and `..` segments are invalid. Excluded directories are pruned.
Existing `workspace.exclude` patterns retain their declared workspace-base
anchor and cannot be overridden by an include filter.

Version control directories `.git`, `.hg`, `.svn`, dependency directories
`node_modules`, `elm-stuff`, `vendor`, `.nuget`, `packages`, and output directories
`bin`, `obj`, `dist`, `build`, `target` are skipped. Primary layout directories
`.morphir` and `.config` are inspected as layouts at their owner; they are not
separate projects. The declared workspace output directory is also skipped. Discovered links are reported and never followed, including
linked ancestors. A nested declared workspace is reported as a boundary and
its children are not inventoried for outer adoption. The maximum inventory is
100,000 directories; exceeding it blocks the plan instead of returning an
incomplete applicable result.

`Inventory` explains candidate, excluded, boundary, already migrated and skipped
link paths. Duplicate names remain distinct paths and produce a diagnostic;
explicit path selection remains unambiguous.

## Reviewed membership and verification

`Members` preserves existing declarations, including globs, order and defaults,
and appends explicit root-relative paths for additions. A root project remains a
root project and never becomes its own glob member. An otherwise projectless
root becomes virtual. Already migrated members retain their bytes and settings.
An existing valid workspace with no conversions succeeds unchanged. An
undeclared empty root reports that adoption is impossible.

`Projects` contains the single-project plans and their loss ledgers. Use each
project's `Presentation` for logs. The canonical `Plan.Candidate` and `Plan.Edits`
contain application values, including secrets; do not log them. Secret references
remain opaque leaves. Unsupported values cannot be dropped to make a batch pass.

Every conversion receives explicit proposed base workspace context. Verification
disables activation and compares declared profile controls separately. The
aggregate candidate excludes every converted classic source together and reruns
shared discovery and distinct-path selection. It must preserve project source
paths and non-adoption settings. Intended ownership, membership, default selection
and shared output-root changes appear separately in `AdoptionEffects`. They do
not authorize other value changes. Existing generated files stay where they are.

## Application and recovery

Application validates source bytes, target bytes or absence, relevant directory
listings and primary topology. It stages the entire batch before replacing any
destination. Cooperating writers coordinate through the shared transaction
adapter. Each file installation is atomic; the batch is not crash-atomic.

Storage-backed previews inspect pending journals without writing and expose
`PendingRecovery`; a pending operation makes the preview inapplicable. On failure
inspect the structured status, restored paths, unresolved paths and
owned artifacts. Pending recovery blocks overlapping shared configuration edits
and project migrations as well as subsequent workspace writes. Use the explicit
shared journal inspection and recovery APIs described in
[transaction recovery](transaction-recovery.md). Do not remove recovery journals
or backups to bypass a guard. Preserve user changes and unresolved evidence.

CLI reports use the same plans and outcomes. See the
[workspace command reference](../cli/workspace-migrate.md), walkthrough and
troubleshooting for exit codes, previews and scriptable JSON.
