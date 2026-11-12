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
