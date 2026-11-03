# Tasks

Implementation prerequisites: `add-classic-project-migration` must be merged to main first.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare an owned worktree/branch after add-classic-project-migration lands; verify its merged SHA, clean baseline, and recorded campaign tracking/ownership.

## 2. Inventory and aggregate planning

- [ ] 2.1 Write failing BDD/API cases for roots with no workspace, include/exclude patterns, default skipped directories, nested workspace boundaries, discovered links, existing migrated members, virtual/root projects, and stable ordering; confirm red before inventory implementation.
- [ ] 2.2 Implement bounded shared inventory and aggregate plans by composing the project migrator, preserving existing membership/defaults/settings and using explicit member paths; verify the inventory/planning suite and document the adoption command's scan rules.
- [ ] 2.3 Add failing then passing verification of project conversion in a common proposed workspace context, with separately reported ownership/membership/selection/output-root effects; verify source paths/non-adoption settings stay equivalent and document that generated files are not moved.

## 3. Coordinated application

- [ ] 3.1 Write failing cases for a blocked member, target/source changes, conflicting overlapping writers, staging failure, partial replacement failure, rollback failure, and interrupted journals; verify no destination is replaced before complete preflight/staging.
- [ ] 3.2 Implement aggregate application using ordered writer coordination, input preconditions, a durable recovery journal, and rollback attempts; verify full rollback or explicit recovery-required outcomes, never claimed cross-file crash atomicity.
- [ ] 3.3 Implement explicit journal inspection/recovery through the shared transaction API and guard overlapping writes while recovery is pending; verify success/complete rollback removes owned transaction artifacts and incomplete recovery preserves evidence, with recovery docs in this group.

## 4. Workspace CLI and integration

- [ ] 4.1 Add red-then-green workspace migrate subprocess scenarios for bounded directory/current roots, repeated include/exclude controls, --dry-run/--json, blocked/no-op results, and absence of config migrate; verify thin command registration and the workspace reference.
- [ ] 4.2 Run format/build/analyzers, shared/project/workspace/tooling suites, aggregate fault/concurrent-process and contract/coverage checks, and a non-CLI batch preview; verify repeat runs preserve bytes/timestamps and sources, then record results.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
