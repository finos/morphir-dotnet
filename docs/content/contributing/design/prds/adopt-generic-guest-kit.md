# Adopt the generic guest kit

Status: Draft. Planning review pending; production implementation has not started.

The approved parent [finos/morphir#1154](https://github.com/finos/morphir/pull/1154), merged at `9e7d90e2fb5708580baef7cdd323cd994030c237`, defines the one-session generic profile. This distinct .NET [OpenSpec change](../../../../../openspec/changes/adopt-generic-guest-kit/proposal.md) delivers its tasks 2.1 to 2.4 through a dedicated development `morphir-mck` executable. Local tracking uses [#563](https://github.com/finos/morphir-dotnet/issues/563), with runtime dependency [#570](https://github.com/finos/morphir-dotnet/pull/570).

## Feature status tracking

| Feature | Status | Acceptance |
| --- | --- | --- |
| Local plan, requirements and ownership | Draft | Independent/parent exact-head review, strict validation and planning merge |
| Pin and independent expected data | Planned | Frozen JSON digest unchanged; separately attributed parent adoption and review |
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
- Existing `WasmHostCommands` already provides crafted run/check/usage boundaries. The dev executable will adapt preserved Tooling parser/test donors without product interfaces or registration. Extism owns removal of dead product registration and Tooling-to-Conformance dependency.
- The exact [ownership map and test design](../../../../../openspec/changes/adopt-generic-guest-kit/design.html#ownership) names shared project hunks and reserved paths. No general Conformance Types/Runner/CliAdapter or WasmHost edits are planned. Build/Build.cs packaging remains held.
- The fixed kit digest stays `f16f2a4c577b507527a77a19a383c2c0083b4db1db131cd06d170e65a88598ae`. Historical expected IR originated from Python 0.4.2; preserve that fact and independently review fixed data rather than generate expectations from the implementation.
- Product #590 forwarding remains unchanged. Parent Rust IR/package gates remain unchanged. No public package/offering/version/release change or CET/Spectre weakening belongs to this delivery.

## Dependencies and blockers

- Independent plan review is queued after parent #1157 review; implementation waits for this planning merge.
- CPU2 is reserved for Extism's initial bounded burst. No .NET build, restore, test, formatter or installation has run in this thread. Request a scoped RED grant after planning readiness.
- The existing process edge needs proof that notification silence and post-shutdown responses are observed from the guest. Local suppression or lifecycle enforcement cannot substitute for real evidence.
- The held generated-file branch at `34fcc6dc` has unresolved native-process defects and an uncompiled watchdog fix. It is not an approved helper, and this change must not copy it or add a duplicate process edge.
- Crafted HostReport is observations-only and refuses a verdict. Parent task 1.4 and documentation/Smithy tasks 1.6/1.7 remain root-owned. Generic results do not qualify a host, RID, package or release.

## Next action

Review and merge the planning tranche; receive the committed #570 refresh; agree project hunks; request one bounded RED test grant. The [task list](../../../../../openspec/changes/adopt-generic-guest-kit/tasks.md) records the subsequent implementation and evidence sequence.

## Planning validation

Strict validation of this change and `mise run openspec:verify` pass. The latter checks the bundle, schema and all 39 current changes/specs. A source comparison confirms parent TIER-1, LIFE-1, SEL-1, KIT-1 and RUN-1 decision blocks, including the case table, remain unchanged. The fixed kit SHA-256 matches its recorded digest. No .NET command or production test has run; the pre-commit hook invokes dotnet husky and is bypassed only for this planning commit under the CPU restriction.
