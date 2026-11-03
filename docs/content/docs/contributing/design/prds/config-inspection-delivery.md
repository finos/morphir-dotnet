# Configuration inspection delivery

Campaign 3, [issue #468](https://github.com/finos/morphir-dotnet/issues/468).

## Baseline and ownership

Clean T3-prepared checkout `/Users/damian/.t3/worktrees/morphir-dotnet/t3-a74ab7cb`, branch `t3/add-config-inspection`, based on current main `23d274d4c87b367fdc79bd21843a9fdf5d74b13d`. Fetch and ancestry checks confirm campaign 1 landed at `3d5b41c0f839c7b2648403ad6aba5c5644f7dfb5` and campaign 2 at this base.

[Campaign 2 completion receipt](https://github.com/finos/morphir-dotnet/pull/467#issuecomment-6600694475) records prerequisite landing and owned cleanup. Campaign 1 receipt is [recorded separately](https://github.com/finos/morphir-dotnet/pull/461#issuecomment-6598647710). Prior evidence does not replace validation of this campaign.

Only inspection is implemented here. Editing and migration remain active plans. Parent owns reviews, check monitoring, merge and subsequent owned cleanup. No watch, merge, bypass, force push, branch/worktree deletion, deployment or release is performed by this implementation agent.

## Execution policy

Heavy .NET jobs run serially with `DOTNET_PROCESSOR_COUNT=2`, MSBuild `-m:1` and bounded workers. Filesystem tests use physical `TMPDIR`, `TEMP`, `TMP` under `/private/tmp/morphir-config-inspection`. Reproducible logs and binaries live there. `docs/superpowers/` is locally excluded and never staged.

The existing resolution result and constructor remain compatible. Additive detailed resolution retains canonical layers and applied values for queries without rereading sources. Microsoft projection consumes one resolved document.

## Red checkpoints

- Shared tracer compile failed because `resolveDetailed` and `Inspection` did not exist, then its runtime test failed against a minimal `get` stub: effective global value expected but missing. Logs `core-compile-red.log`, `core-runtime-red.log` under the owned temp root. The configured Expecto runner selected `Inspection` with `--filter-test-list Inspection --sequenced`.
- Microsoft adapter compile failed for missing projection API, then executable TUnit/Reqnroll ran 13 cases against no-behavior stubs: 13 failed. Logs `microsoft-compile-red.log`, `microsoft-runtime-red.log`.

- Shared query cases grew incrementally after the tracer: quoted-path compile red then 2 green; recursive secret/container redaction runtime red then 3 green. List/source-report/explain stubs then fail 3 of 7 cases, followed by embedded-host Reqnroll red 3/4. Logs `keypath-*`, `redaction-*`, `queries-red*`, `shared-bdd-red*`. These cover empty values, sorted quoted entries, source statuses, scoped ownership and member replacement history.
- CLI configured subprocess runner executes all five read verbs against the actual unregistered CLI: 29/29 fail before production registration. Logs `cli-red-build.log`, `cli-red-test-build.log`, `cli-red.log`.

## Shared query green checkpoint

Core 131/131 and IO/BDD 170/170 pass using executable configured runners, with physical temp paths for filesystem tests. Both builds have zero warnings/errors. Logs `core-green-build.log`, `core-green.log`, `shared-bdd-green-build.log`, `shared-bdd-green.log`. The shared API reference is `docs/content/docs/configuration/inspection.md`. Existing `DocumentResolution` and cache-only APIs remain intact; additive detailed resolution retains canonical and applied layers without extra reads.

## Additional behavior checkpoints

Selected-personal query-scope regression failed 1/10 before preserving its discovered scope after promotion to the selected layer. Core then passes 134/134. Shape-blocking and composite-winner explanations and safe parser-diagnostic presentation also pass. Logs `selection-red*`, `selection-green*`.

CLI initially passes 34/35 BDD subprocess cases and 9 observer cases; the remaining deliberately red case proves unrelated malformed appsettings were loaded by the builder during help. The help-only builder path disables host defaults; ordinary execution defaults remain compatible. Log `cli-host-help-red.log` records that failure before the fix.

## Microsoft adapter checkpoint

Adapter managed tests pass 22/22 after runtime RED13/13 and boundary RED5/22. Added tests prove earlier host providers cannot leak stale array indices or descendants under replacement values; unrelated host siblings remain. Empty .NET key segments are rejected alongside case/separator collisions. Builds are warning-free. Authored adapter coverage is 118/119 lines, 99.16%.

The non-CLI managed, trimmed and native hosts each pass 43 checks, including concrete source-generated/explicit binding, missing versus empty arrays, nested values, collision diagnostics, canonical access and bounded native-tree loading. See `tests/Morphir.Configuration.Microsoft.Smoke/README.md` for exact commands. Logs `microsoft-publish-trimmed.log` and `microsoft-publish-native.log` retain 44/47 pinned CsToml/FSharp.Core warnings respectively; no new adapter/binder/serialization diagnostics occur. Runtime qualification uses `ILLinkTreatWarningsAsErrors=false`; strict warning-free publishing remains unsupported by those pinned dependencies. Publish RID lockfile churn was restored from exact prepublish bytes.

## CLI, numeric JSON and host-help checkpoint

Full configured runners now pass core 135/135, IO/BDD 171/171 and tooling 299/299. Tooling includes 38 subprocess BDD cases and 16 observer tests. Every JSON subprocess report validates `docs/spec/schemas/config-inspection-result.schema.json`. The actual CLI default registration is exercised, not a test-only command tree. Existing IR/cache/management behavior remains covered by the full tooling suite. Logs `final-core.log`, `final-io.log`, `final-tooling.log`.

Additional runtime RED checkpoints cover four empty selectors reaching discovery, invalid environment profile names reaching malformed source reads, unpaired Unicode escapes throwing instead of reporting usage, and a discovered NaN-bearing member rejected by F# structural equality. Empty selectors and profile preflight now fail before the host edge. Key-path parsing catches invalid surrogate decoding. Loading accepts the actual discovered record by identity before its compatibility structural-equality check. Public regression and both NaN CLI scenarios pass; no fixture was weakened. Logs `cli-empty-controls-red.log`, `cli-environment-profiles-red.log`, `unicode-red.log`, `nan-member-red.log`.

The user confirmed the typed JSON detail for NaN/infinities. Nonfinite numbers are tagged `{"$number":"NaN"}`, `{"$number":"Infinity"}` or `{"$number":"-Infinity"}`. A separate envelope `valueTypes` JSON Pointer discriminator identifies actual numbers. Ordinary tag-shaped objects carry no number metadata; no user key is reserved. Finite values retain JSON types, temporal kinds remain metadata, and shared redaction precedes encoding. `serializer-red.log` fails in Utf8JsonWriter before implementation. The actual CLI DTO source is linked into a small host with reflection serialization disabled, not duplicated. Managed, trimmed and native hosts each pass 21 checks, including all nonfinite cases, tag-shaped user objects, nested arrays, pointers, redaction and contribution/entry metadata. See `tests/Morphir.Configuration.Inspection.Smoke/README.md` for commands. Its trimmed/native logs retain 42/47 FSharp.Core warnings and no new serializer diagnostics. Exact prepublish lockfile bytes were restored.
