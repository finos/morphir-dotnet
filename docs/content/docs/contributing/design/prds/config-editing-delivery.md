# Configuration editing delivery journal

Tracking issue: [#473](https://github.com/finos/morphir-dotnet/issues/473).
Change: `add-config-editing`. Implementation is locally qualified and archived; review, CI, landing and cleanup remain parent-owned.

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

### CLI boundary

`validation/Morphir.Tooling.Tests.log` passes all 360 TUnit/Reqnroll cases, including 33 editing subprocess scenarios and 18 no-discovery/host-binding checks. Every editing subprocess report is checked against `docs/spec/schemas/config-edit-result.schema.json` with a fresh schema registry. Persisted values prove default strings, booleans, Int64, floats including large finite literals, complete arrays/objects and literal dotted keys. Unset reveals classic JSON with its origin. Before/after opaque references are redacted, previews preserve bytes, successful overridden writes return 0 and missing unset returns 1.

`core-number-red.log` reproduces rejection of a large literal despite explicit floating input; `core-number-green.log` passes after accepting finite explicit numbers while retaining Int64/composite integer limits. This changes edit input typing only; canonical readers and accepted TOML/YAML/JSON/JSON5 profiles remain unchanged.

The first broad local driver stopped after five passing suites on Bash 3.2's empty-array handling. That driver was repaired to use positional arguments and infer Expecto from project metadata; no product change was needed. Its evidence is preserved in `validation-initial/`. The current broad run uses each configured executable runner, one TUnit worker, sequenced Expecto, bounded nested builds and a process-local path to the already installed PowerShell 7.5.3. No global tool setting changed.

## Final local qualification

The source checkpoint is `cb6ff29`. All 17 configured runners pass; `validation/summary.json` records these exact counts.

| Runner | Passed |
| --- | ---: |
| `Morphir.CodeGeneration.FSharp.Tests` | 333 |
| `Morphir.Configuration.IO.Tests` | 266 |
| `Morphir.Configuration.Inspection.Smoke` | 71 |
| `Morphir.Configuration.Microsoft.Tests` | 22 |
| `Morphir.Configuration.Tests` | 149 |
| `Morphir.Core.Tests` | 237 |
| `Morphir.IR.Pipeline.Plugins.Tests` | 141 |
| `Morphir.IR.Pipeline.Tests` | 299 |
| `Morphir.Internal.CodeGeneration.Tests` | 25 |
| `Morphir.Ion.Tests` | 463 |
| `Morphir.Models.Tests` | 791 |
| `Morphir.Myriad.Integration.Tests` | 12 |
| `Morphir.Myriad.Tests` | 4 |
| `Morphir.SDK.CSharp.Tests` | 20 |
| `Morphir.SDK.Conformance.Tests` | 27 |
| `Morphir.SDK.Tests` | 403 |
| `Morphir.Tooling.Tests` | 360 |

The solution build passes with 20 existing package/generated-code warnings and no errors. C# formatter verification and Fantomas verification pass. The new managed configuration and serializer builds have no warnings/errors. `delivery-coverage.json` records core 3766/3922, 96.02%, and IO 1381/1425, 96.91% on the final source.

Both non-CLI hosts pass managed, trimmed and native `osx-arm64` qualification. The IO host passes 37 new checks, all 228 JSON5 grammar cases, 26 pinned Morphir documents and its existing host/discovery/machine-root checks. The linked-source CLI serializer host passes 71 checks in each mode, with reflection serialization disabled. `publish-summary.json` records actual receipts and warning counts. IO trimmed/native retain 44/47 pinned dependency warning lines; serializer trimmed/native retain 42/47. `ILLinkTreatWarningsAsErrors=false` is explicit on each publish, no diagnostic is suppressed, and no warning-free dependency claim is made. No authored-source linker/AOT diagnostic occurs. Exact prepublish lockfile bytes were restored through the captured SHA-256 manifest.

### Reproduction

Run one heavy job at a time from the repository root. Use physical paths on macOS:

```sh
export DOTNET_PROCESSOR_COUNT=2 BuildInParallel=false
export TMPDIR=/private/tmp/morphir-config-editing/tmp
export TEMP="$TMPDIR" TMP="$TMPDIR"
export PATH="/Users/damian/.local/share/mise/installs/powershell/7.5.3:$PATH"
dotnet build Morphir.slnx -m:1 -p:BuildInParallel=false
# For each runner in the table, use its compiled executable:
dotnet tests/Morphir.Configuration.Tests/bin/Debug/net10.0/Morphir.Configuration.Tests.dll --sequenced
dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --maximum-parallel-tests 1
# Other Expecto projects use --sequenced; other TUnit projects use one worker.
dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet
dotnet fantomas --check src/Morphir.Configuration/Editing.fs tests/Morphir.Configuration.Tests/EditingTests.fs
```

`run-validation.sh` under the campaign evidence root contains the complete 17-runner sequence and bounded process environment. It reads each test project's framework metadata; the serializer smoke takes no runner flags. Nested SDK packs/builds remain serial, using the installed process-local PowerShell. Runtime source is unchanged by the later archive and documentation commits.

Coverage is measured after correctness gates:

```sh
dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --maximum-parallel-tests 1 --coverage --coverage-output-format cobertura --coverage-output "$TMPDIR/io.cobertura.xml"
dotnet test tests/Morphir.Configuration.Tests/Morphir.Configuration.Tests.fsproj --no-build --no-restore -m:1 -p:BuildInParallel=false --collect:"XPlat Code Coverage" --results-directory "$TMPDIR/core-coverage"
/usr/bin/python3 scripts/configuration/summarize-coverage.py "$TMPDIR"/core-coverage/*/coverage.cobertura.xml "$TMPDIR/io.cobertura.xml"
```

Each smoke README gives exact managed/trimmed/native commands. The published modes use `-m:1 -p:BuildInParallel=false -p:PublishTrimmed=true -p:PublishAot=<false|true> -p:TrimmerSingleWarn=false -p:ILLinkTreatWarningsAsErrors=false -p:JsonSerializerIsReflectionEnabledByDefault=false`. The IO executable receives `--fixtures <repo>/tests/Fixtures/Configuration`. `run-publish.sh` and the lockfile capture/restore manifest retain the exact successful sequence.

### Main integration and archive

Main advanced through the documentation-only [SDK archive PR #472](https://github.com/finos/morphir-dotnet/pull/472), landing at `618fbf7b6c5b542122450bf61f4f794e590fe4f5`. It was merged normally into the owned branch. Runtime sources and configuration/tooling tests are byte-identical to `cb6ff29`. Independent SDK main specs and archives match main exactly. Fresh SDK conformance passes 27/27 after integrating its readiness metadata. No SDK archive was authored or altered by this campaign.

Only `add-config-editing` was synchronized and archived at `openspec/changes/archive/2026-11-03-add-config-editing/`. Its main capability is `openspec/specs/configuration-editing/spec.md`. All eight tasks are complete. The nine requirement bodies, 13 scenarios and Purpose match the approved delta exactly. The archived design/proposal/task links are checked; the classic migration prerequisite link is repaired. Independent migration work remains unimplemented.

Strict OpenSpec validation passes 22/22 active/spec items before and after archiving. The html-design snapshot check and both extension tests pass. The generated-output publication main spec and implementation remain unchanged. The archive receipt and task-boundary instruction snapshots are retained under the campaign evidence root.

### Handoff limits and ownership

Local qualification does not claim current-head CI, independent review, landing or campaign completion. Parent owns those gates, including the explicitly authorized workstream admin bypass and subsequent checkout/local-branch/remote-branch/temp cleanup. The implementation worker does not watch, merge, force-push, delete branches or publish releases.

Native writes coordinate cooperating processes on one machine; shared stores needing distributed coordination must supply it through their host. Per-file atomic installation does not provide multi-file group/crash atomicity. Native no-follow policy can reject the conventional macOS `/etc` alias; an authorized physical TOML target avoids that alias. Windows paths and separate drive/UNC hosts are covered by the shared host fixtures; the local native publication target is `osx-arm64`.

All owned external build/test/publish evidence is under `/private/tmp/morphir-config-editing/`, including `tmp`, `editor`, `transaction`, `smoke`, `publish`, `publish-locks`, `validation`, `validation-initial`, coverage files and instruction/archive receipts. Generated `bin`, `obj` and test outputs inside the retained owned checkout also belong to this campaign. Shared tool/package caches are not campaign cleanup targets. No unresolved native recovery artifact was found after the successful qualification runs. Preserve any recovery data discovered during later review instead of deleting it to manufacture a clean result.


### Parent Windows CI fixture correction

Windows run `41671846502` on `9b683b9` passed all 360 tooling tests but failed
21 IO transaction cases. The native observations returned canonical '/' paths,
while the test helper supplied native backslash paths as change destinations.
The existing plan-ownership guard correctly rejected that mismatch before any
write, producing typed I/O outcomes and preventing fault/staging callbacks.

Transaction fixtures now construct canonical paths with `ProjectPath.join`,
and normalize directory-enumeration results before path assertions. The public
example uses `observed.Path` for the corresponding change, making its ownership
contract explicit on Windows. No production guard or native locking behavior
was weakened. The unchanged native CLI execution had already passed Windows CI.
The corrected full IO runner passes 266/266 locally; test compilation, formatter
verification and whitespace checks pass. Evidence is owned under
`/private/tmp/morphir-config-editing/parent-review/`, including the downloaded
Windows failure log and `windows-path-io-full.log`. Ubuntu and macOS were
cancelled by matrix fail-fast after Windows failed; fresh CI remains required.

The independent TOML/transaction review completed without actionable findings.
Its existing-DLL probes passed 1,438 editor operations, 10,000 sampled finite
double roundtrips and 8 native transaction cases. Its disposable evidence is
`/private/tmp/morphir-config-editing-review-writes`; no recovery data was retained.
