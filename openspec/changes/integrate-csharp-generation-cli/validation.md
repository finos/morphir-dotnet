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
Three scoped lockfile additions for the new project graph were separately
reviewed and leased by the coordinator. No package version changes are intended.

## Leases and remaining evidence

Core lease covers Types, TypeEmission, ValueEmission and Generator only, the
new NamespaceCases and its registration, and the public API/diagnostic guide.
Shared leases cover the exact Tooling project/static registration, own PRD row,
one changelog bullet, README example, NOTICE attribution and the three derived
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
