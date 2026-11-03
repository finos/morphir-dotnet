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
| In-memory entry-point foundation | Implemented | Empty input and caller cancellation tests written first; broad lowering pending |
| Modern packaged compiler-host closure | Implemented | Fresh packages, incremental driver AddSource, compiled/executed constant and cancellation |
| Numeric C# 14 and 15 profiles | Implemented | Actual matching compilers compile and execute identical constant-source bytes; broad corpus pending |
| Classic and v4 typed lowering | In Progress | Independent typed type walkers; expressions pending; no serialization or migration bridge |
| Immutable aliases, records, cases and structural equality | Implemented | Shared type corpus compiled/executed in all four family/profile combinations |
| Expressions, lexical scope, ordered patterns and SDK calls | Planned | Shared behavior corpus |
| Determinism, located refusals and traversal cancellation | Planned | Positive and negative behavior tests |
| Package, trimming, full tests and coverage | Planned | Exact commands and denominators required |

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
solution/build/package/docs registrations are integrated and their serial edit
lease is released. Expression lowering, naming/capture, cancellation during
traversal and final package/trimming acceptance remain open.

The type checkpoint uses get-only classes and closed nested cases in both
profiles. Transparent aliases expand to their structural representation, while
custom cases retain nominal identity. Public aliases of private structural
records produce usable public records; exposing a private nominal type fails.
Generated equality and hashing explicitly use the SDK structural protocol for
exactly the same payloads, including nested collections and boxed cases.
