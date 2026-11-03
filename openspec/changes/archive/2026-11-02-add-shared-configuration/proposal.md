# Proposal

## Why

The current model contains cache paths only, and parsing and resolution live in tooling. Other hosts need the same typed settings and MoonBit-compatible semantics without depending on CLI construction.

## What Changes

- Add immutable documents, source identities, diagnostics, validation, and pure resolution over explicitly supplied sources.
- Read TOML, YAML/YML, and JSON/JSON5; normalize classic manifest fields while retaining diagnostics about ignored legacy shapes.
- Implement format/source precedence, complete-array replacement, environment mapping, profile controls and ordering, and winning origins.
- Keep existing cache-only models and legacy resolver behavior available; new contracts are additive. Automatic filesystem discovery and CLI commands land in later changes.
- Pin upstream compatibility fixtures and document the host-independent API and supported parser profile.

## Capabilities

### New Capabilities

- `configuration-documents`: Typed TOML/YAML/JSON5 documents and classic manifest normalization.
- `configuration-resolution`: Ordered source merging, profiles, validation, provenance, and diagnostics.

### Modified Capabilities

None. Existing publication and virtual-file requirements remain unchanged.

## Tracking

- [Layered configuration PRD](../../../../docs/content/docs/contributing/design/prds/layered-configuration.md).
- Implementation prerequisites: none.
- GitHub tracking issue: [#458](https://github.com/finos/morphir-dotnet/issues/458).

## Impact

Extend src/Morphir.Configuration and tests/Morphir.Configuration.Tests; add a reusable format-reader adapter library outside the CLI. Reuse CsToml and YamlDotNet where compatible; qualify JSON5 parsing and all readers for trimming/AOT. No IR/JSON encoding changes.

Implemented through additive document APIs and Morphir.Configuration.IO, with
Expecto, executable TUnit/Reqnroll, pinned grammar/corpus fixtures, coverage and
trimmed/native runtime qualification. The existing public cache APIs are unchanged.
Pinned dependencies retain linker/AOT diagnostics; strict warning-free trimming is
not claimed. See the delivery evidence and smoke-host README for exact results.

## Campaign completion

This change is one delivery campaign, with one PR based on current main after its prerequisites have landed. A checked implementation task list or an open PR is not campaign completion.

1. Implement and validate in a campaign-owned worktree/branch, carrying this change's tests and documentation.
2. Finish review fixes and required checks on the final PR head. Sync/archive this change's specifications into the same delivery, validate that result, and keep the PR description aligned with the final scope.
3. Merge the PR to main under repository review/CI rules. Verify the forge reports merged and the landed commit is reachable from main; record both the PR head and landed SHA, including squash/rebase mapping when applicable.
4. Verify the campaign worktree is clean and contains no unmerged or uniquely held user work. Remove that campaign worktree and its local/remote branches through the applicable workspace/forge mechanisms. Confirm automatic remote branch deletion before trying to delete it again.
5. Record a completion receipt with PR URL, landed SHA, verification/review state, archive location, and worktree/local-branch/remote-branch cleanup results.

Preserve unrelated worktrees/branches and any unmerged work or recovery evidence. No blanket pruning, forced removal, force-push, admin bypass, or release publication is implied. If landing or owned-artifact cleanup remains outstanding, report the campaign as incomplete and name the remaining item. Parent coordination owns review, landing, CI watching and cleanup after implementation PR handback.
