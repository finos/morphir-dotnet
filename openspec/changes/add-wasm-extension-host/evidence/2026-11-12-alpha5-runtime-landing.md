# Alpha.5 runtime landing checkpoint

This checkpoint normally merges main `6de1c7ac5c04895f6eeb629dbe585622768e613b`
into reviewed donor `c909d108b8b4784c08e5a39ae219950eb74b1c2d`. It preserves
history and the product's external `morphir mck` dispatch. No development kit is
added to the product dependency closure.

Windows x64 reports `win_x64_trap_unsupported` before engine loading, file
inspection or module reading. The direct engine constructor also refuses that
platform. The signal-trap experiment switch is removed. CET, context-validation,
signal-trap and Spectre protections retain their defaults. The fix is deferred
under [#609](https://github.com/finos/morphir-dotnet/issues/609).

The current main merge adopts SDK 11.0.100 and the repository's target-framework
properties. Two redundant F# `nonNull` assertions were removed for the new
reference annotations. Seven affected lock files were normalized by restore,
without hand-selected dependency versions, then passed locked restore. The
Conformance test and WASM AOT-probe graphs were restored only; neither was run
or published as part of this checkpoint.

The three old Ubuntu failures are corrected. Both product-host tests now assert
external forwarding and exit 127 with a missing explicit runner, rather than
expecting an embedded kit. The six report-contract fixture files now have
provenance rows. Their input bytes are unchanged; the README and manifest are
identified as local metadata and the four upstream inputs retain their separate
parent `d5890d00` pin.

Fresh Windows ARM64 Release validation under SDK 11.0.100 passed:

| Selection | Passed | Skipped |
| --- | ---: | ---: |
| Extensions | 141 | 0 |
| WASM runtime and fixtures | 231 | 1 |
| Runtime probe, loader, worker refusal, report, listing, agreement and two forwarding controls | 116 | 0 |
| Release-profile and repository-target helpers | 46 | 0 |
| Existing MEP observations and ordinary channel controls | 117 | 0 |
| Total | 651 | 1 |

The skipped case directly exercises the engine's Windows x64 refusal and belongs
on that platform's lane. Guest execution tests are explicitly skipped there
because the runtime is unavailable; they are not reported as passes. The new
probe first failed with the old reason, then passed with the named refusal.
Compiler/setup failures, the initial missing apphosts, and the old three-reason
description assertion remain in separate logs. Both product apphosts were freshly
built before their command controls passed.

Scoped C# whitespace checks, the pinned F# test formatter/check, generated-feature
check, changelog check and strict OpenSpec validation passed. Registry formatting
is **unverified**: pinned Fantomas 5.2.4 cannot parse its existing nullable syntax.
The SDK 11 tool-command lookup initially failed; the already-cached exact pinned
formatter was invoked directly, with no install or update. Full solution tests,
coverage, full formatting, AOT and released-artifact qualification are not claimed.
Normal publication follows the coordinator's explicit targeted-campaign sequencing
exception; no hook is disabled.

Immutable raw streams, results, source/artifact identities and process cleanup
receipts remain in `.dev/out/alpha5-landing/`, excluded from commits. The 25 final
test processes bind source/artifact closure
`90E384BC129A7DACF0F7E886336C38B0D0B65C5F3FC757C3973AB77494281106`.
The separately restored, unexecuted test/probe graphs do not change those tested
implementation bytes. Exact-head CI and the scoped landing review remain gates.

This lands the runtime foundation. Normal CLI compile/generate routing is a
separate experimental product PR. Further conformance/report work, the 21-case
kit, notification-delivery evidence, task 5.4 packaging, tasks 6.1/6.2 qualification
and Rust parity are deferred beyond alpha.5. No host, platform or guest is
qualified by these source tests.

## Actual Windows x64 follow-up

The Windows x64 CI failure at `6775fd7d` exposed two remaining availability
assumptions. The registry test now checks the process's availability and common
reason. The worker success test skips with `win_x64_trap_unsupported` and #609.
The startup-order matrix remains active and checks that this safety refusal
precedes module I/O. The loader independently refuses Windows x64 before reading
a module, including when a caller supplies an incorrectly available runtime.
Its two new actual-x64 cases first failed with `NotFound`, then passed with
`extension.runtime_unavailable` and the named reason.

An isolated Microsoft SDK 11.0.100 AMD64 host and both product apphosts were
verified as x64. The downloaded SDK's SHA-512 matched Microsoft's release
metadata. No machine installation, PATH, SDK pin, CET, Spectre or trap setting
changed. The fresh solution build completed with zero errors and 91 warnings;
a subsequent current-source incremental build also returned zero.

The existing Test target invoked all 35 unit assemblies under x64 emulation.
Tooling passed **1,192**, with **77 skipped**; WASM passed **29**, with **203
skipped**, including guest execution. This was **not a green full Test target**:
SDK.Conformance crashed in `System.Diagnostics.Tracing.EventAttribute.set_Message`,
Toolchain reported six fixture/native-launch setup errors, and Myriad.Integration
reported 12 missing-.NET-10-runtime failures. The two package scripts were not
reached. Real x64 CI remains the judge of those groups. The initial attempted
runtime download selected an apphost pack; a corrected private runtime acquisition
completed afterward, but none of those groups was rerun before publication.

Final focused x64 checks passed: runtime probe **33 passed / 1 skipped**, listing
**17 / 0**, worker-runtime controls **15 / 1**. The full run changed one SDK output
DLL through test-driven builds; its final artifact-closure check therefore failed.
The owned WASM/Tooling source and focused artifacts retained their original
successful-build hashes. Two smoke locks were normalized only from the empty
`osx-arm64` RID key to `win-x64` for locked restore, then restored byte-for-byte;
no package pins or locks are part of this fix.

New Windows x64 skips in this follow-up are the worker success test and the E2E
steps that require a runnable guest or the module-loading stage. The executable
worker scenarios are printing, logging, echo, refused-module classification,
non-WASM file, missing module, output flooding and Ctrl+C. The packaged scenarios
are the worker session and read-only first run. Their guards read the executable's
actual runtime report, not the test process architecture. Help, stale identity,
unknown-argument, runtime-report and unavailable-runtime controls remain active.
These E2E edits compiled in the fresh build; their scenarios were not executed
in this batch. Packaged artifacts are not qualified by this change.

Raw streams, setup failures, source/artifact hashes and process identities are
retained under `.dev/out/wasm-win-x64-repair/`. The private compiler server was
terminated only after matching its PID, creation identity and private SDK path.
Final global .NET/compiler/test inventory was empty. `git diff --check` passed.
Automated scoped formatting was not run after the parent's immediate-push
instruction; full formatting and coverage are not claimed. Publication retains
the authorized bounded-campaign sequencing exception and bypasses no hook.
