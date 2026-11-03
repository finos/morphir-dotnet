# CLI implementation validation

Issue [#481](https://github.com/finos/morphir-dotnet/issues/481).
This record distinguishes observed results from pending acceptance. An open PR,
checked task or successful local check does not establish initiative completion.

## Checkout and prerequisites

Owned checkout `C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-79f2b566`, branch
`t3/integrate-csharp-generation-cli`, initial clean HEAD
`80d516e2e4e62dfd6d69e41c07bae8cfeff4a670`. A fresh native Git fetch confirmed
HEAD equals origin/main, 0/0. GitHub independently reports the following exact
PRs MERGED to main; every recorded landed SHA is reachable from refreshed main:

| PR | Landed SHA |
| --- | --- |
| #462 | 793f378b8559bd667baa49bc0ba2cdf156d088c8 |
| #463 | 7e37689c55eae9510cf12dafd130dc4ae8e3baf1 |
| #469 | 4df3c1ad425f782bfe3cc02e30f577d87dca0f53 |
| #470 | 55f73bdaa93b7bc55a48b184bab8e46a5636fd9e |
| #472 | 618fbf7b6c5b542122450bf61f4f794e590fe4f5 |
| #478 | a8ae8e456ac95875db1ee53ff4a21362315b467a |
| #480 | 80d516e2e4e62dfd6d69e41c07bae8cfeff4a670 |

No original feature-head ancestry or other-developer approval is inferred.
Runtime configuration was independently read as Codex gpt-6.1-sol/high.
OpenSpec root is this repo, schema html-design, initially ready at 0/9.
All 12 frozen SDK contract hashes passed, recorded in `frozen-contracts.json`.

## Observed red and green

All command logs below are under the retained proof root
`C:/Temp/morphir-csharp-cli-79f2b566-20261103`. Each named invocation has stdout,
stderr and result JSON recording executable, arguments, working directory,
owned PID, duration, exit and timeout status. The captured `run.ps1` helper sets
process-local TEMP/TMP under this root, DOTNET_PROCESSOR_COUNT=2 and
MSBUILDDISABLENODEREUSE=1. Explicit builds use `-m:1`,
`-p:UseSharedCompilation=false`, `-nodeReuse:false`.

| Invocation | Observation |
| --- | --- |
| red-compile | Native normal Restore + Compile passed, 121.45s; production unchanged |
| bdd-red | Real CLI classic/v4 scenarios: 2 run, 2 failed; CLI exit 2, unknown-target, only fsharp registered |
| provider-tests-red | Compile failed because CSharpGenerationBackend did not exist |
| provider-build-green / cli-registration-build | New adapter/static reference and registration compiled successfully |
| namespace-api-red | Public namespace test compile failed with 9 missing NamespaceOverride/overload errors before core edits |
| namespace-build-green / namespace-unit-green | Public API compiled; 26/26 namespace cases passed |
| namespace-provider-red-build | Test authoring error: Result map was not unwrapped; not a behavioral RED |
| namespace-provider-red-build-corrected / namespace-provider-red | Corrected tests compiled; 40 run, 7 failed and 33 passed, because provider still rejected namespace |
| provider-options-final-build / provider-options-green | Adapter uses the validated core namespace; exact class filter ran 40/40 passing |
| namespace-compiled-build / namespace-compiled-green | 43/43 namespace cases passed, including compiled C# 14 generic/alias/constructor and cross-module consumers in both typed families plus concurrent isolation |
| owned-fsharp-format | Manifest-pinned Fantomas 5.2.4 refused new shorthand lambda syntax; earlier files formatted, failure retained |
| owned-fsharp-format-corrected | Explicit lambdas used; all six owned F# files formatted successfully |
| first-boundary-full-test | Native normal Restore/Compile/Test passed in 409.23s: 3,912 framework cases, Tooling 444/444, generator 200/200, 15 cold package and 71 inspection checks |
| coverage-scope-build / provider-coverage-static | Added located semantic-refusal mapping case; 42/42 focused provider tests passed; whole new adapter 47/48 collector lines, 97.9167% |
| core-coverage-static | Entire generator 200/200 passed; 3,051/3,261 collector lines, 93.5603%; NamespaceOverride 25/25, no authored core exclusions |
| first-boundary-precommit | Exit 0, 41.32s; inherited unsupported-F# diagnostics retained, separate pinned Fantomas check covers owned F# |
| first-boundary-openspec | Strict mise run openspec:verify passed 22 items and 2 checker tests |
| first-boundary-changelog | Direct configured changelog check passed |

Compiled namespace consumers cover escaped reserved words, literal Greek/CJK,
accented letters, combining and formatting characters and contextual words.
The independently authored type result is 62 and the car invoice is 15,000 cents.
These in-memory compiler checks use the pinned .NET 10 host. New external C# 15,
trimmed and NativeAOT CLI proofs remain pending.

## Preserved failures and resource incident

`registration-options-green` is a misleading historical invocation filename,
not a passing claim. Its attempted union tree filter selected the full Tooling
suite: 429 run, 428 passed, one failed, exit 2, no timeout, 121.34s. The failure
was `Typed values and scoped targets use shared editing services` for
`set ui.color false --type bool`, with `config.edit_rolled_back`, exit 2.
No configuration investigation, fix, suppression, timeout change or broad rerun
to erase that failure was performed. Later focused commands use individual
supported filters and record actual counts.

During that active test session, the namespace RED build ran for 2.86 seconds.
This briefly overlapped two expensive jobs, contrary to the sole-slot rule.
The incident was reported immediately to the coordinator. It is not described
as serialized. The RED build completed; the original test session subsequently
completed normally. The observed owned root/descendant PIDs were absent before
the next heavy command. Subsequent jobs keep one active-session record until an
explicit completed result. No unrelated process was killed.

The initial full restore changed two unrelated configuration smoke lockfiles
with RID-only drift. Their exact diff is retained in
`initial-restore-lockfile-drift.patch`; those two tracked files were restored.
Five scoped lockfile additions for the new project graph were separately
reviewed and leased by the coordinator. No package version changes are intended.

## First checkpoint leases and pending evidence

Core lease covers Types, TypeEmission, ValueEmission and Generator only, the
new NamespaceCases and its registration, and the public API/diagnostic guide.
Shared leases cover the exact Tooling project/static registration, own PRD row,
one changelog bullet, README example, NOTICE attribution and the five derived
project lockfiles. The lead remains sole Git/source writer. Main specs and
archived historical changes remain untouched. JSON rendering is unchanged.

Package checks, fresh documented consumers and published executable proofs
are pending. Task 1.3 stays unchecked until its
whole options/docs/direct-provider acceptance is observed. Later task completion
will be recorded with exact source/head and limits.

Source-bound coverage and strict validation now pass as listed above. The first
coverage invocation, `provider-coverage`, ran the 42 tests successfully but did
not initialize its profiler. Its empty report is retained and is not coverage
evidence. The successful follow-up explicitly statically instruments the actual
provider assembly using `--include-files`. Coverage scope is the complete new
adapter source and, separately, the whole affected generator assembly. It is not
all-repository coverage. No test code, failing legacy configuration scope or
authored source was removed to improve these denominators.

`first-boundary-husky` failed native Git/path discovery with "failed to execute
command 'git'" and "Could not find Husky path", despite native Git in the child
PATH. Both configured checks ran directly and passed as listed above. No hook,
check or validation was disabled and no `--no-verify` was used.

## Ownership and retained artifacts

The lead has the sole expensive-command slot. The reserved read-only auxiliary
slot was released unused: no auxiliary task, thread, helper or extra worktree
was created. Root owns all cleanup, landing, watching and archive follow-up.

The new proof root contains the command wrapper, logs/results, isolated temp
files, frozen-hash audit and captured lockfile drift. Current workspace build
outputs include project bin/obj, build output, .nuke/temp, artifacts and test
results. An exhaustive initial ignored/loose-output inventory was not taken
before the first compile; no claim of initial ignored-output absence is made.
A final absolute-path inventory will identify retained current outputs.

Read-only dependencies remain root-owned: the prior generator/archive evidence
trees and official isolated RC SDK cache. They must not be deleted or modified.
No branch, worktree, artifact or release deletion is performed by this worker.

## Subsequent CLI and NativeAOT acceptance

The submitted checkpoint is `f0d3ee596013fe46fd611f7568b4445e0836ecfa`.
Subsequent evidence below exercises the diagnostic fix and new acceptance tests
in the same owned checkout. Final submitted SHA will be recorded at handoff.

- `acceptance-01` passed all 16 independent compiled consumers, eight each under
  SDK 10.0.403/numeric 14 and SDK 11.0.100-rc.2.26504.105/numeric 15. These cover
  classic/v4 daily rate 5000, classic cross-module invoice 15000, escaped keywords,
  Greek/CJK, combining/formatting characters and contextual words. Generated
  sources were compiled unchanged against the local immutable C# SDK package.
  Its subsequent trimmed publish failed on inherited IL2026 warnings treated as
  errors. That failure and every passing consumer receipt remain retained.
- `acceptance-02` used the coordinator-approved publish-command-only properties
  `TreatWarningsAsErrors=false` and `TrimmerSingleWarn=false`. Warnings remained
  visible. Trimmed generation and a separately compiled business consumer passed.
  Genuine ARM64 NativeAOT linking succeeded, but actual generation failed with
  exit 2 due to eager diagnostic `%A` formatting in TypeEmission and F# structural
  reflection. No successful native generation was claimed for this attempt.
- `diagnostics-public-red` ran 49 namespace/public cases, with 43 passing and six
  failing on expected diagnostic spellings before production correction.
  `diagnostics-public-green` passed 49/49 after the approved diagnostic-only fix.
- `acceptance-03` completed exit 0, no timeout, 115.85 seconds. Both actual trimmed
  and NativeAOT CLIs generated all seven car modules with empty compiler PATH
  and owned empty DOTNET_ROOT. Native PE inspection confirms ARM64 and no CLR
  header. Separate fresh consumer builds/executions each returned 15000. Source
  hashes match the retained pre-fix sources. Native document-literal and
  type-mismatch inputs returned one parseable semantic report, exit 1, located
  diagnostics, no artifacts and no published output.

### Diagnostic compatibility

The five production structural `%A` sites were replaced within the approved
TypeEmission/ValueLowering lease. Raw names use `Name["word",null]`, paths use
`ModulePath[Name[...]]`, and qualified names use
`FQName(PackagePath[...],ModulePath[...],Name[...])`. Shape spelling is exhaustive:
`Parameter(...)`, `Named(...,[...])`, `Product([...])`, `Fields[Name[...]:...]`,
`Arrow(...,...)` and `Unit`. Quotes and backslashes are escaped; control/surrogate
characters use four-digit hexadecimal escapes. Traversals check cancellation.
The eager module-validation location now uses `$modules/ModulePath[...]`.
This intentionally changes diagnostic text and that location spelling. Rule IDs,
public APIs, refusal/cancellation behavior, generated source bytes and paths
remain unchanged. Callers branch on Rule, not human message text.

### Visible publish warnings

No warnings were suppressed. The two publish-only warning properties match the
predecessor acceptance policy; consumer builds retain warnings as errors. The
whole CLI still contains inherited reflection/dynamic-code warnings. Acceptance
covers the executed generation command, not unrelated command AOT safety.

| Warning origin | Trimmed occurrences / distinct | Native occurrences / distinct |
| --- | --- | --- |
| Existing Morphir.Core codecs/generated union | 10 / 10 | 25 / 25 |
| Existing IR tooling/management serialization | 5 / 5 | 10 / 10 |
| Morphir.Models package metadata | 2 / 1 | 2 / 1 |
| Fabulous.AST/Fantomas dependency constraint | 2 / 1 | 2 / 1 |
| FSharp.Core dependency | 45 / 44 | 47 / 46 |
| Fantomas/Fantomas.FCS dependency | 6 / 6 | 0 / 0 |
| Foundatio.Mediator dependency | 6 / 6 | 6 / 6 |
| CsToml dependency | 2 / 2 | 0 / 0 |
| Total | 78 / 75 | 92 / 89 |

Distinct counts remove repeated identical warning lines after removing the
trailing consuming-project annotation. Full individual messages, origins, codes
and repeats are retained in `publish-03-warning-classification.json` and
`publish-03-warning-summary.json`. There are zero warning origins in the changed
C# provider or C# generator sources. IL2026/IL3050 identify inherited reflection
or dynamic-code calls; other IL20xx warnings identify dependency reflection
annotation/type-flow issues. NU1510 is redundant package metadata, NU1608 is the
existing Fabulous.AST constraint, and CS8618 is an existing generated Core union.

### Most recent full run and focused failures

`final-full-test` ran normal native Restore/Compile/Test, explicitly completed
without timeout in 422.89 seconds, helper exit -1/tool exit 1. Restore and Compile
passed. Framework totals are 3961 run, 3960 passed, one unrelated configuration
failure. Tooling was 486/487, C# generator 206/206 and F# generator 333/333.
The failure was `set custom.value 12 --type int` with `config.edit_rolled_back`.
All 33 new real CLI cases passed inside this run. Cold package and inspection
follow-ons were not reached. The earlier 3912 + 15 + 71 passing checkpoint is
separate evidence. No broad retry, configuration fix, suppression or filtering
was used to obtain a different full-run result.

`cli-matrix-tests` previously ran 33 cases with one failing missing-file test.
Its parent directory was absent too, which triggered the existing path-safety
semantic refusal. The fixture now creates its parent to isolate a missing file,
which returns the intended operational JSON report. No generic loader change
was made. `real-csharp-publication-tests` passed all seven actual-source cases.

Publish-only RID lockfile drift from each attempt was captured externally and
restored, retaining no unleased dependency graph changes. The final frozen
contract audit checks all 12 canonical repository text hashes. The first direct
physical-byte audit exposed normal CRLF checkout differences in text files;
canonical LF hashes match, and frozen tracked files have no Git changes.

## Process ownership limitation at post-pack rebuild

Before `post-pack-closure-compile`, the process inventory contained
VBCSCompiler PID 5656, parent PID 7888. The lead launched Nuke Compile before
establishing that process's ancestry. The existing Compile target globally stops
VBCSCompiler and removed this process. PackAll was the likely source because the
pre-full inventory was empty and direct builds disabled shared compilation, but
the retained inventory lacks creation time and ancestor proof. Ownership cannot
be certified and no claim that unrelated processes were unaffected is made for
this boundary. This is an avoidable verification gap, separately reported to the
coordinator. No extra process termination or production workaround followed.

## Final scoped evidence

`diagnostic-shapes-public-tests` passed 59/59. Ten additional public mismatch
cases exercise every explicit shape constructor through both typed families.
`final-core-coverage` ran all 216 generator tests successfully and reports
3130/3340 lines, 93.7126%, with 64/64 instrumented diagnostic-formatting lines
covered. Coverage remains source-bound to the whole generator assembly; the
complete adapter scope retains 47/48 lines from the earlier source-identical run.

`pinned-contracts-cancellation` passed eight fresh package-only consumers:
classic/v4 generic/alias/constructor/equality results 62, and classic/v4
cross-module value/constructor/business results 15000, under both numeric
profiles. Each export compares provider artifact paths/text with direct core
results before writing unchanged sources. The C# driver also observes explicit
null/default NamespaceOverride refusal and caller cancellation precedence.
The stable compiler is 5.9.0-1.26523.112 and RC2 is 5.12.0-1.26504.105.

Cancellation is a captured subprocess running the public MorphirCli builder,
GenerationScenario, unchanged GenerateCommand/handler/resolver and actual static
C# provider. A test-only publication gate verifies real nonempty C# artifacts,
then the caller cancels the host token. Stdout parses as one failure report,
process exit 130, backend morphir.csharp, cancelled diagnostic, no artifacts.
Prior manifest/sources/unowned file content and timestamps remain identical.
This tests the hosting cancellation contract, not OS Ctrl+C delivery. The
reproducible helper and C# driver source are under the owned Generate test folder.

`documented-fresh-consumer` executed the documented pack/new console/add package/
generate/build/run sequence in fresh owned directories. It printed 5000. The
exact README option vocabulary and troubleshooting preview/Boolean-namespace
examples returned parseable reports with their expected exits. Only absolute
checkout/output paths and the native CLI executable location were substituted.

`final-pack-all` passed in 43.45 seconds, writing a fresh owned package directory.
The previously absent checkout artifacts/packages directory received copies for
standard TestBuild discovery. `final-package-tests` passed 40/40 with zero skips.
No existing artifact directory was cleared or deleted by this worker.

`reproducible-contracts-cancellation` passed its eight consumer builds but then
failed prior-generation with missing Morphir.Models Version=1.0.0 after PackAll
changed assembly version stamps in the CLI output closure. The failure is retained
in contracts-02. Normal native Restore+Compile, post-pack-closure-compile, passed
in 64.85 seconds without source changes. Only the affected cancellation portion
was rerun using the repository helper's explicit CancellationOnly switch.
post-pack-cancellation-proof passed in 5.77 seconds, with zero consumer builds
in that invocation. No broad Test retry followed the configuration failure.

An optional extra native raw-name probe, native-diagnostic-refusals, returned
located invalid-name reports for package/module inputs before provider selection.
Its additional assertion expecting a core module location failed because the
shared codec refuses those malformed names first. This is retained as a failed
probe and is not evidence that native core raw-name/collision branches executed.
The accepted native document-literal and type-mismatch refusals in acceptance-03
remain the representative provider semantic-negative evidence. No hidden model
bridge or codec change was introduced to bypass admission.

## Final contribution checks and delivery boundary

Native final-precommit exited 0 in 38.49 seconds. Its inherited unsupported-F#
messages remain in the complete log; final-fantomas-check independently passed
all five owned C# generator F# files plus NamespaceCases using pinned Fantomas
5.2.4. final-changelog passed. Strict final-openspec passed 22 items and two
checker tests. Relevant new Hugo relref targets all resolve, the 12 canonical
frozen hashes match and no runtime/fixture files changed. OpenSpec apply now
reports 9/9, all_done; review/landing/archive/cleanup remain root-owned gates.

The worker remains the only Git writer. A normal conventional checkpoint and
backup push will include only the approved two-file diagnostic fix, its public
tests/page, owned C# tests/proof helpers/docs and coherent active change/PRD.
No further shared-file or lockfile changes are retained. Exact checked/submitted
head, remote match, hosted snapshot and lease releases are provided to root
at handoff, outside this source-bound record to avoid a self-referential SHA.
