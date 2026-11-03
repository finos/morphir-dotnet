# Tasks

Implementation prerequisites: none.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare a campaign-owned worktree/branch from current main, record its baseline and ownership, and link the real tracking issue; verify clean status and the absence of unmet implementation prerequisites.
- [ ] 1.2 Pin the relevant finos/morphir and morphir-moonbit schema/corpus revisions in fixture provenance, including profile, YAML, null/shape, and manifest behavior; verify fixture hashes and record deliberate policy differences without changing IR fixtures.

## 2. Typed documents and format readers

- [ ] 2.1 Write failing Expecto/API and BDD contract scenarios for equivalent formats, typed values, full JSON5 syntax, duplicate keys, malformed inputs, and legacy/canonical conflicts; run the focused configured runners and retain the red results.
- [ ] 2.2 Add immutable typed document/source/diagnostic contracts in Morphir.Configuration and format readers in a new Morphir.Configuration.IO library; implement only enough for those tests to pass and verify the core has no CLI/framework/file-I/O dependency.
- [ ] 2.3 Qualify CsToml/YamlDotNet reuse and the full JSON5 reader against pinned fixtures and trimming constraints, preserve raw legacy fields for migration loss accounting, and document supported reader behavior; verify equivalent values/origins and invalid-input diagnostics in the reader suite.

## 3. Resolution and profile semantics

- [ ] 3.1 Write failing behavior cases for the full source ladder, root/member/machine subtree filters, complete/empty arrays, shape replacement, secrets as leaves, selected-file deduplication, and environment conflicts; verify the focused suite fails before implementation.
- [ ] 3.2 Implement pure ordered resolution, validation, source reports, winning-leaf/array origins, and deterministic environment normalization; verify those cases pass and document explicit input ownership and scope inheritance.
- [ ] 3.3 Add red-then-green cases for profile selection, includes/groups, inactive cycles, bounds, controls, disabled loading, and mixed-format warning suppression; verify profile order and origin parity against the pinned corpus and update the profile guide in this group.

## 4. Compatibility and integration

- [ ] 4.1 Keep existing cache models, Merge functions, CiProfileMode, and legacy resolver callers working through additive contracts; run existing Morphir.Configuration.Tests and tooling configuration tests and document the opt-in transition.
- [ ] 4.2 Verify a non-CLI in-memory host resolves the same fixtures as the reader adapter, run format/build/analyzers and applicable configured tests/coverage, and publish/run a trimmed adapter smoke host; record exact commands and results before PR review.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
