# Adopt the generic guest kit

Status: In Progress. Task 2.1 provenance complete with verified RED and combined 15-test GREEN. Later runner work awaits its next allocation.

The approved parent [finos/morphir#1154](https://github.com/finos/morphir/pull/1154), merged at `9e7d90e2fb5708580baef7cdd323cd994030c237`, defines the one-session generic profile. This distinct .NET [OpenSpec change](../../../../../openspec/changes/adopt-generic-guest-kit/proposal.md) delivers its tasks 2.1 to 2.4 through a dedicated development `morphir-mck` executable. Local tracking uses [#563](https://github.com/finos/morphir-dotnet/issues/563), with runtime dependency [#570](https://github.com/finos/morphir-dotnet/pull/570).

## Feature status tracking

| Feature | Status | Acceptance |
| --- | --- | --- |
| Local plan, requirements and ownership | Approved | #595 merged normally at 61c214afa155ff883471dac18932d86241a7d9c6 after exact d87bb997 review |
| Pin and independent expected data | Implemented | RED verified; exact adoption import; original seven and new eight provenance tests all pass; frozen bytes unchanged |
| Selection and one-process plan | Planned | 21 cases for two IR versions, 16 for one; no legacy offer or legacy flag gate |
| Five moved lifecycle cases | Planned | Exact requests/order/comparisons and actual guest observations |
| Generic report and report-check | Planned | Shared plan/comparisons; rejects edited, missing, legacy and unrun claims |
| Dedicated development CLI | Planned | Crafted and generic runners share Conformance; product forwards before parsing |
| Two-worker Python 0.6.0 evidence | Planned | Same 21 cases, exact module/host/kit digests, both reports recheck |
| Parent host qualification | External dependency | Root-owned task 1.4 verdict/schema/fixed data; remains open |

## Implementation notes

### Planning baseline, 11 November 2026

- Checkout starts at normal #570 commit `1e244a7c`; its refreshed normal commit is not available yet. Preserve history and do not copy the Extism owner's uncommitted work.
- Existing `GuestKit/ModelTier.fs` opens separate describe and model sessions and appends legacy not-run cases. The E2E donor records 16 model cases, which do not satisfy the changed profile.
- Existing `WasmHostCommands` already provides crafted run/check/usage boundaries. The dev executable will adapt preserved Tooling parser/test donors without product interfaces or registration. Extism owns removal of dead product registration and Tooling-to-Conformance dependency. Its temporary Tooling.Tests source link stays until destination tests compile. BothHostsHaveTheExperimentalCommands and all four ExecutableMckWasmHost.feature scenarios need explicit migration with no skips or deleted coverage.
- Offering scripts need coordinated runner/host separation at scripts/wasm-offerings.ps1 lines 130/247/255 and the matching workflow input. The tested product becomes only --host; no script/workflow edit or execution is part of planning.
- The exact [ownership map and test design](../../../../../openspec/changes/adopt-generic-guest-kit/design.html#ownership) names shared project hunks and reserved paths. No general Conformance Types/Runner/CliAdapter or WasmHost edits are planned. Build/Build.cs packaging remains held.
- The fixed kit digest stays `f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae`. Historical expected IR originated from Python 0.4.2; preserve that fact and independently review fixed data rather than generate expectations from the implementation.
- Product #590 forwarding remains unchanged. Parent Rust IR/package gates remain unchanged. No public package/offering/version/release change or CET/Spectre weakening belongs to this delivery.

### Task 2.1 provenance RED and source import, 11 November 2026

- The granted RED burst used HEAD 61c214af and test source SHA-256 `5feeec77a891983f9f85320bbb231da6dfd43ab86c636b5da63e01de014a2f6d`. One locked targeted restore passed in 2.96 seconds and one fresh Release build passed in 39.39 seconds with zero errors and four existing dependency TFM-support warnings. The new Expecto list executed eight tests: one unchanged-pin control passed, seven missing adoption authority/input assertions failed, none ignored or errored. No setup/compiler failure or stale binary was accepted as RED.
- Captured RED evidence remains at `.dev/out/generic-provenance-red/`: `restore`, `build` and `red` stdout/stderr/result files, lock snapshots and `release.json`. Expecto wrote the ANSI-coloured summary to `red.stdout.log`, 9,422 bytes; stderr was empty. All 129 lockfiles stayed unchanged and the final owned/global process inventory was empty. CPU2 was released before this source import.
- Imported only the four raw Git objects from approved parent 9e7d90e2 into `tests/Fixtures/MepGuestKit/adoption/`, with separate source-path/commit/hash provenance and inherited LICENSE/NOTICE links. No uncommitted parent source was copied. All six baseline files, including the original five-row manifest, remain byte-identical and retain their original authority. The fixed kit JSON and contractVersion are unchanged.
- The original inventory test now excludes only the explicit `adoption/` authority boundary. It still detects every unknown baseline file elsewhere. The new group's exact-four-input-plus-provenance guard, fixed digest checks and source mapping remain unchanged. No test is removed or skipped. This separation prevents the later contract from being falsely attributed to the older pin.
- Reading the fixed package and expected IR confirms the small Bool library remains the same reviewed input: module main in mck/generic, with ready returning true and blocked returning false. No expectation was generated or edited from a runner output. Broader comparison mutation controls remain task 2.3; these provenance checks do not establish host or generic-run qualification.
- The separate GREEN grant ran one fresh Release --no-restore build in 7.20 seconds, then only the original seven-test list and new eight-test list. All 15 passed, with zero ignored, failed or errored. The fresh DLL SHA-256 was `f1d64c5f062550f21984fbf7ccf04d5ec80fae8b9ab02913b8722c6045c44936`, emitted after build start. Test source stayed `c374f7996f7f581629cb47a7ebf59ef524a02a51d3f8cb7f153edea02a0519ab`; adoption provenance stayed `665742024ad744f5b875315172aa4c029e2c58bed98189277b16bcfdcda4b641`. All 129 lockfiles and six frozen baseline files remained unchanged. GREEN logs, source/import identity records and `release.json` are separate under `.dev/out/generic-provenance-green/`; CPU2 was released with an empty owned/global process inventory.
- The coordinator explicitly authorized a focused intermediate local commit after these 15 checks, with an exception only to the broader pre-commit sequencing. Full suite, coverage, full formatting, changelog validation and the repository Precommit target have not run in this allocation. Final CI, security, independent review and formatting gates remain open. Actual core.hooksPath is unset and the resolved pre-commit, prepare-commit-msg, commit-msg and post-commit paths are absent, so a normal commit needs no hook bypass. Only documentation/task status changed after the tested build.

## Dependencies and blockers

- Planning gate passed at 61c214afa155ff883471dac18932d86241a7d9c6. This checkout fast-forwarded from d87bb997 without rewritten history. The merge parents are clean runtime donor 18701751b7d5a07b689aa331c29dbe3106563e34 and reviewed planning head d87bb997f4c140625112711520f01acc13a35e12; tree 561c66c4c587e991c19956fe954a89eb7cf6dc05. Parent verified no findings on the exact head; planning lint/OpenSpec/CLA/Ubuntu passed. Optional Windows/macOS checks were pending at merge, and the feature branch had no required checks/protection. This is no final runtime CI or qualification waiver.
- The runtime donor preserves normal main merge 2bc1f86e7e9932b6f0b92cc5d1211d59f84d19a8, source integration b79aacd3f638365c63b2268d76ee2154df052df8 and normal #592 merge 18701751. Static inspection confirms product MCK sources remain excluded from compilation, no Tooling-to-Conformance reference, and early #590 forwarding. No uncommitted donor code was copied.
- The bounded GREEN grant is complete and CPU2 has been released. No further build, test, restore, formatter or native command is authorized by that grant. No full-suite, coverage, final formatting or qualification result follows from the focused provenance checks.
- The existing process edge needs proof that notification silence and post-shutdown responses are observed from the guest. Local suppression or lifecycle enforcement cannot substitute for real evidence.
- The held generated-file branch at `34fcc6dc` has unresolved native-process defects and an uncompiled watchdog fix. It is not an approved helper, and this change must not copy it or add a duplicate process edge.
- Crafted HostReport is observations-only and refuses a verdict. Parent task 1.4 and documentation/Smithy tasks 1.6/1.7 remain root-owned. Generic results do not qualify a host, RID, package or release.

## Next action

Return the cohesive local task 2.1 commit and clean status for review, without push or a new PR. Program.fs, project files and production runner code remain unchanged. Wait for a separate allocation before task 2.2 selection tests or later lifecycle/report/development executable work.

## Planning validation

At planning time, strict validation and `mise run openspec:verify` passed, covering the bundle, schema and all 39 then-current changes/specs. A source comparison confirmed parent TIER-1, LIFE-1, SEL-1, KIT-1 and RUN-1 blocks, including the case table, were retained. No .NET command ran in that planning phase; the dotnet-husky pre-commit hook was bypassed only for the planning commits under their CPU restriction. The later scoped RED execution is recorded separately above; no implementation commit or push has occurred.
