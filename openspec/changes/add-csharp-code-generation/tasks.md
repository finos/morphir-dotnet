# Tasks

Read [design](design.html), [spec](specs/morphir-csharp-transpilation/spec.md) and [handoff](handoff.md). Implementation starts only on a main-based checkout containing the planning and predecessor merge commits. The initiative is not complete until its PR is merged into main and its clean worker worktree is removed.

## 1. Compiler-host and target profile foundation

- [x] 1.1 Confirm the C# SDK predecessor PR is merged into main and read its frozen public API; create the backend PRD and add a test-first in-memory entry-point skeleton; verify no Tooling, Ion or production Roslyn dependency is introduced.
- [x] 1.2 Build the minimal test-only .NET 10 generator-driver/package-load smoke with the real model dependency closure before fixing the public API; verify AddSource compilation, cancellation and no CLI/file I/O, and publish the tested/untested host matrix.
- [x] 1.3 Pin isolated stable/RC test compiler builds and implement validated numeric language profiles 14 and 15 from failing option/consumer cases; verify default 14 is independent of the installed SDK and root global.json is unchanged.

## 2. Types, names and immutable values

- [x] 2.1 Write failing BDD/consumer cases for aliases, generics, tuples, records and custom cases, then implement classic and v4 typed admission/lowering; verify immutable public payloads, visibility and Result parameter meaning under both compilers.
- [x] 2.2 Implement deterministic namespace/module/source allocation and literal escaping after failing keyword, lexical capture, culture and portable-collision cases; verify stable .g.cs paths/bytes and located refusal of unresolvable collisions.
- [x] 2.3 Document SDK type mappings and implement generated record/variant equality using structural SDK values; verify independently allocated nested values compare and hash consistently through a compiled consumer.

## 3. Executable logic and refusals

- [x] 3.1 Implement application, nested function values/partial application, lambdas and nonrecursive lets from failing behavior cases; verify evaluation order, closure capture and shadowing in compiled execution.
- [x] 3.2 Implement admitted patterns, conditionals, record operations and top-level self recursion incrementally; verify first-match semantics and input persistence, while local recursive groups fail explicitly.
- [x] 3.3 Map the admitted SDK operations and required semantics, adding located failures for unresolved references, unsupported distributions/bodies/open records/document literals and representation loss; verify no object-erasure or runtime stubs are emitted.
- [x] 3.4 Add cancellation checks during traversal/emission and a whole-model result boundary; verify a cancelled or invalid later module returns no successful partial artifact set and document diagnostics.

## 4. Shared behavior matrix and package handoff

- [ ] 4.1 Reuse compatible daily-rate/car-rental/ecosystem fixtures in one parameterized compiler/execution corpus across classic/v4 and C# 14/15; verify nonempty results against independent business and SDK observations.
- [ ] 4.2 Pack the core and run a fresh in-memory consumer plus trimming smoke, update backend usage and host-boundary docs, and coordinate root registrations; verify deterministic artifacts and declared runtime requirements.
- [ ] 4.3 Run strict OpenSpec validation and repository-required formatting/full tests/coverage, record exact compiler builds, and hand off typed options/results/diagnostic categories to CLI integration; verify no CLI registration or full source-generator package is added.

## Workflow follow-up

- Apply the [bounded auxiliary policy](handoff.md#auxiliary-delegation-and-resource-slots): up to three auxiliaries per lead, an initial shared global budget of three across at most two leads, one initial auxiliary and CPU/memory headroom checks before expansion, and one delivery-wide expensive-command slot with bounded internal parallelism. Use the exact Codex target and T3 delegate_task with per-thread capability discovery. No recursive fan-out. Coordinate slots with the root coordinator, serialize writing integration, integrate child results and run required checks through the initiative lead, cancel unneeded tasks with task_cancel, and report every child task/worktree/helper for coordinator cleanup.

- Create and link the initiative's PR; commit only owned changes and include test evidence, limitations, issue/PRD links and migration notes. An open PR is an intermediate state.
- The coordinator addresses review/CI feedback, integrates shared files serially, and lands this PR into main only after required checks/reviews and applicable approval requirements are satisfied. Use event-driven PR watching for waits; delegated children do not watch PRs.
- Prefer squash merging unless the PR instructions or repository settings require another method. Verify the exact PR is merged into main and its hosting-service-recorded landed SHA is reachable from refreshed main; original worker head ancestry is not required. Sync/archive this change under its normal OpenSpec workflow and apply the same landing checks to any archival follow-up PR before claiming completion.
- After integrating all auxiliary results, stop or cancel every auxiliary and its owned helpers; apply clean-status, final integrated/submitted-head and landed-PR checks as applicable to every owned auxiliary worktree. Record integration into the initiative PR when an auxiliary has no PR. Release all auxiliary and expensive-command slots. Stop the lead worker and its owned helpers, verify its worktree is clean and HEAD matches its last submitted PR head with no extra local commits, and have the coordinator remove that exact worktree from outside it. Verify both the directory and Git worktree registration are gone. Do not use git branch --merged as proof after a squash. Never force-remove dirty work or remove another initiative's workspace.
- Record PR URL, distinct submitted head and landed SHAs, actual merge method, final-head check/review evidence, main inclusion, archive location and cleanup evidence in the coordinator's delivery record. Release retained PR watches before final completion. Branch deletion and release publication remain separately authorized actions.
