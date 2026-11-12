# Main refresh and MEP checkpoint, 11 November 2026

Normal merge `2aa30374800a2c10c951c7ff6db6df94d870c92b` preserves the existing WASM history and incorporates freshly fetched main `da2167e623b01847ed9df75f985917e266f59a77`. Its first parent is `e64ae9ee044c7c141f12a2b8becc575601742e32`. The only manual conflict was the central-package explanatory comment, resolved to the current Fallout dependency provenance. The separately landed Linux bootstrap and WebUtilities correction were adopted without a duplicate implementation. No rebase or force-push was used.

The local MEP work, including the coordinator's raw-lifecycle correction, was preserved with exact copied bytes and a retained stash before merging. Its public constructors, ordinary ID0, lifecycle guards and absent-params defaults remain. Opted-in sessions use their actual existing transport, ID1, bounded immutable observations, operation admission, original describe receipt, one-shot copied selected offer and fixed probes. Raw initialize/shutdown requests cannot bypass the dedicated operations. Product `morphir mck` still forwards to the separate tool before parsing, and product Tooling has no Conformance reference. The stale Conformance-only registration in the packed-library list was removed; the WASM runtime remains a product dependency. This does not choose a distribution or publication for the development kit runner.

## Granted validation

On Windows ARM64, absolute SDK 10.0.403, two processors, single MSBuild node, no shared compiler/server/node reuse:

- One locked aggregate restore succeeded for `Morphir.Build.Tests` and `Morphir.Mep.Tests`.
- One aggregate Release build compiled those projects serially, including the Fallout build project. No restore/build retry or product correction was needed.
- The four pure build-helper classes passed: ReleaseProfiles 44, PackagedEngine 15, PackagedContents 10, PublishedLayout 11, total 80.
- Six conformance lists passed 57 cases, including the two raw-lifecycle regressions. Seven ordinary selections passed 48 cases, including the C# consumer in-process. Actual total 105, zero ignored/failed/errored; no entire-assembly or negative C# compilation selector ran.
- All 92 lock/config inputs and 19 recorded source observations stayed unchanged. Fresh MEP implementation SHA-256 `A2B039EDBBAA0D747A0ADFE0A5941FFC1494067E604D5A66BF50CE9A23D333A9` matched its test-directory copy; test assembly `5F740E86902C9DE9CC348E3AAC0CA5FABD214C73C3F8F932373CDB0328BBC7CF`. Fresh Fallout build output `EE7D00AFE5AE496CBD5B881EB92BBC393CDFBE4481C7B725BAE5176C6D297AC0` also matched its test-directory copy.

Each process had hidden captured output, a 240-second whole-tree bound and awaited cleanup; all product commands finished within the eight-minute admission. Cleanup checks original process creation identity before any kill. Authorized concurrent installer processes are recorded without being killed. Owned processes and streams settled before release.

Local detailed evidence is `.dev/out/wasm-resume/main-refresh-20261111/validation-A2/`: `release.json`, `HANDOFF.md`, per-stage raw/derived logs, source/input records, AST checks and actual MEP names. The earlier `validation-A` parser failure is immutable and started no heavy child. A2 retained two instrumentation corrections: locating the actual build output and admitting the coordinator's authorized concurrent batch. Neither reran a failed product test or changed product behavior.

## Limits and remaining gates

This is an intermediate checkpoint under the maintainer's bounded campaign and the coordinator's explicit commit sequencing decision. Full suite, coverage, full formatting and standalone changelog validation did not run before this checkpoint. No installed hook was disabled. Exact-head CI and substantive review remain landing gates.

Staged whitespace verification found one extra terminal blank line in the new `ConformanceEvidence.fs`. It was removed after validation; the retained `eof-whitespace-only.json` records that byte-only delta. No token or behavior changed, and no additional product test was run for that formatting correction.

Independent raw-lifecycle correction recheck is not established in the coordinator's records. The same-ID duplicate successful-describe regression is still pending. This checkpoint is not a generic-ready donor or proof of inner-WASM delivery, required guest notifications, generic21, other platforms, AOT or acquired-artifact qualification. Existing CET/Spectre protections and the documented OpenSSL prerequisite stay in force. No new production tag/version, publication or package layout was selected. Report-contract adoption remains a separate next step.
