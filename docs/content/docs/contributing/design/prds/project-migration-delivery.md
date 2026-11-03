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
