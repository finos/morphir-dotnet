# Tasks

Implementation prerequisites: `add-shared-configuration`, `add-project-workspace-discovery` must be merged to main first.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare an owned worktree/branch after shared configuration and discovery are merged; verify both landed SHAs are on main, baseline status is clean, and tracking records are linked.

## 2. Shared queries

- [x] 2.1 Write failing shared-API/BDD cases for effective versus scoped values, quoted path segments, missing versus empty values, source reports, explanations, stable ordering, and redaction; confirm red before adding query behavior.
- [x] 2.2 Implement get/list/show/path/explain result contracts and query services outside Morphir.Tooling; verify those cases pass without constructing the CLI and publish the key-path/inspection API documentation.

## 3. Microsoft configuration adapter

- [x] 3.1 Write failing .NET host tests for complete/empty arrays, nested values, canonical metadata access, case/separator collisions, and opt-in registration; confirm red before adding the adapter.
- [x] 3.2 Add Morphir.Configuration.Microsoft with AddMorphirConfiguration over one resolved result and explicit/source-generated binding support; verify host/options tests, a trimmed/AOT published smoke host, and the documented consumer example.

## 4. CLI inspection

- [x] 4.1 Write failing Reqnroll/subprocess scenarios for every config read command, scopes/origin flags, controls, no-I/O help, JSON warnings, redaction, and 0/1/2 exit codes; confirm red with the configured tooling test runner.
- [x] 4.2 Register a thin configuration scenario through MorphirCliBuilder/ICommandProvider, add source-generated output DTO serialization and stdout-helper formatting, then verify BDD/subprocess cases and update CLI/reference/walkthrough docs with actual result schemas.
- [x] 4.3 Run format/build/analyzers, shared and tooling suites, applicable contract/coverage checks, and smoke the default CLI registration; verify existing IR/management commands remain compatible and record results.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
