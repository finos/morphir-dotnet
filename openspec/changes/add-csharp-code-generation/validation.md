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
