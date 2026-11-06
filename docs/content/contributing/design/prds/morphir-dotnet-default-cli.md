---
title: "PRD: morphir-dotnet as the default Morphir CLI"
linkTitle: "Default Morphir CLI"
description: "Proof criteria, command parity and governance for making morphir-dotnet the default Morphir CLI and the home of the conformance oracle."
status: Draft
created: 2026-11-06
last_updated: 2026-11-06
---

# morphir-dotnet as the default Morphir CLI

## Summary

This PRD defines what morphir-dotnet must prove before it can become the default `morphir` CLI for the Morphir ecosystem. It also defines the governance path to that decision. The decision is recorded as proposed in [ADR 0001](../../../../adr/0001-morphir-dotnet-default-cli-and-conformance-oracle.md). The FINOS Morphir maintainers make the final decision.

morphir-dotnet also becomes the home of the Morphir Compatibility Kit (MCK) and the conformance oracle. The oracle is case data with independently written expectations, not an implementation. The MEP protocol definition stays in `finos/morphir`.

## Problem statement

- The current oracle is the Rust CLI and its kits in `finos/morphir`. Some of its behavior is defective, and other implementations are measured against those defects. Evidence:
  - `--json-lines` writes one compact report line, not one record per event.
  - The generic MEP guest kit (`generic-json-library-v1`) requires a legacy MEP `0.1` stage and a guest that is both frontend and backend.
- morphir-dotnet already ships a Native AOT `morphir` executable with configuration, project, workspace, IR verification and code generation commands. It has no proof yet that it can carry the full ecosystem command set.

## Goals

1. Prove viability with measurable criteria (see [Proof criteria](#proof-criteria)), one change at a time.
2. Make the MCK the oracle for every implementation, including the Rust CLI and morphir-dotnet.
3. Reach command parity in small increments (see [Delivery increments](#delivery-increments)). Each increment is its own change with its own MCK suite. The default proposal needs the core command set, not full parity.
4. Give the maintainers the evidence for a decision, and give users a safe switch.

## Non-goals

- Moving the MEP contract definition (the Gleam contract and the Smithy model) out of `finos/morphir`.
- Removing the Rust CLI. Its future is a maintainer decision.
- Changing IR or configuration wire formats without the agreement of the upstream maintainers.

## Command parity

Upstream baseline: `finos/morphir` at `cde7722eb` (`docs/cli/morphir.usage.kdl`, CLI version `0.4.0-beta.12`). morphir-dotnet baseline: `src/Morphir/Program.cs` scenarios at `11558ba`, with planned changes named.

Status values:

| Status | Meaning |
| --- | --- |
| Present | The command exists with the ecosystem meaning. |
| Partial | A command with the same name exists, but its subcommands or behavior do not yet match the ecosystem command. |
| Planned | An OpenSpec change delivers it. |
| Missing | No command and no change yet. |
| Intentionally different | morphir-dotnet will not copy the upstream behavior, for the stated reason. |

| Upstream command | Upstream purpose | morphir-dotnet | Status | Notes |
| --- | --- | --- | --- | --- |
| `compile` | Compile source to IR | — | Planned | [add-fsharp-compile](../../../../../openspec/changes/add-fsharp-compile/proposal.md). It adds `--project-file`, streaming `--json-lines`, and `--target`/`--generate-output`. |
| `generate` | Generate code from IR | `generate` (F#, C#) | Present | Reading input from a prior compile record is planned in add-fsharp-compile. Config-selected targets, `[codegen.<target>]` options and `--from-partial-compile` are missing. |
| `migrate` | Migrate IR between versions | — | Missing | morphir-dotnet `project migrate` and `workspace migrate` migrate configuration, not IR. |
| `ir` | `ir migrate` | `ir verify` | Partial | `ir verify` (schema validation) exists. `ir migrate` is missing. |
| `config` | `get`, `show`, `path` | `get`, `list`, `show`, `path`, `explain`, `set`, `unset` | Present | morphir-dotnet has more subcommands than upstream. |
| `extension` | `install`, `list`, `update`, `uninstall`, `search`, `bundle`, `repository …` | `list`, `install`, `use`, `remove`, `which` | Partial | The existing commands predate MEP acquisition. Repositories, bundles, search and update are missing. |
| `tool` | `install`, `list`, `update`, `repair`, `rollback`, `uninstall` | `list`, `install`, `use`, `remove`, `which` | Partial | Different verbs. Update, repair and rollback are missing. |
| `dist` | `install`, `list`, `update`, `uninstall` | `list`, `install`, `use`, `remove`, `which` | Partial | Different verbs. Update is missing. |
| `mck` | Run, vendor and check compatibility kits | — | Planned | establish-morphir-compatibility-kit. The case data is the oracle, so the subcommands will differ from upstream on purpose where upstream kits are defective. |
| `schema` | Generate JSON Schema for IR | — | Missing | Bundled v1–v3 schemas exist for `ir verify`, but there is no generator command. |
| `version` | Print version information | `--version` flag only | Missing | Small gap. `info` exists, but its content is different. |
| `cache` | Inspect and clean caches | — | Missing | |
| `diagnostics` | Logs and diagnostic archives | — | Missing | |
| `eval` | Evaluate through a native provider | — | Missing | |
| `itest` | Run Markdown and Gherkin examples through CLI processes | — | Missing | Could become part of the MCK runner. |
| `package` | Registry, signing, restore, resolve | — | Missing | Large area (TUF metadata, signing). |
| `metadata` | V4 linked metadata | — | Missing | |
| `decoration` | Typed decorator sidecars | — | Missing | |
| `ui` | Development workbench in a browser | — | Missing | Depends on the workbench assets. |
| `desktop` | Launch Morphir Desktop | — | Missing | Depends on the desktop application. |
| `playground` | Scratch playground in a browser | — | Missing | Depends on the workbench assets. |
| `kb` | Knowledge-base maintenance for the `finos/morphir` repository | — | Intentionally different | This is repository maintenance tooling, not a Morphir user command. It stays with the upstream repository. |

Summary of the 22 visible upstream commands:

| Status | Count | Commands |
| --- | --- | --- |
| Present | 2 | `generate`, `config` |
| Partial | 4 | `ir`, `extension`, `tool`, `dist` |
| Planned | 2 | `compile`, `mck` |
| Missing | 13 | `migrate`, `schema`, `version`, `cache`, `diagnostics`, `eval`, `itest`, `package`, `metadata`, `decoration`, `ui`, `desktop`, `playground` |
| Intentionally different | 1 | `kb` |

Hidden upstream commands:
- `validate` (experimental): partly covered by `ir verify`.
- `transform` (experimental): missing.
- `usage`, which generates the usage specification: missing.

Upstream global options:
- `--out-dir` is planned for compile records in add-fsharp-compile.
- `--no-banner` does not apply, because morphir-dotnet prints no banner.

Commands that only morphir-dotnet has:
- `project migrate` and `workspace migrate` (classic configuration migration);
- `info`.

## Delivery increments

Parity is not one large change. Each increment is a set of small changes. Each change carries one command family at most, adds its own MCK cases, and lands on its own. The proof criteria (PC-2, PC-3, PC-6) are checked again on every change, so an increment can never make an earlier result worse.

| Increment | Commands and features | Changes | Proof criteria it moves |
| --- | --- | --- | --- |
| 1. Compile and generate loop | `compile` (F# frontend, IR writers, `--json-lines`, task records); `generate` reads a prior compile; first MCK suite | add-fsharp-compile | PC-1 (first suite), PC-2, PC-5 (frontend), PC-6, PC-7 |
| 2. Kit and oracle | `mck run`, `report-check`, `list`; a cold-start benchmark; suites run against the Rust CLI | establish-morphir-compatibility-kit | PC-1, PC-3 |
| 3. Core user commands | `version`; `ir migrate` / `migrate`; `validate` and `schema`; `generate` targets and options from `[codegen]` | One change for each command family (not yet proposed) | PC-1, PC-9 |
| 4. Release reach | The win-arm64 build; monthly signed releases | Release workflow change (not yet proposed) | PC-4, PC-10 |
| 5. Ecosystem acquisition | `extension`, `tool` and `dist` verbs; MEP backend hosting (`generateModel`) | One change for each (not yet proposed) | PC-5, PC-8 |
| Later, one at a time | `cache`, `diagnostics`, `eval`, `package`, `metadata`, `decoration`; `itest` folded into `mck` | Prioritized by user need | PC-9 (tracking only) |
| Separate decision | `ui`, `desktop`, `playground` | Open question 4 | — |

The **core command set** for the default proposal is the commands of increments 1 to 3: `compile`, `generate`, `mck`, `config`, `version`, `ir`/`migrate`, `validate` and `schema`. This set is a proposal, and the maintainers confirm it (open question 1). Commands outside the core set do not block the proposal. Each of them has a planned increment or a recorded difference.

## Proof criteria

morphir-dotnet can be proposed as the default only when every criterion below passes for the core command set. Criteria apply to each increment as it lands: an increment must not make an earlier result worse. Each criterion names the change that proves it. The evidence goes in the [Feature status tracking](#feature-status-tracking) table.

| ID | Criterion | Measure | Proved by |
| --- | --- | --- | --- |
| PC-1 | MCK conformance | 100% of the required cases pass for each suite the CLI claims, with no unexplained skips. The same suites run against the Rust CLI, and both results are published. | establish-morphir-compatibility-kit. add-fsharp-compile provides the first suite (F# compile and IR writing). |
| PC-2 | Native AOT size | Single-file executable of 12 MB or less for each release RID (the repository target for a feature-rich CLI is 8–12 MB). No new trimming or AOT warnings in shipped assemblies. FSharp.Compiler.Service is not in the closure of the AOT executable. | add-fsharp-compile task 9.4, then every release. |
| PC-3 | Cold start | Median cold start of `morphir --version` and `morphir config show --json` is 100 ms or less, and no more than 2× the Rust CLI on the same machine. | A benchmark task in establish-morphir-compatibility-kit, or a separate change. |
| PC-4 | Platform matrix | A published, smoke-tested executable for every upstream release target: linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64. morphir-dotnet does not publish win-arm64 today. | Release workflow change (not yet proposed). |
| PC-5 | MEP host coverage | The MCK host suite passes for MEP `0.2.0-draft.1`: frontend `compileModel` (add-fsharp-compile), backend `generateModel`, and at least one upstream guest run through the .NET host. | add-fsharp-compile (frontend). Backend hosting is not yet proposed. |
| PC-6 | Output contracts | Stdout carries data only. `--json` writes one document. `--json-lines` writes one record per event and ends with one `completed` record. Exit codes are 0, 1, 2 and 130. All of this is verified by the MCK CLI suite. | add-fsharp-compile (STRM-2). establish-morphir-compatibility-kit (CLI suite). |
| PC-7 | Task-record interop | The .NET and Rust CLIs read each other's `.morphir/out` compile records in both directions (compile with one CLI, generate with the other), with locks respected. | add-fsharp-compile (compile-task-records). |
| PC-8 | Extension acquisition | Install, list, update and uninstall of MEP extensions follow the upstream distribution and acquisition design. | Not yet proposed. |
| PC-9 | Command parity | Every command in the core command set is Present. Every other command in [Command parity](#command-parity) has a planned increment, or is Intentionally different with the maintainers' agreement. Full parity is not required for the proposal. | The increment changes in [Delivery increments](#delivery-increments). |
| PC-10 | Release cadence | At least one tagged, signed release each month for three consecutive months, with a changelog. An upstream contract change is supported within one release. | Release process (existing release-manager workflow). |

## Governance and migration path

1. **Prove.** Deliver the changes above. Record the evidence for each criterion in this PRD, with links to the MCK reports.
2. **Report defects with evidence.** For each upstream defect the MCK finds, open an issue in `finos/morphir` with the failing case, the report and the expected behavior. morphir-dotnet implements the expected behavior and documents the difference. It never copies a defect, and it never diverges silently.
3. **Coexist.** Both CLIs ship. Both run the same MCK suites, and this PRD publishes both results. The two CLIs share `.morphir/out` records and configuration files.
4. **Propose.** When PC-1 to PC-10 pass for the core command set, present ADR 0001 and this PRD to the FINOS Morphir maintainers (see `MAINTAINERS.md`) for a decision.
5. **Switch.** After acceptance, the default install channels give the .NET `morphir` executable. The Rust CLI stays available during a published transition period. The CLI reference documents every intentional difference.

## Feature status tracking

| Feature | Status | Change | Evidence |
| --- | --- | --- | --- |
| Decision record | Proposed | ADR 0001 | Waiting for maintainer review |
| F# compile steel thread (first proof point) | Planned | add-fsharp-compile | — |
| MCK home and oracle | Planned | establish-morphir-compatibility-kit | — |
| Cold-start benchmark (PC-3) | Not started | — | — |
| win-arm64 release (PC-4) | Not started | — | — |
| Increment 3: core user commands (PC-9) | Not started | One change for each command family | `version`, `ir`/`migrate`, `validate`, `schema`, `[codegen]` targets |
| Increment 5: ecosystem acquisition (PC-5, PC-8) | Not started | One change for each | `extension`, `tool`, `dist`; MEP backend hosting |
| Later commands (PC-9 tracking) | Not started | One change each, by user need | `cache`, `diagnostics`, `eval`, `package`, `metadata`, `decoration`; `itest` into `mck` |

## Risks

| Risk | Mitigation |
| --- | --- |
| The maintainers do not accept the proposal. | The MCK and the conformance work still add value to every implementation. The coexistence state is stable. |
| Two CLIs named `morphir` confuse users and scripts during coexistence. | Install channels select one executable. `morphir --version` identifies the implementation. The CLI reference lists every difference. |
| The parity work is large: 13 commands are missing. | Do not deliver it as one set of work. Deliver small increments, each of one command family with its own MCK suite. The default proposal needs only the core command set. Mark commands as intentionally different only with maintainer agreement. |
| MEP `0.2.0-draft.1` changes in place. | Pin the contract, check it against upstream on a schedule, and keep protocol code in one project. |
| The oracle could follow the .NET behavior instead of independent expectations. | MCK cases are written independently of any implementation, with provenance. The .NET CLI is qualified by the same cases as the Rust CLI. |

## Open questions

1. Is the proposed core command set (`compile`, `generate`, `mck`, `config`, `version`, `ir`/`migrate`, `validate`, `schema`) the right one for the default proposal? Which MCK suites must pass for it?
2. During coexistence, what are the executable and package names (for example `morphir` and `dotnet-morphir`), and which install channels give each one?
3. Does the MCK case data live in morphir-dotnet, or in a separate FINOS repository that both CLIs consume?
4. Are `ui`, `desktop` and `playground` part of the default CLI, or do they become separate tools?
5. Are the targets for PC-2 and PC-3 (12 MB, 100 ms, 2× Rust) acceptable, or does the default need stricter targets?
