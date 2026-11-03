# Shared configuration delivery evidence

Campaign 1 tracks [issue #458](https://github.com/finos/morphir-dotnet/issues/458).
The owned checkout is `/Users/damian/.t3/worktrees/morphir-dotnet/t3-722f8d5f`,
branch `t3/add-shared-configuration`.

The prepared baseline was `c6fcf0cbec80a25d135d0cfffe2c5f61d03514a4`, including
all six planning snapshots. Main `1c8aa75f63d7ede19cb2ad75b73a6ee8690e4f6f` was
merged without rewriting history at `26aa85b05df41b83b25f3fc97b19e5c97843babc`.
The initial checkout was clean. Campaign 1 has no implementation prerequisites.

## Checkpoint practice

Commit coherent, owned changes after contracts/tests, passing implementation
slices, and documentation/spec updates, and before handoffs or disruptive
workspace operations. Identify red checkpoints explicitly. Record validation and
remaining work here and in handbacks. Preserve unrelated staged/unstaged work;
never stage local `docs/superpowers/` artifacts or add AI coauthor trailers.

The parent owns review, CI watching, landing, and campaign checkout/branch
cleanup. An open PR or checked task list does not complete this campaign.

## Validation journal

- Initial red: `dotnet run --project tests/Morphir.Configuration.Tests -p:BuildInParallel=false -m:1 -- --filter 'Document merge' --sequenced`
  failed with FS0039 for the absent `ConfigValue` and `ValueMerge` APIs. No
  production document behavior had been added. All implementation tasks remain.

- `bb51a21`: initial red contract, FS0039 for the absent document APIs.
- `0fe2abb`: additive contracts; first array replacement Expecto case passes.
  The successful compilation used `dotnet run`, which forwarded `-m:1` to
  Expecto and failed runner argument parsing. Subsequent checks use
  `dotnet build <project> -m:1 -p:BuildInParallel=false`, followed by the built
  executable with `--sequenced` for Expecto.
- `6423ca7` / `294de9f` / `fe23ee9`: profile selector red/green checkpoints.
- `a727a2a`: 10 profile cases pass, including raw control nulls and inactive
  cycles/depth. Ordinary-null overlay case was observed red, then both document
  merge cases passed. The initial format-order resolver case passes. These are
  focused results; full source/profile/reader qualification remains in progress.
- `92c7253`: reader checkpoint is explicitly incomplete. Reader build has zero
  warnings/errors; typed values and extended JSON5 tests pass. Classic
  normalization was observed red before its implementation.
- Source resolution red group: capped core build succeeds with zero warnings;
  9 focused cases run, 4 pass and 5 fail for runtime overrides, selected-source
  deduplication, member filtering/defaults, environment mapping, and required
  selected misses. Member profile-control authority also fails as expected,
  10 other profile tests pass. This checkpoint is not PR-ready.
- `287f1ec`: pinned null/source contract checkpoint above. Subsequent capped core
  build has zero warnings/errors; Resolution 9/9 and Profiles 11/11 pass. Pure
  environment mapping, selected contribution deduplication, complete ownership
  filters, default settings, and selected missing-file errors now pass their
  previously failing cases. Validation, mixed-format warnings, remaining profile
  bounds, reader qualification, coverage/AOT and final docs remain.
