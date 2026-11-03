# Tasks

Implementation prerequisites: `add-shared-configuration` must be merged to main first.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare an owned worktree/branch from current main after add-shared-configuration lands; verify its merged SHA is reachable, the worktree is clean, and the tracking/ownership receipt is recorded.

## 2. Document-aware roots

- [x] 2.1 Write failing BDD/API cases for every primary layout/extension, supplemental workspace JSON, same-group ambiguity, nearest workspace/project fallback, file inputs, Git-only roots, and boundaries; confirm red using the adapter test runner.
- [x] 2.2 Implement the shared snapshot/request-based discovery policy plus filesystem and in-memory loading adapters without System.CommandLine references; verify the root and ambiguity cases pass and update discovery examples in the guide.
- [x] 2.3 Add red-then-green cases for inaccessible candidates, absent optional inputs, mounted boundaries, and discovered links; verify no reads escape the host boundary and source reports distinguish misses from failures, with documented adapter obligations.

## 3. Members and scoped sources

- [x] 3.1 Write failing membership/selection cases for globs/exclusions, virtual/root projects, literal missing/invalid members, duplicate names, path-before-name selection, deepest invocation, defaults, and workspace output ownership; confirm red then implement and verify each group.
- [x] 3.2 Implement platform system/global candidates, MORPHIR_HOME/XDG handling, same-group global ambiguity, adjacent override/profile stems, and isolation with parameterized platform tests; verify ordering through the shared resolver and document machine locations.
- [x] 3.3 Document the standalone/member API and preserve the old WorkspaceDiscovery behavior for ManagementScenario and artifact callers; run the existing workspace/artifact regression suite and prove native/in-memory results match.

## 4. Integration verification

- [x] 4.1 Run configured focused suites plus the required broader checks, format/build/analyzers, boundary/roundtrip contracts, and coverage for touched libraries; verify no existing consumer silently switched to the new root policy and record results.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
