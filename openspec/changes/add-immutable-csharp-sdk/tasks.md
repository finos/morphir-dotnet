# Tasks

Read [design](design.html), [spec](specs/csharp-sdk-runtime/spec.md) and [handoff](handoff.md). Implementation starts only on a main-based checkout containing the planning and predecessor merge commits. The initiative is not complete until its PR is merged into main and its clean worker worktree is removed.

## 1. Package and immutable scalar values

- [ ] 1.1 Confirm the predecessor PR is merged into main and read its frozen signatures/profiles; create the SDK PRD before production edits and verify the package has no compiler, CLI or F# dependency.
- [ ] 1.2 Write failing public consumer/BDD cases for Maybe, Result, Unit and Order, then implement the minimal immutable types and admitted combinators; verify error/success payload meanings and both language-profile consumer builds.
- [ ] 1.3 Implement scalar operations incrementally from failing admitted boundary cases, documenting exact errors and representation limits with the code; verify the shared scalar observations through public APIs.

## 2. Persistent collection semantics

- [ ] 2.1 Write failing persistence and structural equality/hash cases, then add SDK List backed by ImmutableList; verify construction, map, append, head, tail and length preserve old values and nested value equality.
- [ ] 2.2 Implement Dict and Set backed by sorted immutable collections after failing ordering, duplicate and union cases; verify admitted comparers, first-dictionary-wins union, insertion-independent observations and hashes.
- [ ] 2.3 Implement the SDK-local conformance adapter and collection API examples; verify every required admitted C# case passes without skips or regenerated expected results.

## 3. Packaged runtime evidence

- [ ] 3.1 Pack and restore fresh ordinary C# consumers from an isolated local feed; verify the documented API and dependency closure under pinned stable and RC compiler profiles.
- [ ] 3.2 Publish and run representative trimmed and Native AOT runtime consumers; verify variants, nested collections and SDK calls match ordinary-build behavior and document supported runtime targets.
- [ ] 3.3 Coordinate solution/package/index updates, run strict OpenSpec validation and repository-required formatting/full tests/coverage, and produce the downstream API handoff; verify there is one runtime API usable by both generator profiles.

## Workflow follow-up

- Create and link the initiative's PR; commit only owned changes and include test evidence, limitations, issue/PRD links and migration notes. An open PR is an intermediate state.
- The coordinator addresses review/CI feedback, integrates shared files serially, and lands this PR into main only after required checks/reviews and applicable approval requirements are satisfied. Use event-driven PR watching for waits; delegated children do not watch PRs.
- Prefer squash merging unless the PR instructions or repository settings require another method. Verify the exact PR is merged into main and its hosting-service-recorded landed SHA is reachable from refreshed main; original worker head ancestry is not required. Sync/archive this change under its normal OpenSpec workflow and apply the same landing checks to any archival follow-up PR before claiming completion.
- Stop the worker and its owned helpers, verify its worktree is clean and HEAD matches its last submitted PR head with no extra local commits, and have the coordinator remove that exact worktree from outside it. Verify both the directory and Git worktree registration are gone. Do not use git branch --merged as proof after a squash. Never force-remove dirty work or remove another initiative's workspace.
- Record PR URL, distinct submitted head and landed SHAs, actual merge method, final-head check/review evidence, main inclusion, archive location and cleanup evidence in the coordinator's delivery record. Release retained PR watches before final completion. Branch deletion and release publication remain separately authorized actions.
