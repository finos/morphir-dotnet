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
- `c48803f`: passing supplied-source implementation checkpoint. Canonical schema
  validation and mixed-format diagnostics were then observed red and made green,
  Resolution 13/13. The profile worker's `f06fecb` has 33/33 profile cases and the
  then-current complete core suite 81/81 passing. A follow-up default-priority
  regression was observed red, expected host IR default 3 but got builtin 4.
  Its source-order fix and selected-classic fallback cases await the next core
  run. Reader 36/36 includes official JSON5 syntax checks and pinned corpus
  hashes. YAML Default profile expansion and full qualification remain underway.
- `542f129`: accepted YAML Default policy, reader normalization and policy docs
  checkpoint. `93305d9` finishes 39 profile cases. Fresh complete core run passes
  88/88 with zero build warnings, including host/default priority and selected
  classic checkpoint-default regressions that were observed red first.
- `dcedbb3`: readers pass 63/63 with zero build warnings, including the two
  negative-zero cases observed red, official JSON5 conformance, YAML Default,
  raw legacy loss accounting and lazy profile callback protection.
- `DOTNET_PROCESSOR_COUNT=2 dotnet build Morphir.slnx -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true`
  succeeds, zero errors and 20 warnings in unchanged package/generated-code
  projects. Both configuration suites are wired into the existing Nuke test
  target using their configured runners, with bounded workers and compiled output
  reuse. The managed non-CLI smoke executable succeeds.
- The first complete tooling run used macOS's default `/var/folders/...` TMPDIR
  and failed 78 generation/input-capture cases because those tests reject linked
  ancestors. CI explicitly uses a physical temporary directory. A rerun with
  `/private/tmp/morphir-shared-config-tests` is in progress; no production code was
  changed for this environment adjustment.
- OpenSpec progress is 7/10. Remaining: reader trimming/AOT qualification, legacy
  tooling compatibility, final format/coverage/build evidence. Landing and owned
  worktree/branch cleanup remain parent-coordinated after PR handback.
