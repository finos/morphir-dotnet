---
title: "TUF client for morphirup spike, 2026-11-09"
linkTitle: "TUF client spike"
description: >
  An evaluation of the NuGet package Tuf 1.1.1 against the tool release metadata v1 profile, its fixtures and a Native AOT publish, with the alternative sized.
---

# TUF client for morphirup spike, 2026-11-09

Status: evaluation only, for the maintainer's security review. Nothing here is adopted, and no product project references the package. The probe sources are in `spikes/tuf-client/`; the generated case table is `spikes/tuf-client/results/conformance-win-arm64.md`.

Recommendation: do not adopt `Tuf` 1.1.1 as morphirup's client. Its core verification is sound on every fixture case it can express, and it is AOT-clean. But six profile rules need a change to the library itself: an injectable clock, typed failures, access to targets metadata, download bounds, a `spec_version` check and a scheme policy. Implement the profile's client subset in `Morphir.Toolchain` instead, on BouncyCastle's Ed25519 primitive, and test it against the finos/morphir fixtures and the upstream `tuf-conformance` suite. The reasons and the alternatives are at the end.

What the manager needs is the [tool release metadata v1 profile](https://github.com/finos/morphir/blob/main/docs/spec/tool-release-metadata/index.md) and the signed toolchain fixtures of the [toolchain contracts](https://github.com/finos/morphir/blob/main/docs/spec/toolchain/index.md), both read at finos/morphir `c247aad39`.

## The package

| Fact | Value | Obtained from |
| --- | --- | --- |
| Package and version tested | `Tuf` 1.1.1, the latest stable release (9 October 2026). Later versions are 1.2.0 alphas only | nuget.org registration API and search API |
| License | MIT | nuspec, `LICENSE` in the source |
| Source | `github.com/mitchdenny/sigstore-dotnet`, `src/Tuf`, commit `c0f5ff97` (tag `v1.1.1`), named in the nuspec `repository` element | nuspec; `git clone`, then checkout of that commit; I read every file of `src/Tuf` at that commit |
| Maintainers | One nuget.org owner, `mitchdenny`. Contributors: `mitchdenny` (26 commits), Dependabot and GitHub Actions bots. Commits credit GitHub Copilot as co-author | nuget.org search API; GitHub contributors API; `git log` |
| History | Repository created 26 February 2026. 0.2.0 (2 March) to 0.5.0 (20 March), 1.0.0 (13 August), 1.0.1 (23 August), 1.1.0 (27 September), 1.1.1 (9 October). Last push 1 November 2026. 8 stars, 3 forks, 25,170 downloads in total, 8,550 of 1.1.1 | nuget.org; GitHub repository API; `gh release list` |
| Users | `Sigstore` 1.1.1, the same maintainer's package, depends on `Tuf` 1.1.1 | nuget.org registration |
| Security | `SECURITY.md` supports the latest stable release only and asks for private reports. No published GitHub security advisories. NuGet's vulnerability data lists nothing for `Tuf` or `BouncyCastle.Cryptography` 2.7.0 (`dotnet list package --vulnerable --include-transitive`). Open issues: #8 (GitHub Enterprise), #18 (pin Actions to SHAs), #57 (BouncyCastle key factory size under AOT) | GitHub API, `gh issue list`, dotnet CLI |
| Upstream testing | Unit tests, plus the official `theupdateframework/tuf-conformance` 2.5.0 suite in CI against a Native AOT linux-x64 build, with an empty expected-failures file. I found no recorded run of that workflow in the recent run list, so this is configuration evidence, not a seen pass | `.github/workflows/conformance.yml`, `src/Tuf.Conformance/Tuf.Conformance.xfails`, `gh run list` |
| Dependency closure | `BouncyCastle.Cryptography` 2.7.0 only (MIT, The Legion of the Bouncy Castle, `bc-csharp` commit `4007498b`, published 30 July 2026, the latest). Versions before 1.0.0 used NSec (libsodium); commit `dd7cd33` replaced it | nuspec; `dotnet list package --include-transitive`; `git show dd7cd33` |
| Ed25519 | `BouncyCastle` `Ed25519PublicKeyParameters.Verify(Ed25519.Algorithm.Ed25519, …)`, RFC 8032 pure Ed25519, in `TufMetadataVerifier.VerifyEd25519`. The same verifier also accepts ECDSA P-256/P-384, RSA-PSS and experimental ML-DSA (TAP 21) keys | source |
| Canonical JSON | Its own: `TufMetadataParser.CanonicalizeJson` re-parses the envelope with `JsonDocument`, sorts keys with `StringComparer.Ordinal` (UTF-16 order), escapes only `"` and `\`, copies numbers as written and does not normalize to NFC | source |
| JSON parsing | System.Text.Json with source-generated contexts (`TufEnvelopeJsonContext`, `TufMetadataJsonContext`), `PropertyNameCaseInsensitive = true`. No reflection-based serialization | source |

## Conformance

I drove the library through its `ITufRepository` interface with an in-memory transport. The transport serves each role as the canonical bytes of its fixture envelope. Mutated cases are re-signed with the signed fixture's public TEST-ONLY seeds, through BouncyCastle in the harness. 47 cases ran, with the same results under JIT and under Native AOT.

| Suite | Cases | Pass | Pass at the TUF step | Pass only around the library | Fail | Not expressible |
| --- | --- | --- | --- | --- | --- | --- |
| v1 fixture: rotation, refresh, 7 targets, expiry, tamper, identity, mirrors, resolution | 16 | 12 (one is the harness's own serving check) | | 1 (two mirrors) | 1 | 2 |
| Signed fixture `trust` section | 15 | 7 | 7 | | 0 | 1 |
| Profile rules built on the signed fixture's keys | 16 | 6 | | 2 | 7 | 1 |

What passes: every signature, threshold, link, version and digest check the fixtures pin. That covers the dual-threshold rotation 1→2, root rotation refused for a missing old threshold, a missing new threshold and a version skip, plus timestamp rollback, snapshot mix-and-match, all seven v1 targets, the 2020-expired timestamp, length and digest substitution of targets, and the re-linked unsigned targets. The 7 "TUF step" cases are `check-recipe` cases. The library reads the changed target and reports the right code; the case's binding path (`hosts[0].source`, `providers[0].descriptorTarget`) is morphirup's recipe check, not a TUF client's.

What fails or cannot be expressed:

| Requirement | Evidence | Result |
| --- | --- | --- |
| Expiry against one fixed trusted start time, with a supplied reference time | Seven `DateTimeOffset.UtcNow` reads in `TufClient.cs` (lines 533, 641, 655, 719, 896, 970, 983); no clock option. The v1 fixture's expired timestamp expires 2029-12-31T23:59:59Z: against the wall clock it was accepted, and the refresh failed later with an unrelated link mismatch | fail |
| Bounded downloads before parsing (64 KiB root and timestamp, 1 MiB snapshot, 8 MiB targets) | `HttpTufRepository` reads whole responses. A 70 KiB timestamp and a 70 KiB root, padded outside the signed bytes, were accepted. `FetchTargetAsync` receives no expected length, and the target length is compared after the whole body is in memory | fail; metadata bounds can be supplied by a transport decorator (passed) |
| Stop after 32 roots and ask to retry | `MaxRootRotations` (default 1024) silently ends the walk. With 32 and 33 newer roots, the refresh succeeded on root v33 | fail |
| `spec_version` compatible with 1.0 | Parsed, never checked; `2.0.0` was accepted | fail |
| `consistent_snapshot` must be true | Both layouts are supported, with no option; `false` was accepted | fail |
| Offline mode from durable state | `GetTrustedMetadataAsync` always fetches `timestamp.json`, so a client with no network fails. Serving the persisted cache back as the repository works: it re-verifies everything and fetches nothing | fail; passed around the library |
| Release state, channels, learned revocations (`custom.morphir`) | `TargetFileJson.Custom` is parsed and dropped; no API returns targets metadata. The only route is to re-parse the verified bytes the client hands to `ITufCache.StoreMetadata` | not expressible |
| Repository identity | Not computed; trivial outside the library | not expressible |
| Stable diagnostic codes | Only expiry is typed (`TufExpiredException`). Every other code needs the exception message. `JsonException` escapes untyped from parsing. The library cannot tell an offline miss from a missing file | fail |
| Mirrors and the integrity event before a retry | One `ITufRepository` per client; a retry means a new refresh through another transport | around the library |
| Consistent-snapshot target path | Built with `Path.GetDirectoryName`, so on Windows a custom transport receives `artifacts\desktop\1.0.0/<sha256>.windows-x86_64.zip`. `System.Uri` hides this for HTTP | fail (bug) |
| Ed25519 only | The verifier also trusts ECDSA, RSA-PSS and ML-DSA keys that a root lists, and accepts an `ed25519` key whatever its `scheme` | inspection; no option |

Each `GetTargetAsync` call also runs a full refresh: it re-requests the next root and `timestamp.json` before every target. That is safe, but one setup reading many targets is not one refresh with one start time.

## Native AOT and trimming

The probe was published with `-r win-arm64 -p:PublishAot=true`, with the trim, AOT and single-file analyzers on and `TrimmerSingleWarn=false`, which reports per call site. It produced no IL warnings at all: none from Tuf, BouncyCastle or the probe. The library uses only source-generated System.Text.Json; there is no reflection-based serialization.

| Executable | Bytes |
| --- | --- |
| Baseline, no library (fixture parsing, canonical encoding, SHA-256) | 1,728,000 |
| Baseline plus BouncyCastle Ed25519 used directly | 1,799,680 |
| Library, refresh only | 7,779,840 |
| Library with the conformance case runner | 8,118,272 |

So the library costs about 6.05 MB. The ILC map attributes about 1.17 MB to BouncyCastle, 1.07 MB to System.Net (`TufClient`'s constructor references `HttpTufRepository` even when a transport is supplied), 0.44 MB to System.Text.Json and 0.32 MB to Tuf. Upstream issue #57 measures 790 KiB of BouncyCastle kept alive by `PublicKeyFactory`. Ed25519 alone, used directly, is about 70 KB.

The published probe ran in a folder with a space in its path, with PATH set to the Windows system folders only and no `DOTNET_*` variable (`where dotnet` fails there). It completed the refresh, the seven target verifications and all 47 cases, with output identical to JIT.

## Gaps and how they could be closed

| Gap | Around the library | Needs a patch |
| --- | --- | --- |
| Injectable clock, one start time per refresh | No | Yes: a `TimeProvider` option, read once per operation |
| Metadata download bounds | Yes, a bounding transport | Better in the HTTP repository |
| Target download bounded by the declared length | Only by re-parsing cached targets bytes to learn lengths | Yes: pass the expected length to `FetchTargetAsync` |
| Typed failures for the stable codes | Only by message matching, which the library does not promise to keep | Yes |
| Targets metadata and `custom` | Only by re-parsing the bytes the cache receives, with its own parser | Yes: expose the verified targets |
| `spec_version`, `consistent_snapshot`, Ed25519-only scheme policy | After the fact only: the client has already trusted and persisted the metadata | Yes |
| 32-root stop with a retry signal | Yes: compare the persisted root version before and after | Cleaner as a result or an exception |
| Offline mode | Yes: replay the persisted cache as the repository | Cleaner as an option |
| Learned revocation enforcement | morphirup's job, over re-parsed verified bytes | Exposure of targets metadata |
| Mirrors and integrity events | Yes, one refresh per mirror | No |
| Windows target path separator | Yes, the transport normalizes `\` | Trivial fix |
| Repository identity | Yes | No |

Six rows need a change to the library. Upstream might accept such changes, since the project is small and active. Otherwise they mean a fork, and then we would own a TUF client anyway, in another project's design.

## The alternative: implement the profile's client subset

The profile needs less than a general TUF client: one key scheme, consistent snapshots only, top-level targets only (version 1 assigns no meaning to delegations), and a fixed set of roles. The pieces:

- **Canonical JSON.** `Morphir.Toolchain` already has the encoder the fixtures use (NFC, quote and backslash escaping only, UTF-8 byte-order keys, plain integers). It matches the plan digest fixture byte for byte.
- **Strict metadata readers** for root, timestamp, snapshot and targets. They check `_type`, `spec_version` compatibility, positive versions, the exact `expires` format, required `consistent_snapshot`, SHA-256 presence and `custom.morphir`, in the same style as the five toolchain document readers. About 400 lines.
- **Threshold verification.** Unique key IDs, keys of the role only, Ed25519 only, exact sizes. About 100 lines plus the primitive.
- **The update workflow.** Root rotation one version at a time with both thresholds, stopping at 32. Timestamp, snapshot and targets with signatures, rollback, links and version checks, expiry against one start time from an injected clock, and the persisted trusted set. About 500 lines.
- **Transport.** Mirror order, consistent-snapshot names, bounds before parsing, target length bounds while streaming, the integrity event before a retry, and offline refusal. About 250 lines.
- **Durable state.** The trusted root and the last trusted metadata, written atomically under Morphir Home, plus offline loading. About 200 lines.
- **Typed failures** mapped one-to-one to the stable codes, which the readers' style already gives.

That is about 1,500 to 2,000 lines of F#, plus tests. The test corpus exists: the v1 fixture, the signed fixture's `trust` cases, the 16 profile cases of this spike, and the official `tuf-conformance` suite, which upstream Tuf already drives through a small CLI. Allow about two to three weeks for implementation and tests, then a security review.

Ed25519 primitive candidates (.NET 10's `System.Security.Cryptography` has ML-DSA, SLH-DSA and composite ML-DSA, but no Ed25519 type; checked by reflection on 10.0.13 and 10.0.14):

| Candidate | AOT | Maintenance | Notes |
| --- | --- | --- | --- |
| `BouncyCastle.Cryptography` 2.7.0, `Ed25519PublicKeyParameters.Verify` | No warnings here; about 70 KB when used directly | Active; 2.7.0 published 30 July 2026; MIT | Pure managed, RFC 8032. Avoid `PublicKeyFactory` (upstream #57) |
| `NSec.Cryptography` 26.4.0 over `libsodium` | P/Invoke into a native library shipped per RID; not measured here | Active; April 2026; MIT; about 20 million downloads | Adds a native file beside the executable, or static linking work |
| Platform crypto (OpenSSL `EVP_PKEY_ED25519`) | Possible on Linux and macOS | n/a | No Windows CNG equivalent in .NET, so not portable |

Risks of each path:

- **Adopt `Tuf` as is.** Six profile rules remain unmet, two of them security-relevant: no download bounds, and expiry decided by the wall clock at several instants. Diagnostics would rest on message matching. It is a young single-maintainer project with AI-assisted commits, and it adds 6 MB.
- **Patch upstream.** Depends on one maintainer accepting API additions (clock, typed errors, targets access, length-bounded fetch, scheme policy). Until those land we would run a fork.
- **Implement directly.** We own security-critical code. The mitigations are a small scope, typed results, an injected clock, three independent test corpora including `tuf-conformance`, and the maintainer's review. The size cost is small.

## Recommendation

Implement the profile's client subset in `Morphir.Toolchain`, with BouncyCastle's Ed25519 used directly as the only cryptographic dependency. Run the finos/morphir fixtures, the cases of this spike and `tuf-conformance` against it. The library's verification logic is a useful reference, and could serve as a differential oracle in tests if the maintainer wants one, but it should not be the client. If the maintainer prefers a library, the six patches above are the conditions for re-evaluation, and the probe here re-runs them.

## Questions for the security reviewer

1. Is a project-owned TUF client subset acceptable, given the three test corpora above? Or is an external client mandatory whatever its gaps?
2. Is BouncyCastle's RFC 8032 Ed25519 acceptable as the only primitive? Specifically: its handling of non-canonical `S`, small-order and non-canonical points, and cofactored versus cofactorless verification.
3. Should a threshold count distinct key IDs (as Tuf does) or distinct public keys? Two key IDs naming the same key would otherwise count twice.
4. Must clients recompute key IDs from the canonical key? Neither Tuf nor the profile says so.
5. Which canonical JSON is the signing reference for non-ASCII keys? The fixtures' encoder sorts by UTF-8 bytes and applies NFC; Tuf sorts by UTF-16 units without NFC. They agree only on ASCII.
6. Are duplicate JSON members and case-insensitive member names (both accepted by Tuf's parser) to be refused before verification?
7. Is the trusted start time taken once per refresh from the system clock, and is a clock that goes backwards in time detected?
8. Where does durable trust state live under Morphir Home, and who may write it? Tuf's file cache trusts whatever it reads back.
9. Should an integrity failure at one mirror block retries at the next, or only be reported?
10. Is accepting extra schemes (ECDSA, RSA-PSS, ML-DSA) from a root a risk the profile must forbid in code, beyond "Ed25519 only"?
