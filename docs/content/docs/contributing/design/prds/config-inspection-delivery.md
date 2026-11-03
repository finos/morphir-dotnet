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

## Final local validation

The complete solution restore/build succeeds with zero errors and existing package/generated-code warnings, NU1608, NU1510 and CS0436. The initial full build reports 19 warnings; the incremental build after Unicode handling reports 9. This differs from the earlier baseline's 20 due to which projects/generated sources compile, not a claim of warning-free dependencies. New core/IO/Microsoft and serializer-host builds are warning-free. Logs `solution-build.log`, `final-solution-build.log`. `dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet` and Fantomas checks of every changed F# file pass. `git diff --check` passes.

Configured runner evidence is core 135, IO/BDD 171, Microsoft 22 and tooling 299, all passing. Eleven broader suites also pass: C# core 237, internal generation 25, models/roundtrips 791, pipeline 299, SDK 175, SDK conformance 27, pipeline plugins 141, F# backend 333, Ion 463, Myriad 4 and packaged Myriad integration 12. Logs `broader-*.log` and `broader-results.json` record each result. The packaged integration suite was requalified with one worker and a temporary dotnet wrapper adding `-m:1 -p:BuildInParallel=false` to nested build/pack operations, log `packaged-consumer-serial.log`. Earlier broad execution used two bounded TUnit workers, allowing internal integration builds to overlap before this corrected qualification. No unrelated process was stopped.

Current-source coverage union from `core-coverage/*/coverage.cobertura.xml` and `io-coverage.cobertura.xml` is core 3413/3565, 95.74%, and IO 639/662, 96.53%, above prior 95.38%/96.51%. The adapter's authored source is 118/119, 99.16%. Do not combine older core dependency coverage from the Microsoft checkpoint with newer source-line mappings.

Representative reproducible commands, run serially with physical temp environment:

```sh
export DOTNET_PROCESSOR_COUNT=2
export TMPDIR=/private/tmp/morphir-config-inspection
export TEMP="$TMPDIR" TMP="$TMPDIR"
dotnet build Morphir.slnx -m:1 -p:BuildInParallel=false
dotnet tests/Morphir.Configuration.Tests/bin/Debug/net10.0/Morphir.Configuration.Tests.dll --sequenced
dotnet tests/Morphir.Configuration.IO.Tests/bin/Debug/net10.0/Morphir.Configuration.IO.Tests.dll --maximum-parallel-tests 1 --coverage --coverage-output-format cobertura --coverage-output "$TMPDIR/io-coverage.cobertura.xml"
dotnet tests/Morphir.Tooling.Tests/bin/Debug/net10.0/Morphir.Tooling.Tests.dll --maximum-parallel-tests 2
dotnet test tests/Morphir.Configuration.Tests/Morphir.Configuration.Tests.fsproj --no-build --no-restore -m:1 --collect:"XPlat Code Coverage" --results-directory "$TMPDIR/core-coverage"
/usr/bin/python3 scripts/configuration/summarize-coverage.py "$TMPDIR"/core-coverage/*/coverage.cobertura.xml "$TMPDIR/io-coverage.cobertura.xml"
dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet
dotnet fantomas --check src/Morphir.Configuration/Documents.fs src/Morphir.Configuration/Resolver.fs src/Morphir.Configuration/Inspection.fs tests/Morphir.Configuration.Tests/InspectionTests.fs
```

Published host commands and dependency diagnostics are recorded in both smoke READMEs. Managed/trimmed/native Microsoft hosts pass 43 checks each; managed/trimmed/native serializer hosts pass 21 checks each. All stdout receipts parse as JSON and stderr receipts are empty. Canonical typed metadata remains available separately from the safe string projection.

Main was fetched again after validation and remains `23d274d4c87b367fdc79bd21843a9fdf5d74b13d`, so there is no interacting base advancement. Code checkpoint `5518fb9` contains the implementation, regression fixes and serializer. Later delivery commits integrate documentation, the configured runner and archive. Final PR/head/base identities are supplied in the parent handback and forge metadata.

## Specifications and parent handback

Apply was refreshed at each fulfilled task boundary and finally reports `all_done`, 8/8 tasks complete, in `apply-all-done.json`. Both new capabilities were synchronized from the approved deltas, including the confirmed nonfinite JSON scenarios. The new main specs are `openspec/specs/configuration-inspection/spec.md` and `openspec/specs/dotnet-configuration-integration/spec.md`. Their purposes and requirement/scenario bodies were compared byte-for-byte with the deltas after canonical title/Requirements-header normalization. No delta-operation headers remain in main specs.

Only `add-config-inspection` is archived at `openspec/changes/archive/2026-11-03-add-config-inspection/`; original `.openspec.yaml`, design, proposal, deltas and all checked tasks remain reviewable. Prerequisite/document links and the active editing prerequisite link were repaired. Editing, classic migration and workspace migration remain active. The archive CLI reports completed tasks with one nonblocking proposal suggestion to split more than ten deltas; this approved cohesive campaign remains one PR.

`mise run openspec:verify` passes managed-file checks, html-design schema validation, all 22 active/spec items and both extension tests. Evidence is `openspec-verify.log`; `spec-sync-validate.log` and `archive.log` record sync/archive checks.

The configured Nuke runner now includes the Microsoft suite and the linked-source serialization smoke using existing compiled artifacts. No new CI workflow or release/deployment behavior is added. New adapter/binder dependencies are pinned at Microsoft.Extensions.Configuration/Configuration.Binder 10.0.14; intentional central-transitive lockfile updates are retained, while publish-only RID/ILCompiler changes are restored. NOTICE and README describe the additive contribution and consumption paths.

Main was fetched immediately before PR preparation and remains the validated base. The checkout is retained for parent review and is committed clean for handback. All reproducible test/publish logs, coverage reports, smoke binaries and exact prepublish lockfile backups belong to this campaign under `/private/tmp/morphir-config-inspection/`; no unique source edits reside there. Parent owns PR review/current-head CI, merge/landed verification and owned worktree/local-branch/remote-branch/temporary-artifact cleanup. The campaign remains incomplete until those gates are fulfilled. No watch, merge, auto-merge, admin bypass, force-push, branch/worktree deletion, release or deployment was performed.

### Final published-host refresh

After the NaN selection and Unicode fixes, the adapter was rebuilt/published from final delivery checkpoint `569e7cf8fe1e7fc5ca8e2a9001e7cd3ac3895a72`. Managed/trimmed/native receipts all pass 43 checks, and the final adapter suite passes 22/22. Logs `final-microsoft-tests.log`, `final-microsoft-publish-trimmed.log`, `final-microsoft-publish-native.log`; JSON/stderr receipts use `final-microsoft-{managed,trimmed,native}`. Warning counts remain 44/47, exclusively pinned dependencies, with no new adapter/binder/serializer diagnostic. Captured lockfiles were restored exactly and the checkout was clean. The subsequent commit changes this receipt only. Final head/base/PR identities are recorded in the linked tracking issue and parent handback.

## Parent review regressions and qualification

Two current-head bot findings were reproduced before correction:

- `ca5b1ef`: ordinary effective and adjacent-user queries returned success despite
  `discovery.invalid_default` from automatic selection. Two subprocess scenarios
  failed with exit 0 rather than 2. The native inspection host now retains
  selection diagnostics for effective/project/user scopes. Four additional
  scenarios verify workspace, global, system and file scopes bypass unrelated
  automatic member selection. The full tooling runner passes 305/305.
- `082e66f`: a file-scoped classic manifest query included the synthetic
  checkpoint default because its default layer shares the manifest path.
  A public query regression failed before matching excluded Defaults roles.
  Get/list/show/path/explain now inspect the actual file layer; effective
  compatibility defaults and explicitly authored values remain available.
  The complete core runner passes 136/136.

The refreshed full solution build succeeds with zero errors and 20 existing
package/generated-code warnings. IO/BDD passes 171/171 and the Microsoft adapter
suite passes 22/22. The actual CLI DTO smoke adds three file-scope assertions;
managed, trimmed and native osx-arm64 hosts each pass all 24 checks with reflection
serialization disabled. New adapter/binder/serializer diagnostics remain absent;
the documented FSharp.Core warnings remain visible under explicit warning
qualification. Publish-generated lockfile churn was restored.

Fresh configured coverage reports under `parent-review` union to core 3417/3567,
95.79%; IO 639/662, 96.53%; Microsoft adapter 118/119, 99.16%. The summary is
`parent-review/coverage-summary.json`. Builds, subprocess tests and publishing
were bounded to two logical processors and serial heavy jobs. Both selection
and file-scope behavior are documented in the CLI and shared query references.
OpenSpec verification passes all 22 active/spec items, managed schema checks and
both extension tests. Changed-file formatting and whitespace checks pass.

All new evidence is owned under `/private/tmp/morphir-config-inspection/parent-review`.
Current-head CI and the independent query/provider reviews remain pending, along
with main landing and owned artifact cleanup. Blanket admin-bypass authorization
from the user remains in effect after checks and verified review fixes complete.
