# Tasks

Read [design](design.html), [spec](specs/fsharp-sdk-packaging/spec.md) and [handoff](handoff.md). Implementation starts only on a main-based checkout containing the planning and predecessor merge commits. The initiative is not complete until its PR is merged into main and its clean worker worktree is removed.

## 1. Canonical and compatibility package graph

- [ ] 1.1 Confirm the conformance PR is merged into main; capture old package/assembly/type identities and compile a pre-migration consumer, then create the migration PRD; verify the old consumer is runnable before editing production packaging.
- [ ] 1.2 Add failing local-feed installation cases for canonical-only, legacy-only and combined package references; implement the canonical package and dependency-only legacy package while preserving runtime identities; verify one runtime assembly is resolved.
- [ ] 1.3 Update project/package consumers and F# generator runtime integration with a migration guide in the same group; verify existing generated source compiles unchanged and public names/entry points remain stable.

## 2. Covered conformance corrections

- [ ] 2.1 Add the F# adapter for required admitted cases, retaining independent expectations and explicit uncovered gaps; verify the covered failing cases identify actual runtime discrepancies.
- [ ] 2.2 Fix first-dictionary-wins Dict.union from its failing regression, plus only other covered discrepancies, and document observable changes; verify shared cases pass without hiding gaps or changing oracle values.
- [ ] 2.3 Run classic and v4 F# generation/execution and existing SDK tests; verify runtime migration preserves accepted model inputs and document any justified snapshot differences.

## 3. Package and binary compatibility evidence

- [ ] 3.1 Restore and execute the precompiled old consumer against the migrated runtime, then compile fresh consumers against each package combination; verify source, binary and package compatibility separately.
- [ ] 3.2 Coordinate serialized solution/package/PRD-index updates and existing generated-code example references; verify clean restores have no duplicate types or empty compatibility runtime assemblies.
- [ ] 3.3 Run strict OpenSpec validation and repository-required formatting/full tests/coverage; record packed asset graphs, runtime identity and migration examples for the PR reviewer.

## Workflow follow-up

- Create and link the initiative's PR; commit only owned changes and include test evidence, limitations, issue/PRD links and migration notes. An open PR is an intermediate state.
- The coordinator addresses review/CI feedback, integrates shared files serially, and lands this PR into main only after required checks/reviews and applicable approval requirements are satisfied. Use event-driven PR watching for waits; delegated children do not watch PRs.
- Prefer squash merging unless the PR instructions or repository settings require another method. Verify the exact PR is merged into main and its hosting-service-recorded landed SHA is reachable from refreshed main; original worker head ancestry is not required. Sync/archive this change under its normal OpenSpec workflow and apply the same landing checks to any archival follow-up PR before claiming completion.
- Stop the worker and its owned helpers, verify its worktree is clean and HEAD matches its last submitted PR head with no extra local commits, and have the coordinator remove that exact worktree from outside it. Verify both the directory and Git worktree registration are gone. Do not use git branch --merged as proof after a squash. Never force-remove dirty work or remove another initiative's workspace.
- Record PR URL, distinct submitted head and landed SHAs, actual merge method, final-head check/review evidence, main inclusion, archive location and cleanup evidence in the coordinator's delivery record. Release retained PR watches before final completion. Branch deletion and release publication remain separately authorized actions.
