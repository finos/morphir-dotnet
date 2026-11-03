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

## Runtime and coverage qualification checkpoint

- `99aa058` protects integration, seven completed tasks and the full-solution
  build. The physical-temp tooling rerun passes 245/245. Task 4.1 is complete;
  implementation progress is 8/10 before final qualification tracking.
- Fantomas 5.2.4 formats owned F# files; `dotnet format` formats owned C# files.
  C# formatting reports unsupported F# projects, so Fantomas is run separately.
- After formatting, core build has zero warnings and Expecto passes 88/88.
  `dotnet test tests/Morphir.Configuration.Tests/Morphir.Configuration.Tests.fsproj --no-build --no-restore -m:1 --collect:'XPlat Code Coverage' --results-directory /tmp/morphir-configuration-core-coverage`
  also discovers and passes all 88 tests. The configured reader executable passes
  63/63 with `--coverage --coverage-output-format cobertura --coverage-output /tmp/morphir-configuration-reader-coverage.cobertura.xml`.
- The raw core-run package line rate is 86.82%; the reader-run adapter package
  rate is 88.26%, including generated regex implementation. Unioning normalized
  source-line hits from both configured runners gives core 2074/2208 = 93.93%
  and authored adapter code 434/461 = 94.14%. Generated `obj/` code is excluded
  from that authored-source summary. Reproduce with
  `scripts/configuration/summarize-coverage.py <core-cobertura> <reader-cobertura>`.
- The strict trimmed linker-as-errors publish fails in the pinned CsToml and
  FSharp.Core packages. Expanded diagnostics include CsToml generic formatter
  fallback reflection and FSharp.Core structured formatting/reflection plus
  IL2040 absent-resource removal entries. No warning suppression was added.
- Actual trimmed and native `osx-arm64` executables publish and run using normal
  warning severity, explicitly `-p:ILLinkTreatWarningsAsErrors=false`. The native
  artifact is confirmed as a Mach-O arm64 executable. Its expanded fixture run
  succeeds with `syntax_cases=228`, `morphir_cases=26`, and all three formats,
  normalization, profile loading and host parity checks. This is runtime evidence
  with a documented dependency-warning limitation, not a warning-free strict
  linker claim. See the smoke-host README for exact commands and diagnostics.
- A final semantic review found a schema minimum check coercing very large exact
  integers through double. Its focused test is observed red after a successful
  zero-warning build. This checkpoint retains that incomplete regression before
  fixing the comparison. Final tests, warning-aware runtime qualification and
  OpenSpec archive/PR handback remain.

## Final implementation validation

- `1029b94` protects the runtime qualification and observed red integer-bound
  regression. Integer bounds now compare Int64/BigInteger exactly. Fresh full
  core runner and VSTest coverage run pass 89/89; reader/BDD coverage run passes
  63/63, both with zero build warnings/errors.
- Final merged authored-source coverage is core 2076/2210 = 93.94% and readers
  434/461 = 94.14%. The summaries normalize Cobertura source roots and union hits
  from the Expecto/VSTest and executable MTP runners. Generated `obj/` code is
  excluded. The generated pure schema is checked for deterministic regeneration.
- Final trimmed and native hosts both return success with 228 JSON5 syntax checks
  and 26 pinned Morphir documents. They additionally exercise Default YAML
  aliases/tags/nulls, all four TOML temporal types, and a 401-digit integer's schema
  minimum. The native binary is arm64 Mach-O. Commands explicitly retain normal
  warning severity; upstream diagnostics and strict linker failure remain visible
  in the qualification README. No warning suppression or reflection serializer
  was introduced.
- `openspec validate add-shared-configuration --strict --json` passes without
  issues after splitting long new requirements into focused behavior contracts.
  No requirement behavior was removed. Repository restore resets publish-only
  RID/ILCompiler lockfile state, preserving the normal project dependency locks.
- The typed-host error-parity audit found that pure normalization lacked the
  text reader's non-mapping-root diagnostic. Its new focused case was observed
  red, then the same `config.document_shape` error was added without altering
  valid documents. Complete suites now pass core 90/90 and reader 63/63, with
  zero build warnings. Final runtime/coverage refresh follows this code checkpoint.
