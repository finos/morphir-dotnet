# Configured Python commands

Both normal product hosts now run the pinned Python 0.6.0 module through an
owned WASM session for compile and generate. The integration reuses the existing
MEP negotiation, model codecs, response checks and artifact publication. No
worker process or new model checker is introduced. The selected path, optional
ABI and digest come from the settings resolver's effective configuration snapshot;
relative paths retain the configuration file's origin.

The initial six-case test run recorded four assertion failures and two Windows
x64 controls skipped on ARM64. After implementation, real generation passed;
the initial compile example reached the guest but failed because this pin does
not accept addition. The positive example now uses an identity function, and the
original addition example remains as a source-diagnostic/no-IR-publication
negative control. Both hosts also run compile followed by generate from the
compile record, matching the experimental guide. A constructor-compatibility
test separately failed before explicit zero-argument connector constructors were
restored, then passed.

SDK 11.0.100 native Windows ARM64 validation used a locked targeted restore and
fresh Tooling.Tests plus both product-host builds. The host and product apphosts
were verified ARM64, and test summaries identify `net11.0|arm64`. Final focused
results were:

| Selection | Passed | Skipped |
| --- | ---: | ---: |
| Configured WASM commands, both hosts and settings/compatibility controls | 11 | 2 |
| Existing extension selection | 18 | 0 |
| Compile-record generation | 9 | 0 |
| Compile settings | 25 | 0 |
| Existing process frontend compilation | 6 | 0 |
| Existing external-backend BDD feature | 4 | 0 |
| Artifact-path checks | 40 | 0 |
| Total unique cases | 113 | 2 |

The skipped cases are actual Windows x64 compile/generate refusal controls;
they await real x64 CI. Supported source examples execute the real module with
SHA-256 `ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c`.
The module is a published guest, not a fake channel or an inner-guest
notification-delivery proof.

Main was merged normally at `12082e9e5961a5b3bc83809dc695729351c64a79` and the
PR retargeted to main. The WIP checkpoint preserved every source byte through
that merge. C# scoped whitespace/style checks and the new F# owner's pinned
format/check pass. Pinned Fantomas 5.2.4 cannot parse the older F# files' existing
nullable syntax, so their formatter verification is unverified; nullness was not
weakened. Changelog validation passes. The bounded campaign excludes full-suite,
coverage, AOT, packaged-artifact and platform qualification claims. Exact-head CI
and the required F#/C# review remain landing gates.

Raw compiler/runtime REDs, successful build and test results, source/artifact
hashes, architecture proofs and cleanup receipts are retained outside Git under
`.dev/out/python-wasm-product/` and its `resume/` directory. No production version,
tag, release, CET/Spectre policy or guest-notification policy changes here.

## Review corrections: cancellation and channel-open failures

The channel guest now checks the caller token and then its deadline after the
host session returns. Request and describe preserve caller cancellation and map
an expired deadline to `extension.timeout`, matching the process transport's
outcome contract. Four controlled channel cases failed before the fix and pass
after it; they cover both operations and assert the caller's cancellation token.

The configured-module adapter now preserves the channel-open refusal code
(issue #613). A product `generate` command using the pinned initial-memory-over-
ceiling fixture failed its exact diagnostic-code assertion before the fix
(`extension.unavailable`) and passes with `extension.memory_limit`. No output
folder is published. The first broad message assertion passed because the
message already contained the code; the retained RED uses the structured code.

Native Windows ARM64 SDK 11 Release builds of Tooling.Tests and both product
hosts pass. The final focused run has 16 passes and 2 Windows-x64-only skips
(4 cancellation controls plus 12 configured-command passes). This includes the
real pinned Python compile/generate journeys. An initial test-fixture raw-string
compiler error was corrected before behavioral RED; it is not runtime evidence.
Raw results and the current artifact/source closure are retained under
`.dev/out/python-wasm-product/review-fixes/`. No restore, dependency change,
full-suite, coverage, AOT or new platform qualification is claimed. Parent-owned
exact-head CI and review remain the landing gates.
