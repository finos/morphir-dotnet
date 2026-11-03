# Implementation evidence

This file records checked behavior at each checkpoint. It does not declare the
initiative delivered before its full acceptance, PR landing and archive follow-up.

## Foundation

Issue [#474](https://github.com/finos/morphir-dotnet/issues/474). Initial clean
checkout `t3/add-csharp-code-generation`, owned path
`C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-1d28e9ab`, exact base
`618fbf7b6c5b542122450bf61f4f794e590fe4f5`.

Native Git fetched main and `gh pr view` independently confirmed MERGED:

| PR | Submitted head | Actual landed squash SHA |
| --- | --- | --- |
| #462 | 3566994148259bf042cc11d864cf11703b4181e7 | 793f378b8559bd667baa49bc0ba2cdf156d088c8 |
| #463 | 9c4cb3aa72c4606e56380f68dc0dea979e5d3e73 | 7e37689c55eae9510cf12dafd130dc4ae8e3baf1 |
| #469 | 7dde9e57bf1fd3a38fd1dc8df7cd0a65919d402d | 4df3c1ad425f782bfe3cc02e30f577d87dca0f53 |
| #470 | a6f16388ebea138137aa5c6e15589a2c0a14b550 | 55f73bdaa93b7bc55a48b184bab8e46a5636fd9e |
| #472 | 114bd11b9f9127dc2fee18924ee2f37d472be34a | 618fbf7b6c5b542122450bf61f4f794e590fe4f5 |

Each landed SHA and unrelated configuration commit
`422ff4efa3139868bc73f79d1cdf4df529e2b34f` passed
`git merge-base --is-ancestor <SHA> origin/main`. Original submitted heads are
not required ancestors after a squash. Frozen contract artifact
`737e85b3a202ba733b153be637e33cf953342705` is distinct from conformance landing.
All 12 readiness-manifest contract hashes match, with declared LF normalization
for text. All 44 provenance source hashes match their original bytes.

Before production, the feature PRD and BDD stories were written. The first test
build failed because the entry point/project did not exist. The minimal entry
point made empty input and pre-cancellation pass. A subsequent generator-driver
test failed with zero generated trees, expected one. The minimal admitted
integer-constant lowering made AddSource compile and execute `42`.

Unknown source and language versions were observed RED as success, then GREEN
as located whole-model failures. The modern host was refactored to a test-only
incremental generator. Its compilation-provider output honors driver cancellation;
the initial post-initialization-only callback did not observe that cancellation
and was replaced after the failing test. There is no production Roslyn adapter.

Focused execution passes 13 tests, zero ignored/failed/errored:

```powershell
dotnet build tests/Morphir.CodeGeneration.CSharp.Tests/Morphir.CodeGeneration.CSharp.Tests.fsproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --nologo
dotnet exec tests/Morphir.CodeGeneration.CSharp.Tests/bin/Debug/net10.0/Morphir.CodeGeneration.CSharp.Tests.dll --sequenced
```

The Models project's inherited NU1510 System.Text.Json pruning warning remains.
The new generator has no authored dependency on Tooling, Ion, Pipeline, compiler
processes, files, configuration or Roslyn. Its package needs Models,
FSharp.Core 11.0.100 and FSharp.SystemTextJson 1.4.36. Expecto/Mono.Cecil/Roslyn
are host-test dependencies, not the generation package's production closure.

The fresh-package smoke uses the real packed Models/core, a private cache, and
the same test adapter source, with no repository project/source references:

```powershell
& tests/Morphir.CodeGeneration.CSharp.Tests/verify-host.ps1 -EvidenceDirectory C:/Temp/morphir-csharp-generator-1d28e9ab-20261103/early-host-03
```

All 13 tests pass there. The model is supplied in memory; the callback calls
generation/AddSource without CLI or host file reads. Tests obtain compiler
metadata references at the host boundary. The first package attempt in
`early-host-01` retained the actual FSharp.Core 10.1.403 implicit-reference
downgrade and assembly-11 load failure. Disabling only the F# test consumer's
implicit core reference uses the real transitive closure; `early-host-02` passed
11 tests, then the incremental/cancellation refinement passed 13 in `early-host-03`.
No repository-wide retarget or dependency change was used.

Tested host: Windows arm64, .NET 10.0.14, SDK 10.0.403, Roslyn
5.9.0-1.26523.112. Legacy IDE/netstandard/.NET Framework hosts and other platforms
are untested. This is bounded adapter evidence, not full source-generator support.
Mid-traversal cancellation is pending task 3.4. C# 15 compiler execution is pending
task 1.3. No broad type/expression coverage is claimed at this checkpoint.

Root granted a narrow serial lease for the anchored shared registrations in
[shared-edits.md](shared-edits.md). Those edits are applied. The actual native
Windows command `pwsh -NoProfile -File build.ps1 --target Test --configuration Release`
passed Restore, Compile and Test in 7m52. It executed 3,414 suite tests, zero
failed/skipped, 15 cold package checks, and 48 separate inspection-smoke checks.
The explicit sequential array ran the new suite's original 13 tests, all passing.
Five later focused tests cover empty v4, Specs, invalid later-module atomicity,
open-record refusal and documented private access. All 18 pass in the fresh
`foundation-host-04` package consumer. The unchanged production behavior remains
covered by the earlier full run; the five supplemental tests are not folded into
that historical full-suite denominator.

Production-only coverage uses dotnet-coverage 18.7.0, statically instruments the
generator assembly only, and runs the actual 18-test Release executable.
The report measures 112/124 lines, 90.32258064516129%. The earlier 13-test report
was 98/124, 79.03225806451613%, and is retained. This measures only the current
foundation, not the planned generator or the whole repository. No line/type
filter excludes authored generator code. [Microsoft's tool documentation](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage)
describes the static instrumentation used by the owned coverage configuration.

`PackAll --configuration Release --skip Restore Compile` passed. The added direct
metadata test then found Models had no release notes. Root approved only the
exact metadata property. The original RED report is retained; fresh PackAll
passed again in 41s, both new packages pass their direct identity/version,
net10.0 assembly, Apache-2.0, README, dependency and release-note checks, and all
40 actual TestBuild tests passed, zero failed/skipped. The metadata check invoked
the compiled `PathHelper.GetLibraryPackagePath` against both exact versioned
package paths. No publishing target or machine NuGet change occurred.

Native `Precommit --configuration Release` passed in 36s. It reports inherited
unsupported-F# formatter diagnostics while returning success. All three owned
F# files separately pass Fantomas 5.2.4 `--check`. Changelog validation passed.
`dotnet husky run` failed Git/path discovery despite native Git configuration;
no hook was bypassed or disabled, and both configured checks ran directly.
Strict OpenSpec validation passes all 22 items and both checker tests. The
Windows restore's two unrelated configuration-smoke lockfile RID changes were
examined and restored to their original contents. No frozen contract changed.

All heavy commands use the granted sole lease, DOTNET_PROCESSOR_COUNT=2,
bounded MSBuild options and unique process-local TEMP/TMP under the owned
`C:/Temp/morphir-csharp-generator-1d28e9ab-20261103` tree. The capture wrapper
suppresses child windows and terminates/verifies its whole owned tree on timeout.
No auxiliary agent has run. No shared/root cleanup directory has been touched.

## Numeric compiler profiles

The test-only exporter calls the same in-memory entry point with an explicit
numeric profile, then writes its returned sources at the test boundary.
`acquire-compiler.ps1` verifies the official RC2 ZIP against SHA-512
`a74c1ab374a5167ada496dfe0d22367e9b5f2d89eb6d232122beb0e71b30db4bf868e3a06aa5df5aeea8be9fc3194218d2134e1c9de067419e18eed3b34c6e33`.
It copies only matching 10.0.13 reference packs into the isolated toolchain and
verifies all 1,063 destination files against source SHA-256. The full inventory
is under the owned `toolchains/rc2/toolchain.json`. No machine SDK is installed.

```powershell
& tests/Morphir.CodeGeneration.CSharp.Tests/verify-profiles.ps1 -EvidenceDirectory C:/Temp/morphir-csharp-generator-1d28e9ab-20261103/profile-proof-01 -RcDotnet C:/Temp/morphir-csharp-generator-1d28e9ab-20261103/toolchains/rc2/sdk/dotnet.exe
```

Both fresh consumers restore with no feeds or dependencies, compile with numeric
14.0/15.0 and warnings as errors, and actually execute `generated-consumer:42`.
Both target net10.0 and run under the stable 10.0.14 runtime. The selected compiler
versions were queried independently:

- Stable SDK 10.0.403, csc 5.9.0-1.26523.112,
  revision `5a2162bea6845eabc1fc721624a43568c65db3b1`.
- RC2 SDK 11.0.100-rc.2.26504.105, csc 5.12.0-1.26504.105,
  revision `820230c386db5b0106c5430b3e5aee92a7826bf5`.

Both generated-source hashes are
`db6b48392a4c499fb27bb4d1a71c4b3356d0f033306ae4c9152517f61048fef8`.
This proves the initial constant fixture across both real compilers, not yet the
pending broad family/behavior matrix. Default options remain 14 independently
of ambient SDK, and unknown numeric values fail before emission, as previously
observed RED/GREEN. Root global.json is unchanged. Compiler release status is
metadata, not another language option. Production code is unchanged from the
foundation checkpoint, so its full-suite and coverage evidence still apply.

## Immutable type checkpoint

Separate classic/v4 typed walkers now lower transparent aliases, generics,
tuples, closed records, nested anonymous records, custom cases, function types
and unit. BDD and compiled consumer failures preceded these changes. Additional
RED observations exposed culture-sensitive names, nominal record aliases,
generic concrete-record aliases, Dict key constraints, base-typed case equality,
public aliases of private structural records and private nominal payload leaks.
The retained logs record each failure and correction. Unsupported/open types,
arity errors, cyclic aliases, unbound parameters, incomplete bodies and private
nominal leaks return located failures without sources.

The shared independent type observations execute construction, Result error/value
meaning, nested structural equality, transparent aliases and get-only payloads.
Both typed families compile and run under both pinned numeric compilers, with
warnings treated as errors. `type-matrix-02/profiles.json` records all four runs,
each printing `generated-consumer:types-passed`. Their source SHA-256 is identical:
`2bf6dec3b80dc924d19cc0f12c29c3ce8867367068fd8888cb1664d648ed9c4e`.
Separate-assembly consumer tests reject private constructors. The fresh packaged
modern host in `type-host-02` runs all 45 tests, zero ignored/failed/errored.

The production-only static coverage report `types-final.cobertura.xml` measures
796/881 lines, 90.35187287173666%, using all 45 Release tests. No authored code is
excluded. The prior type report measured 757/817, 92.65605875152998%, before the
additional alias visibility code; both denominators are retained. These measure
the current generator, not whole-repository coverage or pending lowerings.

Naming/portable paths, full expressions, mid-traversal cancellation, broad fixture
execution and trimming remain unchecked. No production Roslyn adapter or CLI
registration is introduced. The root shared-file leases were released after the
foundation checkpoint and no shared path is edited in this type checkpoint.

The type checkpoint's native full Nuke Test passed Restore/Compile/Test in 7m25.
The captured suite totals are 3,446 tests, zero failed/ignored/skipped, including
the explicit new sequential suite's 45/45, plus 15 cold package checks and 48
separate configuration inspection smoke checks. This is a fresh full-suite
measurement, not the historical foundation count. ANSI color sequences are
removed before parsing the retained `types-suite-totals.json` evidence.

`PackAll --configuration Release --skip Restore Compile` passed in 47s, then
`TestBuild --configuration Release --skip Restore Compile` passed all 40 tests,
zero failures/skips. `verify-package-metadata.ps1` checked the fresh
`0.3.0-rc.2` packages and compiled `build/bin/Release/_build.dll` exact lookup;
`types-package-metadata.json` records both package hashes and real dependencies.
No publishing target ran. Native Precommit passed in 30s with the same inherited
unsupported-F# diagnostics. Fantomas `--check` passes all six owned F# files;
changelog validation passes; strict OpenSpec validation passes 22 items, its
snapshot checker passes, and its two tests pass. `git diff --check` is clean.

## Pinned implementation references

Read-only source retrieval uses the exact planning revisions, with captured
trees/source files under the owned `prior-art` directory. No source was copied
into production. Python `560a6503b8ffcab87ea667391579b747757bec0d`,
`rust/crates/morphir-python-binding/src/backend/functions.rs`, separates signature
validation from emission and preserves flattened application argument order.
Its refusal of multi-argument partial applications is a source limitation, not
this change's acceptance boundary. Gleam
`dcfa73c218276dd8e172bbc0a2b579110bce8e46`, backend `visitor.rs`, explicitly
refuses parameterized local lets, record expressions without resolved named
constructors and record updates; its local-only value reference rendering is
also insufficient for this generator's qualification requirement.

MoonBit `10732eaf2f05b10ca6e94a78121e2753c814acd8`,
`pkgs/morphir-elm/backend/lower_values.mbt`, and Elm vnext
`1be5323eb24d943c7cb3ac965d5a51d13fd83e5d`,
`moonbit/backend/lower_values.mbt`, demonstrate explicit scope binding/reservation
and ordered application flattening. This emitter will use fresh lexical target
identities and typed nested Func values for the required capture/partial-apply
behavior. These references inform implementation choices; they are not test
oracles or evidence of C# conformance.

## Naming, literals and function checkpoint

Separate typed value walkers now feed private target expressions, whole-function
type checking and deterministic text emission. The initial function BDD consumer
failed for both families before implementation. Real compilation then exposed
C# interpreting `(f)(x)` as a cast in identifier-only cases. Emitting `.Invoke`
corrected it. The authored shared consumer observes generic identity, captured
local functions, nonrecursive shadowing, three-argument partial application,
returned nested functions and caller callback order. No object erasure, dynamic
dispatch or runtime implementation stubs are emitted.

Nineteen original literal cases failed before implementation. Signed zero,
classic nonfinite values, exact Decimal96, BMP chars and supplementary strings
now execute with independent expectations. Invalid UTF-16 string fixtures must
be constructed from a surrogate char at runtime because the F# compiler replaces
the corresponding string-literal metadata; both the mistaken fixture result and
corrected test are retained. Large integers, unsupported chars, invalid UTF-16
and rounded decimals fail with `representation-loss`, without sources.

Additional RED cases found portable case-insensitive paths, malformed names,
reserved protocol payloads, nested-record payloads, generic/case identifiers and
type/value/module collisions. Safe path prefixes avoid Windows device names.
Module static classes now have a `Module` suffix, and lexical targets are fresh.
The shared type/function corpus in `functions-matrix-01` compiled and executed
in all four real family/profile combinations. The source hashes, shared across
all four runs, are:

- `p_Example/m_DataModule.g.cs`:
  `5ac1939afbf1bc9e41626381666e62ee7ec818ab8995505f3ee8f9f8ac057456`.
- `p_Example/m_FunctionsModule.g.cs`:
  `b135ca235afbad84cfdaab62b95d32755cb0ea1bf44384a249d52336ab27ad26`.

The fresh packaged modern host ran 73 tests in `functions-host-01`, all passing.
Supplemental admission/naming tests brought the local Release suite to 90/90.
The matching static production report measured 1,447/1,601 lines,
90.38101186758276%. Its earlier 73-test report was 1,402/1,576, 88.95939086294417%,
and is retained. Two further case-name RED tests were then added and corrected.
The checkpoint's final full run and refreshed coverage are recorded below.
This evidence does not cover pending patterns, record operations,
the broader SDK inventory, traversal cancellation, business fixtures or trimming.

Native `build.ps1 --target Test --configuration Release` passed in 7m15.
`functions-suite-totals.json` records 3,493 suite tests, zero failed/skipped/ignored,
including the explicit sequential generator suite's 92/92, plus 15 cold package
checks and 48 separate configuration inspection smoke checks. Fresh PackAll
passed in 48s, TestBuild passed 40/40, and both new packages passed metadata and
compiled exact-version lookup checks without publishing. Native Precommit passed
in 30s with inherited unsupported-F# diagnostics. All ten owned F# files pass
Fantomas, changelog validation passes, and strict OpenSpec validation passes all
22 items plus snapshot check and two tests. `git diff --check` passes.

The final 92-test production-only static report `functions-final.cobertura.xml`
measures 1,464/1,614 lines, 90.70631970260223%. All authored generator code remains
in the denominator. The fresh package host rerun `functions-host-02` also passes
92/92. Production dependency closure is unchanged. Task 3.1 is checked; task 2.2
remains unchecked pending remaining allocation edge cases and final determinism
verification. The source/compiler proof above predates only additional refusal
guards, which do not change these valid corpus sources. No shared file changed.
