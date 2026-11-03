# Runtime checkpoint validation

This is an implementation checkpoint, not package-profile/AOT certification or
initiative completion. Issue [#464](https://github.com/finos/morphir-dotnet/issues/464).

## Identity and environment

- Worktree `C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-5a5ac278`, branch
  `t3/add-immutable-csharp-sdk`, clean starting base
  `7e37689c55eae9510cf12dafd130dc4ae8e3baf1`.
- Exact #462 and #463 GitHub state is MERGED to main; landed SHAs match the
  coordinator's supplied `793f378b8559bd667baa49bc0ba2cdf156d088c8` and base above.
  Planning commit `890aacb61df601f2876112a2cdd4d9d6f5757ca6` is reachable.
- Windows arm64; .NET SDK 10.0.403, runtime 10.0.14, numeric C#14.0,
  TUnit 1.48.0, MTP 2.2.3, OpenSpec 1.14.3.
- One coordinator-granted expensive-command lease. Builds use `-m:1
  -p:UseSharedCompilation=false -nodeReuse:false`. Test runner uses
  `--maximum-parallel-tests 2`.
- Full validation child processes use unique process-local TEMP/TMP
  `C:/Temp/morphir-csharp-sdk-5a5ac278-20261103`, DOTNET_PROCESSOR_COUNT=2,
  MSBUILDDISABLENODEREUSE=1, BuildInParallel=false and UseSharedCompilation=false.
  The temporary ProcessStartInfo wrapper sets only child environment values,
  suppresses child windows, captures output, and kills/verifies its whole owned
  process tree on timeout. No persistent environment or root props changed.

## Red, green and independent observations

Initial builds observed missing variants, then missing scalar/collection APIs.
The first runtime checkpoint passed 14 tests and all 207 required IDs.
Readonly review then identified real gaps. The missing protocol produced compile
RED; with its minimal declaration, exactly five semantic regression tests failed:
boxed generated records, unsafe equatable fallback, heterogeneous boxed list
keys, erased authored Decimal scale and signed-zero Dict replacement. Minimal
fixes made those tests GREEN. Additional compound-comparer checks also pass.

Final focused Debug execution is 20 tests passed, zero failed/skipped. The public
adapter executes 207 required IDs and passes all 207, with eight representation
exclusions reported separately. Its generated `conformance-results.json` contains
both complete ID sets, exclusions and zero failures. Every frozen operation is
dispatched explicitly through public runtime calls. Called callback errors,
retained operand snapshots and independently constructed equality/hash operands
are checked. Expected observations come only from the frozen corpus.

The adapter preserves representable authored decimal scale, normalizing only
when needed to admit an exactly representable input. It separately asserts raw
result-bit canonicalization for Decimal arithmetic. Numeric result projection
does not excuse redundant scale or signed/scaled arithmetic zero.

All 12 contract digests match, with expected LF normalization for checkout text
under core.autocrlf. All 44 pinned upstream evidence files match their exact byte
digests without normalization. No frozen files changed.

## Executed focused checks

```powershell
dotnet build tests/Morphir.SDK.CSharp.Tests/Morphir.SDK.CSharp.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --nologo
dotnet exec tests/Morphir.SDK.CSharp.Tests/bin/Debug/net10.0/Morphir.SDK.CSharp.Tests.dll --maximum-parallel-tests 2 --coverage --coverage-settings tests/Morphir.SDK.CSharp.Tests/coverage.config --coverage-output-format cobertura --coverage-output C:/Temp/morphir-csharp-sdk-5a5ac278-20261103/runtime.cobertura.xml
dotnet format src/Morphir.SDK.CSharp/Morphir.SDK.CSharp.csproj --no-restore --verbosity quiet
dotnet format tests/Morphir.SDK.CSharp.Tests/Morphir.SDK.CSharp.Tests.csproj --no-restore --verbosity quiet
openspec validate add-immutable-csharp-sdk --strict --no-interactive
mise run openspec:verify
dotnet fsi scripts/validate-changelog.fsx
```

All passed. The build had zero warnings/errors. Production-only coverage includes
Morphir.SDK.CSharp.dll, excludes the adapter/runner, and measures 318/333 lines,
95.4954954954955%. This is runtime coverage, not a repository-wide measurement.
Owned Markdown links and `git diff --check` passed.

`dotnet husky run` failed Git/Husky path discovery despite native Git in PATH.
No pre-commit hook exists in the resolved hooks directory. No checks are disabled
or bypassed. Both configured checks must run directly before a checkpoint commit.

The initial Nuke bootstrap failed CS1501 before Restore/Compile/Test because the
installed Nuke 10.1.0 method lacks the proposed callback overload. This failed
attempt is not full-suite evidence. The coordinator changed both resource chains
to `AddProcessAdditionalArguments` after installed public API inspection.
The corrected full Windows invocation was:

```powershell
& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target Test --configuration Release
```

It passed Restore, Compile and Test in 6m21s: 2,772 suite tests and 15 cold
package-consumer checks, zero failed/skipped. New C# tests passed 20/20 in Release;
the Release machine report contains 207 executed and passed IDs, eight exclusions
and zero failures. Both emitted Restore/Compile command lines contain `-m:1
-p:UseSharedCompilation=false -nodeReuse:false`. No compiler server existed before
the job, so the existing broad cleanup branch had no process to terminate.
Full-solution compilation emitted existing NU1608/Fantomas, NU1510/pruning and
generated interceptor warnings; the focused C# build was warning-free.

The same native bootstrap with `--target Precommit --configuration Release`
passed Lint/full-solution `dotnet format --verify-no-changes` in 25s. The formatter
reports its existing unsupported-F# and workspace diagnostics while exiting zero.
Both configured Husky checks therefore ran directly and passed, despite the
Husky path-discovery failure described above.

Integrated packaging used the already compiled solution:

```powershell
& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target PackAll --configuration Release --skip Restore Compile
& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target TestBuild --configuration Release --skip Restore Compile
```

PackAll's library/tool creation and validation passed, including the new package.
Actual TestBuild executed all 40 tests. It first caught missing new-package
release notes, then enforced the existing Markdown-section convention. Both RED
attempts passed 39 and failed one; neither was skipped or treated as acceptance.
The project now supplies meaningful `### Added` release notes without adding any
build-task/runtime dependency. The final PackAll rerun passed in 34s and the final
TestBuild passed 40/40, zero failed/skipped. The final new-package nuspec has an
empty net10.0 dependency group, Apache-2.0, README and the release notes.
Only project metadata changed after full runtime validation; runtime source and
the 318/333 measured coverage are unchanged. Final full-solution Precommit passed
again in 24s after the metadata fix. Final strict OpenSpec checks passed all 14
items and both checker tests. No dotnet, MSBuild, VBCSCompiler, testhost or Nuke
process remained after validation. Checkpoint durability is recorded in Git and
reported to the coordinator after the normal backup push.

## Downstream API and pending acceptance

The runtime is net10.0, one C# API with no project references or authored runtime
package dependencies, and uses System.Collections.Immutable from the framework.
IsAotCompatible adds SDK-managed Microsoft.NET.ILLink.Tasks 10.0.14 for build-time
analysis, with suppressParent=All; it is not a runtime dependency. `Maybe<T>`,
`Result<TError,TValue>`, Unit, Order, admitted scalar operations and thin immutable
List/Dict/Set wrappers are public. Comparer factories admit explicit scalar,
list and 2/3-tuple Morphir orderings. Boxed list keys validate recursive homogeneous
element shapes, including empty-list wildcards.

Generated/domain records and variants explicitly implement `IMorphirValue`:
`MorphirEquals(IMorphirValue)` checks exact case/type and every field with
`Basics.Equal`; `MorphirHashCode()` hashes the same fields with
`MorphirValue.Hash`. Typed Equals/GetHashCode overrides delegate to that protocol.
No generic CLR equatable, reflection or serialization fallback exists. The
protocol cannot enforce physical immutability or correct user implementations.
The API guide and reusable public-consumer source contain exact examples.

Two pinned ordinary local-feed compiler profiles, trimmed and NativeAOT
consumers, final integrated packaging, PR readiness, landing/archive and cleanup
remain pending. Tasks 1.2 and 3.1/3.2/3.3 remain unchecked. The source-generator
adapter is deferred.

## Ownership

One reserved read-only auxiliary completed in this shared checkout, no edits,
Git operations, heavy commands, helpers, extra worktree or recursive delegation.
Task `node:delegated-task:command%3Amcp%3A2d5a0878-e705-445b-beaa-8dc736792d50%3Adelegate-task%3Acsharp-readonly-review-20261103-01`.
Its four findings are integrated into the regression fixes above; its slot is
released. No other auxiliary has run.

Validation logs, reports, temporary wrapper and process/result manifests are
retained under the unique TEMP directory above. `owned-artifacts.json` also lists
the four initial loose C:/Temp logs and the temporary issue-body file. Debug and
Release conformance ID reports are copied into that evidence directory.
Final checkpoint commit/head,
backup push and lease/helper release will be reported separately. The active
worktree remains for coordinator-owned cleanup after landing.
