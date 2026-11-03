# Tasks

Read [design](design.html), [spec](specs/cli-csharp-generation/spec.md) and [handoff](handoff.md). Implementation starts only on a main-based checkout containing the planning and predecessor merge commits. The initiative is not complete until its PR is merged into main and its clean worker worktree is removed.

## 1. Provider registration and options

- [ ] 1.1 Confirm the backend PR is merged into main and read its frozen request/result contract; create the CLI integration PRD and add failing real-provider registration/option cases.
- [ ] 1.2 Implement the C# provider and static registration using the existing resolver; verify exact capabilities, unknown/ambiguous targets and unsupported required semantics without special-casing C# in the resolver.
- [ ] 1.3 Implement language-version=14 or 15 and optional namespace validation by reusing the core, with docs/examples in the same group; verify defaults, repeated options, wrong JSON kinds, injection-like names and unsupported aliases.

## 2. Model/artifact and publication parity

- [ ] 2.1 Adapt typed sources/diagnostics to the existing artifact tree, then exercise JSON/Ion text/binary/tree forms with one behavior corpus; verify direct-library/provider source parity and execute generated pricing results under both profiles.
- [ ] 2.2 Extend existing publisher scenarios to real C# output, including unchanged reruns, removed owned modules, unowned files, unsafe paths and input/output overlap; verify failed runs preserve previous output.
- [ ] 2.3 Add real CLI JSON/exit/cancellation scenarios and troubleshooting examples; verify one parseable stdout report, stderr diagnostics and correct 0/1/2/130 outcomes without a compiler dependency during generation.

## 3. Published command and integration

- [ ] 3.1 Publish and execute trimmed and Native AOT CLI smoke cases using the real C# provider, then compile the generated sources separately; verify report parsing and expected business output.
- [ ] 3.2 Update generate command reference, examples, getting-started and troubleshooting docs while preserving F# examples; verify documented commands and package references in fresh consumers.
- [ ] 3.3 Coordinate root project/package/index changes and run strict OpenSpec validation plus repository-required formatting/full tests/coverage; verify existing F# generation and publication contracts remain green.

## Workflow follow-up

- Apply the [bounded auxiliary policy](handoff.md#auxiliary-delegation-and-resource-slots): up to three auxiliaries per lead, an initial shared global budget of three across at most two leads, one initial auxiliary and CPU/memory headroom checks before expansion, and one delivery-wide expensive-command slot with bounded internal parallelism. Use the exact Codex target and T3 delegate_task with per-thread capability discovery. No recursive fan-out. Coordinate slots with the root coordinator, serialize writing integration, integrate child results and run required checks through the initiative lead, cancel unneeded tasks with task_cancel, and report every child task/worktree/helper for coordinator cleanup.

- Create and link the initiative's PR; commit only owned changes and include test evidence, limitations, issue/PRD links and migration notes. An open PR is an intermediate state.
- The coordinator addresses review/CI feedback, integrates shared files serially, and lands this PR into main only after required checks/reviews and applicable approval requirements are satisfied. Use event-driven PR watching for waits; delegated children do not watch PRs.
- Prefer squash merging unless the PR instructions or repository settings require another method. Verify the exact PR is merged into main and its hosting-service-recorded landed SHA is reachable from refreshed main; original worker head ancestry is not required. Sync/archive this change under its normal OpenSpec workflow and apply the same landing checks to any archival follow-up PR before claiming completion.
- After integrating all auxiliary results, stop or cancel every auxiliary and its owned helpers; apply clean-status, final integrated/submitted-head and landed-PR checks as applicable to every owned auxiliary worktree. Record integration into the initiative PR when an auxiliary has no PR. Release all auxiliary and expensive-command slots. Stop the lead worker and its owned helpers, verify its worktree is clean and HEAD matches its last submitted PR head with no extra local commits, and have the coordinator remove that exact worktree from outside it. Verify both the directory and Git worktree registration are gone. Do not use git branch --merged as proof after a squash. Never force-remove dirty work or remove another initiative's workspace.
- Record PR URL, distinct submitted head and landed SHAs, actual merge method, final-head check/review evidence, main inclusion, archive location and cleanup evidence in the coordinator's delivery record. Release retained PR watches before final completion. Branch deletion and release publication remain separately authorized actions.
