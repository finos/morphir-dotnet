---
title: "PRD: CLI model generation"
description: "Versioned Morphir model generation through shared loading, backend and publication contracts."
status: Completed
created: 2026-11-02
last_updated: 2026-11-02
---

# CLI model generation

Users need `morphir generate` to produce executable F# libraries from supported classic and v4 models stored as JSON or Ion. Settings resolution, loading, backend selection and publication must remain reusable when file-backed configuration and MEP adapters arrive.

The approved implementation plan is [add-generate-command](../../../../../openspec/changes/add-generate-command/proposal.md), with [design and examples](../../../../../openspec/changes/add-generate-command/design.html) and the authoritative [task checklist](../../../../../openspec/changes/add-generate-command/tasks.md). Related work: [#363](https://github.com/finos/morphir-dotnet/issues/363) and [#368](https://github.com/finos/morphir-dotnet/issues/368). The user authorized implementation and one commit per task on 2026-11-02. Merge and publication require separate authorization.

## User workflow

```powershell
morphir generate --input models/pricing-v4.ion --target fsharp --output generated/pricing --json
```

Input, target and output are explicit. The command supports JSON and Ion model files and Ion trees. Configuration file discovery, YAML model input and MEP admission/hosting remain follow-up work through the agreed contracts.

## Feature status tracking

| Feature | Status | Tasks | Completion evidence |
| --- | --- | --- | --- |
| Merged Ion baseline | Implemented | 1.1 | Fast-forward to `0a7f6ca`; solution locked restore passed |
| Shared behavior corpus and compiler harness | Implemented | 1.3-1.4 | Required matrix coverage and independent expectations across supported versions/formats |
| Versioned model and generation contracts | Implemented | 2.1-2.2 | Public consumer examples and compatible existing APIs |
| Settings and backend resolution | Implemented | 2.3-2.5 | Explicit overrides, fake provider, incompatibility and ambiguity tests |
| Typed entry point and classic compatibility | Implemented | 3.1-3.4 | Existing consumers and shared corpus remain green |
| Required v4 F# semantics | Implemented | 4.1-4.9 | Nonempty generated libraries compile and execute through the same harness |
| Bounded JSON/Ion model loading | Implemented | 5.1-5.6 | Capture limits, retained originals, codec diagnostics and representation parity |
| CLI generation and reporting | Implemented | 6.1-6.4 | BDD scenarios, typed exits, parseable JSON stdout and command documentation |
| Owned artifact publication | Implemented | 7.1-7.3 | Ordered manifest, unchanged receipts, stale-owned removal and failure tests |
| Coverage, trimming and Native AOT | Implemented | 8.1-8.4 | Coverage thresholds pass; trimmed/native executables pass the shared generation gates |
| TOML/YAML generation configuration | Deferred | Follow-up | One canonical configuration model and settings resolver; no parser-specific generator API |
| MEP host/guest adapters | Deferred | Follow-up | Reuse generation contracts with protocol admission and capability negotiation |

## Testing approach

Use one behavior-oriented corpus, compiler/execution runner and assertion approach. Version, physical format, layout and optimization are dimensions of a case. Representation adapters construct inputs; they do not define separate v3/v4 oracles or quality gates. Record expected refusals and genuinely unrepresentable combinations explicitly. Missing required cases fail matrix checks. Cross-version parity supplements independently specified expected results.

V4-only semantics use the same harness and diagnostics. The supported/refused feature matrix and scalar profile are in the design. V4 generation is required in this delivery and cannot be declared complete through decoding-only or empty-model tests.

## Implementation notes

### Baseline, 2026-11-02

- Integrated merged PR #456 without resetting local work; the approved planning artifacts remain intact.
- `dotnet restore Morphir.slnx --locked-mode --nologo` passed with existing package warnings.
- Plain `dotnet test` is incompatible with this repository's mixed .NET 10 testing setup. Use its Nuke Test target, which invokes TUnit and Expecto executables.
- The first Nuke baseline run exposed two environmental Tooling failures: temporary directories under the user's profile discover its ancestor `.morphir` directory. With process-local TEMP/TMP set to an isolated root under `C:\Users\Public`, all 111 Tooling tests passed. No user configuration was modified. The full isolated Nuke baseline passed, including cold package-consumer checks (2 minutes 55 seconds).

### Shared acceptance infrastructure, 2026-11-02

- Extracted the existing FSI helper into Morphir.Testing; existing generator tests and new conformance cases use it. One rental expectation executes for classic JSON v1/v2/v3, with failing checks for missing/duplicate matrix rows and unexplained omissions. All 91 generator tests and the full Nuke Test target passed (3 minutes 9 seconds).

- Added 11 parameterized CLI BDD scenarios before command implementation. Their observed red state is the absent `generate` command; all existing tests still pass in the full Nuke run. They remain active acceptance tests and must turn green when the CLI is integrated. JSON/classic/v4/Ion-tree paths share step definitions and the common daily-rate compiler/oracle.

### Contracts and resolution, 2026-11-02

- Added the common wrapper in `Morphir.IR.Model` to avoid colliding with the existing classic/v4 namespaces. Existing Ion/Myriad consumers compile. Contract tests compile C# and F# callers, and prove private input/option ownership. Ten settings tests and six registry tests pass. The full Nuke run passes existing suites and the new model/contract/resolution tests; only the 11 intentionally red command BDD cases remain failing.

### Typed classic generation, 2026-11-02

- Added public typed generation with source context and shared options while preserving the strict JSON wrapper. Shared rental cases cover classic versions and typed/file APIs with optimization enabled and disabled. Regression tests exposed and closed typed name and type-arity bypasses. All 120 generator tests pass; the full Nuke run has only the same 11 pending CLI acceptance failures, and existing Ion/Myriad suites pass.

### Direct v4 generation, 2026-11-02

- Added v4 type emission with shared F# naming, SDK type spellings, AST formatting and dependency ordering. A single generic-record/union consumer compiles and executes classic/v4 inputs; visibility assertions use the same adapters. All 124 generator tests pass. V4 remains unregistered until the required expressions, validation, refusals and optimizer checks are complete.

- Scalar profile: 42 common rows cover Int32 boundaries, overflow, exact decimal precision/scale and exponents, decimal signed zero, floating-point bit patterns and extremes, escaped strings, BMP and supplementary characters. Explicit reasons account for values classic typed IR cannot represent. The negative-zero case exposed and fixed a shared decimal rendering defect. All 166 generator tests pass; the full Nuke run retains only the 11 pending CLI BDD failures.

- Core expression generation: native v4 traversal covers constructors and partial application, tuple/list/unit values, named/anonymous records, fields, references, lambdas, conditionals and ordered patterns. A shared expression consumer and record-billing case execute for classic/v4. Test-only representation adaptation reuses classic fixtures; production v4 emission never converts to classic IR. All 170 generator tests pass; the full Nuke run still has only the pending CLI failures.

- Local definitions and updates: existing recursion, shadowing, lexical-capture and record-operation fixtures now share corpus consumers and expected values across classic/v4. A new acyclic letrec case exposed a binding-order failure in both emitters; shared dependency grouping fixes it and rejects cyclic non-functions. All 180 generator tests pass; full Nuke results retain only the 11 pending CLI acceptance failures.

- V4 validation: the common diagnostic inventory now runs scope, name, arity, SDK, cycle and type failures for classic/v4. Inferred-type cases use explicit v4 applicability and retain source ranges. Compatible structural/named record hints compile using the declared target representation. Existing SDK List/Maybe/Result fixtures and their oracle now run through the shared inventory. All 205 generator tests pass; full Nuke results retain only the pending CLI failures.

- Refusal profile: shared rows cover Specs/Application, documents, holes, incomplete types/bodies, native/external bodies, open records and nonempty constraints. Every applicable refusal also exercises the existing publisher against prior output and its ownership manifest. Required semantics are explicit admission/context facts rather than an invented IR extension key; provider declarations are checked before invocation, and direct generation honors the same requirement boundary. Optional opaque metadata remains usable. All 241 generator tests and 8 resolver tests pass; full Nuke results retain only the 11 pending CLI cases.

Exact optimization uses shared arithmetic proofs over scalar values, with native v4 traversal. Twenty common compiler/execution rows cover optimization on/off, exact integer/float folds, inexact float/decimal preservation and user-function isolation. The matrix exposed a rounded classic float fold; both versions now require exact binary-rational equality. All 261 generator tests and 136 pipeline-plugin tests pass. The full Nuke run has only the same 11 pending CLI acceptance failures.

- Public v4 entry point: the entire common model corpus now uses `generateModelWith`, with no internal v4 test bypass. Eight pricing rows execute classic/v4 with folding on/off. The static F# provider advertises exact versions and validates its Boolean option. Integrating its dependency exposed a Fantomas compiler type collision in C#; keeping implementation-only AST/formatter compile assets private resolves it while preserving runtime dependencies. Corrected an invalid mixed-case name in the pending v4 CLI fixture. All 269 generator tests pass; the full run passed other suites, and the corrected complete Tooling rerun has 137 passing tests and only the 11 pending CLI cases.

The generator README now maps supported/refused capabilities to the shared inventory and states the scalar ABI and TEST-1 extension procedure. A standalone FSI consumer compiles the documented public functions. All 270 generator tests pass in the full Nuke run; only the 11 command acceptance cases remain red.

### Bounded loading, 2026-11-02

- Owned capture enforces aggregate bytes before allocation and entry, depth and path limits while enumerating. It refuses reparse points and device entries, checks ancestors and opened handles, and reports locked/unreadable members without returning partial input. Nine tests cover boundaries, mutation, cancellation and native Windows links. The full Nuke run still has only the 11 pending CLI cases.

JSON decoding uses the public classic/v4 readers after a bounded token scan. Thirteen tests cover versions, exact source context, retained bytes, malformed/version diagnostics, explicit JSON selection and pre-DOM node/depth limits. The full Nuke run passes all existing suites and new loader tests, apart from the 11 pending CLI cases.

Ion decoding consumes captured bytes/entries through the existing readers, without a JSON bridge. Twelve tests cover native 3.0.0/3.1.0/4.0.0 views, text/binary/tree selection, malformed-Ion diagnostics, aggregate node limits and tree member locations. All pass; the full Nuke run retains only the 11 pending CLI cases.

Eleven profile tests refuse YAML models, JSON/YAML trees, JSON MEP envelopes and text/binary morphir_model wrappers with explicit format/profile diagnostics. They do not attempt admission or downgrade. The full Nuke run retains only the 11 pending CLI cases.

The wire matrix extends the existing daily-rate and scalar inventory through the same `GenerationConformance.run` and FSI runner. Its 64 rows contain 40 compiled/executed programs, 12 expected integer-overflow refusals and 12 explicit unrepresentable Ion-version combinations. Same-version sources and declared order agree across formats, and deleting a required row fails matrix validation. All four matrix tests pass; full Nuke results retain only the 11 pending CLI cases.

The Tooling generation README documents the public loader, tested format/version combinations, finite budgets and owned-byte versus semantic-view guarantees. Consumer tests also prove Specs/Application identity remains separate from backend selection. The full Nuke run retains only the 11 pending CLI cases.

### Owned publication, 2026-11-02

Publication is implemented before CLI orchestration so its handler can use a tested effect boundary. The adapter uses `VFileTree.publish`, the centralized manifest policy and typed receipts. Tests verify declared order, repeat unchanged receipts and unchanged file/manifest timestamps. Both tests pass in the full Nuke run; only the pending CLI scenarios remain red.

Publication preflight checks canonical input/output separation, linked ancestors and leaf files, current artifacts and previous manifest entries before writes. Eleven publisher tests cover ordering, cancellation, overlap, traversal, rooted paths, case collisions, links, generation errors and sibling-prefix handling. They pass in the full Nuke run; only the 11 pending CLI cases remain red.

Three additional publication tests verify stale-owned removal with unowned preservation, unchanged files/manifest after a staged content failure, and a real commit failure after one file has moved. The latter deliberately demonstrates mixed output and its operational diagnostic. The CLI reference states this limited guarantee. All pass in the full Nuke run, apart from the pending command acceptance cases.

### Integration verification, 2026-11-02

Added the previously omitted pipeline-plugin suite to Nuke Test. The complete run passed in 4 minutes 33 seconds: Core 237, Tooling 230, Internal 25, Models 791, Pipeline 299, Pipeline Plugins 136, SDK 175, F# Generation 270, Ion 463, Myriad 4 and Myriad Integration 12. Cold package-consumer checks passed. These suites report no ignored/skipped tests. Required representation rows and explicit unrepresentability reasons are enforced by the common inventory. Additional readiness regressions and published-runtime checks continue under 8.2/8.3.

### CLI orchestration, 2026-11-02

The existing host and source-generated mediator coordinate settings, captured-model loading, static backend selection, validation, generation and owned publication. Hosted command factories receive that same service provider. Three host/handler tests pass, and all 11 original BDD cases now pass with the command binding. The full run exposed the new cancellation regression, now corrected and verified in the reporting task. All 229 current Tooling tests pass; other full-run suites pass.

CLI binding adds explicit paths/target, constrained input format, repeated backend options and JSON mode. Seven subprocess tests cover unknown target/flags, invalid format/options, missing option values and last-value Boolean handling. Parser failures use the command's structured report and code 2. Command actions contain no target switches or separate host construction.

Reports serialize through a source-generated context with plain DTO fields. Six reporting tests cover typed exit codes, a real operational CLI failure, parseable JSON and cancellation before publication. The cancellation test exposed the host's synchronous invocation and lost caller token; asynchronous invocation now forwards it and shutdown does not reuse the cancelled token. Full Nuke verification passes all suites, and the additional operational-report regression passes.

The command reference, getting-started guide and troubleshooting entries document actual flags, profiles, output, exits and ownership guarantees. The v4 daily-rate JSON fixture is shared by examples and BDD/unit adapters. Ran classic JSON, v4 JSON with optimize=false, Ion file, Ion tree and CI/repeat examples with temporary output paths; each returned valid reports and nonempty artifacts, and repeat generation returned unchanged Inventory.fs. Full Nuke Test, including cold package-consumer checks, passed in 4 minutes 52 seconds.

### Coverage and boundary regressions, 2026-11-02

Repository Fantomas and `dotnet format` passed for affected F#/C# files. The complete Nuke Test target, including cold package consumers, passed in 4 minutes 44 seconds. Current suites include 325 generator tests, 245 Tooling tests and 141 pipeline-plugin tests, with no ignored/skipped tests. Shared regressions cover nested bindings, aliases, definite application/field errors, exact arithmetic proofs and constrained dependency signatures. Observed failing tests preceded the validation fixes.

Collected production line coverage with Microsoft `dotnet-coverage` 18.7.0 around that complete run. After merging duplicate module/consumer entries by source path and line, affected production files cover 4,575/4,892 lines, 93.52%; all production files cover 14,034/16,946 lines, 82.82%. Generated `obj` sources, test projects and the test-support library are excluded. Both repository thresholds pass without a baseline exception.

The special-file regression checks refusal before opening. The same ten capture tests also passed on Ubuntu 26.04 ARM64 using a self-contained published test executable; the Unix adapter creates a real FIFO without a writer and checks `/dev/null`. Unix inspection uses the normalized .NET 10 `System.Native` stat ABI before `FileStream` construction. Windows continues to check file attributes and opened handles. No second compiler or generation assertion strategy was introduced.

### Trimmed and Native AOT execution, 2026-11-02

Published self-contained `win-arm64` CLI builds with .NET SDK 10.0.403/runtime 10.0.14, Windows SDK 10.0.26100.0 and MSVC 14.44.35207 ARM64 tools. Both trimmed and Native AOT executables passed all 245 Tooling tests, with zero skips. `MORPHIR_GENERATION_CLI` selects the published-executable adapter for the existing generation BDD, command and wire tests; other CLI tests keep their existing host. Each published gate includes the same 64 wire rows: 40 compiled/executed programs, 12 expected overflow refusals and 12 explicit unrepresentable Ion-version combinations. JSON reports, human output, optimize=false and previous-output preservation pass. The final native executable is 15,498,240 bytes.

Actual native failures exposed assembly enumeration in mediator startup, dynamic formatter reflection and F# union formatting in report labels. Native startup now registers generated handlers directly; ordinary JIT hosts retain existing handler discovery. Native generation retains the complete emitted source without Fantomas, and report labels use explicit union cases. The same compiler/oracles validate that source. Unexpected backend failures log once and preserve the resolved backend identity in a payload-free structured report.

Reviewed detailed warnings with `TrimmerSingleWarn=false`. The final trimmed publish reports 62 IL-warning lines and the native publish 87, including build/analysis repeats. None originate in the new generation implementation. Origins are existing Core JSON converter/serialization APIs, management and verify serialization, Foundatio reflective fallback/type-name helpers, FSharp.Core formatting/reflection, and, in the trimmed JIT build, Fantomas compiler resources and mediator discovery. No warning suppression was added. `TreatWarningsAsErrors=false` matches the repository's trimmed-build practice; these dependency/existing-command warnings remain visible and do not establish that unrelated commands are AOT-safe. Generation uses source-generated JSON and static first-party provider registration, with the actual native dispatch and output tested above.

Publish commands used `-c Release -r win-arm64 --self-contained true`, `-p:PublishTrimmed=true`, and, for native, `-p:PublishAot=true`. The Visual Studio Installer directory was added only to the build process PATH for linker discovery. After a stalled reusable MSBuild worker, its complete process tree was terminated and verified stopped; isolated `-p:UseSharedCompilation=false -nodeReuse:false -m:1` publishes passed. Normal and locked solution restore passed after publishing, without retaining incidental runtime-specific lockfile changes.

### Final capability audit, 2026-11-02

Checked the three delta specs against the command, library APIs, shared inventory and delivered examples. Explicit CLI settings, exact version/distribution selection, typed classic/v4 generation, JSON/Ion loading, refusal diagnostics, ordered publication and typed reports match the delivered contracts. The final audit added dependency-signature constraint refusals and backend identity on failure reports. Required matrix rows remain enforced, including explicit reasons for genuinely unrepresentable encodings.

Examples use supported fixtures and flags. TOML/YAML discovery and MEP syntax remain future-only; the command does not accept their proposed flags or envelopes. The library documentation distinguishes the legacy classic JSON wrapper from the versioned typed API and explains native formatting differences. Publication documentation states the observed staging guarantee and possible mixed output after a commit-time failure. No directory-wide rollback is claimed. `openspec validate add-generate-command --strict` passes. The change remains unarchived pending repository review.

After the final report fix, repeated the full Nuke Test target and coverage collection. All 2,717 tests and cold package-consumer checks passed, with no ignored/skipped tests; Nuke completed in 4 minutes 54 seconds. Final normalized production coverage is 4,577/4,894 affected lines, 93.52%, and 14,036/16,948 overall lines, 82.82%. Formatting verification and locked restore pass. All 39 tracked implementation tasks are complete, each with its own commit.

### PR #457 SDK regression fixes, 2026-11-02

Reproduced both SDK review findings through the common corpus. V4 Maybe/Result constructors lost the expected nominal record payload type; an argument named `id` captured the emitted SDK identity function in both classic and v4. Six representation rows failed before the fixes. Constructor argument types now follow the expected SDK Maybe/Result type, and the shared identity spelling uses the fully qualified FSharp.Core function. All 201 focused model-conformance cases pass, including eight added classic/v4 rows with ordinary and nested bindings. The three payload branches share one independent expected result; there is no new version-specific compiler or assertion path.

The full Nuke Test target and cold package-consumer checks passed again in 4 minutes 51 seconds: all 2,725 tests passed, including 333 generator tests, with zero ignored/skipped tests. Refreshed production coverage is 4,595/4,906 affected lines, 93.66%, and 14,054/16,960 overall lines, 82.87%.

Rebuilt the trimmed and Native AOT Windows ARM64 executables after these fixes; both passed all 245 Tooling tests through the existing published-executable adapters. No new IL warnings originate in the changed generation code. Formatting verification, strict OpenSpec validation and post-publish locked restore pass.

### PR #457 macOS test environment, 2026-11-02

The macOS CI job exposed a test-environment assumption introduced by strict ancestor-link checks. Its default temporary directory was under `/var/folders`, through the OS-provided `/var` link, so capture correctly refused otherwise ordinary fixtures and 78 Tooling tests failed. Windows was cancelled by matrix fail-fast; Ubuntu, lint, OpenSpec and CVE checks passed on that commit.

The shared CI test job now sets TMPDIR, TEMP and TMP to RUNNER_TEMP before running the same workflow on every matrix OS. Production link/special-file refusal is unchanged. Reproduced the issue with the existing ten Linux capture tests: six failed with a linked temporary root, then all ten passed with the physical root. All 245 Windows Tooling tests passed using the same environment settings. Actionlint reports no new findings; both main and the changed workflow report the same existing required/default workflow-input warning. Strict OpenSpec validation and whitespace checks pass. The next macOS job verifies the hosted-runner path itself.

## Open questions

No unresolved scope decisions block the agreed plan. Required semantics remain caller/admission facts because v4 has no standard wire marker; this delivery does not invent one. Wider scalar ABI profiles, Application packaging and ecosystem-wide configuration precedence belong to separately reviewed changes.
