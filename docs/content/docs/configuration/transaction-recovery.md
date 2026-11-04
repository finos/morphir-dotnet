---
title: "Configuration transactions and recovery"
linkTitle: "Transaction recovery"
weight: 55
---

# Configuration transactions and recovery

`WorkspaceMigrationService`, `ProjectMigrationService` and `ConfigurationEditService`
apply through the shared `ConfigurationTransaction` boundary. Configuration tools
coordinate cooperating writers on one machine. They do not provide a distributed
lock, or a crash-atomic replacement of several files.

## Batch application

A transaction first acquires writer coordination and checks pending recovery. It
checks exact source and target bytes, observed absences, directory inventories
and link topology. It creates durable write-ahead metadata before staging every
candidate and original backup on the corresponding target filesystem. All stages,
backups and preconditions must match before the first destination replacement.
Artifact registration requires the predeclared deterministic path to have been
absent before creation and its returned bytes to match the requested content.
A host-returned path or matching transaction ID does not transfer ownership of
an existing file or directory. Directory receipts must be a canonical subset of
the parents observed missing in the plan.

The transaction rechecks remaining inputs between installations. Atomic native
replacement protects one destination at a time.

If a replacement fails, the transaction attempts rollback in reverse order.
`RestoredPaths` identifies originals proven restored. `UnresolvedPaths` identifies
paths whose state is uncertain or whose bytes no longer match the recorded
original or candidate. The result retains artifact paths and their transaction
owner. Configuration bytes, backup bytes and secrets are never included in these
metadata results.

`Written` means the complete candidate was installed and its owned temporary
artifacts were cleaned. `Unchanged` leaves equivalent target bytes and timestamps
alone. `Conflict` means input evidence changed. `RolledBack` means rollback proved
the originals restored and cleaned transaction artifacts. `RecoveryRequired`
means retained evidence needs explicit inspection and recovery. Do not treat a
partial workspace, successful command exit from an unrelated tool, or the presence
of a candidate TOML file as evidence that the batch completed.

## Inspect and recover through the shared API

Recovery is explicit. Keep the returned journal path and inspect it before taking
action. Use the same authorized storage host that owns the paths:

```csharp
var transaction = new ConfigurationTransaction(new NativeConfigurationTransactionHost());
var pending = transaction.InspectPending(new[] { workspaceRoot });
foreach (var item in pending)
{
    Console.Error.WriteLine($"{item.JournalPath}: {item.Code}");
    // Show item.Targets, item.Artifacts, item.UnresolvedPaths to the operator.
}

var inspection = transaction.InspectRecovery(journalPath);
if (inspection.Valid && inspection.UnresolvedPaths.IsEmpty)
{
    // This call is an explicit recovery request by the host/operator.
    var outcome = transaction.Recover(journalPath);
    // Check outcome.Status and preserve outcome.Artifacts on RecoveryRequired.
}
```

Inspection does not change files. Recovery reacquires writer coordination and
checks the metadata and file identities again. Without a valid committed witness,
recovery restores the recorded originals. A target that was absent is removed only
when its current bytes match this transaction's candidate. With a durable committed
witness, recovery verifies all installed candidates and finishes cleanup.

A host can stop between creating a directory and returning its ownership receipt.
Inspection reports these paths in `UncertainDirectories`. Default recovery keeps
the journal and returns `RecoveryRequired`. After reviewing those paths, an
operator can explicitly retain them as user-owned directories:

```csharp
var result = transaction.Recover(journalPath, inspection.UncertainDirectories);
// Check result.Status. result.PreservedDirectories lists directories retained
// by this explicit request; transaction cleanup never claims or removes them.
```

This option accepts only the journal's uncertain paths that remain regular
directories. Linked paths, file replacements and unrelated paths remain blocked.
It does not authorize deletion or discard user content.

If an adapter returns a foreign file or directory, or claims one in an exception,
the live result reports that path as unresolved without claiming it as an owned
artifact. A separate recovery journal records the rejected receipt. Explicit
recovery preserves the unverified external paths, rolls back only verified owned
changes, and removes only the transaction's verified artifacts. A matching owner
ID or filename prefix alone never authorizes moving, restoring or deleting a file.

If a target, backup, stage or journal has unexpected content or link topology,
recovery preserves it and reports an unresolved path. Live cleanup validates the
complete artifact set before deleting any artifact, then rechecks each artifact
immediately before deletion. Changing a backup or even reformatting a journal
therefore produces `RecoveryRequired` and retains the guard. Journal bytes must
match their canonical versioned encoding, including known roles and ownership
paths; whitespace, duplicate/unknown fields and role changes are not silently
normalized during recovery. Do not overwrite the user's
new bytes with an old backup or delete evidence to bypass the guard. Preserve both
versions, review the metadata and restore a specifically approved state before
retrying recovery. Invalid/truncated journals need manual review; automatic
recovery does not invent metadata or infer ownership from a filename alone.

## Journals and interruption windows

Native `.morphir-edit-<transaction-id>-...` files contain private stages, original
backups and metadata journals. Native creation uses exclusive new files and user
read/write permissions. Metadata and file bytes are flushed before proceeding.
Journals identify the complete target set, original and candidate hashes, stage
and backup paths, and guarded directories. They do not contain the source values.
The initial manifest is written in an existing common ancestor before any parent
directory creation. A durable directory receipt records directories whose creation
the host confirmed. The complete write-ahead manifest allows inspection after
interruption before a stage, between staging and replacement, or immediately after a replacement whose
callback did not return.

A committed witness distinguishes a completed installation from an interrupted
commit. Cleanup preserves this witness until the other owned stages, backups and
journals have been removed. An interruption during cleanup therefore remains
identifiable. Complete success and complete rollback remove only owned temporary
files and empty owned directories. An incomplete outcome retains journals and
backups. Cooperating configuration and project/workspace editing paths refuse an
overlapping write until explicit recovery resolves that state.

File flushes and atomic replacement do not promise group atomicity or recovery
from every filesystem, storage-controller or power-loss failure. Uncooperative
processes can modify files outside Morphir's writer lock. Their unexpected bytes
are treated as drift, not silently overwritten during recovery.

## Custom storage hosts

`IConfigurationTransactionHost` remains the storage boundary. Hosts serialize
ordered canonical guarded paths across their writers, report exact byte hashes,
and atomically install one file. Writable hosts explicitly opt in with
`SupportsDurableRecovery = true` and implement `FindRecoveryJournals` and
`EnumerateTransactionArtifacts`. Enumeration must remain
bounded to authorized locations, include retained transaction-owned artifacts,
and never follow links. Stages and backups use
`ConfigurationTransaction.GetArtifactPath(destination, transactionId, role)`;
recovery validates the owner, destination anchor and role, including when several
targets share identical bytes. Returning `null` for artifact enumeration means recovery
cannot prove cleanup completeness and returns `config.recovery_host_capability`.
Older adapters remain source compatible. Application refuses a host without this
capability before any write, returning `IoFailure` and
`config.recovery_host_capability`. Read-only planning remains available. Hosts
must add durable enumeration before opting into writes; an empty process-local
registry is not a substitute.

Recovery metadata uses version 2. Automatic recovery refuses unknown versions and
invalid ownership. Preserve earlier journals for explicit review with their
original host/version. Do not rewrite or silently delete them during an upgrade.

A lost host acknowledgement does not prove a commit failed. If the committed
witness is present and validates against the attempted operation, application
preserves the candidate files and backups and returns recovery required for
explicit completion. If the witness is absent, rollback can proceed.

Rejected transaction-shaped artifacts remain recovery blockers while present.
Recovery preserves the journals and reports their unresolved paths rather than
claiming complete rollback with metadata left behind. Any manual disposition
must first establish the artifact's origin and preserve user changes. Unrelated
foreign files are never cleanup authority.

If the storage host disconnects while a failure is being handled, application
returns a typed recovery-required result with known or possible evidence
locations and unresolved targets. Its fallback does not perform further storage observations
or blind rollback. Reconnect storage and inspect the retained journal
before explicitly recovering.
