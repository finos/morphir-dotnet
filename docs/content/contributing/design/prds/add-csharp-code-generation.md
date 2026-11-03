---
title: "Reusable C# code generation"
---

# Reusable C# code generation

Status: In Progress. [Issue #474](https://github.com/finos/morphir-dotnet/issues/474).
The existing [OpenSpec change](../../../../../openspec/changes/add-csharp-code-generation/proposal.md)
defines acceptance. PR readiness precedes coordinator-owned landing and archival.

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
Local implementation acceptance and API handoff are verified. PR review,
coordinator-owned landing, archival and cleanup remain open.

The type checkpoint uses get-only classes and closed nested cases in both
profiles. Transparent aliases expand to their structural representation, while
custom cases retain nominal identity. Public aliases of private structural
records produce usable public records; exposing a private nominal type fails.
Generated equality and hashing explicitly use the SDK structural protocol for
exactly the same payloads, including nested collections and boxed cases.
