# Proposal

## Why

Loading a classic morphir.json does not let a project adopt the new project system. Users need a reviewed conversion that preserves legacy defaults and works after the classic source is excluded.

## What Changes

- Add a shared project migration planner/apply operation and morphir project migrate [path] with dry-run, JSON reporting, and same-project TOML output selection.
- Map classic fields, materialize Elm/checkpoint defaults, preserve paths and supported extras, and block unmapped values or conflicting target entries.
- Verify before/after project-scoped values and path meaning with the legacy source excluded from the candidate and ambient overrides excluded from both.
- Retain the classic source unchanged, detect source/target drift, and make repeat application a no-op.
- Own migration under project; do not introduce config migrate, workspace membership edits, source-code conversion, or IR migration.

## Capabilities

### New Capabilities

- `classic-project-migration`: Loss-checked classic project adoption with independent-equivalence verification.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: [`add-project-workspace-discovery`](../add-project-workspace-discovery/proposal.md), [`add-config-editing`](../add-config-editing/proposal.md).
- GitHub tracking issue: not yet assigned; link the actual issue before opening an implementation PR. No external issue is created by this planning work.

## Impact

Add shared migration contracts/services and project CLI registration after the editing/discovery APIs; add standalone/member fixture tests and a migration reference. Uses the editor transaction boundary without depending on System.CommandLine in shared code.

Planning only. No runtime behavior or existing public API changes in this proposal commit.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. This planning operation itself does not create a PR, merge code, or delete any artifacts.
