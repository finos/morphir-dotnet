# Establish .NET SDK conformance

Status: In Progress

Tracking: [issue #460](https://github.com/finos/morphir-dotnet/issues/460),
[OpenSpec change](../../../../../openspec/changes/establish-dotnet-sdk-conformance/proposal.md),
[umbrella PRD](dotnet-code-generation.md).

## Goal

Freeze an independently sourced, bounded behavioral contract for both .NET SDK
deliveries. Publish signatures, scalar limits, tagged observations, provenance,
named F# discrepancies and an offline validator. Runtime implementations,
generation and CLI integration belong to successor initiatives.

## Feature status tracking

| Feature | Status | Evidence |
| --- | --- | --- |
| Contribution tracking and delivery ownership | Implemented | #460 and umbrella PRD |
| Pinned source evidence and licenses | Implemented | 44 original-byte files, SHA-256 manifest and declaration bindings |
| Operation signatures and scalar profiles | Implemented | 67 operations, direct/callback errors and precise scalar boundaries |
| Tagged cases and named runtime gaps | Implemented | 215 cases, 207 required, eight exclusions and four required F# gaps |
| Offline validator and regression tests | Implemented | 27 passing tests; 93.42% measured validator line coverage |
| Frozen schema and downstream handoff | Implemented | Pinned readiness manifest, all fixture IDs and successor merge gates verified |
| Integrated validation | Implemented | Full Nuke Test/PackAll/TestBuild, strict OpenSpec, scoped coverage and hooks' configured checks passed |

## Acceptance

The [capability spec](../../../../../openspec/changes/establish-dotnet-sdk-conformance/specs/dotnet-sdk-conformance/spec.md)
defines acceptance. Unknown operations, malformed values, unresolved provenance
and unclassified coverage must fail with located diagnostics. Known gaps retain
their expected observations and do not grant runtime passes. Validation needs no
network, Python, MoonBit or either .NET SDK runtime.

## Implementation notes

### Start gate, 2026-11-02

The isolated lead checkout starts at planning PR #459's landed commit
`890aacb61df601f2876112a2cdd4d9d6f5757ca6`, verified reachable from refreshed
`origin/main`. Submitted planning head `c9b65136eea4f270c5893fbf4f8929f30c4be56b`
is distinct from that squash commit. Issue #460 exists before implementation.
The coordinator granted this lead exclusive solution-registration and PRD-index
integration leases and the delivery's single expensive-command slot.

## Open questions

Source disagreements must be recorded with the selected profile or precise
exclusion before a case is admitted. No runtime is a default oracle.

## Validation evidence

Toolchain: .NET SDK 10.0.403, runtime 10.0.14, Windows arm64, TUnit 1.48.0,
Microsoft.Testing.Platform 2.2.3, OpenSpec 1.14.3. Expensive commands are serial;
builds use `-m:1 -p:UseSharedCompilation=false -nodeReuse:false`, with
`DOTNET_PROCESSOR_COUNT=2` and at most two parallel new-suite tests.

- RED: initial project build failed CS0103 for the missing FixtureValidator.
- GREEN: the first eight tests passed after schema/coverage validation.
- RED: adding 12 profile/projection defects produced exactly 12 failures among
  20 tests. GREEN: all 20 passed after semantic validation.
- RED: exact source-declaration and orphan-gap tests produced two failures among
  22 tests. GREEN: all 22 passed after declaration/required-gap checks.
- `dotnet build tests/Morphir.SDK.Conformance.Tests/Morphir.SDK.Conformance.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --nologo` passed with no warnings/errors.
- `dotnet exec tests/Morphir.SDK.Conformance.Tests/bin/Debug/net10.0/Morphir.SDK.Conformance.Tests.dll --maximum-parallel-tests 2` passed, 22/22 tests, zero skips.
- The same runner with `--coverage --coverage-settings tests/Morphir.SDK.Conformance.Tests/coverage.config --coverage-output-format cobertura` passed. The settings include the test assembly because the validator is test-only, and measure FixtureValidator/FixtureProfiles/TypeSpec, excluding generated runner/test definitions. The first unconfigured run produced no measured assemblies and is not counted as coverage.
- Project-local `dotnet format ... --no-restore` and 42 local documentation links passed.

### Source review checkpoint

A bounded read-only auxiliary found no contradictory literal expectation and
confirmed all 44 source digests. Four findings were open at the first checkpoint:
callback overflow must propagate through higher-order operations; equality and
inclusive comparisons need negative/equal-operand cases; clamp wording must
follow the pinned branch behavior rather than an unsupported finite/ordered
precondition; authored decimal scale and nested Result hashing need observations.
The affected OpenSpec tasks were reopened while those corrections were pending.

### Initial full-suite environment failure

The first Windows Nuke `Test` run restored and compiled successfully, discovered
and passed all 22 validator tests, but failed two unchanged Tooling tests:
`DiscoverWorkspaceRoot_ShouldReturnNull_WhenNoWorkspaceFound` and
`ResolveConfigAsync_ShouldReturnDefaults_WhenNoWorkspace`. Both found the user's
existing `C:/Users/damre/.morphir` above the normal temporary directory. Every
other executed suite passed. The failure stopped Nuke before package lifecycle
checks, so this run is not counted as full-suite success.

With child-process `TEMP` and `TMP` set to uniquely owned
`C:/Temp/morphir-sdk-conformance-b255f4d7`, the same 245 Tooling tests all passed.
The ancestor directories were checked for workspace markers before the run.
Overrides are process-local and restored in `finally`; no user/machine settings,
user markers or unrelated source files changed.

### Verified initial checkpoint

- With that run-local TEMP/TMP and `DOTNET_PROCESSOR_COUNT=2`,
  `& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target Test --configuration Release`
  passed Restore, Compile and Test, including 2,747 suite tests and all 15 cold
  Myriad package lifecycle checks. The new project ran 22 tests, zero skips,
  through Nuke's actual sequential `dotnet exec` path. The complete run took
  4 minutes 55 seconds. Log: `C:/Temp/morphir-sdk-conformance-b255f4d7/nuke-test-isolated.log`.
- `dotnet fsi scripts/validate-changelog.fsx` passed.
- `dotnet husky run` failed to find its Husky path after failing to execute Git.
  The resolved Git hooks directory has no pre-commit hook. Both configured tasks
  were executed directly, without disabling or bypassing a check.
- `& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target Precommit`
  passed the full solution `dotnet format --verify-no-changes`. It retained the
  baseline warnings that F# formatting is unsupported and workspace loading has
  warnings. No F# source was changed.
- Explicit owned-path staging and `git diff --cached --check` passed. A staged
  blob audit verified that all 44 evidence, license and notice files retain the
  exact manifest SHA-256 bytes. A fixture-local `.gitignore` exception admits
  upstream Python's `packages/` source tree, which the root NuGet ignore would
  otherwise omit. Root `NOTICE` remains unchanged.

This is a durable intermediate checkpoint. The four source-review findings remain
open and their affected tasks are unchecked. No runtime conformance or initiative
completion is claimed.

Final verification is recorded below. Readiness and the coordinator's landing
gate remain separate from this initial checkpoint.

### Review corrections

All four review regressions were red first, with four failures among 26 tests,
then green after the changes. Higher-order operations now propagate their
selected callback's declared errors, including checked Int32 increment overflow.
Equality cases distinguish unequal nested values and constructor tags; inclusive
comparison cases include equal operands. Hash observations cover authored Decimal
scale, floating zero sign and nested Result payloads. Invariant operands are
construction values, so their authored scale can differ while normalized values
and equal hashes agree.

The draft clamp precondition had no support in Python Basics lines 261-267 or
MoonBit's typed clamp implementation. It was removed before freezing. Required
cases now preserve source branch order for reversed bounds, NaN/infinity and
signed zero. No expectation was changed to match a .NET implementation.

A further malformed type-argument regression failed first, then passed with
case/field diagnostics. The hardened corpus has 215 cases across 67 operations,
including 207 required cases and eight separately reported representation
exclusions. The focused suite passes all 27 tests without skips. Its final
explicit-assembly coverage report measures 93.42105263157895% of the added
validator logic, with each main validator class above 90%. This is scoped new-code
coverage, not a repository-wide or SDK runtime-conformance percentage.

Coverage command:
`dotnet exec tests/Morphir.SDK.Conformance.Tests/bin/Debug/net10.0/Morphir.SDK.Conformance.Tests.dll --maximum-parallel-tests 2 --coverage --coverage-settings tests/Morphir.SDK.Conformance.Tests/coverage.config --coverage-output-format cobertura --coverage-output C:/Temp/morphir-sdk-conformance-b255f4d7/final-validator.cobertura.xml`.

The source review auxiliary completed read-only, with no files changed, heavy
commands, helpers, Git writes, PR or additional worktree. Its sole allocated
auxiliary slot is released; the remaining two slots were unused. The lead remains
the only Git writer and holds the expensive-command slot until handoff.

### Final verification

With process-local `TEMP=TMP=C:/Temp/morphir-sdk-conformance-b255f4d7`,
`DOTNET_PROCESSOR_COUNT=2` and `MSBUILDDISABLENODEREUSE=1`, the following Windows
commands ran sequentially and passed:

| Command | Result |
| --- | --- |
| `& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target Test --configuration Release` | Restore/Compile/Test passed; 2,752 suite tests and 15 cold package lifecycle checks; zero failed/skipped tests; 5m15s |
| Same bootstrap with `--target PackAll --configuration Release` | All local library/tool packaging succeeded; 34s; no publication |
| Same bootstrap with `--target TestBuild --configuration Release` | All 40 build/package tests passed, zero skips; 21s |
| Focused build and `dotnet exec ... --maximum-parallel-tests 2` | All 27 conformance tests passed again after final gap annotation correction |
| `dotnet fsi scripts/validate-changelog.fsx` | Passed |
| Windows bootstrap with `--target Precommit` | Passed full solution formatting; baseline F# unsupported-format/workspace warnings remain |
| `mise run openspec:verify` | Snapshot/schema checks, all 14 strict items and both extension tests passed |

Logs are retained under the uniquely owned validation directory above, using
`final-Test.log`, `final-PackAll.log`, `final-TestBuild.log`,
`frozen-contract-tests.log`, `final-precommit.log` and `final-openspec.log`.
The initial environmental failures remain in the original temp log
`C:/Users/damre/AppData/Local/Temp/morphir-sdk-nuke-test.log`.

Husky still could not find its path when retried with the native Git directory
prepended to the child process's PATH. No persistent PATH or hook settings changed.
Its configured changelog and Windows Precommit checks ran directly and passed;
no checks were disabled and no `--no-verify` option was used.

A read-only probe of the existing compiled F# SDK confirmed wrapping at the
add/subtract/multiply/negate boundaries, second-dictionary union precedence,
decimal division's zero exception and scale-retaining decimal text. It also
showed that F# `pown` already checks overflow. Only its mistaken gap annotation
was removed; its independently sourced required `Overflow` expectation remains
unchanged. The final fixture validator passed after that metadata correction.
No existing F# implementation was fixed or certified by this initiative.

No production/runtime/emitter source, global SDK pin, central package settings or
unrelated runner configuration changed. Shared integration is limited to the
approved new-project solution/Nuke registration, umbrella PRD/index, README,
one changelog entry and NOTICE.txt. Root NOTICE is unchanged.
No runtime implementation or emitter has changed. Contribution landing also
requires a passing build and sign-off from one other developer under
CONTRIBUTING.md lines 33-35; the coordinator owns that gate and normal required
checks/reviews.

## Delivery state

Implementation is PR-ready. The [readiness manifest](../../../../../tests/Fixtures/MorphirSDK/readiness.json)
pins contract artifact commit `737e85b3a202ba733b153be637e33cf953342705`, all
67 operation IDs, 207 required case IDs, eight explicit exclusions, four F# gap
owners, 12 contract-file hashes and the exact Codex `gpt-6.1-sol`/high dispatch
configuration. `python C:/Temp/morphir-sdk-conformance-b255f4d7/audit-readiness.py`
verified those counts, hashes, identities, disjoint classifications and merge
gate conditions. The hashes use Git blob bytes, with CRLF normalization only
when comparing non-evidence text checkout files.

Both runtime initiatives remain in `awaiting-verified-main-merge`. The
coordinator must verify this PR's actual landed SHA on refreshed main before
dispatch. A squash need not preserve the submitted artifact commit's ancestry.
The coordinator records the PR/landed SHA and advances the readiness/delivery
state during landing/archive follow-up. No fixture exclusion, known gap, skip or
expected failure can satisfy a required runtime case.

PR landing, the required other-developer sign-off, spec synchronization/archive
and owned worktree cleanup remain the root coordinator's responsibility. This
initiative is not complete at the PR-ready milestone.
