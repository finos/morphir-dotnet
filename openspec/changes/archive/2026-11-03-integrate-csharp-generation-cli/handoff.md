# Codex handoff: integrate-csharp-generation-cli

## Assignment and exact runtime

Use Codex gpt-6.1-sol with reasoningEffort high. The coordinator supplies an isolated orchestrator-created worktree and a main-based commit containing this planning change and the merged predecessors. This brief is self-contained; no parent conversation is required.

Implement only integrate-csharp-generation-cli through the openspec-apply-change workflow. Read AGENTS.md, proposal.md, design.html, specs/cli-csharp-generation/spec.md and tasks.md from this change, plus the durable coordination plan at docs/design/dotnet-csharp-generation/orchestration.md and orchestration.json. If this change has moved, use its recorded archive path rather than inventing another active change.

## Start gate

Read the stable archived [C# runtime API handoff](../2026-11-03-add-immutable-csharp-sdk/api-handoff.md#generated-record-and-variant-protocol). Generated records and variants must use the single public `IMorphirValue` / `MorphirValue.Hash` equality protocol, including boxing and nested SDK values. Do not invent a replacement interface. Runtime #470 landed at `55f73bdaa93b7bc55a48b184bab8e46a5636fd9e`; verify main inclusion when dispatched. This archival update does not dispatch generator or CLI implementation.

- Generator [PR #478](https://github.com/finos/morphir-dotnet/pull/478) squash landed at `a8ae8e456ac95875db1ee53ff4a21362315b467a`, submitted head `3db576da36a41abab960d59da130df4262486a38`. Reverify that exact merged PR and landed SHA on refreshed main at dispatch. Read the [archived generator handoff](../2026-11-03-add-csharp-code-generation/handoff.md), [main contract](../../../specs/morphir-csharp-transpilation/spec.md) and stable [public API/diagnostic guide](../../../../docs/content/docs/backends/csharp/diagnostics.md). Original-head ancestry is not squash landing proof. Archive landing and cleanup remain coordinator-owned; this update does not dispatch CLI work.
- Verify the coordinator-supplied base SHA, worktree ownership and clean status.
- Verify predecessor landing through the exact merged PR and its recorded landed SHA on refreshed main. Squash merging is preferred; original worker head ancestry is not required. Keep the submitted head and landed SHA distinct.
- Create/link the repository contribution issue and the feature PRD before production work; do not invent issue IDs. Coordinate the shared PRD index.
- If a predecessor contract is absent or contradictory, report the specific evidence to the coordinator and do independent read-only work while the dependency is corrected. Do not guess a private replacement interface.

## Scope and owned files

### Approved integration lease, 2026-11-03

Issue [#481](https://github.com/finos/morphir-dotnet/issues/481) tracks this
implementation. The coordinator approved additive core namespace work in
`src/Morphir.CodeGeneration.CSharp/{Types,TypeEmission,ValueEmission,Generator}.fs`,
new namespace cases and their test registration, the public diagnostics guide
and the active deterministic-naming delta. Preserve the existing options and
three-argument API. A validated immutable `NamespaceOverride` and overload use
one generation implementation with request-local naming. No runtime, historical
archive or direct main-spec edits are leased.

The lead also holds exact additive leases for the Tooling project reference,
static GenerationScenario registration, own PRD-index row, README CLI example,
NOTICE attribution and one Unreleased changelog bullet. Other shared paths
remain coordinator-owned. The lead is the sole Git/source writer.

Preserve generic reporting. JSON stdout is one report including structured
diagnostics; it has no plain-text logs. Human diagnostics and emitted log text
use stderr. JSON failures do not need duplicate stderr diagnostic text.

- Register exact target csharp through IGenerationBackend with immutable validated options and explicit model capabilities.
- Expose language-version=14 or 15 and optional namespace through existing repeated --option settings; default to 14.
- Reuse existing model loading, publication, JSON reports and exit codes without a second orchestration path.
- Add end-to-end compiled/executed examples, failure preservation, cancellation, and trimmed/Native AOT smoke coverage for the new target.

- `src/Morphir.Tooling/Features/Generate/CSharpGenerationBackend.cs`
- `src/Morphir.Tooling/Scenarios/GenerationScenario.cs`
- `src/Morphir.Tooling/Morphir.Tooling.csproj (new backend reference)`
- `tests/Morphir.Tooling.Tests/Features/Generate/** (C# scenarios)`
- `docs/content/docs/cli/generate.md`
- `docs/content/docs/cli/troubleshooting.md`
- `docs/content/docs/getting-started/** (C# examples)`
- Own this change's OpenSpec files and docs/content/contributing/design/prds/integrate-csharp-generation-cli.md.
- Root solution/package/build/CI files, global.json, the umbrella PRD and its index need serial coordinator integration. Supply exact proposed shared edits and verify their integrated result before checking associated tasks off.
- Preserve unrelated work, existing F# behavior except the explicitly scoped SDK corrections, and IR/JSON wire compatibility. Never edit another initiative to unblock yourself.

## Acceptance and evidence

Treat each scenario in this change's spec as an acceptance requirement and execute the task-group checks as work lands. Follow red/green/refactor, using public behavior and real compiled or packaged consumers. Record exact toolchain builds and commands, required-case coverage, justified snapshot changes, diagnostics and known limitations. Run the repository-required full test suite, coverage and formatting before contribution, in addition to targeted checks. Keep task/PRD progress current.

The SDK corpus is independently sourced. Do not derive expected observations from the implementation under test. Keep stable and RC compiler tests isolated from root global.json. The C# library's modern compiler-host smoke is not the deferred full source-generator adapter.

## Auxiliary delegation and resource slots

As the delivery lead, you may use up to three concurrent auxiliary agents in addition to yourself for useful, bounded, independent work. This is a ceiling, not a request to fill slots. Keep the existing maximum of two active initiative leads. Start conservatively with one auxiliary and expand only after checking CPU, memory and resource headroom.

Coordinate auxiliary slots with the root coordinator. The initial global budget is three active auxiliaries total across delivery threads, shared by both leads. It may increase only after an explicit resource assessment and a recorded root-coordinator decision consistent with the user's resource constraint. Your per-lead ceiling remains three.

Use T3 delegate_task with the exact Codex gpt-6.1-sol, reasoningEffort high target in orchestration.json, putting settings in target.options. Discover orchestrator capabilities once in this thread before its first delegation, with later discovery only under the host's error/provider-change rules. Do not silently substitute providers, models or effort. Give each auxiliary a bounded brief; no recursive fan-out or further delegation below the auxiliary layer is permitted.

Acquire the coordinator's delivery-wide expensive-command slot before a local restore, build, test, coverage, pack, publish or Native AOT job. Only one such job may run across the delivery at a time. Bound its internal parallelism using supported options; back off or serialize under sustained CPU or memory pressure. Lightweight reading and review need no expensive-command slot.

Read-only review agents may share a checkout. Writing agents need explicit disjoint file ownership or orchestrator-created isolated worktrees and serial integration. Never create child worktrees in a shell. You integrate auxiliary results, run integrated required checks, and own the single initiative PR. Report all child tasks, worktrees, helpers, integrated results and slot releases to the coordinator, who retains PR watching, landing, archive follow-up and final cleanup. Delegated children never watch PRs.

Follow delegate next instructions and event-driven completion without polling or sleep loops. Cancel no-longer-needed agents with task_cancel and release their slots. Cleanup evidence includes all auxiliary worktrees/helpers after integration, using the same clean-status, final submitted-head/landed-PR and squash-safe checks where applicable. An auxiliary without its own PR records its integrated commit and the initiative PR instead. Preserve uncommitted work and the shared coordinator checkout.

## Task-boundary commits and backup pushes

Create small, coherent Conventional Commits at meaningful task or task-group boundaries after the scoped work and its required checks are complete. Do not accumulate the entire initiative uncommitted. Include matching tests, necessary docs and accurate OpenSpec/PRD progress in each boundary commit. Neither a commit nor a checked box proves unverified acceptance.

Follow the repository-required pre-commit validation and hooks. Do not bypass checks, use --no-verify, or label failing/incomplete work complete for a checkpoint. If required checks are unavailable, preserve the work and report the concrete blocker. Stage only explicit owned paths; never blanket-stage unrelated work, secrets or transient build artifacts.

Push completed boundary commits promptly and normally to the initiative feature branch, without force, for off-machine durability. Do not wait for PR readiness to back up completed work. After every push to an existing PR, call link_pull_request with that full URL as the next tool call. Keep the final checked head SHA and check evidence current after each push.

Before a handoff, rebase/merge, context transition or cleanup, ensure completed work is committed and pushed; explicitly identify and preserve unfinished/uncommitted work. Give auxiliary writers disjoint ownership or orchestrator-created isolated worktrees. Isolated auxiliary results must be coherently committed before integration, then integrated and pushed into the initiative branch before auxiliary cleanup. Shared-checkout agents coordinate a single Git writer so one agent cannot stage or commit another's unfinished files.

Retain task-boundary commits on the feature branch during delivery. Multiple commits fit the preferred squash merge; the hosting service's final landed SHA on main remains distinct from the final submitted head SHA.

## PR and ownership handback

Create the initiative PR to main and link it through link_pull_request as the next call after opening it or pushing to it. Address implementation feedback assigned by the coordinator, use Conventional Commits without AI coauthor trailers, and preserve normal required CI/review rules. Do not enable auto-merge, delete branches or publish releases without the separately applicable explicit authorization.

Delegated children do not watch PRs. Return the PR URL, final submitted head SHA, check/review state, validation evidence, public contract changes, requested shared-file edits and exact lead and auxiliary task/worktree/helper ownership, integrated results and slot releases to the coordinator. The coordinator records the actual merge method and hosting service's landed SHA separately after landing. Finish the final line as: PR <full URL>, head <SHA>, checks and reviews <state>.

A PR-ready response is your handback milestone, not initiative completion. The coordinator must continue until this PR is merged into main, its merge is verified, specs/archive follow-up is landed and this worker's clean worktree is removed. Do not attempt to remove your active checkout. If work remains, label it explicitly; never call an open PR or retained worktree complete.

Squash-safe cleanup verifies the merged PR and its landed SHA on main, a clean worktree, and a worker HEAD matching the last submitted PR head with no extra local commits. Do not require git branch --merged or original commit ancestry to prove a squash landed.

## Windows execution

Use PowerShell, captured processes and hidden background helper windows. No Bash/WSL unless specifically required and authorized. A timeout must terminate the entire process tree and be verified before retrying. Do not run bd dolt push. Release only owned helpers and preserve uncommitted work for the coordinator's cleanup verification.

## Implementation handoff checkpoint

OpenSpec apply reports all nine implementation tasks complete. This is not
initiative completion: root still owns hosted review/check classification,
landing, exact landed-main verification, normal spec sync/archive and checkout
cleanup. The PRD stays In Progress through those delivery gates. The latest full
native test result is the known unrelated configuration failure documented in
[validation](validation.md), not an all-green claim.

The implementation uses one static CSharpGenerationBackend through the existing
resolver and GenerationScenario. Accepted options are immutable numeric 14/15,
default 14, and an optional validated literal namespace; repeated keys retain
last occurrence. Capabilities are classic Library 1/2/3/3.0.0/3.1.0 and v4
Library 4/4.0.0, with no supported extra required semantics. Both typed families
stay intact. Artifact paths and bytes match direct core generation. JSON rendering,
loading and owned publication use existing generic behavior. Sources remain
immutable C# SDK consumers using IMorphirValue/MorphirValue.Hash.

The additive NamespaceOverride API and four-argument Generate overload retain
existing constructors and the three-argument API. Default output is unchanged.
Two override requests plus default and concurrent calls observe isolated naming.
Namespace options affect the input package only, including qualified type,
value and constructor references; SDK/System names and source paths stay fixed.

Fresh acceptance includes 24 independent pinned consumers, actual trimmed and
NativeAOT generation without compiler PATH, separate business compilation,
representative native semantic refusals and captured CLI 0/1/2/130 contracts.
The owned test publication gate tests caller-token cancellation, not OS signals.
Generation never invokes a compiler; production Roslyn and the full compiler
source-generator adapter remain excluded.

Initial core/shared/five-lockfile leases were released at f0d3ee5. The later
TypeEmission/ValueLowering, NamespaceCases and diagnostics-page correction leases
are held until the next verified normal commit/push and then explicitly released
to root. No other shared/core/lock scope is reopened. No auxiliary task/thread,
helper agent or extra worktree exists. The sole expensive-command lease remains
with the worker until command/process inventory and clean submission handoff.
No delegated PR watch or cleanup is performed.

Owned evidence root is C:/Temp/morphir-csharp-cli-79f2b566-20261103. Root cleanup
must retain its logs, failed attempts, local packages, consumers, published
executables and helpers until review is complete. Absolute file/directory and
command-result inventories are written there. Checkout build outputs include
bin/obj, .nuke/temp, test results and artifacts/packages. Their pre-existing
ignored-output absence was not established. Read-only root-owned dependencies
are C:/Temp/morphir-csharp-generator-1d28e9ab-20261103, its isolated RC2 toolchain,
and C:/Temp/morphir-csharp-archive-650c2d8f-20261103. Do not modify or delete them.
Cleanup of older/locked worktrees remains solely root's existing responsibility.

The validation record preserves the 2.86-second heavy overlap, the unverified
VBCSCompiler 5656/7888 ownership at Nuke's global stop, empty initial coverage
profiler result, wrong union filter, configuration failures, warning-as-error
publish refusal, genuine native reflection failure and post-pack output mismatch.
No clean-resource or all-green claim erases those limitations.

## Landed implementation and archived specification, 2026-11-03

The implementation checkpoint above is historical. [PR #482](https://github.com/finos/morphir-dotnet/pull/482)
subsequently squash landed on main at `0542145a4bcc0edf9c9dd2fb3122df36c73e80d0`,
distinct from submitted head `0e1e86d5e850adb70ec8027e324ba2234a22bdce`.
Both trees are `284eb3240006ec9a8a34ba539caceeb65be0844f`.
All eight final-head hosted checks passed, including three OS jobs. Automated
review completed without findings, with no unresolved threads. No developer
approval is claimed. Root used standing user authorization for admin bypass.

All nine tasks are checked. Both deltas are synchronized into the main
[CLI specification](../../../specs/cli-csharp-generation/spec.md) and
[generator specification](../../../specs/morphir-csharp-transpilation/spec.md).
This dated archive preserves the implementation validation and historical lease
records. Current landing, archive-worker validation and cleanup gates are in the
[delivery record](../../../../docs/design/dotnet-csharp-generation/orchestration.md#cli-landing-and-specification-archive-2026-11-03).
The archive follow-up must land and root must verify cleanup before initiative
completion. Source checkout, evidence, caches and older worktrees remain root-owned.
