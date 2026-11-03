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
| Operation signatures and scalar profiles | In progress | 67 operations; audit found callback error/domain wording omissions |
| Tagged cases and named runtime gaps | In progress | 177 cases and four required F# gaps; strengthen comparison/hash boundaries |
| Offline validator and regression tests | In progress | Initial 22 tests pass; review hardening remains |
| Frozen schema and downstream handoff | In progress | Schema/README draft exists; audit corrections and readiness manifest remain |
| Integrated validation | In progress | Initial full Windows Nuke Test passed; final review and package-build checks remain |

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
confirmed all 44 source digests. Four findings remain open at this checkpoint:
callback overflow must propagate through higher-order operations; equality and
inclusive comparisons need negative/equal-operand cases; clamp wording must
follow the pinned branch behavior rather than an unsupported finite/ordered
precondition; authored decimal scale and nested Result hashing need observations.
The affected OpenSpec tasks remain pending until these corrections are verified.

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

Full-suite, hook and final coverage evidence will be recorded after integration.
No runtime implementation or emitter has changed. Contribution landing also
requires a passing build and sign-off from one other developer under
CONTRIBUTING.md lines 33-35; the coordinator owns that gate and normal required
checks/reviews.

## Delivery state

Implementation is underway. PR landing, spec synchronization/archive and owned
worktree cleanup remain the root coordinator's responsibility. An open PR is an
intermediate milestone.
