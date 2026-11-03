# Integrate C# generation into the CLI

Status: Implementation landed; specifications synchronized and archived in this follow-up. Archive PR landing and coordinator cleanup pending. Issue: [#481](https://github.com/finos/morphir-dotnet/issues/481).

## Problem and intended behavior

Before this delivery, the in-memory C# generator was not registered by
`morphir generate`. This delivery exposes its typed behavior through the established
loading, reporting and safe publication flow. A successful invocation produces
immutable C# sources without requiring a compiler. Consumers compile those
sources separately against `Morphir.SDK.CSharp`.

## Scope and contracts

- Exact target `csharp`, provider identity `morphir.csharp`, registered statically
  beside F# through `IGenerationBackend`. Preserve the generic resolver.
- Classic Library versions 1, 2, 3, 3.0.0, 3.1.0 and v4 Library versions 4,
  4.0.0 only. Reject unsupported required semantics and distributions.
- Repeated `--option` settings retain last-occurrence precedence. Numeric JSON
  `language-version` accepts 14 and 15, default 14. Optional `namespace` accepts
  a validated C# namespace. Unknown keys, aliases, wrong kinds and invalid names
  are invocation failures before emission.
- Direct-library and provider paths produce identical relative paths and UTF-8
  bytes for equivalent models and options. Preserve the typed classic/v4 input,
  source version and immutable SDK equality protocol.
- Reuse JSON, Ion text, binary and tree loading and the existing ownership
  manifest. Preserve unowned files, unchanged timestamps and prior successful
  output after semantic, operational or cancellation failures. Remove only
  stale owned files. Reject unsafe paths and input/output overlap.
- JSON mode writes one report including structured diagnostics to stdout.
  Human diagnostics and emitted logs use stderr; JSON mode need not duplicate
  report diagnostics there. Exit codes are 0 success, 1 semantic failure,
  2 invocation/operational failure, 130 cancel.
- Execute real trimmed and NativeAOT CLI builds, then compile and run the
  emitted business models separately under SDK 10.0.403/C# 14 and isolated
  SDK 11.0.100-rc.2.26504.105/C# 15.

The production SourceGenerator adapter, compiler installation during generation,
new orchestration paths, runtime changes and configuration semantics are excluded.

## Feature status tracking

| Feature | Status | Evidence |
| --- | --- | --- |
| Verified main prerequisites and contribution issue | Implemented | Clean base 80d516e2; seven exact PRs MERGED to main; #481 |
| Real-provider BDD RED and registration | Implemented | Observed classic/v4 CLI unknown-target RED 2/2; all 444 Tooling cases passed in full native run |
| Immutable validated options and core namespace parity | Implemented | Public API RED before production; real CLI defaults/precedence/kind/refusal cases; namespace 59/59; both typed families compiled under both pinned profiles |
| Model/source/diagnostic adaptation across wire forms | Implemented | 16 real CLI wire cases compare direct-core paths and UTF-8 bytes; located semantic reports |
| Safe publication and failure preservation | Implemented | Seven real-source publisher cases plus real CLI rerun/timestamp/stale removal/unowned-file observations |
| Real CLI JSON, stderr and 0/1/2/130 observations | Implemented | 33 real CLI cases passed; separate captured CLI host cancellation returned 130 and preserved prior files/timestamps |
| Pinned compiled business consumers | Implemented | 16 initial fresh consumers plus eight typed/provider consumers, SDK 10.0.403/C14 and isolated RC2/C15; independent 5000/15000/62 observations |
| Executed trimmed and NativeAOT CLI | Implemented | Acceptance-03 actual executables, empty compiler PATH, separate consumers 15000; native semantic refusals/no artifacts; inherited warnings retained |
| Executed command reference and fresh consumers | Implemented | Fresh documented console/package/generate/build/run printed 5000; README and troubleshooting commands parsed |
| Full tests, meaningful coverage, package and format checks | Checks performed, known unrelated failure | Latest full run 3960/3961 with unchanged configuration failure; CSharp 206/206, FSharp 333/333. Focused final core 216/216, coverage 3130/3340 and formatter 64/64; adapter 47/48. PackAll and 40 package checks passed; final native Precommit, pinned Fantomas, changelog and strict OpenSpec checks passed |
| Landed main and archived specifications | Implementation landed, archive follow-up prepared | #482 squash landed at 0542145a; 9/9 tasks; both deltas synchronized into main specifications and dated archive |
| Archive contribution validation | Passed with Husky discovery limitation | Separate full native run 3971/3971 + 15 cold + 71 inspection; PackAll, TestBuild 40/40, strict OpenSpec 22 + 2, direct Precommit/changelog and archive/frozen/link audits passed; actual Husky discovery failed, no bypass |
| Archive follow-up landing and checkout cleanup | Pending | Root must verify archive PR landing and all cleanup gates before initiative completion |

## Test plan

Write BDD scenarios first and observe failure against the actual CLI before
production changes. Add public boundary tests for registration, immutable
options, capabilities, refusal mapping, cancellation and exact direct/provider
parity. Use independently authored business expectations and frozen SDK cases.
Exercise the real CLI for every input representation and reporting category.
Extend publication cases with real sources and verify file content and metadata.
Compile emitted sources without modification in fresh ordinary consumers.

Serialize restore/build/test/coverage/pack/publish through the allocated slot.
Bound processors to two, MSBuild to one and disable shared compilation/node
reuse. Run native `build.ps1 --target Test`, package/build checks, direct hook
checks, strict `mise run openspec:verify`, frozen contract/link audits and focused
coverage of new production scope, at least 90%. Preserve any unrelated
configuration failure without changing, suppressing or filtering its semantics.

## Implementation notes

### Start gate, 2026-11-03

The orchestrator prepared `t3-79f2b566` on
`t3/integrate-csharp-generation-cli`. Initial status was clean; HEAD and refreshed
origin/main equal `80d516e2e4e62dfd6d69e41c07bae8cfeff4a670`, 0/0.
Exact prerequisite PRs #462, #463, #469, #470, #472, #478, #480 report MERGED
to main, and all supplied landed SHAs are reachable. OpenSpec reports repo-local
`html-design`, ready, 0/9 tasks. Runtime is Codex gpt-6.1-sol/high.

The landed `GenerationOptions` record exposes only `LanguageVersion`.
The approved namespace requirement needs a source-compatible core extension.
The approved proposal retains the existing options constructor and three-argument API,
adds a validated namespace value and overload, and threads naming explicitly
through emission. The coordinator granted the exact core/shared leases. No source text
rewriting, IR migration, alternative equality protocol or ambient state is allowed.

## Open questions

- Namespace API and exact core/shared leases approved by the coordinator.
- Implementation #482 landed. Archive follow-up landing and root cleanup verification remain pending.

## Evidence and ownership

New proof root: `C:/Temp/morphir-csharp-cli-79f2b566-20261103`.
Prior generator/archive proof roots are read-only dependencies owned by root.
The lead is the only Git writer. No auxiliary, helper or extra checkout has been
started. Exact commands, outcomes, artifacts and releases will be recorded in
this change's validation and handoff records before handback.

### NativeAOT diagnostic correction and acceptance, 2026-11-03

Actual native execution exposed eager F# `%A` diagnostic formatting in core
module validation. The coordinator leased exactly TypeEmission.fs and
ValueLowering.fs plus public NamespaceCases regressions and one diagnostic-guide
paragraph. Public invalid-name/collision/type-mismatch RED preceded correction.
The fix uses small explicit structural formatters with per-traversal cancellation.
It changes human diagnostic spelling and module-validation location spelling,
retaining rule IDs, source bytes/paths, APIs and refusal/cancellation behavior.

The first publish failed on inherited warnings treated as errors. The approved
policy sets TreatWarningsAsErrors=false and TrimmerSingleWarn=false only on the
owned trimmed/native publish commands. No warning suppression, reflection roots,
serializer change or global setting was added. Acceptance-03 passed actual
trimmed/native generation and separately compiled business output. Visible warning
counts are trimmed 78 occurrences/75 distinct and native 92/89, all from existing
Core/tooling paths or dependencies. There are no changed-CSharp-source warning
origins. This proves the executed generation path, not all CLI commands.

The latest full native test run failed in unchanged configuration semantics:
set custom.value 12 --type int returned config.edit_rolled_back. Preserve its
3960/3961 result. Its downstream cold/inspection stages did not run. The earlier
3912 framework + 15 cold + 71 inspection passing checkpoint remains separate.
No configuration fix, suppression, filtering or broad retry is part of this PR.
Caller-token cancellation was observed through the real CLI command/renderer
and real registered provider, with an owned test publication gate; it does not
claim OS signal delivery coverage. See the [archived validation record](../../../../../openspec/changes/archive/2026-11-03-integrate-csharp-generation-cli/validation.md) for
exact commands, warnings, retained failures and ownership.

### Landing and specification archive, 2026-11-03

[PR #482](https://github.com/finos/morphir-dotnet/pull/482) is merged into main,
submitted head `0e1e86d5e850adb70ec8027e324ba2234a22bdce`, squash landing
`0542145a4bcc0edf9c9dd2fb3122df36c73e80d0`, identical tree
`284eb3240006ec9a8a34ba539caceeb65be0844f`. The archive worker independently
queried that exact PR, fetched main and checked landing ancestry. All eight
final-head checks passed, including three OS jobs; automated review completed
without findings and all threads were resolved. No developer approval is claimed.
Root used the user's standing admin-bypass authorization.

The [dated archive](../../../../../openspec/changes/archive/2026-11-03-integrate-csharp-generation-cli/proposal.md)
retains all nine checked tasks and the source-bound evidence above. The new
[CLI spec](../../../../../openspec/specs/cli-csharp-generation/spec.md) has six
requirements and 12 scenarios. The [generator spec](../../../../../openspec/specs/morphir-csharp-transpilation/spec.md)
retains eight requirements and all 19 original scenarios, adding three namespace
scenarios. Original deterministic naming and default API/output remain unchanged.
The added namespace clauses sit under their corresponding scenarios to retain
their full wording while meeting strict description-length validation.

The [delivery record](../../../../design/dotnet-csharp-generation/orchestration.md#cli-landing-and-specification-archive-2026-11-03)
records archive checks and retained artifacts. This follow-up is documentation
only. No pinned/trimmed/native acceptance matrix or new coverage measurement was
run for this follow-up.
Archive landing and initiative cleanup remain pending until root verifies them.
