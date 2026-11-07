# F# source frontend and compile steel thread

Status: In Progress

Tracking: [#499](https://github.com/finos/morphir-dotnet/issues/499). Planning review: [#495](https://github.com/finos/morphir-dotnet/pull/495).

## Problem and intended behavior

Morphir .NET reads IR and generates F# and C#, but cannot compile source into IR.
The first delivery adds an in-process F# frontend to `dotnet-morphir compile`.
It produces a requested IR version, reports unsupported constructs precisely,
and supports both a later `generate` command and `compile --target`.

The implementation follows the [OpenSpec proposal](../../../../../openspec/changes/add-fsharp-compile/proposal.md),
[design](../../../../../openspec/changes/add-fsharp-compile/design.html),
[capability specifications](../../../../../openspec/changes/add-fsharp-compile/specs/),
and [task checklist](../../../../../openspec/changes/add-fsharp-compile/tasks.md).
Related work includes [#363](https://github.com/finos/morphir-dotnet/issues/363)
and [#368](https://github.com/finos/morphir-dotnet/issues/368).

## Scope and contracts

- Lower the admitted F# core subset once against an IR builder interface. Classic
  and v4 builders construct their own models without conversion between families.
- Write IR versions 1, 2, 3 and 4. Use full `FormatVersion` values, including exact
  SemVer prereleases, in settings, contracts and registries.
- Preserve warnings, information and hints on successful and failed paths.
  Unsupported constructs fail with stable `fsharp.*` codes and zero-based UTF-16
  source ranges. No approximate model is emitted.
- Expand explicitly selected project files through language-neutral providers.
  The MSBuild provider evaluates source order without building or restoring.
- Keep effects at the edges. Stream diagnostics through compile events; derive
  the final report by folding that stream. Stdout is data and stderr is diagnostics.
- Reuse generation resolution and owned publication. Compile records follow the
  ecosystem out-directory, tombstone, installation-ledger and lock contracts.
- Qualify the frontend against independently authored Gherkin MCK cases. No
  implementation generates its own expected results.

## Feature status tracking

| Delivery | Feature | Status | Evidence or next step |
| --- | --- | --- | --- |
| Shared foundation | Tracking epic and feature PRD | Implemented | #499 and this document |
| Shared foundation | Pinned upstream fixtures and SHA-256 provenance | Implemented | Task 1.2; provenance tests observed red, then 2/2 green |
| Shared foundation | Gherkin MCK seed, vocabulary, linter and deterministic runner | Planned | Task 1.3 |
| Tool path, first delivery | Compile BDD acceptance contract | Planned | Task 1.4 |
| Tool path, first delivery | Exact versions, checked diagnostics, native builders and writers | Planned | Tasks 2.1–2.6 |
| Tool path, first delivery | Syntax-led match reconstruction and checked type-evidence proof | Implemented | Revised task 3.1; 34/34 standalone tests, including all eleven original probes |
| Tool path, first delivery | Hermetic FCS edge, admission catalog, planning and lowering | Planned | Tasks 3.2–3.8 |
| Tool path, first delivery | Generator round trip and coverage documentation | Planned | Tasks 3.9–3.10 |
| Tool path, first delivery | Sources, configuration, CLI composition and streamed reports | Planned | Tasks 4.1–4.8 |
| Tool path, first delivery | Compile records and generation from a prior compile | Planned | Tasks 5.1–5.5 |
| Tool path, first delivery | In-memory generation and library/CLI qualification | Planned | Tasks 6.1–6.4 |
| MEP path, second delivery | Pinned 0.2.0-draft.1 codec, envelopes and session core | Planned | Tasks 7.1–7.4 |
| MEP path, second delivery | Frontend-only F# guest and installable package | Planned | Tasks 8.1–8.4 |
| MEP path, second delivery | AOT host, classic wire profiles and transport parity | Planned | Tasks 9.1–9.5 |
| Integration | Coverage, examples, conformance reports and viability evidence | Planned | Tasks 10.1–10.3 |
| Deferred | Project references, external dependency resolution and conditional defines | Deferred | Separate changes |
| Deferred | Incremental compilation, types-only mode and P1 language constructs | Deferred | Separate changes |
| Deferred | Ion, document-tree output, multi-target builds and backend guests | Deferred | Separate changes |

## Acceptance and evidence

The task checklist specifies the acceptance checks. Each behavior follows
red/green/refactor. Tests use the repository's process and sandbox helpers and
never access the real Morphir home or machine configuration.

Writer evidence includes reader round trips, bundled-schema validation and
structural comparisons against independently pinned upstream examples. Frontend
evidence includes typed and located IR, refusal-catalog completeness, deterministic
diagnostics, behavioral observations and IR → F# → IR checks. The same portable
MCK suite runs through library, CLI and, in the second delivery, MEP adapters.

The dotnet-tool milestone is complete only after task 6.4 passes. The AOT/MEP
milestone is separate and requires task 9.4. Required test, format, coverage and
documentation checks remain part of integration; a passing spike does not imply
that either product milestone is complete.

## Implementation notes

### Start, 2026-11-07

- Implementation is authorized by the maintainer's instruction to begin the
  planned steel thread. Publication and merge still need their own approval.
- The implementation branch includes the planning commit and current main,
  including discovery containment and test-isolation helpers. Planning PR #495
  remains unchanged.
- The design identifies match-range joining as a risk and requires its spike
  before lowering. Validate that assumption early, before building on it.
- The first production path is the in-process dotnet tool. No legacy MEP 0.1
  protocol or local replacement protocol will be introduced.

### Match-range join probe, 2026-11-07

- The exact FSF-2 join is disproved by a reproducible probe. Several admitted
  patterns have no exposed decision tree; other trees use the subject range;
  nested clause bodies can lose the source body's range. The planned join
  passes only the match-lambda probe. This original probe remains a historical failing reproduction; the revised task 3.1 is validated below.
- [Findings and reproduction](../../../../../openspec/changes/add-fsharp-compile/spikes/match-range-join.md)
  record all cases and the separate compiler-service/Core compatibility issue.
- The current FCS 34.1.1 pin cannot read the bundled modern Core metadata. A
  local 43.12.403 probe checks every source, but its exact Core dependency
  conflicts with the repository's 11.0.100 pin. Neither central pin is changed.
- The maintainer authorized revising and validating syntax-led reconstruction,
  preserving the supported subset, source order and native builder architecture.

### Syntax-led reconstruction proof, 2026-11-07

- FSF-2 now uses original match syntax for structure and ranges, with FCS-checked
  types and symbol identity. An analysis-only copy adds fresh subject aliases and
  nonexpansive result-match bindings; generated nodes never reach IR builders.
- The standalone proof passes 34/34 tests, including all eleven original probes,
  shadowing, generic field instantiation, computed/reduced subjects, polymorphic
  generalization, constructor arity, nested spans, CRLF, UTF-16 and evidence refusal.
- An identity-call result wrapper failed a real polymorphic counterexample.
  Replacing it with a nonexpansive match binding made that case pass. Signature
  preservation is a required production check, not an assumed property.
- [Replacement findings and reproduction](../../../../../openspec/changes/add-fsharp-compile/spikes/syntax-led-reconstruction.md)
  describe the proof boundary. The spike uses a local exact FCS 43.12.403/Core
  10.1.403 pair without changing central pins. Task 3.2 still owns production
  compiler compatibility, full binding/constraint/reference verification and the
  hermetic evidence edge. Task 3.6 still owns lowering through both native builders.
- Task 3.1 is complete under its revised acceptance criteria. No production
  frontend, emitted IR or MCK qualification is claimed by this checkpoint.

## Open questions

- Resolve and validate the production FCS/Core package pairing in task 3.2.
  The proof's local package overrides do not resolve the repository-wide pairing.
- Confirm the guest package name during the second delivery, as recorded in the
  design. This does not block the in-process frontend.

