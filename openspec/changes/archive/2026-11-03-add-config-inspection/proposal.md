# Proposal

## Why

Users cannot inspect the resolved settings or their winning sources through the current CLI. Hosts also need a standard .NET configuration adapter while retaining Morphir's richer canonical document.

## What Changes

- Add shared get/list/show/path/explain services and stable typed results, redaction, scoped queries, and deterministic key paths.
- Expose morphir config get/list/show/path/explain through an additive configuration scenario using existing command-provider and stdout helpers.
- Provide AddMorphirConfiguration on a dedicated Microsoft adapter library; resolve Morphir semantics before projecting to IConfiguration.
- Document input grammar, JSON results, origin/scope options, profile/isolation controls, and exit codes; help performs no discovery.
- Do not reconfigure unrelated application settings or claim existing IR commands consume every new setting.

## Capabilities

### New Capabilities

- `configuration-inspection`: Effective/scoped queries, explanations, source reports, and config read commands.
- `dotnet-configuration-integration`: A read-only .NET configuration projection with explicit host registration.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: [`add-shared-configuration`](../2026-11-02-add-shared-configuration/proposal.md), [`add-project-workspace-discovery`](../2026-11-03-add-project-workspace-discovery/proposal.md).
- GitHub tracking issue: [#468](https://github.com/finos/morphir-dotnet/issues/468). See the [delivery journal](../../../../docs/content/docs/contributing/design/prds/config-inspection-delivery.md).

## Impact

Add shared query services, a Microsoft.Extensions.Configuration adapter, CLI scenario/DTO serialization, and focused API/CLI tests. Register through MorphirCliBuilder/ICommandProvider. Use explicit/source-generated binding and serialization for Native AOT.

Implementation, tests and consumer documentation are delivered in this campaign. Existing APIs remain compatible; parent review, landing and cleanup remain campaign-completion gates.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. This planning operation itself does not create a PR, merge code, or delete any artifacts.
