# Configuration file transactions

`Morphir.Configuration.IO` exposes `ConfigurationTransaction` independently of CLI discovery and presentation. A host captures `ConfigurationObservation` values for every input used by a preview, including absent candidate files, and passes those observations with immutable `ConfigurationFileChange` values to `ConfigurationTransactionPlan`.

```csharp
var observed = ConfigurationTransaction.Observe(target);
var plan = new ConfigurationTransactionPlan(
    observations: new[] { observed },
    changes: new[] { new ConfigurationFileChange(target, observed, candidateBytes) });
var result = new ConfigurationTransaction().Apply(plan);
```

The editing service supplies the complete observation set. Constructing a transaction directly makes the caller responsible for including its source and target-selection evidence. The target's expected observation is automatically included even if the caller omits it from the general observation list.

`IConfigurationTransactionHost` is the explicit host boundary for in-memory or embedded applications. Its methods capture content/inventory identities, coordinate writers, stage bytes, install one destination, and remove owned artifacts. A host's `Install` implementation must replace a single file atomically. Multiple installations do not constitute a group-atomic transaction.

## Native coordination

The native host serializes cooperating configuration writers with one named OS mutex. This deliberately serializes disjoint edits too. Configuration writes are infrequent, and a single coordinator avoids filesystem case aliases and overlapping directory inventories bypassing separate path locks. The Unix name is `Morphir.Configuration.Edit.v1`; Windows uses `Global\Morphir.Configuration.Edit.v1`. Access or platform failures return an I/O outcome; the implementation does not fall back to an unlocked write. An abandoned mutex grants a new lease, after which every planned observation is checked again.

The lease remains held through recovery and artifact cleanup. Native coordination creates no files alongside configuration targets. The OS owns the named synchronization resource and releases its handles when the lease is disposed or its process exits. Custom hosts must provide a coordination protocol shared by all their writers.

Content identities use SHA-256 rather than timestamps. Directory observations capture inventory evidence; missing files and absent alternate primaries have explicit observations. The transaction rechecks every input after staging and immediately before each installation. It excludes only its own artifact paths from directory inventories and accounts for directories it created for the target. A cooperative writer that obtained an old preview conflicts after the current writer releases its lease.

The named OS coordinator is machine-local. A shared network store with writers on multiple machines needs an injected host with shared-store coordination. The native adapter does not claim distributed locking.

The protocol coordinates hosts that use this transaction service. An unrelated program can still change a file after the final observation; portable filesystem rename does not provide content compare-and-swap. Such edits are outside the coordinator. Recovery never overwrites a target whose current bytes differ from both the original and the staged candidate.

Validation and fault-test evidence will be recorded in the campaign delivery journal before handback.

## Recovery evidence

Each changed destination receives a stage, an original-byte backup when it existed, and a versioned JSON recovery journal before installation. Names contain a unique transaction identifier and do not use the configuration-primary filenames. The journal records ownership, target, original and candidate identities, and stage/backup paths. It contains no raw configuration values. Unix staging files are created with mode `0600`; the backup and staged configuration may contain secrets and must be protected as user configuration.

`ConfigurationTransactionPhase` hooks inject faults at staging, replacement, recovery, and cleanup boundaries. A failure after replacement attempts rollback while holding the coordinator. The original bytes are restored only when the current target still matches this transaction's candidate. A missing original is restored by removing the newly installed candidate. Another writer's bytes are preserved.

The result categories distinguish written, unchanged, conflict, I/O failure before installation, fully rolled-back failure, and recovery required. A recovery-required result identifies every retained artifact by path, kind, and transaction owner. It never represents an unchanged or successful outcome. Keep its journal and backup together, compare the journal's identities with the current target and backup, then recover under the same writer coordination. The API does not automatically recover or delete a previous transaction's artifacts.

Each destination is installed atomically, but earlier destinations can be visible before a later installation fails. Recovery can also fail. Callers must not describe multiple file changes as group atomic.

An injected host that fails after creating an artifact must remove it before throwing or throw `ConfigurationArtifactFailureException` with its owned paths, kinds, and transaction identifier. The service cleans artifacts owned by its transaction if possible. It preserves artifacts with another owner and reports recovery required instead of deleting them. Native staging follows this contract even when writing or flushing a partially created file fails.

Before attempting manual recovery, verify the journal owner and the original backup's SHA-256 identity. A target that differs from both recorded identities requires an operator decision; do not overwrite it automatically. A host can use a fresh transaction with the current target and backup observations to restore approved bytes. Remove the previous transaction's artifacts only after recovery is verified and only after confirming their ownership. A normal new edit never prunes older recovery evidence.

Existing destinations use `File.Replace`; new destinations use a non-overwriting `File.Move`. Both stages live beside the destination. Unix replacement copies the existing permission bits onto the candidate before replacement. New files retain the restrictive staging mode. Byte-identical candidates bypass staging and installation, preserving the target's last-write timestamp.

## Validation evidence

The focused executable TUnit suite is `ConfigurationTransactionTests`. Campaign logs live under `/private/tmp/morphir-config-editing/transaction/`; the campaign delivery journal records final integrated results.

| Behavior | Failing evidence | Passing evidence |
| --- | --- | --- |
| Replacement rollback | `red-rollback.log` | `green-rollback.log` |
| Changed source or absent alternate primary | `red-recheck.log` | `green-recheck.log` |
| Linked ancestor rejection | `red-links.log`, `red-target-guard.log` | `green-links.log`, `green-target-guard.log` |
| Directory evidence with owned staging | `red-directory.log` | `green-directory.log` |
| Overlapping writers | `red-lock.log` | `green-lock.log`, `cross-process.log` |
| Incomplete recovery journal | `red-journal.log` | `green-journal.log` |
| Cleanup failure | `red-cleanup.log` | `green-cleanup.log` |
| Recovery target and backup integrity | `red-recovery-guard.log` | `green-recovery-guard.log` |
| Partially created stage ownership | `red-partial-stage-build.log` | `green-partial-stage.log` |
| Proven post-rollback outcome | `red-fault-matrix.log` | `green-fault-matrix.log` |
| Existing Unix permissions | `red-mode.log` | `green-mode.log` |
| Target evidence ownership | `red-plan-guard.log` | `green-plan-guard.log` |
| Stage and backup integrity before installation | `red-stage-integrity.log` | `green-stage-integrity.log` |
| Unambiguous inventory identities | `red-inventory.log` | `green-inventory.log` |
| Partially created parent ownership | `red-parent-ownership.log` | `green-parent-ownership.log` |

`qualification.log` verifies immutable input snapshots, a wholly in-memory host, byte-identical no-op timestamps, and observable per-file installation followed by multi-file rollback. `cross-process.log` runs a second executable TUnit process against the same native coordinator. It proves that the child cannot stage while the parent owns the lease and that the stale child conflicts after the parent installs its candidate. Windows-specific namespace and metadata behavior still require Windows CI; local qualification ran on macOS.

Hosts that create target directories can implement `CreateParentDirectories(destination, transactionId)` to attach the current owner to failures. The default overload preserves existing host implementations; those implementations must clean any partial directory creation themselves before throwing. The native implementation returns canonical `/` paths and either cleans created directories or reports typed retained ownership.
