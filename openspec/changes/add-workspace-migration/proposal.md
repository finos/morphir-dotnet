# Proposal

## Why

A repository can contain several classic projects without any declared Morphir workspace. Users need one plan that adopts the workspace and its projects consistently rather than manually assembling member files.

## What Changes

- Add shared bounded classic-project inventory and a workspace migration plan, exposed by morphir workspace migrate [directory].
- Operate before a new workspace exists; show deterministic project candidates, exclusions, nested workspace boundaries, and existing migrated members.
- Reuse the project migrator, propose explicit member paths and root/virtual workspace identity, and preserve existing memberships and root settings.
- Validate every project and the aggregate workspace before any destination replacement; stage writes and journal rollback/recovery without claiming cross-file crash atomicity.
- Retain all classic sources, report conflicts and incomplete rollback explicitly, and make repeat runs no-ops.

## Capabilities

### New Capabilities

- `workspace-migration`: Bounded multi-project adoption, reviewed membership, and coordinated migration application.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: [`add-classic-project-migration`](../archive/2026-11-03-add-classic-project-migration/proposal.md).
- GitHub tracking issue: [#479](https://github.com/finos/morphir-dotnet/issues/479).

## Impact

Add shared inventory/aggregate planner and workspace CLI commands; extend the transaction adapter to coordinate a bounded batch with journaled recovery. Cover filesystem and in-memory hosts; do not alter the generated-output publication capability.

Planning only. No runtime behavior or existing public API changes in this proposal commit.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. This planning operation itself does not create a PR, merge code, or delete any artifacts.
