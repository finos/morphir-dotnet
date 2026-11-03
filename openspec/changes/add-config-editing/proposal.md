# Proposal

## Why

Inspection alone leaves users and other hosts to edit files by hand without knowing which scope they changed. Shared editing should preserve documents and report the resulting effective value.

## What Changes

- Add shared target selection, edit planning, validation, and apply operations; expose config set/unset as thin CLI adapters.
- Support typed values and one project/workspace/personal/global/system/explicit TOML target; report ambiguity and inherited or overridden results.
- Preserve TOML comments and unrelated text, promote lower-format-only scopes to their corresponding TOML primary, and keep YAML/JSON unchanged.
- Add read-only edit previews and a preconditioned file-transaction adapter with per-file atomic replacement, optimistic concurrency, and explicit recovery reporting.
- Do not implement migration here; editing is the write foundation for the following changes.

## Capabilities

### New Capabilities

- `configuration-editing`: Typed scoped TOML edits, previews, concurrency checks, and config set/unset.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: [`add-config-inspection`](../archive/2026-11-03-add-config-inspection/proposal.md).
- GitHub tracking issue: [#473](https://github.com/finos/morphir-dotnet/issues/473).

## Impact

Add shared edit models/planner and adapter-owned TOML editing/transaction services; extend config scenario and tests. Existing safe-tree-publication governs generated output, not user-file transactions, and is not changed or given stronger guarantees.

Planning only. No runtime behavior or existing public API changes in this proposal commit.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. This planning operation itself does not create a PR, merge code, or delete any artifacts.
