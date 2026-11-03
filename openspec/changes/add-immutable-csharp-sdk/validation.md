# C# SDK validation

The runtime checkpoint below is followed by actual packaged profile/publication
proof. PR readiness is not initiative completion. Issue
[#464](https://github.com/finos/morphir-dotnet/issues/464).

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

## Downstream API

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

The exact API and generated record/variant implementation requirements are in
[api-handoff.md](api-handoff.md). The source-generator adapter remains deferred.

## Isolated packaged consumers and publications

Runtime source is unchanged from the pushed checkpoint
`0b01ff037f0496071e8ba4689c785376658a8f30`. Its full tests, coverage and package
checks above remain the evidence for that exact source. This follow-up adds only
the reusable PowerShell verification harness, task progress and documentation.
No repeated broad runtime tests are claimed or needed for those changes.

Microsoft's [RC2 release](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc2/11.0.0-rc.2.md)
and [official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json)
pin SDK 11.0.100-rc.2.26504.105, Windows arm64 ZIP. It was downloaded only into
the owned evidence directory's toolchains folder. SHA-512 matched exactly:

```text
a74c1ab374a5167ada496dfe0d22367e9b5f2d89eb6d232122beb0e71b30db4bf868e3a06aa5df5aeea8be9fc3194218d2134e1c9de067419e18eed3b34c6e33
```

The SDK's net10.0 targeting references are 10.0.13. Matching installed SDK
reference packs were copied only into the isolated RC directory: Core 348,
ASP.NET 438, Desktop 277 files. Every one of the 1,063 copied files matched its
source SHA-256. They are compiler inputs, not consumer dependencies. Root
global.json and user/machine/repository dependency settings remain unchanged.

The final successful invocation was:

```powershell
& tests/Morphir.SDK.CSharp.Tests/verify-package-consumers.ps1 `
  -EvidenceDirectory C:/Temp/morphir-csharp-sdk-5a5ac278-20261103/package-proof-03 `
  -StableDotnet C:/Users/damre/AppData/Local/dotnetup/dotnet/dotnet.exe `
  -RcDotnet C:/Temp/morphir-csharp-sdk-5a5ac278-20261103/toolchains/rc-11.0.100-rc.2.26504.105/dotnet.exe `
  -Mode All
```

This packed local-only version `0.0.0-sdkproof.0b01ff0`, SHA-256
`8997d674e06ea871ad7feafde1c6bad45c6609c515318243f822a532c18370a0`.
The nuspec records the runtime source checkpoint and has no runtime dependencies.
Each ordinary consumer had a fresh project and private empty NuGet cache,
local-only source mapping, no fallback feed and no project/source reference.
Each project.assets.json contains exactly Morphir.SDK.CSharp at that version.

| Profile | Numeric language | Selected SDK | Exact compiler/version revision | Result |
| --- | --- | --- | --- | --- |
| Stable | 14.0 | 10.0.403 | 5.9.0-1.26523.112 / 5a2162bea6845eabc1fc721624a43568c65db3b1 | Restore/build/run passed, zero warnings/errors |
| RC2 | 15.0 | 11.0.100-rc.2.26504.105 | 5.12.0-1.26504.105 / 820230c386db5b0106c5430b3e5aee92a7826bf5 | Restore/build/run passed, zero warnings/errors; SDK prerelease informational message only |

Both compile the identical public consumer source, SHA-256
`6bfdc7168e13bf14954040104df7a581db768da169d19c7c63f345a9da27dc9f`.
It covers Maybe/Result payload meanings and bypassed callbacks, Unit/Order,
immutable nested List/Dict/Set values, explicit compound/ordinal comparers,
generated record protocol after boxing/tuples, recursive NaN inequality,
signed-zero equality/hash and replacement, checked overflow, nonfinite errors
and exact Decimal behavior. Both target net10.0 and run with 10.0.14.
The same source was published and executed for win-arm64 as trimmed and NativeAOT
under the stable SDK, with all assertions and output `sdk-consumer:passed`
matching both ordinary builds. Publish flags include `-warnaserror`,
`-p:SelfContained=true`, exact runtime 10.0.14, and bounded MSBuild/ILC parallelism.
Trimming uses PublishTrimmed=true/TrimMode=link/ILLinkTreatWarningsAsErrors=true;
NativeAOT uses PublishAot=true. No trim/AOT warning was suppressed or accepted.

Native prerequisites were existing VS Build Tools 17.14.37614.0, MSVC
14.44.35207 with Hostarm64/arm64 linker, and Windows SDK 10.0.26100.0. No native
toolchain was installed or changed. The successful NativeAOT publish logged
Generating native code; the executed Consumer.exe is 1,831,424 bytes, SHA-256
`52f842bd0bd9f6612481501c6f80aa01dc5006bda2f2df612210519e173354a5`.
Independent artifact audit confirms ARM64 PE machine 0xAA64, CLR header RVA 0,
and no Consumer.dll/coreclr.dll in the publication. The trimmed executable is
139,776 bytes, SHA-256
`35ad0ed871421d4827b9cf7670da6059d0af0919265a822d763b1ebe6ac53ac0`;
that figure describes its apphost only, not the self-contained directory size.

Each child has its own DOTNET_ROOT/PATH/TEMP/TMP and exact SDK global.json with
rollForward=disable. Host runtime roll-forward is process-local LatestPatch,
so SDK tools requesting a servicing build can use the installed stable patch.
The existing VS Installer directory is added only to child PATH for vswhere.
No parent environment mutation occurs. All jobs are serialized, captured and
hidden, with whole-owned-tree timeout termination and absence verification.

Earlier evidence is retained honestly: proof-01 passed stable but RC restore
needed the missing ASP.NET/Desktop targeting packs; proof-02 first exposed
restore's unsupported --self-contained option, then overstrict host runtime
roll-forward, then missing vswhere on child PATH during native linking. These
were verification/tooling setup issues. Final fresh proof-03 reran all four
checks with the corrected harness and passed. No runtime production source,
frozen expectation, inventory ID, exclusion or profile changed to obtain passes.

Limits are explicit: net10.0 API and these two exact compiler profiles; native
publication is a representative Windows arm64 consumer, not all OS/RID/generic
combinations. No native C# union or full source-generator support is claimed.
Landing, spec sync/archive and final cleanup remain coordinator-owned.

## Final publishing registration

The coordinator added only the C# package's exact versioned PathHelper lookup,
existence check and push registration to PublishLibs and PublishLocalLibs.
`dotnet build build/_build.csproj --no-restore -m:1
-p:UseSharedCompilation=false -nodeReuse:false --nologo` passed with zero
warnings/errors. The compiled helper resolves the existing package
`artifacts/packages/Morphir.SDK.CSharp.0.3.0-rc.2.nupkg`, SHA-256
`8c0317228749ac45c85afa62d6f2c632a8dd5c6b387694936bcc9e39a0f12a27`.
Both targets depend on PackLibs, which depends on Compile then Restore.
Captured Nuke help listed that graph, then exited with "The handle is invalid"
from console handling; Restore/Compile were NotRun. This is graph inspection,
not a passing publishing run. No publishing target was executed, no remote
package was pushed, and machine NuGet source configuration was not changed.
The initial PowerShell reflection probe rejected a wrapped path argument;
the explicit string/object-array probe then successfully called the compiled
PathHelper. Logs and `final-publishing-path.json` retain these observations.

Final Nuke Precommit formatting verification passed in 23s with the existing
unsupported-F# project diagnostics. The configured changelog validator passed.
Husky again failed Git/path discovery; neither hook nor check was bypassed, and
both configured checks were executed directly. Runtime and frozen files remain
unchanged from the source checkpoint, so full runtime tests/coverage were not
repeated for documentation, the proof harness and publishing registration.
Final `mise run openspec:verify` passed all 14 strict items and both checker
tests; apply instructions report 9/9 complete and state all_done. Relative
documentation links, PowerShell parsing and git diff --check passed.
All nine implementation tasks are now verified. PR delivery, newer-main
integration, review, landing, archive and cleanup remain separate workflow steps.

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
