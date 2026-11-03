# .NET SDKs and C# generation

Status: In Progress

Deliver five initiatives through explicit OpenSpec apply workflows. Each has one
implementation lead and one focused PR. A delivery finishes after its PR and
spec-sync/archive follow-up land on main and the coordinator removes its clean
owned worktrees. The coordinator retains the shared bootstrap checkout.

## Feature status tracking

| Initiative | Status | Implementation owner | Predecessor on main | Contract |
| --- | --- | --- | --- | --- |
| [establish-dotnet-sdk-conformance](../../../../../openspec/changes/establish-dotnet-sdk-conformance/proposal.md) | In progress | Conformance lead, `t3/dotnet-sdk-conformance` | Planning PR #459, landed `890aacb61df601f2876112a2cdd4d9d6f5757ca6` | Inventory, profiles, schema and evidence |
| [add-immutable-csharp-sdk](../../../../../openspec/changes/add-immutable-csharp-sdk/proposal.md) | Planned | C# SDK lead, dispatched by coordinator | Conformance initiative | Immutable public SDK API |
| [introduce-fsharp-sdk-package](../../../../../openspec/changes/introduce-fsharp-sdk-package/proposal.md) | Planned | F# SDK lead, dispatched by coordinator | Conformance initiative | Package compatibility and covered corrections |
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

All leads use Codex `gpt-6.1-sol` with `reasoningEffort: high`. There is one
delivery-wide expensive-command slot. The conformance lead holds it exclusively
until handoff. At most three auxiliaries are available to this sole active lead;
start with one and assess resource headroom before expansion. Auxiliaries cannot
delegate. The lead is the sole Git writer in any shared checkout.

## Implementation notes

### Conformance start, 2026-11-02

[Issue #460](https://github.com/finos/morphir-dotnet/issues/460) tracks the first
implementation. The lead verified planning PR #459 merged into main and its
landed SHA is reachable from refreshed `origin/main`. This lead has an exclusive
lease to register its new test project in `Morphir.slnx` and maintain this PRD and
the conformance index entry. Other initiatives remain untouched.

## Delivery evidence

The coordinator records PR URLs, final submitted heads, distinct landed SHAs,
archive paths and cleanup evidence after each milestone. The conformance PRD
records local validation. No initiative is merged, archived or cleaned yet.
