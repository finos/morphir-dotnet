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

### CLI adapter and subprocess contracts

The actual baseline CLI and tooling test assembly were built before CLI runtime
implementation. `cli/cli-red-run-02.log` contains 27 intended missing-command
failures; two early alias-test assertions were corrected to the existing generic
usage contract and are not claimed behavioral evidence. The first integrated
29-case run passed 27 and exposed a missing-filter token being consumed as text
and an unstructured workspace host-initialization crash. Both were reproduced
and fixed. Its mutable log was later overwritten by a script; terminal evidence
and `cli/parse-probe.log` remain, so that overwritten file is not claimed as the
original failure receipt. Later ordinal checkpoint receipts are immutable.

`cli/cli-green-02-run.log` passes 29/29 after actual CLI rebuilding. An isolated
existing-DLL probe then reproduces a pending-journal dry-run incorrectly returning
applicable preview. `cli/pending-cli-red.log` and the associated JSON receipt
preserve that failure, followed by explicit shared rollback. The final adapter
surfaces pending recovery, uncertain directories and preserved directories through
safe JSON and human outcomes. `cli/cli-green-03-run.log` passes all 31 subprocess
BDD cases after rebuilding the actual CLI against final shared sources. Every
operation report validates against the checked-in schema using a fresh registry.

The cases include current/supplied bounded roots, repeated include/exclude,
root/virtual/existing workspaces, no adoption/no-op, loss blockers, raw nested
secret redaction, depth 120, parse/initialization failures, source preservation,
no config migrate alias, durable pending recovery and a real process interrupted
after mkdir. Explicit recovery resolves every real CLI fixture. The actual CLI
build retains two existing package warnings; tooling retains three existing
package/generated warnings. `cli/format-03.log` passes own C# formatter verification.
`cli/final-receipt.json` and `cli/final-source-sha256.json` record exact commands,
counts and source identity. Task 4.1 was checked at its refreshed returned location.

### Live artifact ownership review

During final qualification, lead review identified two unverified live-application
ownership boundaries. `Stage` return paths were registered as owned without
checking their deterministic target/role identity, and live cleanup did not first
check whether a backup/journal had changed. Eight new regressions are written
before fixes. Tasks 3.2 and 3.3 were explicitly reopened at fresh returned
checkbox locations; earlier passing receipts are source checkpoints, not final
qualification of these guards. Broad runner evidence is retained as an initial
checkpoint while the new cases are reproduced and resolved.

The initial broad run completed all 17 configured executable runners successfully
before the live-ownership fixes. Its receipts are preserved in
`validation-initial/`; they are not substituted for final-source qualification.
Counts include core 177, IO 371, tooling 433 and Microsoft 22. The initial full
solution build has 11 existing dependency/generated warnings and no errors.
C# full formatter verification and changed F# Fantomas checks pass. The final
ownership regression set reproduces all ten failures in
`transactions/live-ownership-red-run.log`, with a clean preceding build.

### Coverage-driven public API qualification

Fresh managed non-CLI qualification passes 28 workspace checks, 32 project
migration checks, 44 editing checks, 228 JSON5 grammar cases and 26 pinned Morphir
documents. The compiled host exercises batch preview/application, safe generated
report serialization, source retention, output retention, byte/timestamp repeats,
drift and explicit recovery. `qualification-first-summary.json` records exact
commands and exits; all six format/build/smoke/collector gates pass.

The first current-source coverage union is core 4188/4345, 96.39%, and IO
2367/2459, 96.26%. IO is below prerequisite 97.15%, so final qualification remains
open. Public API boundary tests were added for bounded invalid inputs, mounted
listing/read/drift failures, native staging/parent failures, explicit recovery
faults/capabilities/options, encodings and full JSON5 escaped values/opaque arrays.

That work also reproduced a real multi-format no-op defect: an existing declared
workspace without root TOML could stage an empty primary despite `Changed=false`.
`format-noop-red-run.log` fails all six YAML/YML/JSON/JSON5/supplemental/hidden
variants before the fix. The aggregate now overlays a root file only when it
has workspace edits or an actual root conversion. `format-noop-green-run.log`
passes all six with unchanged source bytes and no new primary. Task 2.2 was
explicitly reopened for this finding before the minimal production fix.

### Final discovery and composition corrections

`discovery-verification/actual-discovery-probe.json` records the initial defect:
application could discover four members after a preview verified only one through
a sanitized inventory tree. The native discovery regression suite first fails
three of four cases, then passes all four. Verification now uses actual shared
discovery listings, retains filtered existing membership and rejects declared
globs that cross a hard dependency/output/nested-workspace boundary. It verifies
both retained-classic and source-excluded views. No generic discovery semantics
or existing member declarations are changed to make verification pass.

`root-composition-red-run.log` reproduces a root classic manifest's original
membership being rejected against adoption's own new members. Shared component
planning now maps against the original target, validates explicit proposed
membership against discovery, and composes only the reviewed membership edit.
A fabricated original target and undeclared membership are rejected. A separate
red case proves component plans must not independently apply hypothetical
workspace context; components are now read-only and aggregate application owns
all writes. Existing target membership conflicts remain blockers.

`root-base-run.log` passes all seven composition cases after actual rebuilding,
including custom output roots and `workspace.root='sub'` with a root source path.
The common model uses canonical ownership controls without rendering temporary
configuration or weakening raw unsupported-value accounting. Every member uses
the same base workspace; ordinary source paths/settings retain the shared
single-project verifier. `root-composition-green-io.log` passes the preceding
428-case full IO checkpoint; final qualification below supersedes it.

Live artifact ownership regressions now pass. Stage and directory receipts must
match predeclared deterministic ownership; cleanup validates every owned artifact
before removing any. Altered backups/journals and unverified foreign stage paths
retain evidence and durable overlap guards. Inspection exposes unresolved paths
after process-local result loss. Deliberately unresolved fault fixtures remain
under the owned external artifact root for parent inspection; they are not
silently cleaned or represented as successful recovery.

## Final runtime qualification

Runtime checkpoint: `de44fa5` (`fix: verify aggregate context and artifact ownership`),
based on exact main `e3b9a053120a49e94c70c30cd4f62ef783672c64`.
`qualified-source-checkpoint.json` records hashes of every changed file. Subsequent
delivery edits are documentation/specification only. All following commands run
from the owned checkout, one heavy operation at a time, with
`DOTNET_PROCESSOR_COUNT=2`, `BuildInParallel=false`, `RestoreDisableParallel=true`
and `TMPDIR`, `TEMP`, `TMP` set to `/private/tmp/morphir-workspace-migration/tmp`.

- Locked solution restore: `dotnet restore Morphir.slnx --locked-mode -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true`.
- Final build/analyzers: `dotnet build Morphir.slnx --no-restore -m:1 -p:BuildInParallel=false`; zero errors, ten existing package/generated warnings. Actual CLI rebuilt before subprocess qualification.
- Changed C# formatter: `dotnet format Morphir.slnx --no-restore --include <all changed C# paths> --verbosity quiet`; exact expanded argv is in `qualification-first-summary.json`. Full formatter verification uses `dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet`.
- F# formatting: `dotnet fantomas --check src/Morphir.Configuration/Migration.fs src/Morphir.Configuration/ProjectDiscovery.fs`.
- Managed host: `dotnet tests/Morphir.Configuration.IO.Smoke/bin/Debug/net10.0/Morphir.Configuration.IO.Smoke.dll --fixtures tests/Fixtures/Configuration`.

The final configured executable sweep passes all 17 runners, 3,887 tests and
smoke checks. Each invocation is `dotnet tests/<project>/bin/Debug/net10.0/<project>.dll`
with the flag below. The process-local PATH adds campaign `bin/dotnet` for serial
nested builds and existing PowerShell 7.5.3 for F# SDK package tests;
`DOTNET_HOST_PATH` points to that campaign wrapper. Global tools/caches are untouched.

| Project | Runner flags | Passed |
|---|---|---:|
| Morphir.CodeGeneration.FSharp.Tests | `--sequenced` | 333 |
| Morphir.Configuration.IO.Tests | `--maximum-parallel-tests 1` | 429 |
| Morphir.Configuration.Inspection.Smoke | none | 71 |
| Morphir.Configuration.Microsoft.Tests | `--maximum-parallel-tests 1` | 22 |
| Morphir.Configuration.Tests | `--sequenced` | 177 |
| Morphir.Core.Tests | `--maximum-parallel-tests 1` | 237 |
| Morphir.IR.Pipeline.Plugins.Tests | `--sequenced` | 141 |
| Morphir.IR.Pipeline.Tests | `--sequenced` | 299 |
| Morphir.Internal.CodeGeneration.Tests | `--sequenced` | 25 |
| Morphir.Ion.Tests | `--sequenced` | 463 |
| Morphir.Models.Tests | `--sequenced` | 791 |
| Morphir.Myriad.Integration.Tests | `--maximum-parallel-tests 1` | 12 |
| Morphir.Myriad.Tests | `--sequenced` | 4 |
| Morphir.SDK.CSharp.Tests | `--maximum-parallel-tests 1` | 20 |
| Morphir.SDK.Conformance.Tests | `--maximum-parallel-tests 1` | 27 |
| Morphir.SDK.Tests | `--sequenced` | 403 |
| Morphir.Tooling.Tests | `--maximum-parallel-tests 1` | 433 |

`validation/final-receipt.json` preserves expanded commands, durations and counts;
individual logs preserve actual runner discovery. IO includes shared/project/
workspace BDD, full aggregate fault/recovery/ownership and separate-process
interruption/overlap tests. Tooling includes 31 real workspace subprocess BDD
scenarios and fresh-registry JSON-schema validation. Models/SDK runners retain
codec/roundtrip contracts. Plain `dotnet test` is not substituted for these runners.

Coverage commands:

```sh
dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll \
  --maximum-parallel-tests 1 --coverage --coverage-output-format cobertura \
  --coverage-output /private/tmp/morphir-workspace-migration/io.cobertura.xml
dotnet test tests/Morphir.Configuration.Tests/Morphir.Configuration.Tests.fsproj \
  --no-build --no-restore -m:1 -p:BuildInParallel=false \
  --collect:"XPlat Code Coverage" \
  --results-directory /private/tmp/morphir-workspace-migration/core-coverage
/usr/bin/python3 scripts/configuration/summarize-coverage.py \
  /private/tmp/morphir-workspace-migration/core-coverage/*/coverage.cobertura.xml \
  /private/tmp/morphir-workspace-migration/io.cobertura.xml
```

Final source coverage: core 4,188/4,345 (96.39%); IO 2,495/2,561 (97.42%).
Both exceed prerequisite source checkpoints. The core collector is supplementary;
177 sequenced Expecto tests establish actual test execution. No source exclusions
or warning suppressions were added to meet qualification.

Managed, trimmed and native non-CLI batch hosts each pass 38 workspace checks,
32 project migration checks, 44 editing checks (9 editor, 22 shared, 13 transaction),
228 official JSON5 syntax cases and 26 pinned Morphir documents. They exercise
read-only/native preview, aggregate application, no-op bytes/timestamps, source
and generated-output retention, drift, redacted generated serialization, explicit
journal recovery and uncertain-directory preservation. Native output is a Mach-O
64-bit arm64 executable. Exact publish argv and exits are in `publish/summary.json`:

```sh
dotnet publish tests/Morphir.Configuration.IO.Smoke/Morphir.Configuration.IO.Smoke.csproj \
  -c Release -r osx-arm64 -m:1 -p:BuildInParallel=false \
  -p:RestoreDisableParallel=true -p:PublishTrimmed=<false|true|true> \
  -p:PublishAot=<false|false|true> -p:ILLinkTreatWarningsAsErrors=false \
  -p:TrimmerSingleWarn=false -p:JsonSerializerIsReflectionEnabledByDefault=false \
  -o /private/tmp/morphir-workspace-migration/publish/<managed|trimmed|native>
/private/tmp/morphir-workspace-migration/publish/<mode>/Morphir.Configuration.IO.Smoke \
  --fixtures /Users/damian/.t3/worktrees/morphir-dotnet/t3-f370bf10/tests/Fixtures/Configuration
```

The three modes run sequentially with the corresponding values above. Managed
publish has zero warning lines; trimmed retains 44 and native retains 47 pinned
CsToml/FSharp.Core analysis-warning lines. These remain visible upstream
limitations, not a strict warning-free trimming claim. No suppression or package
update was introduced. `publish-locks/restored.json` proves all 45 lockfiles were
restored byte-for-byte after publishing. Reports and stderr are retained separately.

## Specification sync and handoff gates

Full formatter verification and final locked restore pass. OpenSpec apply returned
`all_done`, 9/9, before archive. The current specs-rule snapshot was read before
writing the new main spec. Only `add-workspace-migration` was synchronized and
archived at `openspec/changes/archive/2026-11-03-add-workspace-migration/`.
`openspec/specs/workspace-migration/spec.md` preserves the approved Purpose and
all 11 requirement bodies/14 scenarios byte-for-byte under canonical main-spec
headings. `archive-verification.json` verifies exact content, nine checked tasks,
retained `.openspec.yaml`, balanced HTML and valid local design/proposal links.
The archive's prerequisite and PRD links are adjusted for archive depth.

`MISE_TRUSTED_CONFIG_PATHS="$PWD" mise run openspec:verify` passes before and
after archive: managed html-design snapshot/schema, all 22 current change/spec
items, and both extension tests. `openspec validate --specs --strict --json`
passes 18/18 and `openspec validate --archived --strict --json` passes 11/11.
No archived path is passed as a direct CLI item name. Independent active plans
are unchanged against the required baseline. NOTICE and contribution policy
remain satisfied; no AI coauthor trailers or local `docs/superpowers/` artifacts
are staged.

The implementation is locally qualified and archived, but this sixth campaign
is incomplete. Parent owns independent final review, current-head CI watching,
authorized landing and verified owned checkout/local-branch/remote-branch/artifact
cleanup. This worker opens one focused PR with issue #479 and exact head/base,
then hands back without watching, merging, deleting sources/branches/checkouts,
releasing or deploying. PR head and forge state belong in the PR/issue handoff
receipt; this committed runtime checkpoint is not a claimed review/CI approval.

### Owned external artifact and recovery inventory

All external artifacts remain beneath `/private/tmp/morphir-workspace-migration/`.
`artifact-inventory.json` lists every owned file and `artifact-inventory-summary.json`
lists retained recovery paths without configuration values. At final qualification
the inventory contains 2,739 files (320,872,941 bytes), excluding the inventory's
own output files; final PR/receipt files added afterward are also owned beneath
that same root. Major groups are:

- `tmp/`: physical test/subprocess temporary roots and deliberately retained fault
  fixtures, including injected user-change and ownership evidence.
- `transactions/`, `cli/`, `discovery-verification/`: immutable red/green/probe,
  interruption and explicit-recovery receipts. Two deliberately malformed parser
  samples have no real destination operation.
- `validation/`: final 17-runner logs/expanded commands/counts;
  `validation-initial/` is explicitly an earlier checkpoint.
- `publish/`: three published hosts, build logs, separate JSON results/stderr;
  `publish-locks/`: 45 original lockfiles and byte-identical restoration proof.
- Coverage XMLs/summary, format/build/restore/OpenSpec receipts, source hashes and
  campaign-local orchestration scripts/wrapper. Checkout `bin/obj/TestResults`
  are reproducible local build artifacts, not new external cleanup roots.

There are 114 retained `.morphir-edit-*` evidence files across 37 containing
directories: 112 from generated fault fixtures and two synthetic malformed parser
samples. These are intentionally unresolved/altered test evidence, not production
operations. The exact paths and transaction IDs are in the inventory. Earlier
helper receipts reporting no unresolved journals describe their earlier checkpoint;
the later ownership/cleanup regressions intentionally retain new evidence.
No file was removed or manufactured to improve the cleanup claim. Preserve these
journals/backups and their surrounding user-change fixtures until explicitly
inspected/recovered or dispositioned by the parent. Owned cleanup therefore
remains an explicit parent obligation. Global caches/tools and unrelated
campaign artifacts are excluded from ownership.

### Current-main interaction at PR preparation

GitHub main advanced during qualification to
`0542145a4bcc0edf9c9dd2fb3122df36c73e80d0` through independent C# generation
PRs #478, #480 and #482. The owned branch retains the explicitly required starting
baseline `e3b9a053120a49e94c70c30cd4f62ef783672c64`; PR #477 remains its merged
prerequisite. No unrelated generation work is copied or modified in this branch.
The intervening production changes add a separate generation backend/reference
and model package-release notes; they do not change configuration/project/workspace
services. README/troubleshooting edits are in different sections.
`git merge-tree --write-tree origin/main HEAD` succeeds without conflicts
(integration tree `a866f8115332651717e14fe1b0a7ec4b8db3e460` at the documentation
checkpoint). This is merge compatibility evidence, not execution of a merged
checkout. The 17-runner/runtime receipts qualify this branch at its required
baseline; parent-owned current-base PR CI remains necessary for the new backend
and additional independent suites. The PR records the actual newer base separately.

## Parent policy review follow-up

Independent review of `ff5347f` identified three concrete defects: existing
workspaces bypassed original-value checks for newly adopted projects, supplied
exclusions did not prevent root conversion, and new members were reported with
existing workspace ownership/output roots before adoption. All five new public
API cases fail in `parent-review/policy-red-02.log`. An earlier malformed filter
selected zero tests and is not red evidence. All three actual CLI regressions
fail against the original executable in `parent-review/policy-cli-red.log`.

The shared coordinator now checks each newly adopted project against its original
non-adoption settings in both new and existing workspaces. Root exclusions apply
to conversion selection while root identity and necessary workspace composition
remain. Adoption effects use actual prior membership to distinguish retained and
standalone contexts. The native smoke includes these paths.

The first full IO run passed 433 of 434 cases and exposed an older success
fixture relying on the newly rejected silent backend change. Its new project
now explicitly authors the matching backend, retaining a valid successful
preservation case. Its member-local profile activation declaration was also
removed so that fixture does not rely on control metadata changing authority
through adoption. The rebuilt CLI passes the three new cases. The refreshed
full IO runner passes 434 cases. Final full-suite, coverage, runtime, CI and
independent transaction-review results remain delivery gates.

All 17 configured executable runners pass again after the parent policy fixes.
The full IO runner now passes 434 cases and tooling passes 436, including all
three new real CLI regressions; all other runner counts remain as above.
`validation/summary.json` and the refreshed individual runner logs retain exact
commands, duration and exits for this current-source checkpoint. The full
solution build passes with nine existing warnings and zero errors, and changed
C# files passed formatter processing before that build.

Final policy qualification after `19504a8` passes full formatter verification,
OpenSpec/extension verification, fresh IO coverage and all three published host
modes. The unchanged F# source maps reuse their core collector with the fresh IO
report: core 4188/4345, 96.39%; IO 2493/2559, 97.42%, recorded in
`parent-review/policy-coverage-summary.json`. Managed, trimmed and native hosts
each pass 42 workspace checks, 32 project migration checks, 44 editing checks,
228 JSON5 cases and 26 Morphir fixtures. Published stderr is empty; trimmed and
native dependency warning counts remain 44/47 with the documented explicit
warning-severity qualification. Lockfiles are restored byte-for-byte. Exact
commands and exits are in `parent-review/policy-qualification-summary.json` and
the refreshed publish summary. Original retained recovery evidence is untouched;
new parent validation fault-fixture evidence also remains for final disposition.
Independent transaction/recovery review, current-head CI, landing and audited
cleanup remain outstanding campaign gates.

### Ancestor recovery discovery race

Linux CI on `ff5347f` fails the valid empty-workspace no-op CLI scenario with
`config.edit_io`. Its generic renderer does not expose the original exception,
so the retained CI log alone cannot identify the exact failing filesystem call.
A focused concurrent probe then reproduces a relevant native recovery race:
`FindRecoveryJournals` fingerprints every ancestor directory through `Observe`,
and an unrelated sibling disappearing during that fingerprint throws
`FileNotFoundException`. `parent-review/recovery-race-red.log` records this
failure before production changes.

Journal discovery now checks directory kind and refuses linked ancestors without
fingerprinting unrelated contents. Source/target/listing observation and stale
plan guards retain their original strict behavior. The focused regression scans
while unrelated files churn; a companion test preserves linked-ancestor refusal.
Both pass in `parent-review/recovery-race-green.log`. The full refreshed IO runner
passes 436 cases. Current-head Linux CI still qualifies the original CI symptom.

All 17 runners pass again after the recovery scan correction: IO 436, tooling
436, core configuration 177 and Microsoft 22; the other counts are unchanged.
The full solution build and changed-file formatter pass. The refreshed full
runner logs and summary retain current-source execution evidence. This is a
verified native discovery race fix; current-head CI remains required before
claiming the Linux no-op failure resolved on the runner.
