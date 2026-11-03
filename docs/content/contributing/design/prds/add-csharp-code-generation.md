---
title: "Reusable C# code generation"
---

# Reusable C# code generation

Status: Implementation landed; spec/archive follow-up prepared, landing and coordinator cleanup pending. [Issue #474](https://github.com/finos/morphir-dotnet/issues/474).
The existing [OpenSpec change](../../../../../openspec/changes/archive/2026-11-03-add-csharp-code-generation/proposal.md)
defines acceptance. All 13 implementation tasks are complete. The synchronized
[main specification](../../../../../openspec/specs/morphir-csharp-transpilation/spec.md)
contains eight requirements and 19 scenarios. Archive preparation does not finish delivery.

## Feature status tracking

| Feature | Status | Verification |
| --- | --- | --- |
| In-memory entry-point foundation | Implemented | Typed immutable inputs/results, whole-model refusals, empty input and initial caller cancellation |
| Modern packaged compiler-host closure | Implemented | Fresh packages, incremental driver AddSource, compiled/executed constant and cancellation |
| Numeric C# 14 and 15 profiles | Implemented | Actual matching compilers execute the shared type/function/pattern/record/SDK corpus with identical source bytes |
| Classic and v4 typed lowering | Implemented | Independent typed type/value walkers and runtime bindings; no serialization or migration bridge |
| Immutable aliases, records, cases and structural equality | Implemented | Shared type corpus compiled/executed in all four family/profile combinations |
| Typed function values, application, lambdas and nonrecursive lets | Implemented | Shared compiled capture/shadowing/partial-application/callback-order observations across both families/profiles |
| Deterministic naming, paths and literals | Implemented | Reordered declarations, Turkish culture, keywords/capture, portable collisions, Unicode/numeric fidelity and empty allocation |
| Ordered patterns, conditionals, record operations and self recursion | Implemented | Shared compiled first-match, constructor/tuple/list/alias/literal patterns, lexical binding and input persistence; reversed v4 field order checked separately |
| Admitted SDK operations and explicit refusals | Implemented | All 67 frozen operations execute 207 required observations through both typed paths, including persistence and equality/hash invariants; eight exclusions remain exclusions |
| Determinism, located refusals and traversal cancellation | Implemented | Active alias expansion and both-family large-string generation cancel without sources; invalid later modules retain the whole-model boundary |
| Shared business fixture matrix | Implemented | Original classic 1/2/3 car workflows, labeled synthesized v4 car, direct daily-rate and compatible v4 ecosystem type slice execute under both pinned compilers |
| Fresh package consumer and trimming | Implemented | Both families/profiles generate in memory from fresh packages; self-contained win-arm64 link trimming preserves source hashes and observations with no IL warnings |
| Final full tests, coverage and API handoff | Implemented | 3,554 framework cases plus 15 package/48 inspection checks; generator 2,977/3,187 measured lines, 93.41%; explicit whole-suite report scope and CLI API/diagnostic handoff documented |

## Design and boundary

The net10.0 core references Models and uses separate typed classic/v4 walkers with
a shared private target representation and deterministic C# text writer. Public
input remains VersionedModel. Roslyn belongs only to tests. CLI registration and
the full AdditionalFiles/config/incremental adapter are separate initiatives.
No repository-wide retargeting is authorized.

## Implementation notes

The initial checkout was clean on `t3/add-csharp-code-generation` at
`618fbf7b6c5b542122450bf61f4f794e590fe4f5`. All five predecessor PRs were queried
independently and their specified landed revisions are reachable from refreshed
main, including the unrelated configuration commit `422ff4e`.
All 12 frozen contract digests match. Expected behavior remains independently
sourced from the 67-operation, 215-case contract, with 207 required IDs and eight
representation exclusions. Historical F# gaps remain historical evidence.

## Open questions

The packaged modern compiler-host smoke passed with the real Models dependency
closure. Legacy IDE and netstandard hosts remain untested. Root-approved shared
solution/build/package/docs registrations are integrated. Business fixtures and
fresh package/trimming checks pass within the documented tested host boundary.
Local implementation acceptance and API handoff are verified. Implementation
PR #478 landed; this archive follow-up's landing and coordinator cleanup remain open.

The type checkpoint uses get-only classes and closed nested cases in both
profiles. Transparent aliases expand to their structural representation, while
custom cases retain nominal identity. Public aliases of private structural
records produce usable public records; exposing a private nominal type fails.
Generated equality and hashing explicitly use the SDK structural protocol for
exactly the same payloads, including nested collections and boxed cases.

## Landing and archive follow-up, 2026-11-03

[PR #478](https://github.com/finos/morphir-dotnet/pull/478) was squash merged into
main at `a8ae8e456ac95875db1ee53ff4a21362315b467a` from submitted
`3db576da36a41abab960d59da130df4262486a38`. Both have tree
`b65fea03980e445e094c38b0e9f6bfe8888cc948`; the landed SHA is reachable from
refreshed main. All final-head hosted checks passed, including both aggregate
CI checks and all three OS jobs. One macOS job's existing CLI 30s timeout passed
on its single rerun; the separate same-SHA macOS run also passed.
The finite nested-alias P2 was fixed in `ac3fc4a`, replied to and resolved.
No other-developer approval is claimed. Root used the user's standing admin-bypass authorization.

The table's final-validation row records its original checkpoint. Later generator
evidence is 157/157 with source-bound coverage 2,978/3,188, 93.4128%, 18 pinned
compiled consumers and packaged/trimmed host proof. The integrated `068ae14`
run passed 3,826 framework cases, 15 cold package checks and 71 inspection checks.
The later portability-corrected local run passed 3,825/3,826 and failed an unrelated
configuration typed-number rollback; unchanged Tooling reran 400/401 with the
same rollback behavior. Those are failures, separate from hosted final-head green
checks. Earlier all-assembly instrumentation failures remain in the archived
validation record. No configuration fix, timeout change or test exclusion is part
of this follow-up. CLI stays undispatched; the stable
[API and diagnostic guide](../../../docs/backends/csharp/diagnostics.md) remains
the successor input. Full evidence and historical denominators are preserved in
the [archived validation record](../../../../../openspec/changes/archive/2026-11-03-add-csharp-code-generation/validation.md).
