# Conformance delivery evidence

Status on 2026-11-03: implementation landed; archive PR and cleanup pending.
This record describes observed delivery. The [orchestration plan](orchestration.md)
and [manifest](orchestration.json) remain static planning snapshots.

## Implementation landing

[Issue #460](https://github.com/finos/morphir-dotnet/issues/460) was delivered by
[PR #462](https://github.com/finos/morphir-dotnet/pull/462), merged into `main`
by squash on 2026-11-03 at 07:38:16 UTC. Keep these revisions distinct:

| Revision | SHA | Meaning |
| --- | --- | --- |
| Planning landed base | `890aacb61df601f2876112a2cdd4d9d6f5757ca6` | Implementation lead's verified predecessor on main |
| Contract artifact | `737e85b3a202ba733b153be637e33cf953342705` | Immutable submitted contract revision |
| Submitted implementation head | `3566994148259bf042cc11d864cf11703b4181e7` | Final head evaluated by host checks |
| Squash landed commit / archive checkout base | `793f378b8559bd667baa49bc0ba2cdf156d088c8` | Actual #462 result on main |

The coordinator verified the exact PR was MERGED with base `main`, refreshed
main and confirmed landed-SHA reachability. The archive worker independently
repeated `gh pr view 462 --repo finos/morphir-dotnet --json
url,state,baseRefName,headRefOid,mergeCommit,mergedAt,reviews,statusCheckRollup`,
`git fetch origin main` and
`git merge-base --is-ancestor 793f378b8559bd667baa49bc0ba2cdf156d088c8 origin/main`.
All passed. Refreshed main also includes disjoint dependency PR #452; this
follow-up retains its prepared #462 base. Original submitted-head ancestry is
not required for a squash.

All eight host checks passed on the submitted head: EasyCLA, dotnet-scan, lint,
Linux tests, Windows tests, macOS tests, OpenSpec workflow and aggregate ci.
The coordinator reported no automated review findings. GitHub reports no
submitted developer review. CONTRIBUTING.md calls for one other developer's
sign-off; none was submitted. The user explicitly requested and authorized
the coordinator's admin-bypass merge of #462. This is the actual exception,
not a claim that developer approval occurred. It does not authorize merging
this follow-up or any successor PR.

## Spec synchronization and archive

OpenSpec resolved the repository-local root and `html-design` schema. All four
planning artifacts were done; task counts were exactly 10/10. The only delta
was the new `dotnet-sdk-conformance` capability with five ADDED requirements.
Archive guidance and one specs-instruction snapshot were read before writing.

The synchronous sync created the [main capability spec](../../../openspec/specs/dotnet-sdk-conformance/spec.md)
with the delta Purpose and all requirement/scenario blocks preserved. Only the
title and ADDED section header changed to main-spec form. Strict main-spec
validation passed, and content comparison found nothing left to apply before
the source directory moved to the [archive](../../../openspec/changes/archive/2026-11-03-establish-dotnet-sdk-conformance/proposal.md).
Its `.openspec.yaml` and all 10 completed tasks are preserved. Archive-relative
documentation links and incoming predecessor references were repaired.

This sync/archive is prepared in the follow-up branch. It becomes main's state
only when the follow-up PR lands. An open follow-up PR is not completion.

## Contract and subsequent runtime gates

The [readiness manifest](../../../tests/Fixtures/MorphirSDK/readiness.json)
records the actual landing and retains `submittedContractCommit` and all 12
frozen-file hashes exactly. No expectations, evidence, provenance, exclusions,
required IDs or runtime code changed. The contract remains 67 operations and
215 cases, comprising 207 required IDs and eight representation exclusions.
The four unresolved F# gaps remain FS-DICT-UNION, FS-INT-CHECKED,
FS-DECIMAL-DIV and FS-DECIMAL-TEXT.

The C# and F# runtime leads must start from refreshed main containing the
verified landed SHA and recheck contract hashes, profiles and IDs. Both must
execute and pass every required ID. Gaps, exclusions, skips and expected
failures cannot count as passes. The F# package initiative must resolve all four
named gaps. C# generation waits for the C# SDK's verified landing; CLI integration
waits for generation. This archival submission certifies no runtime behavior.

## Validation and ownership

The [conformance PRD](../../content/contributing/design/prds/establish-dotnet-sdk-conformance.md)
retains implementation validation: 2,752 suite tests, 15 cold package lifecycle
checks, 40 build/package tests, 27 fixture tests and 93.42% measured validator
line coverage. These figures describe the predecessor's local checks.

The archive follow-up repeated the following checks on Windows arm64, .NET SDK
10.0.403 and runtime 10.0.14. Restore/build/test/coverage commands were serial
under the single expensive-command lease. `DOTNET_PROCESSOR_COUNT=2` bounded
MSBuild and test defaults; Nuke ran suites sequentially. The focused runner
explicitly used `--maximum-parallel-tests 2`. Process-local
`TEMP=TMP=C:/Temp/morphir-sdk-archive-eff883da-092e2b02` and
`MSBUILDDISABLENODEREUSE=1` were restored in `finally`. No user/machine settings
or workspace markers changed.

- `& 'C:/Users/damre/scoop/apps/pwsh/current/pwsh.exe' -NoProfile -File build.ps1 --target Test --configuration Release`
  passed Restore/Compile/Test, all 2,752 suite tests and 15 cold package lifecycle
  checks, zero failures/skips, in 5m30s. Log `nuke-Test.log`.
- `dotnet exec tests/Morphir.SDK.Conformance.Tests/bin/Release/net10.0/Morphir.SDK.Conformance.Tests.dll --maximum-parallel-tests 2 --coverage --coverage-settings tests/Morphir.SDK.Conformance.Tests/coverage.config --coverage-output-format cobertura --coverage-output C:/Temp/morphir-sdk-archive-eff883da-092e2b02/validator.cobertura.xml`
  passed 27/27 tests with zero skips. Release validator line coverage was
  339/365, or 92.87671232876712%; each main validator class exceeded 90%.
  This Release measurement is separate from the predecessor's Debug figure;
  validator source is unchanged. No runtime or repository-wide coverage is
  claimed. Log `conformance-coverage.log`.
- `mise run openspec:verify` passed snapshot/schema checks, all 14 strict items
  and both checker tests. `openspec validate --specs --strict` passed all eight
  main specs, and `openspec validate --archived --strict --no-interactive`
  passed all three archives, including this change's 10/10 tasks.
- `dotnet husky run` exited 1 with Git/Husky path discovery failure, matching
  the predecessor environment. There is no installed pre-commit hook in the
  resolved Git hooks directory. Both configured checks ran directly:
  `dotnet fsi scripts/validate-changelog.fsx` and the Windows bootstrap above
  with `--target Precommit`. Both passed. The latter ran full-solution
  `dotnet format --verify-no-changes` with the baseline unsupported-F# and
  workspace warnings. No checks were disabled or bypassed. Logs `husky.log`,
  `changelog.log` and `nuke-Precommit.log`.
- `python C:/Temp/morphir-sdk-archive-eff883da-092e2b02/audit-archive.py` passed
  frozen hashes, all 44 original evidence bytes, complete contract identities,
  unchanged readiness policy, exact archived content, synced requirements,
  static plan semantics, retained notices and 136 local links across 36
  documents. `git diff --check` passed.

Logs, audit script, validation wrapper and coverage output remain under that
unique temp directory for coordinator review and cleanup. Its path pointer is
`C:/Temp/morphir-sdk-archive-eff883da-path.txt`.
`dotnet build-server shutdown` succeeded for MSBuild and VB/C# servers. The
subsequent process inventory found no dotnet, MSBuild, VBCSCompiler, testhost
or Nuke build processes. No auxiliary or helper was started for this follow-up.

The coordinator already removed the implementation lead's exact worktree
`C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-b255f4d7` and Git registration
after clean/submitted-head checks. Its branches remain. Its read-only auxiliary
completed without another worktree. No implementation helpers or build servers
remained. The current archive worktree is
`C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-eff883da`; it must remain available
until its PR lands and the coordinator verifies clean status and matching
submitted HEAD, then removes the directory and registration from outside it.

Coordinator-owned retained artifacts remain untouched:
`C:/Temp/morphir-sdk-conformance-b255f4d7/**`,
`C:/Users/damre/AppData/Local/Temp/morphir-sdk-conformance-evidence/**`,
the loose files listed in the retained `owned-initial-artifacts.json`, and
`C:/Users/damre/AppData/Local/Temp/morphir-sdk-nuke-test.log`.
The coordinator owns their final disposition and this worker's retained logs.
Branches are retained; no branch deletion or release is authorized here.

Completion still requires this follow-up landed on main, verified spec/archive
inclusion, and all owned worktree/helper/artifact cleanup recorded by the
coordinator. The other four initiatives remain separate deliveries.
