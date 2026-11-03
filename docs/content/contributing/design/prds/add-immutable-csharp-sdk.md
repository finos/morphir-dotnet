# Immutable C# SDK

Status: In Progress

Tracking: [issue #464](https://github.com/finos/morphir-dotnet/issues/464),
[OpenSpec change](../../../../../openspec/changes/add-immutable-csharp-sdk/proposal.md),
[frozen contract](../../../../design/dotnet-sdk-contract.md).

## Goal

Deliver one independent `Morphir.SDK.CSharp` package for ordinary net10.0 C#
applications and future generated code. Provide immutable optional/result values,
Unit, Order, the 67 admitted operations, and persistent collections with Morphir
value semantics. No FSharp.Core, compiler, model, CLI or Roslyn dependency.

## Feature status tracking

| Feature | Status | Evidence |
| --- | --- | --- |
| Contribution and predecessor gates | Implemented | Issue #464 and PRD before production; exact #462/#463 landing; own project has no package or project references |
| Maybe, Result, Unit and Order | Implemented | Public unit/BDD and both exact ordinary package compiler profiles pass |
| Checked scalar and decimal behavior | Implemented | Frozen scalar observations and callback errors pass; decimal authored-scale and canonical-result checks pass |
| Persistent List, Dict and Set | Implemented | Retained operands, structural equality/hash, homogeneous comparers and signed-zero replacement regression pass |
| Public runtime conformance adapter | Implemented | After review fixes, 207/207 required IDs executed/passed; eight exclusions separate; API examples written |
| Isolated package consumers | Implemented | Same source; numeric C#14.0/SDK10.0.403 and C#15.0/SDK11 RC2 restore/build/run with one local package dependency |
| Trimmed and NativeAOT consumers | Implemented | Both win-arm64 publications execute the same public assertions/output, warnings treated as errors |
| Integrated validation and handoff | Implemented | Full suite, production coverage, package registration and downstream API handoff; PR landing/archive/cleanup remain coordinator-owned |

## Acceptance

The OpenSpec scenarios and frozen `morphir-dotnet-sdk-v1` / schema 1 /
`dotnet-sdk-v1` inventory define acceptance. Expected results, IDs, exclusions,
schemas and evidence remain unchanged. Every required ID must execute through
the public C# runtime, including callback errors, retained operands and independently
constructed equality/hash comparisons. No skip or known gap counts as a pass.

The package targets net10.0 and uses System.Collections.Immutable from the
framework. The same API serves both language profiles, trimmed applications and
NativeAOT. The source-generator adapter is deferred.

## Implementation notes

### Start gate, 2026-11-03

The prepared checkout is `t3/add-immutable-csharp-sdk` at main commit
`7e37689c55eae9510cf12dafd130dc4ae8e3baf1`, initially clean. GitHub independently
reports #462 merged to main at `793f378b8559bd667baa49bc0ba2cdf156d088c8` and
#463 merged at the supplied base. Both landed commits and planning commit
`890aacb61df601f2876112a2cdd4d9d6f5757ca6` are present. Submitted worker heads
are distinct from squash results. Historical readiness cleanup booleans do not
override these landing observations.

Issue #464 and this PRD precede production edits. Shared solution, sequential
test runner, package orchestration, README, NOTICE, changelog and PRD index
changes require coordinator integration. Root global.json remains unchanged.

The initial expensive-command lease was released before starting any heavy job
so the F# lead can capture its untouched legacy package baseline. No auxiliary
or helper has been started. Build commands will use process-local isolated
TEMP/TMP, DOTNET_PROCESSOR_COUNT=2, sequential MSBuild and bounded test parallelism.

### First runtime checkpoint

RED builds observed missing Maybe/Result/Unit/Order types, then missing scalar,
collection and adapter APIs before their implementation. The first GREEN build
had zero warnings/errors and 14/14 focused unit/Reqnroll tests passed with no
skips. Both the conformance unit and BDD path executed all 207 required IDs and
reported eight representation exclusions separately. The adapter compares frozen
observations and checks retained operands even when a callback throws. It checks
independently constructed SDK values through public equality and hashing.

Initial adapter numeric dispatch accidentally coerced boxed Int results to
Double. The corpus caught five mismatches; explicit Int boxing fixed the adapter
before the passing run. No frozen expectation changed. A later signed-zero
dictionary replacement regression is authored but not yet run.

The heavy lease was released at this boundary for the F# package RED checkpoint.
Uncommitted runtime/tests/docs and coordinator-integrated shared edits remain
preserved until required full-suite, coverage, format and precommit checks pass.
Compiler profile and publication tasks remain unchecked. The one reserved
read-only auxiliary is reviewing runtime semantics, without edits or heavy jobs.

### Read-only review findings

The reserved auxiliary completed in the shared checkout with no edits, Git
commands, helpers, builds or extra worktree. Its slot is released. Exact task
identity is `node:delegated-task:command%3Amcp%3A2d5a0878-e705-445b-beaa-8dc736792d50%3Adelegate-task%3Acsharp-readonly-review-20261103-01`.
It found four bounded issues: record equality after boxing/tuple nesting, CLR
equatable fallback violating recursive NaN semantics, heterogeneous boxed list
keys, and adapter decimal normalization hiding distinct authored scale and
noncanonical result bits. Regression tests are authored, awaiting a heavy lease
for observed RED before production fixes. The public protocol proposal
`IMorphirValue` and `MorphirValue.Hash` is coordinator-approved as an explicit,
reflection-free generated/domain-value contract; it cannot enforce physical
immutability. The consumer example exercises that protocol after boxing/nesting.

### Review regression checkpoint

The missing public protocol first produced compile RED. With the protocol declared,
five semantic tests failed for their intended reasons: unsafe equatable fallback,
boxed record rejection, heterogeneous boxed list keys, erased decimal authored
scale and dictionary signed-zero replacement. Minimal fixes made all five GREEN.
The focused suite then passed 20/20 tests with zero failures/skips, including
additional compound-comparer and empty-nested-list checks. The public adapter
again executed and passed every one of the 207 required IDs; its machine report
contains both complete ID sets and eight separate exclusions, with no failures.

Measured production-only Debug runtime coverage is 318/333 lines, or
95.4954954954955%. The report includes Morphir.SDK.CSharp.dll, not test adapter or
generated runner logic. All 12 frozen contract digests match after expected LF
normalization of checkout text, and all 44 original upstream evidence byte hashes
match without normalization. No expectations, IDs, profiles or evidence changed.

Owned project formatting, strict change validation, `mise run openspec:verify`
and the configured changelog validator passed. `dotnet husky run` failed with
Git/Husky path discovery errors despite native Git in PATH. The resolved Git
hooks directory contains no pre-commit hook. No hook or check was disabled;
the configured Windows Precommit check remains required before committing.

The first full Nuke bootstrap stopped at CS1501 for the new resource configurator
callback, before Restore/Compile/Test. Installed Nuke 10.1.0 exposes the
parameterless SetProcessArgumentConfigurator extension, so the proposed corrected
integration uses AddProcessAdditionalArguments with explicit bounded MSBuild
arguments. This failed attempt is not full-suite evidence.

### Integrated runtime checkpoint

With the coordinator's corrected argument integration, the full supported native
Windows Nuke `Test --configuration Release` passed Restore/Compile/Test in
6m21s. All 2,772 suite tests and 15 cold package-consumer checks passed with no
failures/skips. Release included all 20 new C# tests and reran every required
runtime ID/invariant, 207/207 passed and eight exclusions separate. Actual emitted
Restore and Compile commands contain all three bounded MSBuild arguments.

Nuke `Precommit --configuration Release` passed full-solution formatting
verification in 25s, with existing unsupported-F# and workspace diagnostics.
The configured changelog check also passed. Husky itself retains the known
path-discovery failure; no hook was bypassed or disabled. Integrated package
registration is being checked before the runtime boundary commit. Compiler
profiles and trimmed/NativeAOT tasks remain pending and unchecked.

Actual `PackAll --configuration Release --skip Restore Compile` created and
validated the new C# package. `TestBuild` executed all 40 build/package checks;
its first two attempts caught missing release notes and then the existing
Markdown-section requirement. The own project metadata now supplies meaningful
`### Added` notes without a new dependency. The final PackAll rerun passed in
34s; final TestBuild passed 40/40 with no failures/skips. Inspection confirms the
new nuspec has an empty net10.0 dependency group. This validates integrated
packaging, not the still-pending isolated ordinary/compiler-profile consumers.

Final Precommit verification passed again in 24s after the metadata fix. Strict
OpenSpec verification passed all 14 items and both checker tests. No dotnet,
MSBuild, compiler server, testhost or Nuke helper remained after validation.
The runtime group is ready for its first verified Conventional Commit and normal
backup push. Five of nine OpenSpec tasks are complete; variant compiler-profile
proof and all three packaged/integrated handoff tasks remain unchecked. No PR,
package-profile/AOT certification, landing or cleanup completion is claimed.

### Final package and downstream checkpoint

The runtime checkpoint was committed and normally backed up at
`0b01ff037f0496071e8ba4689c785376658a8f30`. Its production source remains unchanged.
The final fresh `verify-package-consumers.ps1 -Mode All` run restored, compiled
and executed the same public consumer against an isolated local package under
numeric C#14.0 / SDK10.0.403 and numeric C#15.0 / SDK11.0.100-rc.2.26504.105.
Both ordinary dependency graphs contain exactly one runtime package and no
repository project references. Representative trimmed and NativeAOT win-arm64
publications also executed with the same asserted output. Compiler revisions,
official RC archive hash, artifact hashes, prerequisites and earlier failed
setup attempts are recorded in [validation](../../../../../openspec/changes/add-immutable-csharp-sdk/validation.md).

The [downstream API handoff](../../../../../openspec/changes/add-immutable-csharp-sdk/api-handoff.md)
defines the single reusable API, generated-record IMorphirValue equality/hash
requirements, callback errors and comparator limits. Native C# unions and the
source-generator adapter remain deferred. Native publication evidence is
representative Windows arm64 evidence, not certification of every target.

The coordinator registered the exact versioned C# package in both PublishLibs
and PublishLocalLibs. The Nuke assembly compiles and the packed path exists;
neither publishing target was executed and machine NuGet configuration was not
changed. Final formatting and OpenSpec evidence supplements the runtime
checkpoint's full-suite, coverage, PackAll and TestBuild results.

### Coordinator integration

The C# branch incorporates verified main with F# SDK squash commit
`4df3c1ad425f782bfe3cc02e30f577d87dca0f53`. Shared build and publication
registrations preserve both runtimes and the dependency-only F# compatibility
package. C# runtime/test source and the earlier pinned-profile/NativeAOT proofs
remain unchanged. The full integrated rerun passes all 3,290 suite tests and 15
cold package lifecycle checks, including C# 20, F# 403 and generator 333. Native
Precommit, PackAll, all 40 package tests, changelog and strict OpenSpec checks
pass. See validation for the first run's stale legacy build-output
failure, preserved artifacts and exact successful rerun evidence.

## Open questions

No contract exceptions are accepted. Implementation acceptance is verified;
Hosted CI/review, landing, spec sync/archive and worktree
cleanup remain coordinator-owned delivery steps.
