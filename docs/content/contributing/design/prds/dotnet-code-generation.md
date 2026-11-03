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
| [add-immutable-csharp-sdk](../../../../../openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/proposal.md) | Implementation and archive #472 landed; source worktree/artifacts removed | C# SDK lead, retired by coordinator | Conformance initiative | Immutable public SDK API |
| [introduce-fsharp-sdk-package](../../../../../openspec/changes/archive/2026-11-03-introduce-fsharp-sdk-package/proposal.md) | Implementation and archive #472 landed; source worktree/artifacts removed | F# SDK lead, retired by coordinator | Conformance initiative | Package compatibility and covered corrections |
| [add-csharp-code-generation](../../../../../openspec/changes/archive/2026-11-03-add-csharp-code-generation/proposal.md) | Implementation #478 landed; archive prepared, follow-up landing and cleanup pending | C# generation lead, `t3/add-csharp-code-generation`; coordinator retains cleanup | C# SDK initiative landed | Typed generation/options/diagnostics |
| [integrate-csharp-generation-cli](../../../../../openspec/changes/archive/2026-11-03-integrate-csharp-generation-cli/proposal.md) | Implementation #482 landed; specifications archived here, archive landing and cleanup pending | CLI lead retired; archive worker, root owns cleanup | C# generation #478 and archive #480 landed | Existing CLI publication contract plus source-compatible namespace override |

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
by the coordinator. The generator source worker is stopped and root-owned.
The archive worker has the sole bounded validation lease and a narrow status/link
edit lease for this follow-up. No auxiliary has been launched. Leases are
released at handoff; the coordinator owns landing and cleanup.

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
separates runtime acceptance from the original conformance validation. Combined
spec/archive PR #472 landed at `618fbf7b6c5b542122450bf61f4f794e590fe4f5`.
The coordinator removed both source SDK worktrees and their owned evidence and
toolchains, and retired their threads. Shared archive cleanup remains incomplete.
Empty `t3-1e3c6fbe` is Windows permission-denied; prior empty `t3-eff883da` was
blocked by automatic approval policy and is not retried. The generator does not
own either directory or the root bootstrap checkout.

Generator [#474](https://github.com/finos/morphir-dotnet/issues/474) now implements
the existing initiative from the verified archive base. At function checkpoint
`a4cc96a`, the packaged .NET 10 host passes 92 tests; the shared classic/v4
type/function corpus compiles and executes under both pinned numeric 14/15
compilers. The tested adapter host is .NET 10.0.14/SDK 10.0.403 Windows arm64.
At that checkpoint, ordered patterns, record operations, broader SDK mapping and
mid-traversal cancellation remained pending. CLI integration is still gated and
undispatched. Historical counts above describe their original checked revisions.

At pattern/record checkpoint `8dca9b6`, the packaged .NET 10 host passes 126/126
tests. All four classic/v4 x pinned numeric 14/15 type/function/pattern/record
consumers compile and execute. The tested adapter host remains
.NET 10.0.14/SDK 10.0.403/Windows arm64; other compiler and IDE hosts remain
untested. At that checkpoint, SDK mapping, mid-traversal cancellation and business-fixture acceptance
remained pending. CLI integration remains gated and undispatched.

At cancellation checkpoint `a2b67c9`, the packaged .NET 10 host passes 143/143;
active traversal/emission cancellation returns no sources. Subsequent fixture
evidence, recorded in the generator change's validation record, passes 153 focused
tests and 18 actual pinned-compiler executions. These include original classic
car-rental 1/2/3 and daily-rate inputs, direct v4 daily-rate, a labeled synthesized
v4 car input, and a separately labeled compatible books-and-records type slice.
Whole ecosystem originals with missing types or draft bodies still fail with
located diagnostics and no partial output. A fresh typed package consumer passes
self-contained win-arm64 link trimming with identical observations/source hashes
and no IL warnings. Tested package/adapter host remains .NET 10.0.14/SDK
10.0.403/Windows arm64; other IDE hosts and generator Native AOT remain untested. Final
delivery checks are in progress. CLI integration remains gated and undispatched.

The paragraphs above retain their historical checkpoint counts. Generator
[PR #478](https://github.com/finos/morphir-dotnet/pull/478) subsequently squash
landed at `a8ae8e456ac95875db1ee53ff4a21362315b467a`, submitted head
`3db576da36a41abab960d59da130df4262486a38`, with identical tree
`b65fea03980e445e094c38b0e9f6bfe8888cc948` and final-head hosted checks green.
The [generator PRD](add-csharp-code-generation.md#landing-and-archive-follow-up-2026-11-03)
records the separate local configuration rollback failures, final generator
coverage and approval limits. Its eight-requirement, 19-scenario main spec and
13/13-task archive are prepared here. This follow-up must land and coordinator
cleanup must finish before initiative completion. CLI remains undispatched.

The undispatched CLI statements above describe earlier generator checkpoints.
CLI implementation [#482](https://github.com/finos/morphir-dotnet/pull/482) is now
squash landed at `0542145a4bcc0edf9c9dd2fb3122df36c73e80d0`, distinct from its
submitted head `0e1e86d5e850adb70ec8027e324ba2234a22bdce`. All eight final-head
hosted checks passed. Its six-requirement/12-scenario CLI specification and three
additional generator namespace scenarios are synchronized in this dated archive
follow-up. The [CLI delivery record](../../../../design/dotnet-csharp-generation/orchestration.md#cli-landing-and-specification-archive-2026-11-03)
preserves the local configuration failure, warning/coverage limits and process
incidents. Archive PR landing and all cleanup remain root-owned pending gates.
