---
title: "PRD: CLI model generation"
description: "Versioned Morphir model generation through shared loading, backend and publication contracts."
status: In Progress
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

This is the implementation target, not a command already delivered by this PRD. Input, target and output are explicit. The first command supports JSON and Ion model files and Ion trees. Configuration file discovery, YAML model input and MEP admission/hosting remain follow-up work through the agreed contracts.

## Feature status tracking

| Feature | Status | Tasks | Completion evidence |
| --- | --- | --- | --- |
| Merged Ion baseline | Implemented | 1.1 | Fast-forward to `0a7f6ca`; solution locked restore passed |
| Shared behavior corpus and compiler harness | Planned | 1.3-1.4 | Required matrix coverage and independent expectations across supported versions/formats |
| Versioned model and generation contracts | Planned | 2.1-2.2 | Public consumer examples and compatible existing APIs |
| Settings and backend resolution | Planned | 2.3-2.5 | Explicit overrides, fake provider, incompatibility and ambiguity tests |
| Typed entry point and classic compatibility | Planned | 3.1-3.4 | Existing consumers and shared corpus remain green |
| Required v4 F# semantics | Planned | 4.1-4.9 | Nonempty generated libraries compile and execute through the same harness |
| Bounded JSON/Ion model loading | Planned | 5.1-5.6 | Capture limits, retained originals, codec diagnostics and representation parity |
| CLI generation and reporting | Planned | 6.1-6.4 | BDD scenarios, typed exits, parseable JSON stdout and command documentation |
| Owned artifact publication | Planned | 7.1-7.3 | Ordered manifest, unchanged receipts, stale-owned removal and failure tests |
| Coverage, trimming and Native AOT | Planned | 8.1-8.4 | Recorded test/coverage results and actual published-executable runs |
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

## Open questions

No unresolved scope decisions block the agreed plan. Wider scalar ABI profiles, Application packaging and ecosystem-wide configuration precedence belong to separately reviewed changes.
