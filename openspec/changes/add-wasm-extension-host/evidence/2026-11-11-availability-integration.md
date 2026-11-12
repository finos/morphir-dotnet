# Availability and product boundary integration, 11 November 2026

This records bounded Windows ARM64 validation of the pending merge from main
`f4fc12a2a80072b5739cf1922911d8f72ac3d21a` into the WASM branch at
`1e244a7c4308f8d66bde983d222f29b7aaab606e`, plus its availability follow-up.
At evidence collection the merge and follow-up were uncommitted. These results
are not an exact committed-head, Native AOT, Unix or qualification result.

The product keeps the early extension-worker hook and forwards every `mck`
invocation unchanged to the separately installed tool, as main's dispatch
contract requires. `Morphir.Tooling` no longer references Conformance or compiles
its former embedded MCK adapter/scenario. Their source and tests are preserved
for the distinct development entrypoint in `adopt-generic-guest-kit`. A temporary
source link keeps the adapter tests compiled; obsolete embedded-command tests
still require the explicit migration recorded by that change. No test was
removed or ignored to obtain these focused results.

The availability query now returns one outcome carrying either availability or
the existing validated `UnavailableReason`. Static host/deployment refusals win
before any engine probe. Eligible WASM entries map the three typed runtime
reasons once into the common reason. Listing consumes that outcome's `CanRun`
and nullable `ReasonOrNull`. Runtime-report wire codes and declared offering
sets are unchanged. Probe caching remains with the process's shared lazy value.

## Red, green and independent review

The first regression run built the tests against the old behavior. Registry
passed 12 cases; WASM delivery passed 13 with three expected failures, one for
each missing common reason. A later compilation caught an explicit
`IsAvailable` member colliding with the union case's generated augmentation.
That compile failure is distinct from behavior evidence. Renaming the explicit
member and its callers to `CanRun` fixed it.

| Focused check | Result |
| --- | --- |
| Extensions.Tests Registry | 12 passed, no skips/failures |
| Extensions.Tests WASM delivery | 21 passed, no skips/failures |
| Tooling.Tests ExtensionListWasmTests | 17 passed, no skips/failures |
| Morphir.Tests MckRoutingTests | 20 passed, two Unix-only SIGTERM cases skipped |
| Managed.Tests ManagedMckRoutingTests | 3 passed, no skips/failures |
| Standalone changelog validation | Passed |

The routing tests exercise produced Windows apphosts, argument/stream/exit
forwarding and process-tree cleanup. No Native AOT publish or Unix routing run
was made. Targeted restores ran only where assets lacked a new project
reference or still declared the removed product Conformance reference. No lock
file changed beyond the existing mechanical merge.

A fresh independent F#/C# reviewer found no interop findings or behavioral
defects in saved diff SHA-256
`46DFEF130336C806EB2A2C15A79B81040A120577CC18D9C6394A71947E2F1B08`.
The reviewer checked saved and working file hashes before and after reading,
and identified only optional direct C# coverage for `Match`. This is a review
of an uncommitted source snapshot, not of the eventual committed result.

| Reviewed source | SHA-256 |
| --- | --- |
| `src/Morphir.Extensions/Registry.fs` | `ff440fb261e93e4243558af1299e77dc17a792b0f562afcc7640e2680d73fba0` |
| `src/Morphir.Tooling/Features/Extensions/ExtensionListReport.cs` | `a76b38b5aa56fb12835edddf757db20a68f712835c9c0982182e41a00163551a` |
| `tests/Morphir.Tooling.Tests/Features/Extensions/ExtensionListWasmTests.cs` | `1acbecc34af3a8d31d5add357940ffb6955f6d15af6c21105aa366e2a542c472` |
| `tests/Morphir.Extensions.Tests/WasmDeliveryTests.fs`, before formatting | `74f9960baa65e815c8b8b3fa38d3fb9adcfee25cfc16f7c03603c77e56298c6e` |

The first three hashes remain unchanged. Pinned Fantomas reformatted only the
owned F# test file, whose final hash is
`7636204a026ae24f3e779ea9aa137908296ca374c2806ac9980de01a96fbb986`.
The formatting delta was inspected; the affected project was rebuilt and its
Registry 12/12 and WASM delivery 21/21 filters passed again.

## Formatting limits and intermediate commit policy

Separate `dotnet format whitespace` and `dotnet format style` checks passed
for the Tooling and Tooling.Tests projects with `--verify-no-changes`,
`--no-restore`, and only the owned three C# include paths. Workspace-load
warnings were retained. No analyzer/solution-wide formatter was run.

Repository-pinned Fantomas 5.2.4 cannot parse existing `T | null` syntax in
`Registry.fs`, including lines 91, 94 and 115. Its check failed; the source was
not downgraded or changed to satisfy the older parser. No formatter was
installed or updated. The separate check of the formatted F# test file passed.
Registry format verification is therefore unproved by this tool.
The coordinator inspected the production diff and baseline nullable annotations,
found no additional scoped defect, and explicitly accepted this compatibility
limit for the intermediate donor sequence. This is not a formatter pass.

The deployment coordinator explicitly authorized intermediate donor commits
under an exception to the local full-suite/coverage/full-format-before-commit
sequence, after the scoped checks and their actual limits are reconciled. That
exception does not authorize landing an unverified final PR. Full-suite,
coverage, full formatting, exact-head CI, platform/security gates and substantive
review remain outstanding. No installed Git hook was disabled or bypassed.
Production version, tag and publication are not selected or authorized.

All heavy commands used the pinned SDK selection, two processors, one project
at a time, no shared compiler/node reuse, captured hidden processes and bounded
whole-tree kill-and-wait cleanup. Each burst ended with empty owned-process
inventories and an explicit CPU2 release. Exact local commands, separate RED and
GREEN logs, manifests and the donor file map are retained under
`.dev/out/wasm-resume/` in the implementation checkout.

The CET design and its source trace remain an investigation, not a native
engine patch or a qualification result. Offering-report patches remain
unapplied. Packaging and released-artifact qualification are not established by
any result above.
