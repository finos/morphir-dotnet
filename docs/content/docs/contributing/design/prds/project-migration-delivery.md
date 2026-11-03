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
