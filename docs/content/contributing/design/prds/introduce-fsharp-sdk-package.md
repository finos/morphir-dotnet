# Introduce the canonical F# SDK package

Status: Implementation landed; archive follow-up and coordinator cleanup pending. Contribution: [issue #465](https://github.com/finos/morphir-dotnet/issues/465).
Implementation plan: [OpenSpec change](../../../../../openspec/changes/archive/2026-11-03-introduce-fsharp-sdk-package/proposal.md).

## Purpose and scope

Publish `Morphir.SDK.FSharp` as the canonical F# runtime package. Keep the
`Morphir.SDK` namespace, assembly identity and existing public signatures.
`Morphir.SDK` remains a dependency-only compatibility package resolving the same
implementation. Existing generated F# source must compile unchanged, and a
consumer compiled before migration must run with the migrated runtime.

Execute every required ID in `morphir-dotnet-sdk-v1`, schema 1, profile
`dotnet-sdk-v1`: 67 operations, 207 required cases and eight separately reported
representation exclusions. Correct only covered discrepancies and document
observable changes. C# SDK, emission semantics, CLI and release publication are
outside this change.

## Feature Status Tracking

| Feature | Status | Evidence or next check |
| --- | --- | --- |
| Predecessor landing | Verified | #462 landed `793f378b8559bd667baa49bc0ba2cdf156d088c8`; #463 landed `7e37689c55eae9510cf12dafd130dc4ae8e3baf1`; both exact PRs MERGED to main and landed commits reachable from initial HEAD |
| Untouched legacy package and runnable binary | Verified baseline | Package `0.3.0-rc.2`; precompiled consumer executed 11 checks before production edits |
| Canonical and dependency-only package graph | Verified packaging group | RED/GREEN all three fresh graphs; integrated solution build, SDK package test and Nuke PackLibs passed |
| Source, public API and binary compatibility | Verified | All three fresh graphs execute the unchanged old binary and new source, preserve 281 declarations and prove migrated runtime load path/hash |
| Runtime conformance adapter | Focused verified | 207 required IDs actually executed and passed, plus execution-set summary; eight representation exclusions separate |
| Covered semantic corrections | Verified | Ten actual mismatches corrected; 207/207 required IDs pass; Int64 and custom SRTP compatibility regression tests pass |
| Classic/v4 generation and examples | Verified | 333 generation tests pass with unchanged snapshots; one classic/v4 Order program compiles and executes through the canonical package |
| Shared build/solution/documentation integration | Verified | Coordinator applied approved paths; integrated solution, PackAll and TestBuild 40/40 pass |
| Full tests, formatting and production coverage | Verified with recorded limitations | 2,973 tests plus 15 lifecycle checks pass; strict OpenSpec, Precommit and changelog pass; changed-code coverage accepted; inherited formatting/Husky limitations below |
| PR, landing, archive and cleanup | Planned | Lead delivers PR-ready result; coordinator owns landing, archive and worktree removal |

## Acceptance

- Canonical-only, legacy-only and combined local-feed installs each resolve one
  `Morphir.SDK.dll`, with no compatibility assembly and no compiler/CLI runtime
  dependencies. Each fresh graph uses an isolated package cache.
- The old `Morphir.SDK.Order` assembly-qualified identity remains valid. All old
  public metadata declarations remain available. Additions must not narrow
  generic arithmetic or replace existing decimal division's return type.
- Unchanged generated source and existing classic/v4 generation entry points
  compile and execute through the canonical package. Emission bytes and public
  declaration names remain unchanged.
- All 207 required IDs execute and pass through the F# runtime adapter. Every
  callback error propagates; operand snapshots, nested structural equality and
  hashes, and key ordering are checked. Exclusions/gaps/skips are never passes.
- Relevant targeted checks, native Windows Nuke full suite, strict OpenSpec,
  formatting and measured production logic coverage pass before contribution.

## Implementation Notes

### Untouched baseline, 2026-11-03

Initial clean branch `t3/introduce-fsharp-sdk-package`, checkout
`C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-757a20ef`, HEAD
`7e37689c55eae9510cf12dafd130dc4ae8e3baf1`. No production SDK or packaging edit
preceded capture. `Packaging/capture-baseline.ps1` packs the real legacy project,
restores a new package consumer into an isolated cache, builds it and executes
the compiled DLL. It records exact arguments, exits, toolchain, source/package
hashes, exported public metadata and runtime identities.

Successful retained evidence directory:
`C:/Temp/morphir-fsharp-sdk-757a20ef-baseline-20261103-r2/`.

| Baseline artifact | SHA-256 |
| --- | --- |
| `old-feed/Morphir.SDK.0.3.0-rc.2.nupkg` | `6268f165ae03beb03d69485a33c8b9cd37960ad876c67026a446e369f06a0205` |
| `old-consumer/bin/Release/net10.0/Morphir.SDK.dll` | `b33b1af466d8ffdb8f322c9ac091029e9f519616ce0890de68e1138dfda48aa4` |
| `old-consumer/bin/Release/net10.0/LegacyConsumer.dll` | `41afc3e57b4ebf350467d0d812d0fdb63eda1878eecd9099d30c992d60768228` |
| `old-public-api.txt` (281 declarations) | `efb9e51d732dddb69fab78dd4535a09e9b85227f0b3e4f325e836172739bc77f` |

Observed assembly identity:
`Morphir.SDK, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null`.
Observed type identity:
`Morphir.SDK.Order, Morphir.SDK, Version=0.3.0.0, Culture=neutral, PublicKeyToken=null`.
F# type aliases resolve to their existing FSharp.Core/BCL types; they are not
new forwarding CLR types.

The first attempt, retained at the corresponding path without `-r2`, packed,
restored and built but failed execution with missing FSharp.Core assembly
version 11. Its implicit compiler dependency `10.1.403` had downgraded the
runtime package's declared `11.0.100` dependency (NU1605). The successful
consumer explicitly selects the version from the untouched package's nuspec
and treats NU1605 as an error. This is consumer dependency selection, not a
runtime packaging fix. New package consumers must test this same requirement.

Toolchain: Windows arm64 10.0.26300; .NET SDK `10.0.403`, commit `5a2162bea6`;
MSBuild `18.9.15+5a2162bea`; runtime `10.0.14`, commit `0427b9abbe`.
Captured pack/build use `-m:1 -p:UseSharedCompilation=false -nodeReuse:false`;
restore uses an explicit feed/cache and `--disable-parallel`. The script bounds
each child to 180 seconds and kills its process tree on timeout. Process-local
TEMP/TMP and concurrency settings restore in `finally`. No timeout occurred.
The baseline-only lease was released; process inventory found zero
dotnet/MSBuild/VBCSCompiler/testhost/Nuke processes. No auxiliary was started.

### Compatibility decisions

- Canonical project explicitly retains `AssemblyName=Morphir.SDK` and the
  existing assembly-version/public-key policy. The package ID can change without
  changing this runtime identity.
- The legacy project packs an exact dependency on the canonical version and
  neither compiles nor ships an empty implementation assembly.
- Existing inline `Basics.add/subtract/multiply/negate` are generic. Checked
  behavior must preserve their existing callers and CLR/F# signatures.
- Keep `Decimal.divide : decimal -> decimal -> decimal`. Map logical
  `Decimal.div` to an additive optional-result API; changing the old signature
  would break source compatibility. Existing `subtract/multiply` names remain.
- A precompiled inline call can retain its old embedded behavior. The binary
  identity guarantee does not claim retroactive replacement of inlined code.

## Open questions and remaining evidence

Runtime sources moved byte-for-byte into the canonical project after package
graph RED. At that package-only checkpoint, all 14 moved `.fs` hashes matched the untouched
baseline. Subsequent covered corrections changed only Basics, Decimal and Dict. The legacy
project overrides Build/Rebuild after explicit SDK imports and emits no assembly.
Its single dependency is exactly `[0.3.0-rc.2]` of the canonical package. Local
transitive pinning is disabled only in the metapackage so it does not promote
FSharp.Core into a second direct dependency. A NuGet `_._` placeholder marks
the target framework without adding an assembly or suppressing package analysis.

Package RED logs are retained at
`C:/Temp/morphir-fsharp-sdk-757a20ef-package-red-20261103/`:
canonical/combined restore returned NU1101; legacy-only resolved the old
package-owned runtime and failed canonical ownership. Source mapping confined
both SDK IDs to the local feed.

Focused GREEN evidence is retained at
`C:/Temp/morphir-fsharp-sdk-757a20ef-package-green-20261103-r3/`.
Each of three isolated caches resolved exactly one canonical-owned runtime,
built the unchanged source, passed its 11 checks, retained all 281 old API
declarations, and executed the original precompiled consumer. The consumer hash
remained `41afc3e57b4ebf350467d0d812d0fdb63eda1878eecd9099d30c992d60768228`.
All three host traces resolved each graph's copied canonical runtime, hash
`53bd9799bbd683ccfae305235ca4bab4b49c7c9c2697c63f30a16f9780185a84`.
No compatibility DLL existed in bin/obj or the legacy package. Earlier GREEN
attempts are retained: they exposed the missing dependency when
`ReferenceOutputAssembly=false` was set, then the promoted FSharp.Core
dependency and NuGet's normalized exact-version syntax. These were corrected
before the successful run; none is counted as a pass.

The coordinator applied the seven approved shared-path edits directly in
this checkout, with the lead remaining sole Git writer.
Integrated `dotnet build Morphir.slnx -c Release -m:1
-p:UseSharedCompilation=false -nodeReuse:false` passed with the repository's
existing dependency/generated-code warnings. A reserved-identifier warning
introduced by the test wrapper was fixed; the focused SDK test rebuild then
passed with zero warnings. The existing SDK executable's package test passed
1/1 (all three graph matrices internally) with `--filter-test-list
"F# package migration" --sequenced --parallel-workers 2`.
Native Windows `build.ps1 --target PackLibs --configuration Release --skip
Restore Compile` then passed in 21 seconds, using the already completed solution
build. PackLibs validated the canonical runtime package and packed the
dependency-only compatibility package. Its warnings were the pre-existing
NU1510 and Fabulous.AST/Fantomas NU1608 warnings. No check was disabled.

Exact commands, logs and integrated package assets/hashes are retained under
`C:/Temp/morphir-fsharp-sdk-757a20ef-validation/`; the latest complete package
matrix is
`C:/Temp/morphir-fsharp-sdk-757a20ef-validation/temp/morphir-fsharp-sdk-packages-e892d71a4b2e4d40a247613da91f1292/`.
Old project bin/obj outputs were preserved, not deleted, at
`C:/Temp/morphir-fsharp-sdk-757a20ef-pre-migration-project-output/`.
Canonical/legacy packages contain their respective migration READMEs. The Nuke
runtime DLL hash equals the package-test runtime hash above. All commands were
serial, with process-local `DOTNET_PROCESSOR_COUNT=2`, node reuse disabled and
shared compilation disabled. Explicit solution and focused builds used MSBuild
`-m:1`; Nuke's pack invocation used its existing tool settings. No build/test
helper remained in the release inventory, and the packaging lease was released.



### Covered corrections and executed conformance

The typed adapter is authored from the frozen inventory/corpus, without reading
or executing the implementation to obtain expected results. Missing public
`round/floor/ceiling/truncate` and optional `Decimal.div` first produced compilation
RED. After additive APIs compiled, the actual semantic RED executed all 207
required IDs and exposed ten mismatches. Corrections are limited to Int32 checked
arithmetic, MinInt remainder/modulo, direct ordinal string comparison,
first-dictionary-wins union and canonical Decimal text. The canonical package
README records examples and observable behavior changes.

A regression found that applying generic checked arithmetic also changed Int64
behavior outside the admitted Int32 profile. The final implementation checks
Int32 operands and preserves the original generic fallback, including custom
SRTP operand/result types. Three compatibility regressions pass. No old public
signature was removed; all 281 captured declarations remain. `Decimal.divide`
retains its decimal return type and zero-divisor behavior; `Decimal.div` is additive.
Existing inlined binary code is not retroactively replaced.

The final adapter run has 207 distinct required IDs executed and passed,
zero required skips/expected failures/gaps, and eight separately reported
representation exclusions. Operand immutability, callback exception identity,
structural equality/hash and ordering checks execute in the typed cases. The
frozen corpus, inventory, IDs, schema, profiles and expectations are unchanged.
The SDK suite has 396 passing tests, including the 207 cases, one execution-set
summary, one package-matrix test, three generic compatibility tests and nine
public CLR entrypoint tests. The existing 175 SDK tests still pass.

### Production coverage and matched baseline

AltCover 8.9.3 instruments temporary copies, with no runtime wrappers,
NoInlining flags or runtime reflection dependency. The 396-test run passes with
only production-source points counted. Its whole-runtime measurement is
123/421 sequence points, 29.216%, and 86/352 distinct point-start lines, 24.432%.
Inline SDK bodies compiled into consumers generally have consumer PDB locations;
those test-source points are rejected rather than counted as production.
This is not repository-wide 80%/90% compliance or a blanket coverage waiver.

Against captured base source obtained through `git show`, conservative span
classification covers 60/60 instrumentable changed production sequence points
and 33/33 distinct changed-point start lines. A multiline point overlaps changes
when its inclusive start/end span intersects changed textual lines; a final
line with end column <= 1 is excluded. These denominators do not represent all
textual changed lines. `--linecover` provides no branch coverage claim.

One matched pair compiles an old-compatible consumer against the genuine old
package and executes the exact same consumer bytes against old/current runtimes.
It independently asserts the same 25 public CLR behaviors. Both pass, with the
same AltCover version and filters: `assemblyFilter=^(?!Morphir[.]SDK$).*`,
`pathFilter=tests`, `linecover`, and no local-source filter on either side.
The analyzer rejects non-SDK modules, consumer/test source and unknown files,
accounts for `src/Morphir.SDK` moving to `src/Morphir.SDK.FSharp`, and includes
all 14 runtime source files. Old coverage is 28/376 sequence points, 7.4468%,
and 17/328 point-start lines, 5.1829%. Current coverage is 49/421 sequence points,
11.639%, and 29/352 point-start lines, 8.2386%. This demonstrates no measured
decrease under that matched execution set. It does not turn the low aggregate
measurement into a threshold pass. The coordinator accepted the changed-code
and matched-baseline evidence for this implementation gate.

Matched consumer SHA-256:
`ce0a744393f4811a821ee6f49fed7c23b556a1f1ac24e108af93f49432e9d307`.
The old runtime hash remains the untouched value in the baseline table. Current
measured runtime SHA-256 is
`9b67c59a53933dc71829fe072e7b04f49e07d1bbc142a643e5dbaadf9c3709eb`.
The matched consumer is separate from the genuine pre-migration binary fixture;
both forms of compatibility evidence are retained.

Coverage artifacts:

- `C:/Temp/morphir-fsharp-sdk-757a20ef-semantic-validation/runtime-clr.opencover.xml`
- `C:/Temp/morphir-fsharp-sdk-757a20ef-semantic-validation/runtime-clr-metrics-span.json`
- `C:/Temp/morphir-fsharp-sdk-757a20ef-paired-coverage/paired-results.json`
- `C:/Temp/morphir-fsharp-sdk-757a20ef-paired-coverage/paired-metrics.json`
- `C:/Temp/morphir-fsharp-sdk-757a20ef-paired-coverage/old.opencover.xml`
- `C:/Temp/morphir-fsharp-sdk-757a20ef-paired-coverage/current.opencover.xml`

### Integrated validation checkpoint

Native `build.ps1 --target Test --configuration Release` passes after the
Int32-only refinement: 2,973 tests, including classic/v4 generation 333/333,
plus 15 cold package lifecycle checks. No existing generated snapshot changed.
Nuke emits the approved MSBuild node/shared-compilation/node-reuse bounds on
both Restore and Compile. Native Precommit exits zero; dotnet format reports
its existing lack of F# project support. Explicit Fantomas 5.2.4 checks expose
pre-existing formatting debt in the three touched runtime files when checked
against the captured base under the same settings. New F# tests are formatted
separately, preserving hash-locked old consumer and emitted fixture inputs.
No blanket runtime reformat is included.

Integrated `PackAll --skip Restore Compile` passes, followed by TestBuild 40/40
with zero skips. The skips here name already completed build targets, not tests.
Existing NU1510 System.Text.Json pruning and NU1608 Fabulous.AST/Fantomas warnings
remain documented. Every command, result and isolated package graph is retained
under `C:/Temp/morphir-fsharp-sdk-757a20ef-semantic-validation/`.
A subsequent full run, `nuke-Test-packaged-generation.log`, passes the same
2,973 tests plus 15 lifecycle checks in 5m29 with the joined generated-package
proof and formatted new tests. The unchanged emitter emits the captured Order
program from classic 3 and promoted v4. The fixture retains its captured Windows
bytes, SHA-256 `831a21dd7c3e64100d257f583f284b26ce46e591e85ca6746d16b5aaa6694903`.
The generator test accounts explicitly for platform line endings, and local
canonical-package consumers copy the fixture byte-for-byte. Two separate
compilations/executions pass four assertions each. The full 333-case generation
suite is not replicated in the package matrix.

Newly authored F# test/helper files and touched TranspilerTests pass pinned
Fantomas checking. A genuine baseline TranspilerTests check also passes; its
introduced formatting was corrected rather than called inherited debt. In the
three modified runtime files, only touched blocks were aligned with the pinned
formatter. The remaining formatter changes are outside added/replaced lines,
recorded at `C:/Temp/morphir-fsharp-sdk-757a20ef-format-audit/changed-block-audit.json`.

This production whitespace refinement followed coverage collection. The exact
measured sources and all 14 hashes are retained at
`C:/Temp/morphir-fsharp-sdk-757a20ef-semantic-validation/coverage-source-snapshot/`.
Reanalysis explicitly using that snapshot reproduces the accepted counters in
`runtime-clr-metrics-snapshot.json`; the counters describe that measured layout,
not the final formatted layout. No additional instrumentation round or aggregate
coverage claim is made. Post-format build passes with zero SDK warnings; all
396 SDK tests, all three source/API/binary/package graphs and both generated
canonical consumers pass again. The focused classic/v4 emitter fixture check
also passes after formatting.

The final complete matrix is
`C:/Temp/morphir-fsharp-sdk-757a20ef-semantic-validation/temp/morphir-fsharp-sdk-packages-97732d39700e488087e615bdafd6dbea/package-results.json`.
Its final runtime SHA-256 is
`6d9bef7a5b752243d451fac6278eb0221817b0cc0e620f72ddcdbdda08e59e23`.
All host traces prove this migrated runtime path/hash, with the original consumer
hash unchanged. This binary hash differs from the measured pre-format runtime
because source positions/debug information changed.

`mise run openspec:verify` passes the shared workflow check, schema validation,
all strict change/spec validation and MoonBit workflow tests. The configured
changelog validator passes. `dotnet husky run` actually exits 1 with
`failed to execute command 'git'` and `Could not find Husky path`, even when
native Git is added to process PATH. Both configured checks were run directly:
changelog validation and native Windows Nuke Precommit. No hook bypass or
`--no-verify` was used. This environment limitation remains visible in handoff.

All nine implementation tasks are verified. PR creation/hosting review, landing,
archive and cleanup remain separate delivery steps owned with the coordinator.

### Coordinator review follow-up

PR #469 was integrated with main `23d274d4c87b367fdc79bd21843a9fdf5d74b13d`
in normal merge commit `763720376a99c5cd3f97d46bca606764f316ca17`. The NOTICE
resolution retains both the SDK attribution and main's configuration/discovery
notices. The earlier full-suite results describe the implementation checkpoint,
not this newly integrated main.

Three review findings were independently confirmed and reproduced. The six
culture tests initially had four failures: German parsing changed `1.5M` to
`15M`, and French parsing rejected the invariant text. The later-version matrix
failed all three graphs at actual assembly version `0.4.1.0`. The first version
probe was ineffective because the changelog target overrode Version; its
replacement uses an isolated changelog and asserts the requested packed version.
Test-authoring/setup errors are retained separately from these behavioral REDs.

The parser now tries canonical invariant decimal text first, without thousands
separators, then retains current-culture parsing for other input. The package
matrix derives identity from the canonical asset while still enforcing the
existing name, culture and unsigned policy. The genuine old consumer remains
unchanged. Both library publishing targets now select the exact versioned
canonical package before the compatibility package. No publishing target has
been executed.

After the C# lead released the slot, coordinator SDK build passed without warnings
and all 403 SDK tests passed, including 207 frozen required IDs, six culture cases,
both package-version matrices and the unchanged old-binary/generated consumers.
The final integrated Windows Nuke Test passes all 3,270 suite tests and 15 cold
package lifecycle checks in 6m52. This includes main's 124 configuration and
166 configuration-I/O tests, SDK 403 and unchanged generator 333. Native
Precommit passes in 29 seconds with its inherited unsupported-F# diagnostics.
PackAll passes in 31 seconds; TestBuild passes all 40 tests with zero skips.
Both exact versioned SDK paths used by the compiled publishing registrations
exist in the packed output. Publishing itself remains unexecuted.

A fresh SDK-only AltCover run also passes all 403 SDK tests. The final measured
source layout covers all 65 changed instrumentable sequence points and all 44
point-start lines; Decimal.fromString covers all six of its sequence points.
Whole-runtime coverage is 126/424 sequence points (29.717%) and 95/361 point-start
lines (26.316%). These remain source-point metrics, not branch or aggregate
threshold claims. Exact final hashes for all 14 source files and the conservative
span classifier are in `runtime-review-metrics.json`, with original OpenCover and
Cobertura reports alongside it under the retained semantic-validation directory.
The earlier pre-format report and matched-old/current evidence remain intact.

## Runtime landing and archival follow-up, 2026-11-03

The [SDK runtime delivery record](../../../../design/dotnet-csharp-generation/sdk-runtime-delivery.md) records the exact merged PR, distinct submitted and landed SHAs, final-head checks, review resolution, measured coverage and remaining ownership. All nine implementation tasks are complete. Canonical spec synchronization and the dated archive are prepared in this docs-only follow-up; its landing and coordinator cleanup are still required. No release publication or generator/CLI dispatch is claimed. Earlier checkpoint notes remain historical evidence.
