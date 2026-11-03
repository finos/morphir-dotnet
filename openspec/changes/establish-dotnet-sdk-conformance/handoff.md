# Codex handoff: establish-dotnet-sdk-conformance

## Assignment and exact runtime

Use Codex gpt-6.1-sol with reasoningEffort high. The coordinator supplies an isolated orchestrator-created worktree and a main-based commit containing this planning change and the merged predecessors. This brief is self-contained; no parent conversation is required.

Implement only establish-dotnet-sdk-conformance through the openspec-apply-change workflow. Read AGENTS.md, proposal.md, design.html, specs/dotnet-sdk-conformance/spec.md and tasks.md from this change, plus the durable coordination plan at docs/design/dotnet-csharp-generation/orchestration.md and orchestration.json. If this change has moved, use its recorded archive path rather than inventing another active change.

## Start gate

- Verify the planning artifacts are on main. This is the first implementation initiative.
- Verify the coordinator-supplied base SHA, worktree ownership and clean status.
- Verify predecessor landing through the exact merged PR and its recorded landed SHA on refreshed main. Squash merging is preferred; original worker head ancestry is not required. Keep the submitted head and landed SHA distinct.
- Create/link the repository contribution issue and the feature PRD before production work; do not invent issue IDs. Coordinate the shared PRD index.
- If a predecessor contract is absent or contradictory, report the specific evidence to the coordinator and do independent read-only work while the dependency is corrected. Do not guess a private replacement interface.

## Scope and owned files

- Curate a bounded, provenance-pinned SDK inventory and language-neutral cases from Morphir Python, MoonBit, and morphir-elm vnext.
- Define tagged expected values, explicit .NET representability limits, strict coverage classifications, and a documented extension process.
- Record existing F# mismatches as named gaps without weakening downstream runtime acceptance.
- Publish the dependency graph, ownership rules and self-contained Codex handoffs for the five changes.

- `tests/Fixtures/MorphirSDK/**`
- `tests/Morphir.SDK.Conformance.Tests/**`
- `docs/design/dotnet-sdk-contract.md`
- Own this change's OpenSpec files and docs/content/contributing/design/prds/establish-dotnet-sdk-conformance.md.
- Root solution/package/build/CI files, global.json, the umbrella PRD and its index need serial coordinator integration. Supply exact proposed shared edits and verify their integrated result before checking associated tasks off.
- Preserve unrelated work, existing F# behavior except the explicitly scoped SDK corrections, and IR/JSON wire compatibility. Never edit another initiative to unblock yourself.

## Acceptance and evidence

Treat each scenario in this change's spec as an acceptance requirement and execute the task-group checks as work lands. Follow red/green/refactor, using public behavior and real compiled or packaged consumers. Record exact toolchain builds and commands, required-case coverage, justified snapshot changes, diagnostics and known limitations. Run the repository-required full test suite, coverage and formatting before contribution, in addition to targeted checks. Keep task/PRD progress current.

The SDK corpus is independently sourced. Do not derive expected observations from the implementation under test. Keep stable and RC compiler tests isolated from root global.json. The C# library's modern compiler-host smoke is not the deferred full source-generator adapter.

## PR and ownership handback

Create the initiative PR to main and link it through link_pull_request as the next call after opening it or pushing to it. Address implementation feedback assigned by the coordinator, use Conventional Commits without AI coauthor trailers, and preserve normal required CI/review rules. Do not enable auto-merge, delete branches or publish releases without the separately applicable explicit authorization.

Do not spawn additional agents. Delegated children do not watch PRs. Return the PR URL, final submitted head SHA, check/review state, validation evidence, public contract changes, requested shared-file edits and exact worker worktree/helper ownership to the coordinator. The coordinator records the actual merge method and hosting service's landed SHA separately after landing. Finish the final line as: PR <full URL>, head <SHA>, checks and reviews <state>.

A PR-ready response is your handback milestone, not initiative completion. The coordinator must continue until this PR is merged into main, its merge is verified, specs/archive follow-up is landed and this worker's clean worktree is removed. Do not attempt to remove your active checkout. If work remains, label it explicitly; never call an open PR or retained worktree complete.

Squash-safe cleanup verifies the merged PR and its landed SHA on main, a clean worktree, and a worker HEAD matching the last submitted PR head with no extra local commits. Do not require git branch --merged or original commit ancestry to prove a squash landed.

## Windows execution

Use PowerShell, captured processes and hidden background helper windows. No Bash/WSL unless specifically required and authorized. A timeout must terminate the entire process tree and be verified before retrying. Do not run bd dolt push. Release only owned helpers and preserve uncommitted work for the coordinator's cleanup verification.
