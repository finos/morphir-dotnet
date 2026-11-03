# Workspace migration delivery journal

Tracking issue: [#479](https://github.com/finos/morphir-dotnet/issues/479).
Change: `add-workspace-migration`, campaign 6 of six.

## Ownership and baseline

Owned checkout `/Users/damian/.t3/worktrees/morphir-dotnet/t3-f370bf10`, branch `t3/workspace-migration`, initially clean at exact required main baseline `e3b9a053120a49e94c70c30cd4f62ef783672c64`. GitHub main agrees. Prerequisite PR #477 is merged at that same SHA, and its [receipt](https://github.com/finos/morphir-dotnet/pull/477#issuecomment-6613145141) confirms review/checks and owned cleanup. The real open tracking issue was created and read before coding.

OpenSpec list/status/instructions apply returned schema `html-design`, ready, 0/9 tasks. Full returned proposal/design/spec/tasks and layered configuration PRD, project/config editing journals and shared APIs were read. Only this change will be synchronized/archived when all implementation tasks are genuinely complete. Independent active plans remain untouched.

Parent owns independent final review, CI watch, authorized landing and checkout/branch/artifact cleanup. This implementation worker does not watch, merge or delete checkout/branches. The campaign remains incomplete until parent gates finish.

## Work and evidence ownership

Lead owns bounded inventory, aggregate composition/verification, integration and qualification. Two Codex helpers own shared transaction/recovery and CLI/report/schema/subprocess tests respectively. Helpers preserve one another's changes. One heavy restore/build/test/publish at a time, `DOTNET_PROCESSOR_COUNT=2`, serial MSBuild/restore, sequenced Expecto and TUnit `--maximum-parallel-tests 1`.

All owned external artifacts are under `/private/tmp/morphir-workspace-migration/`, including physical `tmp/`, `transactions/`, `cli/`, build/runner/coverage/publication logs and manifests. Global caches/tools are not changed. Process-local PowerShell 7.5.3 is used for F# SDK tests. Unresolved recovery journals/backups are preserved and inventoried.

## Checkpoints

Setup completed before runtime changes. Proposal now links actual issue #479. Task 1.1 was updated at returned line 8 and apply progress rechecked.

### Inventory and planning checkpoints

- `inventory-red-build.log` proves the aggregate API was absent before production.
  Initial BDD and API cases preceded implementation. `inventory-io-full-01.log`
  passes 323/323 IO/TUnit/Reqnroll cases, including three workspace BDD cases.
- `planning-red-run.log` passes 10 and fails four of 14 focused cases, reproducing
  repeated-inventory classification, non-adoption setting drift, glob reopening
  skipped/nested directories and filtered-root identity. `planning-green-run.log`
  passes all 14 after fixing those behaviors through shared discovery and mapping.
- `boundaries-red-run.log` passes four and fails four boundary cases. These expose
  retained classic root context, configured output skipping, invalid defaults and
  paths containing glob syntax. `recovery-preview-red-run.log` separately fails
  pending-journal preview before read-only recovery inspection is added.
  `boundaries-green-run.log` passes all 23 focused planner/application cases with
  clean compilation, including source/target/listing/topology drift and repeat
  source-byte/target-byte/timestamp checks. No generated output is moved.
- Early inventory build attempts while recovery production was incomplete and a
  zero-test filter run are retained as unsuccessful attempts, not qualification.
  A lightweight FSI probe had an FSharp.Core load mismatch; configured executables
  provide correctness evidence instead.
- Tasks 2.1 and 2.2 were checked at fresh CLI-returned locations and progress
  confirmed after each update. Aggregate equivalence/adoption qualification and
  application/recovery qualification remain active.

### Coordinated application and recovery

- `transactions/batch-red-run.log` fails both first-stage ordering cases before
  production; later staging and recovery runs prove every candidate and backup
  exists before the first replacement. Complete preflight blocks a bad member,
  stale source/target/listing/alternate primary and overlapping writers.
- `transactions/recovery-red-build.log` and `inventory-build-01.log` prove missing
  explicit inspection/recovery APIs. `transactions/boundary-red-run.log` reproduces
  read-only unrelated-directory staging and lost common-ancestor cleanup guards.
- `transactions/capability-red-run.log` proves a legacy custom host could write
  without durable enumeration. Application now returns a typed capability failure
  before artifacts unless a host explicitly supplies durable support.
- `transactions/evidence-red-run.log` reproduces null/overflow journal parser
  exceptions. Recovery now reports invalid metadata without changing it. Stable
  artifact names are tied to target, role and owner. A mounted serialized-state
  host proves recovery is not process-memory-only; tampered backups/user changes
  preserve evidence and block automatic restoration.
- `transactions/final-precondition-red-run-02.log` reproduces source drift after
  the last install. Final rechecking now detects it and attempts rollback.
  `transactions/parent-creation-red-run.log` reproduces a process exit after mkdir
  but before the host returned its ownership receipt. A durable initial journal
  now precedes mkdir; uncertain directories remain explicit and require reviewed
  retention, never inferred cleanup ownership.
- `transactions/last-windows-green-run-02.log` passes 87 focused cases. Seven actual
  process-interruption windows and separate-process pending-write refusal pass.
  `planner-final-build-02.log` is clean, and `planner-final-io-02.log` passes the
  complete current-source IO runner, 371/371, after the lead's later fixes.
- Success and proven rollback clean owned artifacts. Explicit recovery resolves
  test-owned native faults. Retained malformed parser samples are synthetic;
  a pre-fix mkdir-crash fixture retains generated empty directories and child
  logs as review evidence, without a recoverable target or journal. Those samples
  are preserved for parent cleanup. No real unresolved native journal/backup was
  found in the helper's final inventory.
- Native coordination is machine-local, and file replacement is per-file atomic.
  No cross-file crash-atomicity or distributed-lock guarantee is claimed.

### Common-context verification and source policy

`transactions/lead-declared-controls-red.log` passes one and fails one declared
control test. `transactions/lead-layout-red.log` fails both mixed-layout cases.
The aggregate now compares declared controls with activation disabled through the
same shared control policy as single-project verification. It supplies an explicit
existing supported TOML target to the shared planner, preserving owner anchors
and all existing target/ambiguity guards. Both root and member layouts pass.

`source-policy-red-run.log` reproduces a normalized legacy JSON5 primary being
misreported as already migrated. Inventory now sends every normalized legacy
candidate to the project planner, which retains its established `.json` classic
source contract and returns `migration.classic_required` for `.json5` primaries.
Full JSON5 syntax in supported `morphir.json` remains accepted; no reader policy
is changed or loosened. Canonical JSON5 projects remain valid canonical projects.

The first final planner build tried to call an internal F# helper from C# and
failed compilation. Its subsequently invoked stale-DLL tests are not a current
qualification gate. `planner-final-build-02.log` is the successful rebuilt source,
followed by `planner-final-io-02.log`, 371/371 current tests. Task 2.3 was marked
only after that qualification, at the refreshed source location.
