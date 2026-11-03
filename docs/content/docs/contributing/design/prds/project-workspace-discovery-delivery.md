# Project and workspace discovery delivery

Campaign 2 tracks [issue #466](https://github.com/finos/morphir-dotnet/issues/466).
Owned checkout: `/Users/damian/.t3/worktrees/morphir-dotnet/t3-2ee0222b`.
Branch: `t3/add-project-workspace-discovery`.
Prepared and current main baseline: `3d5b41c0f839c7b2648403ad6aba5c5644f7dfb5`.
The initial checkout was clean. Its tree is
`66609771da2eb88ac9e3cab3c21b6e8d201a6748`, the verified squash tree of
[prerequisite PR #461](https://github.com/finos/morphir-dotnet/pull/461).
The [parent completion receipt](https://github.com/finos/morphir-dotnet/pull/461#issuecomment-6598647710)
records campaign 1 landing and cleanup, including its 33 reproducible temporary
artifacts. That tree is a tree object, not a commit ancestry identifier.

The parent owns PR review/check watching, merge and cleanup. This worker delivers
one reviewable PR, without watching, merging, deleting branches/worktrees,
force-pushing, publishing or deploying. Campaign completion remains pending until
parent landing and cleanup receipts exist. Future campaigns remain active.

## Validation checkpoints

- Initial BDD scenario describes a member primary below a workspace. The capped
  adapter test build fails with CS0246 for the absent `InMemoryProjectTree` and
  `ProjectDiscoveryResult` APIs. No discovery implementation exists at this red
  checkpoint. Logs are in `/private/tmp/morphir-discovery-tests/`.
- All builds use `DOTNET_PROCESSOR_COUNT=2`, single-node MSBuild, disabled
  parallel restore/build, and one heavy job at a time. Executable TUnit/Reqnroll
  uses one worker and physical macOS temporary paths. Core uses Expecto.
