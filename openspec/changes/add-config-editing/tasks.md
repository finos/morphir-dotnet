# Tasks

Implementation prerequisites: `add-config-inspection` must be merged to main first.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare an owned worktree/branch after add-config-inspection is merged; verify its landed SHA, clean baseline, and recorded worktree/branch ownership.

## 2. Targets and plans

- [ ] 2.1 Write failing shared-API/BDD cases for scope selection, ambiguous defaults, quoted key paths, typed scalar/composite inputs, set/unset inheritance, invalid values, and no-write previews; confirm red before implementation.
- [ ] 2.2 Implement shared target selection, immutable edit plans, validation, and resulting-effective-value explanations; verify the plan suite through an in-memory host and update scope/value/preview documentation.

## 3. TOML editing and application

- [ ] 3.1 Write failing roundtrip/fault cases for comment-preserving changes, unrelated values, JSON/YAML-only promotion, explicit non-TOML targets, unchanged writes, and stale source/target or alternate-primary observations; confirm red before the writer.
- [ ] 3.2 Implement the TOML syntax-preserving editor and adapter-owned preconditioned transaction service with serialized overlapping writers and per-file atomic replacement; verify those cases pass and document preconditions and host write guarantees.
- [ ] 3.3 Inject staging/replacement/rollback faults and verify typed outcomes, retained recovery evidence only when needed, cleanup after success/complete rollback, and no silent overwrites; document recovery behavior with the failing and passing fault-suite results.

## 4. CLI editing and integration

- [ ] 4.1 Add red-then-green subprocess scenarios and thin config set/unset adapters for --type, --dry-run, scoped targets, JSON, inherited values, overridden writes, and exit codes; verify stdout is parseable and update CLI/walkthrough examples in this group.
- [ ] 4.2 Run format/build/analyzers, configuration/editor and tooling suites, contract/coverage checks, and concurrent-writer tests; verify no generated-output publication requirements were weakened or repurposed and record results.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
