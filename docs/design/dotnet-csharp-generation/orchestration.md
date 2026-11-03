# C# generation coordination plan

This is a static planning snapshot from before worker dispatch. Its planned statuses, executionStarted flag, settings and empty landing fields describe that snapshot, not live execution. The machine-readable [orchestration.json](orchestration.json) retains the same DAG and delivery gates. Archive follow-up repairs path references only; the legacy activeChangePath field now resolves to the [conformance archive](../../../openspec/changes/archive/2026-11-03-establish-dotnet-sdk-conformance/proposal.md). Current landing and cleanup evidence is in the [conformance delivery record](conformance-delivery.md).

## Outcome and completion rule

Every initiative ends with its PR landed into main and its own worktree cleaned up. A passing local build, finished OpenSpec checklist, draft/open PR, or PR-ready child response is an intermediate milestone. OpenSpec's isPlanningComplete/isComplete fields describe artifact completeness here; they do not prove implementation or delivery.

Completion requires the PR URL, final submitted head SHA, merge method, landed SHA confirmed on main, required checks/reviews, synced/archived specs on main, and evidence that the stopped worker's clean worktree directory and Git registration are gone. The landed SHA is the hosting service's recorded merge/squash/rebase result, not necessarily the submitted head. If archiving needs another PR, land it before marking the initiative complete. A thread becoming settled or archived does not remove its worktree.

The worker owns implementation, tests, docs and its PR-ready result. The coordinator owns cross-thread integration, PR watching, landing, archive follow-up and worktree cleanup. That allocation lets workers finish safely without trying to remove the directory in which they are running.

## Dispatch configuration

The current T3 catalog was inspected and supports this exact target:

~~~json
{
  "providerInstanceId": "codex",
  "driverKind": "codex",
  "model": "gpt-6.1-sol",
  "options": {
    "reasoningEffort": "high"
  }
}
~~~

Use orchestrator delegate_task with isolation worktree, mode async and role implementation. One child Codex delivery lead owns one change and one initiative PR; bounded auxiliary delegation follows the policy below. Put settings in target.options; do not silently substitute a provider, model or effort. Recheck capabilities before actual dispatch under the orchestration rules for that session.

Children receive no parent history. Read the matching handoff.md and include its contents, the absolute repository context supplied by the orchestrator, predecessor PR URLs and verified merge SHAs, frozen contract paths, and the shared-file coordination policy in the task. The orchestrator prepares the checkout; never create a child checkout in a shell or ask the child to set one up.

Before dispatch, commit and land the planning artifacts so they are in the selected main commit. delegate_task bases a worktree on the coordinator's current commit: verify that HEAD is the selected main commit with the prerequisites, rather than merely fetching origin/main. A dirty parent does not transfer its uncommitted artifacts.

Observed source baseline during planning: 1c8aa75f63d7ede19cb2ad75b73a6ee8690e4f6f, feat: add model generation CLI with classic and v4 F# support. Both this checkout's HEAD and its local origin/main ref pointed to that commit. Revalidate remote main before execution; this observation is not a dispatch SHA for uncommitted planning files. Preserve the existing typed model, Ion loading and generation/publication implementation when preparing the planning PR.

These are orchestrated child threads. Do not substitute a top-level thread launch unless the user explicitly requests top-level threads.

## Dependency and delivery order

1. establish-dotnet-sdk-conformance starts after planning is on main. Its PR freezes the initial inventory, fixture schema, profiles and provenance.
2. add-immutable-csharp-sdk and introduce-fsharp-sdk-package become runnable after initiative 1 is merged into main. They can run in parallel in separate worktrees.
3. add-csharp-code-generation starts after initiative 2 is merged into main and its public API/fixtures are verified. It does not depend on the F# package migration.
4. integrate-csharp-generation-cli starts after initiative 4 is merged into main and the typed generation/option/diagnostic contract is verified.
5. The deferred add-morphir-csharp-source-generator is not an active/scaffolded change. Its early compiler-host constraints and test-only smoke belong to initiative 4.

Maximum initiative-lead concurrency remains two, independently of the shared auxiliary budget. A successor starts from main with its dependency merged, not a sibling's unmerged worktree or a guessed interface. Worktree cleanup of a predecessor may finish independently after its merge; never report the predecessor complete until cleanup is verified.

## Auxiliary agents and machine resources

Each initiative delivery lead may use up to three concurrent auxiliary agents in addition to the lead, only for useful, bounded, independent work. This is a ceiling, not a request to fill slots. Keep at most two initiative leads active. Start conservatively with one auxiliary and expand only after checking CPU, memory and other resource headroom.

The initial coordinator-wide budget is three active auxiliary agents total across the delivery threads. Leads request and release slots through the root coordinator; two leads share this budget rather than each launching three. Increasing the global ceiling requires an explicit resource assessment and a recorded root-coordinator decision consistent with the user's resource constraint. The per-lead ceiling remains three.

All leads and auxiliaries use the exact Codex gpt-6.1-sol, reasoningEffort high target above. Use T3 delegate_task for explicitly configured auxiliary children, with settings in target.options. Discover orchestrator capabilities once in each thread before its first delegation, and again only under the host's error/provider-change rules. Do not silently substitute a provider, model or effort. No recursive fan-out is permitted below the auxiliary layer; each auxiliary receives a bounded brief and does not delegate again.

Only one expensive local restore, build, test, coverage, pack, publish or Native AOT job may run across this delivery at a time. The root coordinator allocates this slot to a lead or auxiliary. Bound the command's internal parallelism with supported options, and back off or serialize work under sustained CPU or memory pressure. Lightweight reading and review do not need the expensive-command slot.

Read-only review agents may share a checkout. Writing agents need explicit disjoint file ownership or orchestrator-created isolated worktrees, with serial integration into the initiative lead's checkout. Never create child worktrees in a shell. Each lead integrates auxiliary results, runs the integrated required checks, owns its single initiative PR, and reports every child task, worktree and helper to the root coordinator. The root coordinator retains PR watching, landing, archive follow-up and final cleanup. Delegated children, including initiative leads and auxiliaries, never watch PRs.

Follow each delegate's returned next instruction and event-driven completion without polling or sleep loops. Cancel no-longer-needed agents with task_cancel. Release allocated agent and expensive-command slots when work finishes. Record auxiliary task/thread identity, ownership, worktree path if any, integrated result and helper state in the delivery record.

## Ownership and shared integration

- establish-dotnet-sdk-conformance: `tests/Fixtures/MorphirSDK/**`, `tests/Morphir.SDK.Conformance.Tests/**`, `docs/design/dotnet-sdk-contract.md`; its own OpenSpec artifacts and feature PRD.
- add-immutable-csharp-sdk: `src/Morphir.SDK.CSharp/**`, `tests/Morphir.SDK.CSharp.Tests/**`, `docs/content/docs/sdk/csharp/**`; its own OpenSpec artifacts and feature PRD.
- introduce-fsharp-sdk-package: `src/Morphir.SDK/**`, `src/Morphir.SDK.FSharp/**`, `tests/Morphir.SDK.Tests/**`, `src/Morphir.CodeGeneration.FSharp/** (runtime integration only)`, `tests/Morphir.CodeGeneration.FSharp.Tests/** (runtime integration only)`, `examples/** (F# runtime references only)`; its own OpenSpec artifacts and feature PRD.
- add-csharp-code-generation: `src/Morphir.CodeGeneration.CSharp/**`, `tests/Morphir.CodeGeneration.CSharp.Tests/**`, `docs/content/docs/backends/csharp/**`; its own OpenSpec artifacts and feature PRD.
- integrate-csharp-generation-cli: `src/Morphir.Tooling/Features/Generate/CSharpGenerationBackend.cs`, `src/Morphir.Tooling/Scenarios/GenerationScenario.cs`, `src/Morphir.Tooling/Morphir.Tooling.csproj (new backend reference)`, `tests/Morphir.Tooling.Tests/Features/Generate/** (C# scenarios)`, `docs/content/docs/cli/generate.md`, `docs/content/docs/cli/troubleshooting.md`, `docs/content/docs/getting-started/** (C# examples)`; its own OpenSpec artifacts and feature PRD.

The coordinator owns serial integration of Morphir.slnx, central package/build configuration, CI, the umbrella PRD and PRD index. Workers can build/test their project directly and supply exact shared-file patches in their handoff; the coordinator applies and verifies them serially before the initiative PR is ready to merge. Do not mark a task complete merely because a proposed shared edit has been described.

Initiative 1 creates the umbrella PRD at docs/content/contributing/design/prds/dotnet-code-generation.md. Each initiative creates its own PRD before production edits. Shared index/status updates go through the coordinator. New contribution issues are created or linked during implementation; no upstream issue IDs are imported as this repo's tracking IDs.

Conformance fixtures become read-only inputs to successors. If a downstream worker finds a contract error, report the failing case and proposed correction to the coordinator; do not independently fork the fixture schema or expected values. Land the correction through its owner and rebase/restart consumers from the corrected main commit.

## Task-boundary commits and backup pushes

Create small, coherent Conventional Commits at meaningful task or task-group boundaries once the scoped work and its required checks are complete. Do not accumulate an entire initiative uncommitted. Include matching tests, necessary docs and accurate OpenSpec/PRD progress updates in each boundary commit. A commit or checked box must not imply unverified acceptance.

Follow repository-required pre-commit validation and hooks. Never bypass checks, use --no-verify, or mark failing/incomplete work complete to create a checkpoint. If required checks are unavailable, preserve the work and report the concrete blocker. Stage only explicit owned paths; never blanket-stage unrelated work, secrets or transient build artifacts.

Push completed boundary commits promptly to the initiative feature branch, normally and without force, for off-machine durability. Do not wait for PR readiness to back up completed work. After every push to an existing PR, call link_pull_request for that full PR URL as the next tool call. Keep the final checked head SHA and its evidence current after each push; checks on an earlier head do not prove the new head.

Before a handoff, rebase/merge, context transition or cleanup, ensure completed work is committed and pushed. Identify unfinished or uncommitted work explicitly and preserve it. Auxiliary writers need disjoint ownership or orchestrator-created isolated worktrees. Isolated auxiliaries deliver coherent committed results before integration; the lead integrates and pushes them into the initiative branch before auxiliary worktree cleanup. Shared-checkout agents coordinate a single Git writer so one agent cannot stage or commit another's unfinished files.

Keep task-boundary commits on the feature branch during delivery. Multiple commits are compatible with the preferred squash merge; record the final submitted head and the separate hosting-service-recorded landed squash SHA on main.

## PR lifecycle

Squash merge is the user's preferred merge method for this work, subject to repository settings and any instruction on the particular PR. Record the actual method. A squash creates a new commit: neither the worker head SHA nor its original commits need to be ancestors of main. Keep headSha as the final submitted PR head and mergeSha as the hosting service's recorded landed commit; never overwrite one with the other.

Verify landing by checking that the exact initiative PR is merged into main, obtaining its recorded landed SHA, refreshing main, and confirming that landed SHA is reachable from main. Required CI/review evidence must refer to the final submitted head. For a squash, that proves landing without a head-ancestry check. The same PR-based verification supports a merge commit or rebase when selected. Do not substitute git branch --merged, original commit ancestry, patch similarity, a closed-but-unmerged PR or a deleted remote branch for merged-PR evidence.

Successor worktrees start from refreshed main containing the predecessor's landed SHA. Contract/provenance handoffs record that landed SHA as the main-based reference rather than requiring a worker commit to survive squashing. Apply the same rule to the planning PR and any archival follow-up PR.

Use planned, ready, implementing, pr-open, waiting-checks-or-review, ready-to-land, merged, archived, cleanup-pending and complete as separate states. Keep a local delivery record with task ID, thread link, base/worktree identity, PR URLs, SHAs and validation evidence. Before the first archive, persist state in the umbrella PRD or a local delivery record outside change directories; integrate checked-in status updates serially so parallel workers do not race to rewrite this manifest.

Each worker follows the explicit apply workflow and red/green/refactor, preserves IR/JSON contracts, and records targeted and repository-required full test/coverage/format results before committing. Commits have no AI coauthor trailers. The worker opens/pushes the initiative PR and calls link_pull_request as its next tool call for that PR.

The worker returns a PR-ready result to the coordinator; delegated children never watch PRs. The coordinator handles review comments, requested fixes, merge conflicts and serial shared-file integration. Required checks and reviews must pass before landing. Respect the user's requested merged-to-main outcome and the applicable repository/approval rules. Do not enable GitHub auto-merge or publish releases unless separately authorized.

When waiting for PR checks or reviews, the coordinator uses watch_pull_request and event-driven continuation, never sleep loops or gh --watch. Follow each delegate's returned next instruction; do not poll running delegates. After a merge, verify the hosting service's merged state and the recorded landed commit's inclusion in main before unlocking successors. Release PR watches when the initiative is finished.

Sync and archive the initiative through its OpenSpec workflow. If archive is after the implementation merge, use and land a small archival follow-up PR. Preserve the active/archived artifact locations in the delivery record and update cross-change links in that archival PR. This durable plan remains at docs/design/dotnet-csharp-generation/ when an individual change folder moves.

## Worktree cleanup

Cleanup is the coordinator's responsibility after the worker is idle and the initiative and archival PRs are confirmed merged with their recorded landed commits on main. Capture the lead's and every auxiliary's exact worktree path when created. After auxiliary results are integrated, stop or cancel the auxiliary and its owned helpers and release its file handles. Cleanup evidence covers every auxiliary worktree/helper as well as the lead, using the same clean-status, last submitted-head, landed-PR and squash-safe checks where applicable. An auxiliary without its own PR records its final delivered commit and integration into the initiative PR; compare its clean HEAD to that recorded commit and verify its results in the landed initiative PR. Do not invent an auxiliary landed PR. Never remove the shared coordinator checkout or discard uncommitted work.

From outside the worker worktree, verify the resolved absolute path is the recorded owned path, belongs to the intended repository/worktree root and is not the coordinator or another worker's checkout. Check Git status including untracked files and confirm the worker HEAD matches its last recorded submitted PR head, with no extra local commits. Preserve any unexpected work and keep cleanup pending until it is accounted for. A clean branch can legitimately remain absent from git branch --merged after a squash; that does not invalidate the merged-PR evidence. Never force-delete or recursively remove a guessed path.

Prefer an available host worktree-removal operation. If none exists, use native Git worktree removal from the coordinator's external checkout, without force. Verify both the path's absence and removal from Git's worktree list. Thread archival alone is insufficient. The currently exposed T3 catalog lists worktree discovery/creation but no removal tool, so the native-Git fallback may be needed.

Worktree removal does not require deleting its branch. Branch deletion and release publication retain their explicit authorization requirements; do not use automatic delete-branch switches as a cleanup shortcut. If cleanup cannot finish, report merged-but-cleanup-pending rather than complete.

## Worker handback

The handback must include the change ID, PR URL, head SHA, exact validation commands/results, review/check state, changed public contracts, migration notes, any shared-file edits still needed, lead worktree identity, all auxiliary tasks/worktrees and integrated results, slot releases, and owned running helpers. A child stops once its PR-ready handoff is delivered; the coordinator then completes landing and cleanup.

The child's final line follows the host contract: PR <full URL>, head <SHA>, checks and reviews <state>.

The coordinator's final initiative result adds actual merge method, landed mergeSha, main verification, archive location, and worktree-directory/registration cleanup evidence. Retain the distinct final submitted headSha and its check/review evidence. No initiative is complete while any required field remains pending.

## Local machine constraints

Use captured PowerShell processes and the native Git/PowerShell executables from AGENTS.md. Suppress background windows. Terminate and verify an entire child process tree before retrying a timed-out command. Do not run bd dolt push on this machine because of the observed process storm. This planning action does not initialize a Beads database; these OpenSpec artifacts and the delivery record provide durable handoff context.
