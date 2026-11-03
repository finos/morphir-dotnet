# Codex handoff: integrate-csharp-generation-cli

## Assignment and exact runtime

Use Codex gpt-6.1-sol with reasoningEffort high. The coordinator supplies an isolated orchestrator-created worktree and a main-based commit containing this planning change and the merged predecessors. This brief is self-contained; no parent conversation is required.

Implement only integrate-csharp-generation-cli through the openspec-apply-change workflow. Read AGENTS.md, proposal.md, design.html, specs/cli-csharp-generation/spec.md and tasks.md from this change, plus the durable coordination plan at docs/design/dotnet-csharp-generation/orchestration.md and orchestration.json. If this change has moved, use its recorded archive path rather than inventing another active change.

## Start gate

- Verify add-csharp-code-generation's PR is merged into main and its merge SHA is in this checkout; read its frozen contracts. An open predecessor PR or completed artifact status is insufficient.
- Verify the coordinator-supplied base SHA, worktree ownership and clean status.
- Verify predecessor landing through the exact merged PR and its recorded landed SHA on refreshed main. Squash merging is preferred; original worker head ancestry is not required. Keep the submitted head and landed SHA distinct.
- Create/link the repository contribution issue and the feature PRD before production work; do not invent issue IDs. Coordinate the shared PRD index.
- If a predecessor contract is absent or contradictory, report the specific evidence to the coordinator and do independent read-only work while the dependency is corrected. Do not guess a private replacement interface.

## Scope and owned files

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
