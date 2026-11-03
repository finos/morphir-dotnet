# Classic project migration delivery journal

Tracking issue: [#476](https://github.com/finos/morphir-dotnet/issues/476).
Change: `add-classic-project-migration`, campaign 5 of six.

## Ownership and baseline

- Owned checkout: `/Users/damian/.t3/worktrees/morphir-dotnet/t3-5e544978`.
- Owned branch: `t3/classic-project-migration`, externally renamed from the prepared `t3/5e544978` at the same baseline. Initial status was clean.
- Required and verified baseline: `56d9de917f6c3035c8a2f994f49dfc96294619d8`.
- Discovery PR #467 landed at `23d274d4c87b367fdc79bd21843a9fdf5d74b13d`, an ancestor of the baseline.
- Editing PR #475 landed at the baseline. Its [completion receipt](https://github.com/finos/morphir-dotnet/pull/475#issuecomment-6610672018) confirms validated tree, checks, archive and cleanup.
- OpenSpec `html-design` was ready, 0/8 implementation tasks. All returned context files, including the full design, were read before implementation.
- Parent owns independent final review, CI watching, authorized landing and cleanup. This worker will not watch, merge, delete branches/checkouts or begin workspace migration. Campaign completion remains pending those parent gates.

## Work and evidence ownership

Two Codex helpers own the pure F# planner/tests and CLI/provider/subprocess tests. The lead owns IO coordination, integration, documentation, qualification and delivery. Helpers preserve one another's edits and do not run heavy builds independently.

All owned external artifacts are beneath `/private/tmp/morphir-project-migration/`. Physical `tmp/` is used for TMPDIR/TEMP/TMP. Builds use `DOTNET_PROCESSOR_COUNT=2`, serial MSBuild and disabled parallel build/restore. Executable Expecto uses `--sequenced`; TUnit uses `--maximum-parallel-tests 1`. One heavy job runs at a time. Unresolved recovery evidence must be preserved.

## Checkpoints

- Setup verified clean checkout, branch ownership, prerequisite ancestry and authoritative editing receipt. Actual tracking issue #476 exists before coding. No runtime changes at this checkpoint.

- Mapping RED: `core-red-mapping.log` exits 1 for the missing pure migration API before its implementation. IO BDD RED: `io-red.log` exits 1 for the missing preview service before its implementation.
- CLI RED: the actual CLI output was rebuilt successfully, then `cli-red-run.log` runs 27 subprocess BDD cases, all failing for the missing project command. An earlier class-name filter selected zero tests and is not evidence; the corrected category filter is `/*/*/*/*[Category=ProjectMigration]`.
- Equivalence RED: after mapping compiled, `core-red-verification-02.log` fails for the missing `ProjectMigration.verify` before verification implementation. A preceding reserved-word syntax failure was fixed and is not counted as behavioral evidence.
- Base workspace pipeline settings may override classic low-priority checkpoint defaults. `planWithContext` uses the base-only before snapshot to materialize the effective legacy defaults, preserving member behavior rather than forcing checkpoint over a declared workspace choice.

- Core GREEN: `core-green.log` passes 170/170 Expecto cases; `core-green-02.log` passes 172/172 after explicit conflict-values and base-workspace evidence regressions. `core-red-conflict.log` first proved those APIs absent. Core builds report no warnings/errors.
- IO `io-green-03.log` passes 296/296 TUnit/Reqnroll cases. Equal-value text/BOM checks preserve original spans; insertion can precede a table, so the test checks retained text rather than asserting an unchanged file prefix. Native cases cover adjacent and all supported primary layouts, source retention, repeat timestamps, source/target/workspace/alternate-primary drift and read-only/foreign-plan refusal.
- CLI `cli-green-02.log` passes 34/34 focused command and actual subprocess cases after rebuilding `src/Morphir/bin`. The preceding run passed 32 and exposed absent conflict values plus a missing option value consuming `--json`; both regressions are green. Every subprocess JSON report is validated against a fresh schema registry, including initialization and parse failures.
- A read-only host receives safe complete data through `Presentation`, while canonical plan data is explicitly separate. The planner has no CLI dependency. No existing cache-only API or owner/personal/machine-loading policy changed.

- The full IO executable now passes 303/303 cases in `validation/Morphir.Configuration.IO.Tests.log`, including migration post-install rollback and explicit retained-recovery outcomes. Fault fixtures verify transaction ownership and backup bytes before manual recovery, then remove only resolved owned evidence. No unresolved recovery artifact remains from those checks.
- Source/target/relevant workspace evidence is rechecked by the existing transaction service. Application rejects unverified, read-only and foreign-service plans. Preview never acquires writer coordination or stages bytes. The classic source is never a transaction destination.

## Final local qualification

All 17 configured executable runners passed serially. `validation/summary.json`
records each exact command and exit status. Additional boundary tests then passed
in the final focused runners, increasing core from 172 to 175 and IO from 303 to
307 without changing production behavior. The adapter coverage invocation is a
coverage collector, not a substitute for the configured sequenced Expecto gate.

| Runner | Passed |
| --- | ---: |
| `Morphir.CodeGeneration.FSharp.Tests` | 333 |
| `Morphir.Configuration.IO.Tests` | 307 |
| `Morphir.Configuration.Inspection.Smoke` | 71 checks |
| `Morphir.Configuration.Microsoft.Tests` | 22 |
| `Morphir.Configuration.Tests` | 175 |
| `Morphir.Core.Tests` | 237 |
| `Morphir.IR.Pipeline.Plugins.Tests` | 141 |
| `Morphir.IR.Pipeline.Tests` | 299 |
| `Morphir.Internal.CodeGeneration.Tests` | 25 |
| `Morphir.Ion.Tests` | 463 |
| `Morphir.Models.Tests` | 791 |
| `Morphir.Myriad.Integration.Tests` | 12 |
| `Morphir.Myriad.Tests` | 4 |
| `Morphir.SDK.CSharp.Tests` | 20 |
| `Morphir.SDK.Conformance.Tests` | 27 |
| `Morphir.SDK.Tests` | 403 |
| `Morphir.Tooling.Tests` | 397 |

The full solution build succeeds. The first broad build reports 18 existing
package/generated-code warnings, and the final analyzer-enabled build reports
11 existing warnings with zero errors. Changed-library/test builds report zero
warnings/errors. C# formatting and full solution formatter verification pass;
Fantomas formats and checks the new F# source/tests. No parser, SDK or dependency
version changes are included.

`coverage-final-summary.json` unions the final current-source reports from
`core-boundary-coverage/*/coverage.cobertura.xml` and
`io-boundary-coverage.cobertura.xml`. Core is 4178/4335, 96.38%; IO is 1553/1599,
97.12%. Both improve on the prerequisite's 96.01% and 96.94%. Added cases qualify
safe read-only presentation, equal typed floats/references, invalid Unicode,
invalid/missing source states, changing mounted targets and unstable storage.
The full Models runner includes 791 model/codec roundtrip checks. IO contracts,
JSON result-schema subprocess checks and pinned upstream fixtures also pass.
The scripts/format-all, scripts/test-all and scripts/check-contracts placeholders
listed in AGENTS.md are absent; the configured commands above supply their gates.

### Non-CLI managed, trimmed and native hosts

The IO smoke host is extended with migration and linked-source generated report
serialization. All three osx-arm64 publishes execute successfully with reflection
serialization disabled. Each reports 24 migration checks, 44 existing editing
checks, 228 official JSON5 syntax cases, 26 pinned Morphir documents and the
existing discovery/machine-root/owner checks. Published stderr is empty.
`file` identifies the native output as Mach-O 64-bit arm64.

`publish/summary.json` retains exact argument arrays, exit codes and warning
counts. Managed publishing has zero warnings. Trimmed/native publishing retains
44/47 warning lines from pinned CsToml and FSharp.Core. Each explicitly sets
`ILLinkTreatWarningsAsErrors=false`; no diagnostic is suppressed and no strict
warning-free trimming claim is made. Authored migration source has no linker/AOT
diagnostic. Per-file atomic installation and machine-local writer coordination
remain the existing limits; hosts needing distributed coordination supply it.

`publish-locks/manifest.json` records prepublish lockfile SHA-256 values, and
`restored.json` confirms every captured file restored byte-for-byte. Native
publication introduces no lockfile/dependency change. All artifacts are local
qualification outputs; no release, deployment or external publication occurred.

### Reproduction

Run one heavy job at a time from the repository root:

```sh
export DOTNET_PROCESSOR_COUNT=2 BuildInParallel=false
export TMPDIR=/private/tmp/morphir-project-migration/tmp
export TEMP="$TMPDIR" TMP="$TMPDIR"
export PATH="/Users/damian/.local/share/mise/installs/powershell/7.5.3:$PATH"
dotnet build Morphir.slnx -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
dotnet tests/Morphir.Configuration.Tests/bin/Debug/net10.0/Morphir.Configuration.Tests.dll --sequenced
dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --maximum-parallel-tests 1
dotnet tests/Morphir.Tooling.Tests/bin/Debug/net10.0/Morphir.Tooling.Tests.dll --maximum-parallel-tests 1
# Repeat the configured arguments from validation/summary.json for the other runners.
dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet
dotnet fantomas --check src/Morphir.Configuration/Migration.fs tests/Morphir.Configuration.Tests/MigrationTests.fs
```

Coverage collectors and publish commands follow the preceding configuration
editing journal and the updated IO smoke README. The owned `run-suites.py`,
`format.py` and `publish-host.py` scripts retain the exact local execution.
PowerShell 7.5.3 is selected only through the process-local PATH for package tests.
No global tool configuration changed.

### Scope and recovery audit

`no-config-alias-receipt.json` records that actual `config migrate --json` returns
structured exit 2. The implementation registers only project migrate. There is
no IR/source conversion, sibling batch, workspace adoption or membership edit.
Classic manifests remain byte-identical. Existing cache-only and public loading
APIs remain compatible; selected personal and machine ownership are unchanged.
Native recovery fault fixtures resolve their verified backups explicitly. No
unresolved recovery artifact remains from local qualification.

## Specification sync and parent handoff

OpenSpec apply reports `all_done`, 8/8 implementation tasks. Only
`add-classic-project-migration` is synchronized and archived at
`openspec/changes/archive/2026-11-03-add-classic-project-migration/`.
The new main capability is `openspec/specs/classic-project-migration/spec.md`.
Its Purpose and all nine requirement bodies with 11 scenarios are byte-identical
to the approved delta, with canonical main-spec headings. The archived task
validator passes, the original `.openspec.yaml` remains present, and the design
has balanced HTML, inline CSS and valid local links. Archive-relative prerequisite
and PRD links are repaired. Workspace migration remains active at 0/9 tasks;
only its prerequisite link changed.

`mise run openspec:verify` passes before and after archive: the managed html-design
snapshot, all 22 active/spec items and both extension tests. Strict main-spec
validation passes 17/17; archived-task validation passes 10/10, including this
archive. The CLI rejects path separators in direct item names, so archived delta
content is qualified through its exact match to the strict-validated main spec,
the pre-archive strict validation and the dedicated archived-task gate. The
unsupported direct-path command receipt is preserved explicitly, without being
counted as a successful validation.

The final PR targets `main`, still at the required baseline
`56d9de917f6c3035c8a2f994f49dfc96294619d8` when checked after qualification.
The PR body and parent handoff record the actual final head separately.
This journal is implementation delivery evidence, not CI/review approval or a
campaign-completion receipt. Parent owns independent review, CI watching,
authorized landing and cleanup. No watch, merge, branch/worktree deletion,
workspace migration, release or deployment was performed by this worker.

### Owned artifact inventory

All external artifacts are owned beneath `/private/tmp/morphir-project-migration/`:

- `tmp/`: physical temporary root used by every owned test/build subprocess.
- `validation/`: 17 configured runner logs and exact command/result summary.
- `core-coverage/`, `core-boundary-coverage/` and IO Cobertura files: collector
  evidence, with `coverage-final-summary.json` selecting the final source maps.
- `publish/`: managed, trimmed and native hosts, build logs, JSON results, stderr
  and exact argument/warning summary.
- `publish-locks/`: original lockfile bytes, SHA-256 manifest and restoration proof.
- Root-level red/green/build/format/OpenSpec receipts and reproduction scripts,
  plus PR handoff text and the generated `artifact-inventory.json`.

Generated `bin`, `obj` and TestResults within the retained owned checkout are also
campaign artifacts. Shared package/tool caches and unrelated worktrees are not
cleanup targets. No unique source work is held in external artifacts; all source,
tests, documentation and spec changes are committed in this PR. Recovery fault
fixtures were resolved explicitly; the final inventory records no unresolved
native recovery files. Preserve any later unresolved evidence during parent
review. The checkout and branch are intentionally retained for parent landing.
