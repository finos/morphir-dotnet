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
