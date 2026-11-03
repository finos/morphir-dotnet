# SDK runtime delivery evidence

Status on 2026-11-03: both runtime PRs landed; this combined docs-only
specification/archive follow-up and coordinator cleanup remain pending.
The [coordination plan](orchestration.md) and [manifest](orchestration.json)
remain planning snapshots. Their path references follow the archives, but their
planned status, resource ceilings and empty delivery fields are not observations.
Generator and CLI implementation remain undispatched.

## Verified implementation landing

The archive worker independently fetched `origin/main`, queried each exact PR
with `gh pr view --json url,state,baseRefName,headRefOid,mergeCommit,mergedAt,reviews,statusCheckRollup`,
and confirmed both actual landed commits are reachable with
`git merge-base --is-ancestor <landed-SHA> origin/main`. The prepared archive
checkout starts at `55f73bdaa93b7bc55a48b184bab8e46a5636fd9e`. Squash results,
submitted heads and earlier artifact checkpoints have different meanings.

| Revision | C# SDK, [#470](https://github.com/finos/morphir-dotnet/pull/470), [issue #464](https://github.com/finos/morphir-dotnet/issues/464) | F# SDK, [#469](https://github.com/finos/morphir-dotnet/pull/469), [issue #465](https://github.com/finos/morphir-dotnet/issues/465) |
| --- | --- | --- |
| Original lead submission checkpoint | `ed8aa66a794021a541a7836af0a32cd8ae09516d` | `63068adfbc7dd458bf661834d920f04007fe4215` |
| Final submitted PR head | `a6f16388ebea138137aa5c6e15589a2c0a14b550` | `7dde9e57bf1fd3a38fd1dc8df7cd0a65919d402d` |
| Actual squash landed SHA | `55f73bdaa93b7bc55a48b184bab8e46a5636fd9e` | `4df3c1ad425f782bfe3cc02e30f577d87dca0f53` |
| Merged into main | 2026-11-03 12:05:23 UTC | 2026-11-03 11:28:55 UTC |

C# production source checkpoint is
`0b01ff037f0496071e8ba4689c785376658a8f30`. Runtime/test source was unchanged
during main integration. Root verified submitted-versus-landed SDK content
exactly; this worker also checks those source trees independently. Original
worker commit ancestry is not required after a squash.

All eight check categories passed on each final head: EasyCLA, dotnet-scan,
lint, Linux/Windows/macOS tests, OpenSpec workflow and aggregate ci. F# had three
real review findings; the coordinator reproduced, fixed and resolved them.
C# had no hosted review findings. Neither PR had another developer's submitted
approval. The user supplied standing blanket authorization for admin bypass,
which the coordinator used for both squash merges. That authorization is not
developer approval. This worker does not merge this follow-up.

F# Windows CI initially hit an unchanged privacy-test timeout/cleanup race.
A same-SHA retry passed. A PR-body edit triggered another same-SHA workflow,
also green. The history remains relevant; no unrelated compiler-helper change
belongs in this archive delivery.

## Canonical specifications and archive

OpenSpec resolved this checkout's local root, schema `html-design`, all four
artifacts done and exactly 9/9 CLI-tracked tasks for each change. Archive
context and one specs-instruction snapshot per change were read. No additional
artifact `rules` or operation guidance was returned. The user had already
selected inline sync followed by archive for both complete changes.

| Capability | Added requirements | Scenarios | Preserved archive |
| --- | --- | --- | --- |
| [csharp-sdk-runtime](../../../openspec/specs/csharp-sdk-runtime/spec.md) | 6 | 11 | [2026-11-03-add-immutable-csharp-sdk](../../../openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/proposal.md) |
| [fsharp-sdk-packaging](../../../openspec/specs/fsharp-sdk-packaging/spec.md) | 5 | 8 | [2026-11-03-introduce-fsharp-sdk-package](../../../openspec/changes/archive/2026-11-03-introduce-fsharp-sdk-package/proposal.md) |

Each new main spec preserves the delta Purpose verbatim and all requirement and
scenario content. Only the title and ADDED header change to canonical main-spec
form. Strict main-spec validation and exact content comparison passed before
each `changeRoot` moved. Neither capability had a main spec or MODIFIED,
REMOVED or RENAMED operations. Nothing remains to apply. Both `.openspec.yaml`
files and all completed task markers are retained.

The stable [C# API handoff](../../../openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/api-handoff.md#generated-record-and-variant-protocol)
defines the single public `IMorphirValue` / `MorphirValue.Hash` equality
protocol. Successor generator and CLI briefs point there. Generated records
and variants must compose that protocol through boxing, tuples and SDK
collections; a successor must not invent another interface. Native C# unions
and the full source-generator adapter remain deferred.

These specs and archives become main's state only when this follow-up PR lands.
Completed implementation tasks do not certify archival landing or cleanup.

## Frozen conformance and separate runtime acceptance

The [readiness manifest](../../../tests/Fixtures/MorphirSDK/readiness.json)
retains all 12 frozen contract-file hashes, the submitted contract artifact SHA
`737e85b3a202ba733b153be637e33cf953342705`, and all original-byte evidence.
The baseline stays 67 operations, 215 cases, 207 required IDs, eight exclusions
and four historical F# gap records. No frozen case, inventory, schema,
provenance, authoring file or contract document changed.

Each public runtime adapter independently executed and passed 207/207 required
IDs. Each reports eight representation exclusions separately. The F# runtime
delivery resolved the covered FS-DICT-UNION, FS-INT-CHECKED, FS-DECIMAL-DIV and
FS-DECIMAL-TEXT discrepancies. Their original gap observations remain history.
Exclusions and gaps are never counted as passes. The original conformance
initiative validated its corpus; it did not certify either runtime. Its
`sdkRuntimeConformanceClaimedByThisInitiative: false` assertion and original
validation counters remain unchanged. New observations live in the separate
`sdkRuntimeDelivery` metadata, with archival landing and cleanup explicitly false.

## Retained implementation validation

The latest combined main integration passed native Windows Nuke Test with
3,290 suite tests plus 15 cold package lifecycle checks, zero failures/skips:
C# SDK 20, F# SDK 403 and F# generator 333. PackAll passed in 34 seconds,
TestBuild passed 40/40, Precommit passed in 28 seconds, and strict OpenSpec
passed 21 items plus two checker tests. Changelog and frozen digest checks
passed. The first combined attempt encountered owned pre-migration legacy
bin/obj DLLs; root preserved them outside the source tree, then the full rerun
passed without code or assertion changes. This fresh checkout does not import
those stale outputs.

C# production-only coverage remains 318/333 lines, 95.4954954954955%.
The identical ordinary consumer compiled and ran under numeric C# 14.0 with
SDK 10.0.403 and numeric C# 15.0 with SDK 11.0.100-rc.2.26504.105. Actual
trimmed and NativeAOT win-arm64 publications executed successfully. These are
source-bound, representative proofs, not all-target certification. Exact
commands, compiler revisions and artifact hashes remain in the archived
[C# validation record](../../../openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/validation.md).
This docs-only follow-up does not reinstall toolchains or repeat instrumentation,
compiler-profile or NativeAOT proofs for unchanged source.

Final F# measured production source covers 65/65 changed sequence points and
44/44 changed point-start lines; the parser covers 6/6 sequence points.
Whole runtime coverage is 126/424 points, 29.717%, and 95/361 point-start lines,
26.316%. These are not aggregate 80%/90% or branch-coverage claims. Accepted
matched-old/current and preformat-source measurements stay historical in the
[F# PRD](../../content/contributing/design/prds/introduce-fsharp-sdk-package.md).

Husky native Git/path discovery fails in this environment. Both configured
checks passed directly without hook bypass or disabling checks. Existing
unsupported F# formatter diagnostics and inherited runtime formatting,
dependency and generated-code warnings remain documented. No publishing target,
release or machine NuGet configuration was changed.

## Archive worker ownership and remaining delivery

This archive checkout repeated the following validation under Windows arm64,
.NET SDK 10.0.403 and runtime 10.0.14. The source-bound coverage/profile/AOT
proofs above remain unchanged and were not instrumented or reinstalled again.

- Native `build.ps1 --target Test --configuration Release` passed
  Restore/Compile/Test, 3,290 suite tests and 15 cold package lifecycle checks,
  zero failures/skips. Nuke reports 7m27s; the captured bootstrap wrapper took
  453.94 seconds. C# SDK 20, F# SDK 403 and generator 333 all passed.
- Native `build.ps1 --target Precommit --configuration Release` passed
  full-solution `dotnet format --verify-no-changes` in 30 seconds, 33.47 seconds
  including bootstrap. Existing unsupported-F# diagnostics remain visible.
- Native `build.ps1 --target PackAll --configuration Release --skip Restore Compile`
  passed in 33 seconds, 36.34 seconds including bootstrap. Restore/Compile were
  already verified by Test; no publishing target ran.
- Native `build.ps1 --target TestBuild --configuration Release --skip Restore Compile`
  passed all 40 tests, zero failures/skips. Both configured contribution checks
  passed directly: Precommit above and `dotnet fsi scripts/validate-changelog.fsx`.
  `dotnet husky run` exited 1 with `failed to execute command 'git'` and
  `Could not find Husky path`, even with native Git in process PATH. There is
  no installed pre-commit hook in the resolved Git hooks directory. No bypass
  or hook configuration change was used.
- `mise run openspec:verify` passed all 21 strict items and both checker tests.
  `openspec validate --specs --strict --no-interactive` passed both incremental
  pre-move sync checks. Final main-spec validation passed all 13 specs, and
  `openspec validate --archived --strict --no-interactive` passed all seven archives.
- `python C:/Temp/morphir-sdk-archive-1e3c6fbe-20261103/audit-archive.py`
  passed all 12 frozen digests, 44 original-byte evidence files, unchanged
  conformance-era readiness data, canonical spec content, complete archived
  task sets and retained planning semantics. It checked 217 local links and
  51 anchors across 42 documents. `git diff --check` passed.

Restore changed only the smoke lockfile's host RID from osx-arm64 to win-arm64.
The checked-in lock input was restored after validation and is excluded from
this docs-only contribution. No production, test implementation, build, SDK pin
or global setting changed. No command timed out.

The only archive worker uses Codex `gpt-6.1-sol`, reasoning effort `high`, branch
`t3/archive-sdk-runtime-docs`, checkout
`C:/Users/damre/.t3/worktrees/morphir-dotnet/t3-1e3c6fbe`.
Its task request is `dotnet-sdk-archive-20261103-01`; task/thread identities
and the complete retained artifact inventory are supplied in the final handoff.
No auxiliaries, new worktrees, background helpers or successor implementation
were started. Root supplied the sole delivery-wide expensive-command lease.

Archive checks run serially with `DOTNET_PROCESSOR_COUNT=2`, no shared compiler
or MSBuild node reuse, bounded MSBuild arguments and process-local TEMP/TMP at
`C:/Temp/morphir-sdk-archive-1e3c6fbe-20261103/temp`. The wrapper restores its
environment in `finally`, captures hidden child output, and terminates the
owned process tree on timeout. Compiler-server preflight must find no unrelated
server before Nuke Compile's broad cleanup. Logs, scripts and manifests are
retained under `C:/Temp/morphir-sdk-archive-1e3c6fbe-20261103` for root cleanup.

Both implementation source checkouts and their existing evidence remain
untouched. Root owns final checks/reviews, this archival PR's landing, exact
landed-SHA verification and cleanup. The policy-blocked removal of the empty
conformance directory `t3-eff883da` remains blocked; this worker neither retries
nor works around that rejection. All branches, worktrees and retained artifacts
remain available. Overall initiative completion is still pending.
