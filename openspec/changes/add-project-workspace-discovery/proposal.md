# Proposal

## Why

The existing WorkspaceDiscovery prefers Git roots and never reads project declarations. Tools and embedded hosts need bounded, document-aware discovery that finds a workspace above a standalone member.

## What Changes

- Add shared discovery over host-supplied file trees with filesystem and in-memory adapters.
- Find root, hidden, and dot-config primaries, layered formats, and supplemental morphir-workspace.json; reject same-group ambiguity.
- Walk to the nearest declared workspace with nearest-project fallback; expand members/exclusions and expose explicit/default/invocation selection.
- Resolve platform system/global candidates, adjacent user files, profile candidates, isolation, and source reports for the shared resolver.
- Preserve the legacy WorkspaceDiscovery entry point for existing artifact-management callers; migrate new consumers explicitly.

## Capabilities

### New Capabilities

- `morphir-project-system`: Document-aware roots, bounded discovery, membership, project selection, and machine locations.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: [`add-shared-configuration`](../archive/2026-11-02-add-shared-configuration/proposal.md).
- GitHub tracking issue: [#466](https://github.com/finos/morphir-dotnet/issues/466).

## Impact

Add reusable discovery/loading adapters and project records near Morphir.Configuration; use existing tests/Morphir.Tooling.Tests/Configuration fixtures as legacy regression coverage. No System.CommandLine dependency in the shared project system.

Planning only. No runtime behavior or existing public API changes in this proposal commit.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. This planning operation itself does not create a PR, merge code, or delete any artifacts.
