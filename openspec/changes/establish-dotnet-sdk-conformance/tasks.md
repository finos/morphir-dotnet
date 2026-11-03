# Tasks

Read [design](design.html), [spec](specs/dotnet-sdk-conformance/spec.md) and [handoff](handoff.md). Implementation starts only on a main-based checkout containing the planning and predecessor merge commits. The initiative is not complete until its PR is merged into main and its clean worker worktree is removed.

## 1. Contract and evidence

- [x] 1.1 Create the umbrella PRD and feature status table, link this change and the other four, and coordinate its index entry; verify every delivery has an owner and explicit predecessor.
- [x] 1.2 Pin exact upstream revisions and licenses for the Python, MoonBit, Gleam and Elm vnext evidence; verify every admitted operation resolves to checked-in source provenance.
- [ ] 1.3 Write the bounded operation/signature and scalar-profile inventory, including runtime arithmetic failure outcomes, decimal/text limits and comparability; verify no admitted signature or boundary remains implicit.
- [ ] 1.4 Add tagged behavioral cases and named existing-runtime gaps, including Dict.union precedence; verify expected observations remain independent of .NET implementations.

## 2. Offline fixture validation

- [x] 2.1 Write failing malformed-case and coverage-classification checks, implement the minimal offline validator, and document its command; verify duplicate IDs, missing expectations, unclassified operations and invalid tags fail with located errors.
- [ ] 2.2 Validate canonical projections, callback IDs and profile exclusions with representative nested values, ordered collections and numeric boundaries; verify validation passes without network or another language runtime.
- [ ] 2.3 Freeze the fixture schema and inventory in the README with an extension example; verify the C# and F# handoff checklists can identify all required cases and known gaps.

## 3. Integration evidence

- [x] 3.1 Integrate only this change's solution/test registration and PRD-index edits through the coordinator; verify the integrated test invocation discovers the new validator project.
- [ ] 3.2 Run strict OpenSpec validation, the fixture validator, repository-required formatting, full tests and coverage checks; record commands/results and verify no runtime or emitter behavior changed.
- [ ] 3.3 Produce a readiness manifest listing the pinned contract commit, fixture IDs, follow-up owners and exact Codex configuration; verify both runtime initiatives are eligible only after this PR is merged to main.

## Workflow follow-up

- Apply the [bounded auxiliary policy](handoff.md#auxiliary-delegation-and-resource-slots): up to three auxiliaries per lead, an initial shared global budget of three across at most two leads, one initial auxiliary and CPU/memory headroom checks before expansion, and one delivery-wide expensive-command slot with bounded internal parallelism. Use the exact Codex target and T3 delegate_task with per-thread capability discovery. No recursive fan-out. Coordinate slots with the root coordinator, serialize writing integration, integrate child results and run required checks through the initiative lead, cancel unneeded tasks with task_cancel, and report every child task/worktree/helper for coordinator cleanup.

- Create and link the initiative's PR; commit only owned changes and include test evidence, limitations, issue/PRD links and migration notes. An open PR is an intermediate state.
- The coordinator addresses review/CI feedback, integrates shared files serially, and lands this PR into main only after required checks/reviews and applicable approval requirements are satisfied. Use event-driven PR watching for waits; delegated children do not watch PRs.
- Prefer squash merging unless the PR instructions or repository settings require another method. Verify the exact PR is merged into main and its hosting-service-recorded landed SHA is reachable from refreshed main; original worker head ancestry is not required. Sync/archive this change under its normal OpenSpec workflow and apply the same landing checks to any archival follow-up PR before claiming completion.
- After integrating all auxiliary results, stop or cancel every auxiliary and its owned helpers; apply clean-status, final integrated/submitted-head and landed-PR checks as applicable to every owned auxiliary worktree. Record integration into the initiative PR when an auxiliary has no PR. Release all auxiliary and expensive-command slots. Stop the lead worker and its owned helpers, verify its worktree is clean and HEAD matches its last submitted PR head with no extra local commits, and have the coordinator remove that exact worktree from outside it. Verify both the directory and Git worktree registration are gone. Do not use git branch --merged as proof after a squash. Never force-remove dirty work or remove another initiative's workspace.
- Record PR URL, distinct submitted head and landed SHAs, actual merge method, final-head check/review evidence, main inclusion, archive location and cleanup evidence in the coordinator's delivery record. Release retained PR watches before final completion. Branch deletion and release publication remain separately authorized actions.
