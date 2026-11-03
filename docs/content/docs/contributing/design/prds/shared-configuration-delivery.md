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

## Delivery validation receipt

- Validated implementation checkpoint: `d5e4d2f`. Complete core 90/90, reader/BDD
  63/63 and tooling 245/245 pass. The full solution builds with zero errors and
  the same 20 package/generated-code warnings in unchanged projects.
- Full `dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet`
  exits zero. Fantomas checks all owned F# files successfully. The schema generator
  reproduces its formatted output byte for byte. `git diff --check` passes.
- Final configured coverage runs pass 90 and 63 tests. Their source-line union is
  core 2079/2213 = 93.94% and authored readers 434/461 = 94.14%. Reports are
  `/tmp/morphir-configuration-core-coverage-delivery/9ad9e498-b5be-4d0a-a1e2-02ba9374276c/coverage.cobertura.xml`
  and `/tmp/morphir-configuration-reader-coverage-delivery.cobertura.xml`.
- Both final published executables return
  `{"success":true,"formats":3,"json5":true,"normalization":true,"profile_loading":true,"host_parity":true,"syntax_cases":228,"morphir_cases":26}`.
  Publish logs are `/tmp/morphir-config-trimmed-delivery.log` and
  `/tmp/morphir-config-native-delivery.log`; binaries are under
  `/tmp/morphir-config-trimmed` and `/tmp/morphir-config-native`. Warning severity
  is explicitly normal for runtime qualification. Strict linker-as-errors still
  fails in pinned dependencies; no diagnostic suppression hides that limitation.
- All 10 tracked implementation tasks are complete and OpenSpec apply reports
  `all_done`. Strict change validation passes. This does not declare campaign
  completion: PR review/checks, main landing and owned worktree/local/remote branch
  cleanup remain parent responsibilities.

## Specification handback

The completed implementation change is archived at
`openspec/changes/archive/2026-11-02-add-shared-configuration/`. Main specifications
are `openspec/specs/configuration-documents/spec.md` and
`openspec/specs/configuration-resolution/spec.md`. All 22 added requirements and
their scenarios were compared with the delta specs; new Purpose sections were
copied verbatim. Strict validation passes all 9 main specs and all 7 remaining
active changes, 16/16 items with no issues.

The other five campaign plans remain active with zero checked implementation
tasks. Only their shared-configuration prerequisite links were updated where
needed. The original six-plan snapshot remains in inherited commit `c6fcf0c` and
this PR's history. Do not start dependent implementation until this PR lands.

Parent handback must record the created PR URL, actual head SHA and base SHA, then
own review/check watching, merge verification and separate worktree/local/remote
branch cleanup receipts. No PR watch, branch deletion, worktree removal, force
push, admin bypass, release or deployment was performed by this implementation
worker. Reproducible build/coverage logs and published smoke artifacts under
`/tmp/morphir-config-*` are retained for parent inspection and later cleanup.

## Current-base reconciliation

Before PR creation, main advanced to `890aacb61df601f2876112a2cdd4d9d6f5757ca6`
with documentation-only C# generation/SDK planning, PR #459. It was merged at
`31598987b0edc4d5db27c3237e13df9caf4124eb`. The merge changes no `src`, `tests`,
`build`, solution, dependency or SDK/toolchain inputs, so the recorded runtime
validation remains compatible. Strict OpenSpec validation on the reconciled base
passes 21/21 items: 12 active changes and 9 main specs. All unrelated plans are
preserved. Review/landing/cleanup remain outstanding at this handback checkpoint.

## Parent review checkpoint

The full PR diff whitespace check identified literal CR/CRLF bytes in the pinned
JSON5 newline grammar fixtures. A narrow root attribute disables whitespace
checking for those raw newline fixtures; their original bytes and hashes remain
unchanged. An extra trailing blank line was removed from the shared reader BDD
feature. These review changes do not change production behavior or dependency
inputs. Full-range `git diff --check` passes with the fixture attribute applied.

Windows CI on the earlier head exposed checkout normalization of the three
hash-pinned configuration corpus/schema snapshots. Git's `core.autocrlf=true`
checkout filter reproduced all three reported hash mismatches. The configuration
fixture tree now uses `-text`, matching the existing Ion-fixture policy; the same
checkout-filter check preserves every expected snapshot hash afterward. CI must
confirm this fix on Windows before landing.

## Parent semantic review fixes

CI passed all checks on `51e9eca`, including all three operating systems. Parent
review then addressed six distinct findings before landing:

- `0037d66`: a failing resolver test confirmed that environment JSON secret
  references retained stale lower object fields. Environment values now use the
  shared path-aware secret normalization and retain their original raw tree.
  A second red test showed `logging.file` being treated as a credential, losing
  the file when a later layer added a logging level. The root and the schema's
  logging container keep ordinary map semantics.
- `6ccff70`: three culture regressions failed for Thai calendar years, Finnish
  time separators and an early date outside the Saudi calendar's range.
  Typed TOML temporal getters now use explicit invariant ISO formatting, keeping
  local date-times without an offset and preserving explicit offsets. Tests
  exercise the pinned parser's supported microsecond precision.
- `c72bab7`: red tests reproduced a missing validation error for a null-only
  object replacing an integer, and an error attributed to a lower empty object
  after a higher source populated it. Empty replacement objects now retain
  provenance; populated maps discard obsolete empty-object leaf origins.
- `c795935`: a red test demonstrated the missing pinned-schema string-pattern
  rule. The generator now emits pattern rules and validation evaluates them with
  culture-invariant, nonbacktracking regular expressions. Leading/trailing
  whitespace and whitespace-only Elm extensions fail at the supplying source.

The complete core runner passes 95/95. The reader/BDD runner passes 66/66,
including the culture regressions. Both projects build without warnings. The
trimmed and native osx-arm64 smoke hosts reran 228 JSON5 syntax checks and 26
Morphir documents, with new culture and schema-pattern checks. Both passed.
Publish commands match the smoke README, with outputs under
`/tmp/morphir-config-review-trimmed` and `/tmp/morphir-config-review-native`;
the native log is `/tmp/morphir-config-review-native.log`. Existing pinned
dependency diagnostics remain visible under the previously documented warning
qualification. No dependency versions, fixture bytes or lockfiles changed.

Fresh core VSTest/Coverlet and executable reader coverage runs both pass. Their
union reports core 2134/2267 authored lines, 94.13%, and readers 441/468, 94.23%.
The reports are `/tmp/morphir-config-review-core-coverage/ee02bb36-e337-4426-b669-537601fa7447/coverage.cobertura.xml`
and `/tmp/morphir-config-review-reader-coverage.cobertura.xml`, processed by the
existing `scripts/configuration/summarize-coverage.py` tool. Changed-file
Fantomas checks and C# whitespace formatting pass; full-range and working-diff
whitespace checks pass. New-head CI, landing and owned checkout/branch cleanup
remain outstanding.
