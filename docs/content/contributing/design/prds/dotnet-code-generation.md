# .NET SDKs and C# generation

Status: In Progress

Deliver five initiatives through explicit OpenSpec apply workflows. Each has one
implementation lead and one focused PR. A delivery finishes after its PR and
spec-sync/archive follow-up land on main and the coordinator removes its clean
owned worktrees. The coordinator retains the shared bootstrap checkout.

## Feature status tracking

| Initiative | Status | Implementation owner | Predecessor on main | Contract |
| --- | --- | --- | --- | --- |
| [establish-dotnet-sdk-conformance](../../../../../openspec/changes/archive/2026-11-03-establish-dotnet-sdk-conformance/proposal.md) | Implementation and archive #463 landed; cleanup pending | Conformance lead, `t3/dotnet-sdk-conformance` | Planning PR #459, landed `890aacb61df601f2876112a2cdd4d9d6f5757ca6` | Inventory, profiles, schema and evidence |
| [add-immutable-csharp-sdk](../../../../../openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/proposal.md) | Implementation landed; archive follow-up/cleanup pending | C# SDK lead, dispatched by coordinator | Conformance initiative | Immutable public SDK API |
| [introduce-fsharp-sdk-package](../../../../../openspec/changes/archive/2026-11-03-introduce-fsharp-sdk-package/proposal.md) | Implementation landed; archive follow-up/cleanup pending | F# SDK lead, dispatched by coordinator | Conformance initiative | Package compatibility and covered corrections |
| [add-csharp-code-generation](../../../../../openspec/changes/add-csharp-code-generation/proposal.md) | Planned | C# generation lead, dispatched by coordinator | C# SDK initiative | Typed generation/options/diagnostics |
| [integrate-csharp-generation-cli](../../../../../openspec/changes/integrate-csharp-generation-cli/proposal.md) | Planned | CLI lead, dispatched by coordinator | C# generation initiative | Existing CLI publication contract |

The full Roslyn source-generator adapter remains deferred. C# generation's
compiler-host smoke does not constitute that adapter.

## Coordination

[orchestration.md](../../../../../docs/design/dotnet-csharp-generation/orchestration.md)
and [orchestration.json](../../../../../docs/design/dotnet-csharp-generation/orchestration.json)
define ownership, dispatch, dependencies, squash-aware verification and cleanup.
Successors start from main containing their verified predecessor's landed SHA.
No successor may guess a missing contract or use an unmerged sibling checkout.

The static plan specifies Codex `gpt-6.1-sol` with `reasoningEffort: high` and
one delivery-wide expensive-command slot. Actual resource leases are supplied
by the coordinator. This bounded archive follow-up holds that slot and has no
authorized auxiliaries. The implementation lead released its slots and stopped
its helpers at handoff.

## Implementation notes

### Conformance start, 2026-11-02

[Issue #460](https://github.com/finos/morphir-dotnet/issues/460) tracks the first
implementation. The lead verified planning PR #459 merged into main and its
landed SHA is reachable from refreshed `origin/main`. This lead has an exclusive
lease to register its new test project in `Morphir.slnx` and maintain this PRD and
the conformance index entry. Other initiatives remain untouched.

## Delivery evidence

The conformance [readiness manifest](../../../../../tests/Fixtures/MorphirSDK/readiness.json)
pins submitted contract artifact commit `737e85b3a202ba733b153be637e33cf953342705`.
It names 67 operations, 207 required cases, eight representation exclusions and
four required F# gaps. Local Nuke Test passed 2,752 suite tests and 15 package
lifecycle checks; PackAll and all 40 TestBuild checks passed. The offline
conformance suite passes 27 tests with 93.42% measured validator line coverage.
The C# and F# runtime leads can use the verified conformance landing after
checking the frozen contract on refreshed main. Generation and CLI work remain
gated on their own predecessor merges. All 207 required IDs must execute and
pass in each runtime, including the four named F# gaps.

The [conformance delivery record](../../../../../docs/design/dotnet-csharp-generation/conformance-delivery.md)
records #462, submitted and landed SHAs, the explicit admin-bypass authorization,
missing developer approval and cleanup evidence. Conformance implementation and
its archive follow-up #463 are on main. Coordinator cleanup remains pending,
including the policy-blocked empty-directory removal; this worker does not retry it.

Both SDK runtime PRs are now landed. Their
[delivery record](../../../../design/dotnet-csharp-generation/sdk-runtime-delivery.md)
separates runtime acceptance from the original conformance validation. This
follow-up prepares their specs/archive; landing and coordinator cleanup remain
pending. Generator and CLI work remain undispatched.
