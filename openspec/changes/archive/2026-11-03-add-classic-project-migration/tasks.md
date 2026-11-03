# Tasks

Implementation prerequisites: `add-project-workspace-discovery`, `add-config-editing` must be merged to main first.
Read [the design](design.html) and change-local specs before starting. Existing cache-only APIs remain compatible. Use red-green-refactor and the test projects' configured runners: Morphir.Configuration.Tests is Expecto; tooling is executable TUnit plus Reqnroll. Do not assume plain VSTest discovers every suite.

## 1. Campaign setup

- [x] 1.1 Prepare an owned worktree/branch after discovery and config editing land; verify prerequisite landed SHAs, clean status, and campaign ownership/tracking.

## 2. Mapping and loss accounting

- [x] 2.1 Write failing BDD/API migration fixtures for classic fields, Elm/checkpoint defaults, same-project output layouts, raw unsupported dependencies/decorations, conflicting canonical fields, and existing TOML values; confirm red before adding migration code.
- [x] 2.2 Implement a shared single-project migration planner that reuses readers/editor plans, preserves supported extras and source-path meaning, and blocks every unaccounted value; verify mappings/conflicts/idempotence and document before/after TOML.

## 3. Independent verification and apply

- [x] 3.1 Write failing cases where retained legacy JSON or ambient overrides would hide a wrong candidate, including standalone and member context, defaults, paths, and expected provenance changes; confirm red before equivalence implementation.
- [x] 3.2 Implement comparison with the converted source excluded and machine/personal/environment/CLI/active-profile overlays removed, retaining base workspace context; verify the equivalence suite and document exactly what is compared.
- [x] 3.3 Apply only verified plans through the transaction service with source/target/workspace drift checks; verify dry-run writes nothing, sources remain byte-identical, failed plans preserve files, and repeats do not rewrite, with migration/recovery docs updated.

## 4. Project CLI and integration

- [x] 4.1 Add failing then passing project migrate subprocess scenarios for directory/manifest/current inputs, --output/--dry-run/--json, IR-input rejection, redaction, and failure categories; register a thin project command provider and verify the dedicated CLI reference and walkthrough.
- [x] 4.2 Run format/build/analyzers, migration/shared/tooling and contract/coverage suites plus a non-CLI migration host smoke; verify no config migrate alias, IR conversion, workspace membership edit, or classic-source deletion was introduced and record results.

## Workflow follow-up

These are required campaign-completion steps after tracked implementation work, not optional housekeeping. Checking every box above is not campaign completion.

- Open one focused PR for this campaign, link the actual tracking issue and PR in the host, and record head/base SHAs. Keep docs, tests, NOTICE/contribution requirements, and commit trailers consistent with repository policy; do not stage local docs/superpowers artifacts.
- Maintain the PR through current-head CI and review fixes using the host's PR watch rather than polling. Refresh relevant checks after interacting base changes; no admin bypass, force push, release, or deployment is implied.
- Once implementation is complete, sync/archive this OpenSpec change into the same delivery and validate the archived main specs, design, and task evidence before final PR approval. Update links/status where archive paths change.
- Land the PR on main under repository-required checks/review. Verify authoritative merged state and that the landed SHA is reachable from main; record the original validated head and its squash/rebase relationship where applicable.
- Verify the campaign worktree is clean and contains no unique unmerged work. Remove only this campaign's worktree and local/remote branches through the owning workspace/forge mechanisms, checking for automatic remote deletion first. Confirm their absence; do not force-remove or broadly prune.
- Remove owned temporary build/test artifacts only when they contain no unique work. Preserve unrelated worktrees/branches and any unresolved recovery journal/backups; resolve or explicitly report a cleanup blocker rather than declaring completion.
- Deliver a receipt with PR URL, landed SHA, required-check/review evidence, archive/spec-sync location, and separate worktree/local-branch/remote-branch cleanup results. Keep the campaign incomplete while any landing or owned-cleanup obligation remains.
