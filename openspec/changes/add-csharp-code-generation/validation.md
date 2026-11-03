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

## Allocation completion and status-doc lease

Four final allocation tests exposed three RED behaviors: empty libraries with an
empty package path unnecessarily failed namespace validation, and inherited CLR
module/type member identities produced success-shaped invalid output. The
corrections preserve empty success and refuse those collisions. Reversing v4
type/module declaration insertion order preserves exact returned sources. All
96 focused Release tests pass. Task 2.2 is now verified and checked.

Root granted a narrow status-only lease for the PRD index and umbrella. The index
records the completed SDK archive #472 and retained empty-directory cleanup
limitations. The umbrella binds its stronger type/function host claims to
checkpoint `a4cc96a` and its 92-test denominator, with the exact tested modern
host and still-gated CLI. No historical evidence or blocked/locked directory is
changed. The lease will be released after this boundary is committed and pushed.

The 96-test allocation boundary passed the full native run in 7m14, with 3,497
suite tests plus 15 cold checks and 48 inspection checks. PackAll passed in 47s,
TestBuild passed 40/40, fresh package metadata/exact lookup passed, and Precommit
passed in 31s. Static generator coverage measured 1,482/1,632, 90.80882352941176%.

A final audit added function-generic collision and empty-name cases. The first
helper normalized `"a"` and `"A"` to the same valid Morphir name. Its two wrong
expectations caused `allocation-final-full-test` to fail, 98/100 generator tests,
with all other executed suites passing. That failure is retained. The corrected
fixture constructs distinct typed `Name` values directly. Removing the new guard
then produces four actual RED failures in `function-generics-raw-red.log`.
Reusing `uniqueNames` for function type parameters makes the 100-test focused
Release suite pass. The fresh bounded Release build precedes a new native full
Test run with Restore/Compile skipped; no runtime suite is substituted or omitted.

The corrected native full run passed in 5m38. Its captured totals are 3,501 suite
tests, including generator 100/100, plus 15 cold checks and 48 inspection smoke
checks, with zero failures/skips/ignored tests. Fresh PackAll passed in 46s,
TestBuild passed 40/40, metadata and compiled exact-version package lookup passed,
and Precommit passed in 30s with the inherited F# diagnostics. The fresh packaged
host passes 100/100 in `allocation-host-01`; all four real compiler executions
pass in `allocation-matrix-01`, preserving the previously recorded valid source
hashes. Static generator coverage is 1,495/1,637, 91.32559560171044%, in
`allocation-complete.cobertura.xml`. No authored generator code is excluded.

All ten owned F# files, changelog validation, strict OpenSpec's 22 items, snapshot
checker and its two tests are checked before commit. Leased Markdown relative
links resolve. No publishing target, blocked directory, other shared path or
auxiliary is involved. Temporary pattern test drafts remain unregistered and are
explicitly not passing acceptance evidence.

## Ordered patterns and persistent record operations

The registered pattern drafts first produced two actual RED failures in
`patterns-red.log`, one per family. Separate typed classic/v4 walkers now check
patterns and expressions before target emission. The shared compiled consumer
observes first-match order, custom constructors and constructor function values,
tuple aliases, pattern lambdas, destructuring, recursive list sum, list literals,
record construction/fields/updates, field functions, conditionals and unit.
Further cases check nine-element tuples, Boolean/string/unit patterns, selected
conditional branches, duplicate binders, branch types, unknown fields, update
types and explicit local-recursion refusal. A separate v4 execution reverses
record/update field order and verifies the callback trace independently.

Five additional RED failures in `patterns-name-red.log` exposed inherited CLR
record-field collisions and function/type-parameter identity collisions. Located
name refusals correct both. The focused Release suite passes 126/126. Static
production-only coverage is 2,506/2,687 lines, 93.26386304428731%, in
`patterns-01.cobertura.xml`, with all authored generator code in the denominator.

`patterns-matrix-01/profiles.json` records four actual compiler executions of the
same type/function/pattern/record corpus. Both families compile and run with
numeric 14.0 on SDK 10.0.403/csc 5.9.0 and numeric 15.0 on SDK
11.0.100-rc.2.26504.105/csc 5.12.0, using the pinned revisions recorded above.
All four source sets are identical. The Data module SHA-256 remains
`5ac1939afbf1bc9e41626381666e62ee7ec818ab8995505f3ee8f9f8ac057456`;
the expanded Functions module is
`fc16928b47a13acf59cb28d90e51a4bd8e6732fb40b494fc5737a373cf6612e9`.

The first native full Test run reused compiled outputs and failed in 52 existing
Tooling CLI tests because CLI processes could not load Morphir.Configuration or
Morphir.IR.Pipeline. The generator's 126 tests and all other executed suites
passed; cold checks did not run after the Test failure. The complete failed run
is retained in `patterns-full-test.log`. With no active compiler/test processes,
the normal native Test target was restarted with Restore and Compile included
to reconstruct the CLI dependency closure. Its result is recorded below.

All twelve frozen contract digests still match, owned Markdown relative links
resolve, and strict OpenSpec validation passes all 22 items. Task 3.2 is checked
from the compiled behavior evidence; SDK mapping, traversal cancellation,
business fixtures and final package/trimming acceptance remain pending.

The rebuilt native full Test passed in 7m09, including Restore 2s, Compile 1m23
and Test 5m42. `patterns-suite-totals.json` captures 3,527 suite tests, including
generator 126/126, plus 15 cold package-consumer checks and 48 configuration
inspection smoke checks. Failures, skips and ignored tests are zero. PackAll
passed in 48s. Fresh Models/generator packages pass metadata validation and the
compiled exact-version package lookup in `patterns-package-metadata.json`.
No publishing target was executed. All six frozen JSON files match their exact
raw SHA-256 hashes; the remaining text hashes use the manifest's LF normalization.
All 44 original evidence files match their raw byte hashes.

TestBuild passes 40/40. The fresh packaged host passes 126/126 in
`patterns-host-01`, with the unchanged production closure and bounded tested
host described above. Native Precommit passes in 32s with the inherited
unsupported-F# diagnostics; changelog validation and strict OpenSpec's snapshot,
schema, 22 items and two tests pass.

The first Fantomas check incorrectly supplied directories as file arguments and
failed with UnauthorizedAccessException. The corrected explicit file check found
five formatting differences. After formatting, a fresh Release build and all
126 tests pass. The refreshed all-authored-production report is 2,514/2,695 lines,
93.28385899814471%, in `patterns-final.cobertura.xml`. A new PackAll passes in 46s;
both fresh packages pass metadata/exact lookup checks, and `patterns-host-02`
passes 126/126. These final changes are formatting only; the full native result
above covers the unchanged behavior. The final source export is compared with
the exact four compiler-tested source sets before committing.

## SDK bindings and frozen generated-call observations

The initial SDK RED run failed in both families at unmapped multiply. Six RED
checks then covered that slice plus unsupported comparison/function-equality
domains. Typed public SDK bindings made 132 tests pass. Four further RED checks
showed that SDK domains must validate after record-field inference; moving that
validation makes 134 tests pass. The original duplicate test names were corrected
before observing the field-domain failures, with both setup attempts retained.

FrozenSdkCases embeds the unchanged inventory and cases. Its first generated-call
RED failed at Basics.divide. Bindings now cover all 67 frozen operations through
the real SDK, including typed structural Dict/Set comparers and exact Decimal.
Both typed paths execute all 207 required IDs. The adapter checks exact ID-set
membership, values/errors, preservation fingerprints, equality/hash invariants
and canonical Decimal arithmetic bits. All eight representation exclusions remain
exclusions; all four historical F# gap records remain unchanged authority.

Two test-adapter discrepancies are retained. `sdk-frozen-green-01` incorrectly
required identical Decimal scale bits for ordinary List.singleton observations;
the frozen contract compares Decimal values across scale differences.
`sdk-frozen-green-02` tried constructing scale 29 directly for an exactly reducible
input. The corrected adapter removes trailing zeros only as required to fit
Decimal96, never rounds/truncates, and retains canonical-bit checks specifically
for Decimal arithmetic. `sdk-frozen-invariants` passes 136/136 with the full frozen
invariants. No frozen expected value or runtime source was changed.

Four subsequent RED checks cover constructor value references and public function
exposure of private nominal types. Those fixes pass 140/140, including Order
patterns. Another two actual C# compiler failures exposed repeated generic record
fields with reordered equivalent nested field declarations. Shape matching now
ignores that declaration order while value evaluation order remains unchanged.
The focused suite still passes 140/140.

All four executions in `sdk-matrix-02` compiled and ran successfully, but its byte
comparison correctly failed because the newly authored shared fixture supplied
different ordered record values to classic's map and v4's ordered list. The
corrected fixture supplies equivalent value order while retaining differing type
field order. The separate reversed-v4 evaluation-order case remains. The checker
was not weakened. `sdk-matrix-03` passes all four real executions and byte checks,
covering the shared type/function/pattern/record/SDK corpus and all 207 required
SDK observations under each compiler. Data-module SHA-256 remains
`5ac1939afbf1bc9e41626381666e62ee7ec818ab8995505f3ee8f9f8ac057456`;
Functions-module SHA-256 is
`1eb034c70e636c224d9540ec73ce971f0e31d90857bbd7a49e4aca910e1bb7cf`.
The exact compiler builds and net10 runtime remain those pinned above.

Final authored-production static coverage in `sdk-final.cobertura.xml` is
2,903/3,121 lines, 93.01505927587311%, with no authored code excluded. The earlier
SDK report remains 2,895/3,111, 93.05689488910318%. Task 3.3 is checked and apply
instructions refresh to 9/13. Full native, fresh package-host and package checks
are recorded below when complete. No CLI registration, production compiler
adapter, runtime change or additional production dependency is introduced.

Root granted a new serial two-file status-only lease for the index C# row and the
umbrella's present status. The appended umbrella paragraph binds its 126-test
host and four-profile pattern/record claims explicitly to checkpoint `8dca9b6`.
The `a4cc96a` paragraph and 92-test count remain historical, with its former
pending scope qualified as historical. SDK cleanup and other rows are untouched.
The serial lease covers only these edits and ends at the verified commit/push
boundary.

The SDK boundary's native Restore+Compile+Test run passes in 7m21: Restore 3s,
Compile 1m33 and Test 5m45. Captured `sdk-suite-totals.json` records 3,541 framework
tests, including generator 140/140, plus 15 cold package checks and 48 inspection
smoke checks, with zero failures/skips/ignored tests. Frozen observations within
aggregate tests are not added again to that framework-test denominator.
PackAll passes in 47s. TestBuild passes 40/40 using the freshly compiled outputs.
Both packages pass fresh metadata and compiled exact-version lookup checks in
`sdk-package-metadata.json`. The updated resource-copy edge runs the same 140
tests successfully from fresh packages in `sdk-host-01`, including the test-only
driver. The production closure and bounded .NET 10.0.14/SDK 10.0.403/Windows arm64
host claim are unchanged. No publishing target runs.

Native Precommit passes in 30s with the inherited unsupported-F# diagnostics.
All 15 owned F# files pass the explicit Fantomas check. Changelog validation,
strict OpenSpec's snapshot/schema/22 items/two tests, owned and leased Markdown
relative links, and `git diff --check` pass. All twelve frozen contract hashes,
including exact raw JSON, and all 44 original evidence hashes still match.

## Active traversal cancellation

The observed RED `cancellation-red.log` left the owned generation thread running
for the full 30-second post-cancel join during acyclic alias expansion. The
background test process exited, and process absence was verified before retrying.
The initial test-build indentation error is retained separately. Checks inside
recursive expansion first made that behavior green. Token checks now also cover
substitution, field-shape matching, visibility validation, name allocation, SDK
comparer recursion, typed patterns/values and text emission. A final check guards
the successful whole-model result. The public API and dependency closure did not
change.

`cancellation-final-focused.log` passes 143/143. Cancellation happens after the
nonempty worker is observed active. Alias expansion stops in 1.204 ms; classic
and v4 million-character strings stop in 0.221 and 0.150 ms in that run. These are
bounded observations, not a latency SLA. Each worker joins before assertions, and
each result retains its family/version with no sources. Existing later-module
refusal checks still reject the whole source set. The coverage run also passes
143/143 with 2,967/3,187 authored production lines, 93.09695638531534%, and no
authored exclusions, in `cancellation.cobertura.xml`.

Fresh classic/v4 exports in `cancellation-source-proof` have exactly the source
hashes of `sdk-matrix-03`. Thus the four prior actual pinned compiler executions
bind the unchanged generated bytes; this checkpoint does not claim a new compiler
execution for token plumbing. Full native and package checks follow below.

The normal native Restore+Compile+Test run passes in 7m19, with Restore 2s,
Compile 1m28 and Test 5m48. `cancellation-suite-totals.json` derives 3,544 framework
cases from the captured log, including generator 143/143, plus 15 cold package
checks and 48 inspection checks. There are no failed, skipped or ignored cases.
PackAll passes in 48s; TestBuild passes 40/40 against the fresh build. Actual fresh
packages pass metadata and compiled exact-version package lookup without any
publishing target. `cancellation-host-01` restores a fresh cache and passes
143/143, retaining the bounded Windows arm64/.NET 10.0.14/SDK 10.0.403 adapter claim.

Native Precommit passes in 31s with the inherited unsupported-F# formatter
diagnostics retained. All 16 owned F# files pass explicit Fantomas checking.
Changelog validation, strict OpenSpec snapshot/schema/22 items/two tests, owned
Markdown relative links and diff checks pass. All 12 frozen contract digests and
44 original evidence digests still match. Task 3.4 is checked and refreshed apply
instructions report 10/13. No shared/status path is edited at this boundary.

## Original fixture matrix and local trimming

The first registered fixture run, named `business-red`, passes 152/152 without a
production change. It is an initial behavior check, not an observed RED. Added
raw-source SHA-256 guards and a required matrix-ID-set check make the focused run
153/153. The first matrix-list build exposed four discarded sequence-expression
warnings; explicit yields fix the harness before actual compiler execution. That
setup evidence is retained. Test formatting changes no generated C# bytes.

`business-matrix-01/profiles.json` passes the four classic/v4 x numeric 14/15
shared-corpus consumers, including all 207 required frozen SDK observations per
consumer. `business.json` records another fourteen actual compiler/execution
consumers, seven independently observed inputs under each pinned compiler:

- ORIGINAL classic car-rental 1, 2 and 3, unchanged embedded source bytes and exact
  source-version spelling, execute daily rate 5000, Available/Reserved/Rented/
  Available states, invoice 15000, all seven typed rejections and persistence of
  original inventory and prior billing obligations.
- SYNTHESIZED TEST INPUT v4 car library, constructed only by a test-side typed
  builder from the original classic 3 input. It supplies no expected values and
  is never called by production. It establishes equivalent typed-family behavior,
  not upstream v4 provenance or production migration support.
- ORIGINAL daily-rate classic 3 and direct v4 integer-version 4 both return 5000.
- COMPATIBLE TYPE SLICE of original v4 books-and-records retains all original type
  definitions, access and field/case payloads. It excludes exactly `open-deal` and
  `close-deal`, both original IncompleteBody/Draft values, with exclusion metadata
  in every exported case.json. Nonempty records/cases execute payload fidelity,
  get-only properties, independent equality/hash and nested dictionary persistence.

Equivalent car inputs and equivalent daily-rate inputs have identical generated
paths/bytes across the typed families and numeric profiles. All eighteen consumers
actually compile and run, not merely parse. They use the exact pinned compiler
builds recorded above, target net10.0 and execute on runtime 10.0.14. Whole original
ecosystem fixtures also run separate located refusal/no-source checks. The
regulatory example references outflows/supplemental/assets beyond its two recorded
type definitions; books-and-records contains its two draft bodies. No declarations
or implementations are invented, and no full-original conformance is claimed.

Original books-and-records raw SHA-256 is
`bdc25e9fb332a49f7f12bfa80c907eca482ae5b63bad07575ef06194ab99fda3`;
complete-example is
`30fbea9879b377dde11d0a4916b926dba152069e7b24bd43e4cf33cb27fcd979`.
All seven original fixture paths/hashes are checked in BusinessCases.sources and
exported as provenance, independently of generated behavior. Original files are
unchanged.

`trimming-01` packs fresh Models/core packages, restores a private consumer cache,
and executes both typed families at numeric 14/15 on the shared nonempty
type/function/pattern/record/SDK text corpus. It verifies repeated generation,
family/version preservation, pre-cancellation, unsupported-profile refusal and
runtime requirements. A self-contained win-arm64 publish with PublishTrimmed=true
and TrimMode=link runs successfully. Untrimmed and trimmed observations/source
hashes match byte-for-byte; the publish has zero IL warnings. This is a local test
binary, not a publication/release target. Its package closure is exactly Models,
core, FSharp.Core 11.0.100 and FSharp.SystemTextJson 1.4.36. It has no Roslyn,
Expecto, Mono.Cecil, Tooling, Ion, Pipeline or SDK runtime package. The separate
adapter harness retains its own test dependencies.

Trimming evidence is bounded to Windows arm64, SDK 10.0.403 and net10.0 runtime
10.0.14. Generator Native AOT, other architectures/operating systems and legacy
IDE/netstandard hosts remain untested. No library is retargeted. Backend usage,
host limits and diagnostic categories are documented. Tasks 4.1/4.2 are checked;
refreshed apply status is 12/13. Root approved a serial four-path status lease for
the exact final-shared-edits.md proposal, plus the stale SDK-archive suffix in
CHANGELOG and the generator Native AOT qualifier. Only those shared paths are
edited. The lease releases after the verified checkpoint push.

## Final local acceptance and API handoff

Fresh normal `pwsh -NoProfile -File build.ps1 --target Compile --configuration
Release` passes Restore in 2s and Compile in 1m28, total 1m31. Then dotnet-coverage
18.7.0 collects the actual native `build.ps1 --target Test --configuration Release
--skip Restore Compile` body in 6m53. Dependencies are skipped only after that
fresh successful compile, so static instrumentation is not overwritten. The
captured command/result files retain the exact invocation. This is reported as
two commands, not a new single-command normal Restore+Compile+Test run.

`business-suite-totals.json` derives 3,554 framework cases from the captured log,
including the registered sequential generator suite at 153/153, plus 15 cold
package-consumer and 48 inspection checks. There are zero failures, skipped or
ignored cases. The 207 frozen observations inside each aggregate SDK test are
not added again to the framework-case total.

`whole-suite.cobertura.xml` reports 24,118/28,646 lines, 84.19325560287649%, across
18 production assembly identities. Generator coverage is 2,977/3,187,
93.41073109507374%, with no authored exclusions. `whole-suite-scope.json` records
the requested 20 distinct src assembly identities and all existing Release output
copies. `dotnet-morphir` and the Blazor `Morphir.Live` assembly are absent from the
report. The shared `Morphir.SDK` identity reports SDK.FSharp source paths; this
report does not establish coverage of every legacy SDK source. Cold consumers
built after instrumentation may not contribute to pre-instrumented copies.
This denominator is the collector's full-suite report, not every repository
source/output, and no comparison with an unmeasured baseline is claimed. The
previous generator-only static denominator remains independently recorded above.

Final PackAll passes in 48s. TestBuild passes 40/40. Actual fresh packages pass
metadata and compiled exact-version package lookup in
`business-package-metadata.json`, without executing publishing targets.
`business-host-01` restores fresh packages/cache and passes 153/153, including the
in-memory driver. Native Precommit passes in 32s while retaining its inherited
unsupported-F# formatter diagnostics. All 18 owned F# files pass explicit
Fantomas checking. Changelog validation, strict OpenSpec snapshot/schema/22 items/
two tests, owned/leased Markdown links and diff checks pass. The first expanded
link-check attempt used an empty base for root Markdown files; the corrected
absolute-base check passes. All 12 frozen contract digests and 44 original
evidence digests still match in final-frozen-proof.json.

The backend diagnostic guide hands the CLI successor the typed entry point,
explicit options, whole-model result statuses, runtime requirements, source
encoding/path contract and every current refusal rule category. The production
core retains separate classic/v4 typed walkers and no Roslyn, Tooling, Ion,
compiler-process, file/config or implicit migration dependency. No CLI
registration, release publication, machine NuGet change or full source-generator
package is introduced. Generator Native AOT and unlisted IDE hosts remain
untested, preserving predecessor runtime AOT evidence separately.

All 13 implementation tasks are verified. This is PR-ready local acceptance, not
initiative completion: coordinator-owned review/CI, landing, spec synchronization,
archive and clean worker/artifact removal remain. The checkout and owned proof
root remain intact for that handoff.

## Coordinator review follow-up: finite nested aliases

PR #478's automatic review identified a valid cycle-detection defect: a finite
`NestedBox<NestedBox<int>>` argument was expanded while the outer alias was
already marked as visiting. Two public compiled-consumer regressions, constructed
directly in classic and v4, first failed with `recursive-alias`. The fix expands
actual arguments in the caller's scope before entering the alias body's cycle
scope. Genuine recursive aliases still fail with a located diagnostic and no
sources. Six targeted alias checks pass, including independent nested structural
equality/hash observations and genuine-cycle refusals. These cases are also in
the shared consumer used by both pinned compilers.

The coordinator retained `review-alias-red*` and `review-alias-green*` evidence
under the existing owned proof root. A fresh normal Nuke Restore/Compile passes
in 2s/1m33, followed by the actual Test body under the same full-suite collector
configuration with Restore/Compile explicitly skipped. Test passes in 6m49:
3,558 framework cases, including generator 157/157, plus 15 cold package-consumer
and 48 inspection checks. The suite total comes from seven MTP and ten Expecto
summaries after stripping terminal escapes; the initial unstripped parser result
is retained separately and is not acceptance evidence.

`review-alias-whole-suite.cobertura.xml` reports 2,978/3,188 generator lines
(93.41279799247177%) and 24,119/28,647 lines across 18 reported production
assembly identities (84.19380737948127%). The same missing `dotnet-morphir` and
`Morphir.Live` identities, shared SDK source identity and full-suite denominator
limits above apply. This is not all repository source coverage or a comparison
with current main's coverage. No authored exclusions were added.

`review-alias-matrix-01` passes all four shared and fourteen business actual
compiler executions. Shared Data source SHA-256 is
`c086cd656676eb858ac62ad13fb2621828b580878afcbe107edb6c1d9dee3490`;
Functions is `33053d8327ed2981b426c8324fb9ae5e05b9e68d9eb6f684b82f9e19a08b3148`.
The shared type input now deliberately includes the finite nested aliases.
Original, synthesized and compatible-slice business labels remain unchanged.
The fresh `review-alias-host-01` passes 157/157. The fresh
`review-alias-trimming-01` package consumer passes untrimmed and self-contained
win-arm64 link trimming with identical observations/source hashes and zero IL
warnings. Host/runtime and untested generator Native AOT/IDE limits are unchanged.

PackAll passes in 47s and TestBuild passes 40/40. All 12 frozen contract hashes
and 44 original-byte evidence hashes still match. These local results concern
the submitted branch based on `618fbf7`; they do not claim local execution of
new configuration work on main `56d9de9`. Its clean hosted PR merge ref was
independently verified against the expected virtual merge tree; hosted CI is
responsible for that integration's validation. Review-fix contribution checks
and the final pushed revision are recorded by the coordinator separately.

Fresh package metadata and compiled exact-version lookup pass for both packages.
Native Precommit passes in 30s with the inherited unsupported-F# diagnostics;
all 18 owned F# files pass Fantomas. Changelog, strict OpenSpec snapshot/schema/
22 items/two tests, the validation record's local link and diff checks pass.
An initial OpenSpec launcher used an absent mise executable path; no process
started, and the corrected installed path passed without product/checker changes.

## Coordinator integration with current main

The coordinator merged main `e3b9a053120a49e94c70c30cd4f62ef783672c64`
normally. The sole conflict was NOTICE.txt; its resolution retains main's entire
notice followed by the typed C# contribution notice. Generator source, tests,
Models, build registration and toolchain/dependency settings are unchanged from
the reviewed `ac3fc4a` checkpoint. Its 18 compiled consumers and fresh packaged
host/trimming proofs therefore remain applicable to the same inputs and code.

Fresh integrated Restore/Compile passed in 2s/1m47. Two full-suite runs with
all-production-assembly instrumentation failed in newly landed configuration CLI
editing tests. The first failed the explicit nested-array input case; the rerun
passed that case but failed the Boolean and typed-number editing cases instead.
Each reported a rolled-back exit 2 instead of written exit 0. Generator 157/157
and all other executed suites passed in both runs. Those failures and reports
remain in `review-main-whole-coverage*`; their cause is not established, and no
configuration implementation or test was changed. The user explicitly chose to
keep this PR focused on C# generation.

The complete integrated native Test body then passed in 5m57 with the committed
backend-only collector settings: 3,826 framework cases, including generator
157/157, plus 15 cold package-consumer and 71 inspection checks. No tests were
filtered or skipped. That collector invocation did not initialize its profiler,
so its empty report is not coverage evidence. A separate direct 157/157 generator
run with explicit test-output discovery and generator-only instrumentation
reports 2,978/3,188 production lines, 93.41279799247177%, in
`review-main-focused.cobertura.xml`. This is backend coverage; there is no claim
of a passing integrated all-assembly coverage run. The earlier successful
18-assembly measurement remains tied to its earlier source tree.

Integrated PackAll passes in 50s, TestBuild passes 40/40, and fresh metadata plus
compiled exact-version lookup pass for both packages. Native Precommit passes in
37s with the same unsupported-F# diagnostics. Strict OpenSpec passes all 22 items
and two checker tests. No publishing target was executed. The integration changes
no generator F# files, so the reviewed 18-file Fantomas result remains applicable.
