# Configuration editing delivery journal

Tracking issue: [#473](https://github.com/finos/morphir-dotnet/issues/473).
Change: `add-config-editing`. Implementation is in progress; landing and cleanup remain parent-owned.

## Ownership and prerequisite

- Owned checkout: `/Users/damian/.t3/worktrees/morphir-dotnet/t3-62fedf74`.
- Owned branch: `t3/add-config-editing`. Initial checkout was clean.
- Base: `422ff4efa3139868bc73f79d1cdf4df529e2b34f`.
- Prerequisite [PR #471](https://github.com/finos/morphir-dotnet/pull/471) is merged at that base. Validated head `a5efd7dc9dec893025c7da05cc4d7c8017efc00a` and base have identical tree `43f9469cce6a64009cda037ef856f9663ee67a97`.
- Authoritative [completion receipt](https://github.com/finos/morphir-dotnet/pull/471#issuecomment-6604000394) confirms prerequisite checks, review fixes, archive and owned cleanup.
- Campaign temporary root: `/private/tmp/morphir-config-editing`, including physical `tmp`, editor, transaction and validation evidence. Parent owns eventual cleanup; unresolved recovery artifacts must be preserved.
- Parent owns independent review, CI monitoring, merge and checkout/branch/temp cleanup. This implementation worker will not watch, merge, force-push or delete branches.

## Work allocation

The implementation worker owns shared F# models/planner, IO orchestration, CLI, documentation and delivery. Two helpers own disjoint TOML editor and transaction implementation/test files. Only one heavy .NET job runs at a time, with `DOTNET_PROCESSOR_COUNT=2`, `-m:1 -p:BuildInParallel=false`, bounded executable test workers and sequenced Expecto. All processes use physical `TMPDIR`, `TEMP`, `TMP` beneath the campaign root.

## Validation

Tests are recorded red before production behavior, then green after implementation. No baseline count is claimed as a result.

### Shared plans boundary

- `core-red-01.log` proves missing plan API; `core-green-01.log` passes the initial immutable string plan.
- `core-red-02.log`/`core-green-02.log` qualify complete typed values. Selection, unset/inheritance, hidden invalid values, numeric/path-shape guards and virtual-root personal ownership have subsequent red/green checkpoints 03–07.
- `core-full-build.log` and `core-full.log` pass all 147 core Expecto tests after formatting. The 11 new tests cover typed values, scope selection, quoted paths, immutable observations, inheritance, redaction and numeric semantics.
- `io-plan-red.log` proves the reusable IO planning API is absent before implementation. `io-plan-green.log` proves an in-memory preview invokes no writer operations.
- Exact-buffer evidence, independent explicit-path authorization and canonical candidate topology resolution have `io-plan-red-02.log`/`io-plan-green-02.log` evidence. Isolated-read parity has `io-plan-red-03.log`/`io-plan-green-03.log`. The latter passes 10 integrated IO cases.
- Actual CLI subprocess BDD is red before registration. `cli-red-02-current.log`, after rebuilding the actual CLI, fails 11 of 23 cases for unimplemented flags and raw-reference presentation. `cli-green-03.log` passes all 23 after implementation; builds refresh `src/Morphir/bin` before subprocess tests.

The global CLI fixture uses a separate captured home beneath its test root, preventing a global fallback from also becoming a conflicting project primary. On this macOS host `/etc` is a link. Native system preview obeys the existing no-follow policy and returns typed exit 2 without a write. Pure system selection and explicitly authorized physical targets remain available. No filesystem-link policy was widened to pass a test.

### TOML editor boundary

The [editor qualification](config-editing-toml-qualification.md) records red/green checkpoints 01–12 and the final 30/30 focused tests. Source and test builds have zero warnings/errors. The editor preserves token spans, comments, BOM, line endings, quoted/dotted key spelling and following-header presentation. Complete inline/table/array edits are verified against the canonical reader, including temporal/nonfinite values, NaN equality, signed zero, opaque references and the pinned TOML corpus. No parser dependency or accepted TOML version changed.

The extended IO host passes 37 new managed checks plus the existing 228 JSON5 checks, 26 Morphir fixture documents and discovery/machine-routing checks. `smoke/managed-build.log` has zero warnings/errors; `smoke/managed-run.log` records editor 9, shared editing 15 and transaction 13. Trimmed/native publication is still pending at this boundary.

### IO and transaction boundary

The [transaction qualification](config-editing-transactions.md) records 32 focused passing cases and paired failing/passing receipts for staging, replacement, rollback, cleanup, ownership, same-process and actual cross-process coordination. In-memory application and immutable candidate copies are verified. Native coordination is machine-local, not distributed.

Integrated `io-workspace-red.log` proves both explicit workspace and root-default writes originally reported the wrong effective view when a selected member still won. `io-workspace-green.log` passes both after target selection was separated from effective resolution. `io-edge-red.log` identifies unstable planning evidence as incorrectly categorized I/O; `io-edge-green.log` proves it now reports a conflict. Tests also retain selected member/personal ownership under isolation, refuse explicit alternate-primary ambiguity, exercise complete inline dotted subtrees, reject lossy Unicode, retain BOM encodings and detect blocked parent paths without modifying them.

After these cases, `core-final.log` passes 148/148 Expecto tests and `io-edge-green.log` passes the full 266/266 IO/TUnit/Reqnroll tests. Builds report zero warnings/errors. Current-source coverage in `coverage-final-summary.json` unions `core-final-coverage/*/coverage.cobertura.xml` and `io-edge-coverage.cobertura.xml`: core 3757/3913, 96.01%; IO 1381/1425, 96.91%. Both exceed the prerequisite's authored-source measurements. The executable Expecto runner is the correctness gate; the adapter-based invocation supplies coverage only.

The actual generated report sources also pass a 71-check managed serializer host with reflection serialization disabled, including editing before/after, raw-reference redaction, overridden values, recovery ownership and depth 127. `serializer-managed-build.log` reports zero warnings/errors.
