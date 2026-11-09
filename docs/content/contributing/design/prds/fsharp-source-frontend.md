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
| Shared foundation | Gherkin MCK seed, vocabulary, linter and deterministic runner | Implemented | Task 1.3; 29 focused tests, 94.44% sequence coverage, native osx-arm64 smoke |
| Tool path, first delivery | Compile BDD acceptance contract | Implemented | Task 1.4: 12 scenarios in `tests/Morphir.Tooling.Tests/Features/Compile/Compile.feature`, observed red (12/12 failed, `compile` absent), then held `@ignore @pending:<task>` until the named task enables them |
| Tool path, first delivery | Exact versions, checked diagnostics, native builders and writers | Implemented | Tasks 2.1–2.6: `Morphir.Compilation` (168 tests), classic and v4 writers checked against readers, schemas, the pinned v4 kit and upstream IR; docs in `docs/content/docs/api/compilation.md`; follow-ups #514, #515 |
| Tool path, first delivery | Syntax-led match reconstruction and checked type-evidence proof | Implemented | Revised task 3.1; 34/34 standalone tests, including all eleven original probes |
| Tool path, first delivery | Hermetic FCS edge and checked match evidence | Implemented | Task 3.2; `src/Morphir.Frontend.FSharp`, 39 focused tests in `Morphir.Frontend.FSharp.Tests` |
| Tool path, first delivery | Admission catalog | Implemented | Task 3.3; `Admission.fs`, 38 rules with examples, 62 admission tests |
| Tool path, first delivery | Planning | Implemented | Task 3.4; `Planning.fs`, 21 planning tests; `[<MorphirName>]` in `Morphir.FSharp.Annotations` |
| Tool path, first delivery | Type lowering | Implemented | Task 3.5; `Lowering.fs`, 10 tests through both builders |
| Tool path, first delivery | Value and pattern lowering | Implemented | Task 3.6; `ValueLowering.fs`, `TypedIndex.fs`, 12 tests through both builders |
| Tool path, first delivery | SDK mapping | Implemented | Task 3.7; `SdkMapping.fs`, 9 table tests (inventory, FSharp.Core and F# generator round trip) and 11 lowering tests |
| Tool path, first delivery | Registration and corpus run | Implemented | Task 3.8; `FSharpFrontend.registration` over the FE-1 contract (`IFrontend`), the `library` adapter, 39 corpus scenarios (one refusal per catalog code and a semantic case with hand-written IR for versions 1–4), 468 cases qualify; behavioral verdict moved to task 3.9 |
| Tool path, first delivery | Behavioral verdict and generator round trip | Implemented | Task 3.9; vocabulary 1.1 call step and value codecs 1 (morphir-elm `DataCodec` shape) and 2 (tagged objects), `Runner.runWith` with a `CallChecker`, the .NET checker through the F# backend and FSI; 50 scenarios, 600 cases and 33 call observations qualify; every successful case round-trips for versions 1–4 after three backend fixes and one frontend access fix |
| Tool path, first delivery | Coverage documentation | Implemented | Task 3.10; `docs/content/docs/frontends/fsharp/coverage.md` generated from the admission catalog (38 codes, each with an example), a test that fails when the page differs from the catalog, and the F# frontend overview with Fable and Fantomas as prior art |
| Tool path, first delivery | Sources, configuration, CLI composition and streamed reports | Implemented | Tasks 4.1–4.3 and 4.6–4.8: `morphir compile` in `src/Morphir.Tooling/Features/Compile/`, `FSharpFrontendScenario` only in `dotnet-morphir`, schema amendments for SemVer `ir.format_version` and the shared `project_file`/`extension` keys, the fold property over 600 corpus cases, JSON and JSON Lines reports, `docs/content/docs/cli/compile.md` and the project-file sources reference |
| Tool path, first delivery | MSBuild project-file provider and shared bounded capture | Implemented | Tasks 4.4 and 4.5: `MsBuildProjectFileProvider` (evaluation only, through a bounded child process) and `SourceCapture` over the bounded capture core that `generate` also uses; 17 provider tests with real `.fsproj`/`.csproj` files, 6 process-runner tests, 11 source-capture tests; the 10 generate capture tests pass unchanged |
| Tool path, first delivery | Compile records and generation from a prior compile | Implemented | Tasks 5.1–5.5: `CompileTaskStore`, `compile.lock` and `-o` installation in `src/Morphir.Tooling/Features/TaskRecords/`, 85 test cases against the pinned upstream record; `compile` publishes through the store (`-o`, `--out-dir`, `installed` in the report) with the MSBuild provider and the shared capture in both hosts; `RecordGenerationSettingsResolver` gives `generate` without `--input` the IR of the most recent compile and holds the shared lock until loading ends; 23 new tests, the project-file, record and tombstone acceptance scenarios, and the documented two-step example in `docs/content/docs/cli/generate.md` |
| Tool path, first delivery | In-memory generation and library/CLI qualification | Implemented | Tasks 6.1–6.4: `GenerationStages.RunFrom`; `compile --target`/`--generate-output`/`--option` on the written bytes, with the generate and generatePublish stages; the MCK `cli` adapter, checked against a scripted fake executable; the complete suite (600 variants) qualifies `dotnet-morphir` through the `library` and `cli` adapters, and a sample through `compile --target`. See [Tool-path qualification, 2026-11-09](#tool-path-qualification-2026-11-09) |
| MEP path, second delivery | Pinned 0.2.0-draft.1 codec, envelopes and session core | Implemented | Tasks 7.1–7.4 on the add-mep-core core; compileModel request types, result sum, reserved options, RFC 8785 context digest, 64 MiB frames and compileModel tuple negotiation; 6 new contract entries and 9 tests |
| MEP path, second delivery | Frontend-only F# guest and installable package | Implemented | Tasks 8.1–8.4; `morphir-fsharp-frontend` with JSON compileModel tuples for 4.0.0 and 3.0.0; 14 tests, including replay of the pinned transcripts against the guest process and a pack-install-describe check |
| MEP path, second delivery | AOT host, classic wire profiles and transport parity | Implemented | Tasks 9.1–9.5: `Morphir.Compilation.Mep` (`MepFrontend`, AOT-safe), `MepFrontendExtensionConnector` in `CompilationScenario`, the classic path through `3.0.0` and `ClassicJson.readTypedDistribution`; the Native AOT `morphir` with the installed guest gives the library IR for 588 of 600 suite variants (the 8 others are the `.fsi` case, recorded in `conformance/deviations.md`). See [Default-CLI viability evidence, 2026-11-09](#default-cli-viability-evidence-2026-11-09) |
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
milestone is separate and requires task 9.4.

**Milestone status, 2026-11-09:** the tool-path (dotnet-tool) milestone is
complete: task 6.4 passes (see the evidence below). The AOT/MEP milestone is not
complete; it needs task 9.4. Required test, format, coverage and
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
- Task 3.2 resolves the production pairing. The central pins are now
  FSharp.Compiler.Service 43.12.403 and FSharp.Core 10.1.403. FSharp.Core
  11.0.100 is unlisted on NuGet, and no stable compiler service depends on an
  11.x Core. The whole solution builds and its F# test projects pass with the new
  pair. Renovate updates the two packages as one group.
- The production evidence edge uses the compiler's `ParsedInput.fold` visitor,
  not the spike's reflection walker. It compares every original symbol use with
  the analysis copy (symbol, checked type, generalization and constraints), and
  refuses with `fsharp.unsupported_match_shape` and a stable reason in the data.
  Tests prove each refusal reason by changing the analysis copy.

### Portable conformance foundation, 2026-11-07

- Updated `feat/fsharp-compile` with the plan landed in #495, preserving the
  implementation PRD, pinned fixtures and completed-task markers.
- Added `Morphir.Conformance`, an internal AOT-compatible library over the
  official Gherkin 35.0.0 parser already used by Reqnroll. The seed feature and
  closed vocabulary live under `conformance/`; no expected result comes from a
  compiler or writer.
- Case reading validates source documents, stable ids, portable artifact paths,
  step/tag semantics, explicit matrices and explained exclusions before running
  an adapter. Cases and artifact digests use ordinal portable file order.
- The library adapter receives compile input only. Comparison and expectations
  stay in the runner. Reports retain exact model text, diagnostic keys, requests,
  exclusions and operational failures, including partial observations and earlier
  warnings. An operational failure does not truncate later cases.
- Evidence: the focused test project passes 29 tests, including the three fixture
  provenance tests. AltCover measured 94.44% sequence coverage and 73.13% branch
  coverage for `Morphir.Conformance`, without excluding handwritten code.
- The native osx-arm64 smoke parses the shipped seed and rejects the deliberately
  incorrect adapter for all twelve variants. Expanded analysis warnings introduce
  no warning details beyond the existing `morphir` AOT host baseline. The baseline
  and smoke use the same SDK, runtime and FSharp.Core pin. Existing FSharp.Core
  reflection/formatting warnings remain visible; none are suppressed.
- Reproduce unit checks with `dotnet run --project
  tests/Morphir.Frontend.FSharp.Tests -- --sequenced`. The AOT and warning-baseline
  commands are documented in `conformance/README.md` and below. Coverage was
  collected by the repository's existing AltCover 8.9.3, instrumenting only
  `Morphir.Conformance` and executing the same test assembly.
- This completes the task 1.3 seed, not frontend qualification. Task 3.8 binds
  the production in-process compiler through the `library` adapter. Every
  semantic case of task 3.8 has hand-written IR for every version, and the
  classic writers order name-keyed lists by name.
  Task 3.9 adds behavioral qualification through F# generated from the compiled
  IR. Its semantic cases without hand-written IR rely on the call checker and
  the round trip to read each model with its version's reader. CLI/MEP adapters and the full kit
  manifest/profile/report-check features stay in their planned deliveries.

```console
dotnet publish tests/Morphir.Conformance.Smoke -c Release -r osx-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -o /tmp/morphir-conformance-aot
/tmp/morphir-conformance-aot/Morphir.Conformance.Smoke conformance
dotnet publish src/Morphir/Morphir.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -o /tmp/morphir-existing-aot-baseline
```

### Windows checkout provenance, 2026-11-07

- Windows CI exposed CRLF-sensitive manifest parsing. The new regression first
  reproduced the empty-entry result and now verifies identical LF/CRLF parsing.
- Git attributes preserve original MEP/out-directory fixture bytes and enforce LF
  for the portable case set. Fixture payloads and recorded hashes are unchanged.
- A disposable Git index/checkout with `core.autocrlf=true` reproduced byte changes
  before the fix and now preserves every pinned hash and case-set artifact byte.
  The original committed fixture blobs were also verified against their manifests.
- The focused project now passes 28 tests. The full Release test target, including
  package checks, passed in 5m27s; lint and explicit Fantomas checks also passed.
  The cancelled Linux/macOS jobs were matrix fail-fast cancellations after the
  Windows provenance failure.

### Declared scenario matrices, 2026-11-07

- Review identified that a header-only Examples block generated no variants, so
  matrix validation could omit its scenario while accepting another complete one.
- The regression reproduced acceptance of a complete scenario beside an empty
  scenario. Matrix validation now starts from declared scenario IDs and locations,
  reporting every missing combination even when a scenario has no generated rows.
- All 29 focused tests pass. AltCover reports 94.44% sequence and 73.13% branch
  coverage for `Morphir.Conformance`, with no handwritten code excluded.
- The full Release test target, including package checks, passed in 3m26s. The
  native osx-arm64 smoke accepts the shipped seed and rejects the empty scenario
  with all twelve missing combinations and no qualification report. Expanded
  AOT/trim warnings add nothing beyond the existing CLI baseline.

### Tool-path qualification, 2026-11-09

Task 6.4 ran the complete MCK suite `frontend-fsharp` through the `library`
adapter (the in-process frontend) and the `cli` adapter (`dotnet-morphir
compile --json-lines`), and a sample through `compile --target`. Every kit
verdict (structural, diagnostics and behavioral, with the .NET call checker)
passes. The morphir-dotnet checks also pass: the IR of every successful CLI
variant is structurally equal to the library IR, for every input kind, and the
diagnostics of every failed variant are equal. The IR → F# → IR round trip of
task 3.9 runs in the same test project and passes. MEP parity is part of group 9.

- Build: commit `03d7942`, `dotnet-morphir` `0.4.0-alpha.2+03d7942b8e8e9736170a2907228ae872896bff51`,
  macOS arm64, 18 cores, shared process gate of 9. The five adapter tests take
  3 min 43 s.
- Case set SHA-256: `48bd2accf4c217d442b8fb92f289272e7777ee4e327d8003005a667476c128f7`,
  vocabulary 1.1, IR versions 1–4, input kinds files, directory and project file.

| Report | Adapter | Implementation SHA-256 | Variants | Verdicts | Report SHA-256 |
| --- | --- | --- | --- | --- | --- |
| `library.json` | `library` | `04d3610e…c89e3` (`Morphir.Frontend.FSharp`) | 600 (50 cases) | 596 passed, 4 excluded, qualified | `ce48807d73ea55b09f710be7064e7fd1259db718c88b7af19a9ba7cabfbff7a6` |
| `cli.json` | `cli` | `68625ebb…d60ec` (`dotnet-morphir`) | 600 (50 cases) | 596 passed, 4 excluded, qualified | `3794b6a090057f52dc66af0b439184402a660095a266e7ba148b9dfd3d8cf3ae` |
| `cli-target-fsharp.json` | `cli --target fsharp` | `68625ebb…d60ec` | 192 (16 cases) | 192 passed, qualified | `f33f1d47a65b7ab3b34022b2b0fe105be060f94fe7be86125846826280972dbb` |
| `cli-target-csharp.json` | `cli --target csharp` | `68625ebb…d60ec` | 192 (16 cases) | 192 passed, qualified | `fa971d04a2ff7b8cfac6f54d1e0744e504135e1d96ec0b96cc1320d6782136fe` |

The 4 excluded variants are the directory rows of the `.fsi` case: ordinal
order puts `Pricing.fs` before `Pricing.fsi`, and F# refuses that order. The
`--target` sample is the first 12 semantic cases and the first 4 refusal cases,
with all their rows.

The reports are the deterministic output of `Report.toUtf8`. They are 1.9 MB
(library) and 8.9 MB (cli) of JSON, mostly model JSON, so they are stored with
gzip in `openspec/changes/add-fsharp-compile/evidence/mck-2026-11-09/`. The
SHA-256 values above are of the uncompressed reports. The implementation
identity holds the commit and the assembly SHA-256, so another build gives other
report bytes with the same verdicts.

Reproduce the reports:

```console
git checkout 03d7942
dotnet build Morphir.slnx
MORPHIR_MCK_REPORT_DIR=/tmp/mck-reports dotnet run --project tests/Morphir.Frontend.FSharp.Tests --no-build -- --sequenced --filter "MCK cli adapter (tasks 6.3 and 6.4)"
```

On macOS, set `TMPDIR` to its resolved path first
(`TMPDIR=$(cd "$TMPDIR" && pwd -P)/`), because the model loader refuses a path
through the `/var` link.

### Default-CLI viability evidence, 2026-11-09

This section collects the evidence of task 10.2 for the default-CLI decision.

MCK verdict reports, one for each adapter. All reports are in
`openspec/changes/add-fsharp-compile/evidence/mck-2026-11-09/`, compressed with
gzip; the SHA-256 values are of the uncompressed reports.

| Adapter | Implementation | Variants | Verdicts | Report |
| --- | --- | --- | --- | --- |
| `library` | `Morphir.Frontend.FSharp` in process | 600 | 596 passed, 4 excluded, qualified | `library.json`, see [Tool-path qualification](#tool-path-qualification-2026-11-09) |
| `cli` | `dotnet-morphir compile` | 600 | 596 passed, 4 excluded, qualified | `cli.json` |
| `cli --target fsharp`, `cli --target csharp` | `dotnet-morphir compile --target` | 192 each | 192 passed, qualified | `cli-target-fsharp.json`, `cli-target-csharp.json` |
| `mep-guest` | `MepGuestAdapter` over the guest process | every case | IR bytes and diagnostics equal the `library` adapter; a `.fsi` case is refused before dispatch | `MepFrontendTests` (no report file) |
| `cli` with `--extension` | Native AOT `morphir` with the installed `morphir-fsharp-frontend` | 600 | 588 passed with the library IR, 4 excluded, 8 refused (`frontend-fsharp-0081`, `.fsi`); not qualified because of the `.fsi` case | `cli-aot-mep.json`, SHA-256 `1fae5506a20f5db5a6eba8fb1ad52ee34398ffbd53628281e898d9c8b8fc9300` |

Reproduce the AOT report on macOS arm64 (the other reports have the command in
[Tool-path qualification](#tool-path-qualification-2026-11-09)):

```console
dotnet build Morphir.slnx
dotnet publish src/Morphir/Morphir.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:PublishTrimmed=true -o /tmp/morphir-aot
dotnet pack src/Morphir.Frontend.FSharp.Mep -c Release -o /tmp/guest-pkg
dotnet tool install Morphir.Frontend.FSharp.Mep --version <version> --tool-path /tmp/guest-tool --configfile <nuget.config with /tmp/guest-pkg>
MORPHIR_AOT_CLI=/tmp/morphir-aot/morphir MORPHIR_FSHARP_GUEST=/tmp/guest-tool/morphir-fsharp-frontend MORPHIR_MCK_REPORT_DIR=/tmp/mck-reports \
  dotnet run --project tests/Morphir.Frontend.FSharp.Tests --no-build -- --sequenced --filter-test-case "AOT morphir"
```

Native AOT single-file size and compile time on each published RID, from the
manual AOT workflow with `test-suite: measure-compile` on commit `f287a2a`. The
times are the median wall time of 11 runs of `morphir compile` for one F# file:
without a frontend (the settings, sources and frontend-selection stages, which
end with `compile.frontend_unavailable`), and with the installed F# guest
(which includes the start of the framework-dependent guest process).

| RID | Runner | Size | Compile without a frontend | Compile with the guest | Run |
| --- | --- | --- | --- | --- | --- |
| `linux-x64` | `ubuntu-latest` | 23,108,408 bytes (22.0 MiB) | 39 ms | 2089 ms | [42598228850](https://github.com/finos/morphir-dotnet/actions/runs/42598228850) |
| `linux-arm64` | `ubuntu-24.04-arm` | 23,833,392 bytes (22.7 MiB) | 45 ms | 2574 ms | [42598232152](https://github.com/finos/morphir-dotnet/actions/runs/42598232152) |
| `win-x64` | `windows-latest` | 27,385,344 bytes (26.1 MiB) | 130 ms | 3040 ms | [42598235473](https://github.com/finos/morphir-dotnet/actions/runs/42598235473) |
| `osx-arm64` | `macos-latest` | 22,603,784 bytes (21.6 MiB) | 77 ms | 1478 ms | [42598238830](https://github.com/finos/morphir-dotnet/actions/runs/42598238830) |

Reproduce a row on its platform with `scripts/measure-compile-aot.sh <rid>
<guest-command>`, or run the "Manual AOT Testing" workflow with that RID and
`test-suite: measure-compile`. The job summary prints the four values.

Most of the time with the guest is the start of the .NET runtime and the F#
compiler service in the guest process. The AOT host itself starts in less than
150 ms on every RID.

The documented differences from the Rust CLI are in
[`conformance/deviations.md`](https://github.com/finos/morphir-dotnet/blob/main/conformance/deviations.md):
`--json-lines` writes an event stream that ends with a `completed` record
(Rust writes one report line), and the guest is not qualified with the upstream
guest kit, which needs a legacy `0.1` stage and a backend. The guest also does
not claim `.fsi` signature files.

## Open questions

- Confirm the guest package name during the second delivery, as recorded in the
  design. This does not block the in-process frontend.

