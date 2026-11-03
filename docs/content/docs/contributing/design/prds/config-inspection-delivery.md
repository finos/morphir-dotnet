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


### Parent provider review fixes

The independent provider review found two host-boundary defects at `45196ca`.
Machine configuration candidates were all assigned the invocation volume, so
Windows invocations on D: could not read ordinary C: system/user locations or
redirected UNC roaming locations. `MachineBoundaries` now plans distinct roots
without observing them, and the native host explicitly authorizes those roots.
`ProjectLoadOptions.MachineTrees` routes candidates within the supplied hosts;
the existing single-tree API remains compatible. No project boundary is widened,
and missing authorization still yields a structured read error without a read.
Eight portable regressions cover cross-drive/UNC reads, relative MORPHIR_HOME,
Unix root planning, invalid relative locations, missing authorization and isolation.
The initial build failed on the missing multi-root contract; all eight now pass.

Two new CLI BDD cases reproduced process aborts, exit 134, when malformed host
appsettings were present. The builder retains supported initialization failures
for an opted-in configuration provider using a minimal fallback host. Commands
validate arguments and profile controls before rendering a generic `config.load`
failure, one JSON result and exit 2. Invalid arguments retain `config.usage`.
Help remains read-free and other command defaults retain their existing behavior.
Both initially failing subprocess cases now pass. Logs are `host-red.log`,
`host-green.log`, `machine-red-build.log` and `machine-green.log` in `parent-review`.


Final host-fix validation passes core 136/136, IO/BDD 179/179, Microsoft adapter
22/22 and tooling 307/307. The tooling suite includes the two new initialization
subprocess regressions. An initial broad tooling run used macOS's symlinked
default temporary path and failed filesystem boundary checks; rerunning with
physical `TMPDIR`, `TEMP` and `TMP` passed all 307 cases. No product change was
made for that environment issue. Full solution build succeeds with zero errors
and 9 existing warnings on this incremental invocation. Current-source coverage
union is core 3420/3567, 95.88%, and IO 662/685, 96.64%; the unchanged Microsoft
adapter retains its 118/119, 99.16% qualification.

The IO smoke now exercises both cross-drive and UNC machine hosts in managed,
trimmed and native osx-arm64 binaries, plus all 228 JSON5 syntax cases and
26 pinned Morphir fixtures. Each receipt reports `machine_roots: true`, success
and empty stderr. Existing CsToml/FSharp.Core linker diagnostics remain visible
under the documented warning qualification; no authored IO/boundary warnings
were introduced. Publish-only lockfile changes were inspected and restored.
Logs and receipts are owned under `parent-review`, including `host-final-build.log`,
`io-host-full.log`, `tooling-host-full.log`, `machine-*-publish.log` and
`machine-*-result.json`. Changed-file formatting and whitespace checks pass.

OpenSpec verification was refreshed after the host fixes and passes all 22
active/spec items and both extension tests. Current-head CI and the independent
query review remain pending before the authorized landing and cleanup.


### Windows CI follow-up

The failed Windows job in run `41624211494` used the superseded `45196ca` head.
Twenty-one non-isolated subprocess cases failed with C:/ProgramData read errors
from the D: invocation-volume authorization bug, fixed in `740aeba`. One isolated
explanation case passed execution but compared canonical '/' source paths with
native Windows backslash paths. Both the explanation and origin assertions now
construct expected canonical paths through `ProjectPath.join`. This changes only
test expectations; public report paths retain the shared canonical contract.
The Ubuntu/macOS jobs were cancelled by the matrix's default fail-fast behavior
after Windows failed. Current-head checks must qualify the replacement commit;
none of those older results are treated as current-head evidence. The downloaded
failure log is retained under `parent-review/ci-windows-45196ca.log`.

The affected explanation and origin subprocess cases each pass after this
test-only correction, and the tooling test build, changed-file formatter and
whitespace checks pass. Native Windows confirmation remains CI-owned.


### Parent query review fix

The independent query review found that supported deep values failed inspection
presentation at `45196ca`: readers accept 128 levels, while both the intermediate
JsonDocument and generated report serializer retained System.Text.Json's default
64-level limit. A linked-source smoke regression first failed with a reader-depth
exception, `depth-red.log`. Raising only the value writer/parser limit then
failed at the report envelope's generated serializer, `depth-envelope-red.log`.
The common 256-level limit now covers the writer, JsonDocument and generated
context, allowing the reader limit plus report nesting and typed numeric tags.

The smoke host adds 24 checks across 65- and 127-segment nested objects through
show, subtree get and explain. It checks successful JSON, deepest-leaf numeric
tags/type metadata and redaction before encoding. Managed execution passes all
48 checks. Two additional CLI BDD cases load real TOML at those same depths and
validate the published JSON schema, leaf values, exit code and redaction. Both
pass. The CLI reference documents an appropriate depth limit for consumers.
The disposable review probe directory is
`/private/tmp/morphir-config-inspection-review-queries`.


Depth qualification is complete: managed, trimmed and native osx-arm64 serializer
hosts each pass all 48 checks with reflection serialization disabled and empty
stderr. The full tooling suite passes 309/309, including 48 subprocess scenarios.
Full solution compilation and changed-file formatting pass. Only documented
FSharp.Core dependency linker diagnostics remain; publish-only lockfiles were
inspected and restored. Receipts are `parent-review/depth-*-result.json`.

### Integration with updated main

Main advanced to `4df3c1ad425f782bfe3cc02e30f577d87dca0f53` with the canonical
F# SDK package migration, PR #469. The SDK runtime moves to a canonical project
while preserving its namespace and assembly identity. It does not change the
configuration libraries, readers, Microsoft adapter or serializer sources, so
their native qualification remains applicable. The solution/README auto-merged;
NOTICE had an append conflict, resolved by preserving the entire SDK notice
followed by the entire configuration notice. No history was rewritten.

The combined tree builds with zero errors and 20 existing warnings. All 16
configured runners pass sequentially with one TUnit worker, sequenced Expecto,
two logical processors and a dotnet wrapper serializing nested builds/packs.
Results: C# core 237, tooling 309, configuration core 136, IO/BDD 179, Microsoft
adapter 22, serializer 48 checks, internal generation 25, model/roundtrips 791,
pipeline 299, SDK 403, SDK conformance 27, pipeline plugins 141, F# backend 333,
Ion 463, Myriad 4 and packaged Myriad integration 12.

Two SDK package-consumer checks initially failed because the installed
PowerShell shim had no selected version. Process-local selection of PowerShell
7.5.3 allowed them to run; the final ownership check then found four stale SDK
DLLs under this checkout's pre-migration ignored bin/obj outputs. Those two
ignored directories contained no tracked files and were removed. The unchanged
SDK suite then passed 403/403, including both package versions and all canonical,
legacy, combined, generated-source and precompiled-binary consumers. No SDK
product code or global tool configuration was changed for these environment
issues. Original failed receipts remain separate from the successful rerun.

`parent-review/integrated-final-results.json` indexes the final receipts, including
`integrated-sdk-final.log`. The first unselected-shim attempts named two disposable
paths outside the main evidence directory; neither contains product work:
`/private/tmp/morphir-fsharp-sdk-packages-a13f59919fc0435dbd9ec893900a2f45` and
`/private/tmp/morphir-fsharp-sdk-packages-f52a160b844543a4859e20842d67a7c9`.
Other package evidence is under the owned `parent-review/sdk-package-temp`.
OpenSpec verification on the combined tree passes all 22 active/spec items and
both extension tests. Both independent reviews are complete and their reported
findings are fixed. Fresh current-head CI, landing and artifact cleanup remain.


### Concurrent schema-validation harness fix

Current-head macOS run `41630213426`, head `8118ff0`, failed one tooling BDD
case, quoted literal-dot paths, while constructing the published inspection
schema. JsonSchema.Net reported `RefResolutionException` for a generated local
URI ending in `#/$defs/entry`. The CLI execution itself had succeeded. All other
suites passed; matrix fail-fast cancelled Ubuntu and Windows. The full failed
job log is `parent-review/ci-macos-8118ff0.log`.

Inspection scenarios had called `JsonSchema.FromText` without BuildOptions,
sharing the library's global registry during concurrent schema construction.
The pinned JsonSchema.Net 8.0.5 source uses an unsynchronized Dictionary for
that registry. Both the [pinned BuildOptions source](https://github.com/json-everything/json-everything/blob/3520d7ac43e5c6c9b91abeac5af992eb82ffbf63/src/JsonSchema/BuildOptions.cs)
and [registry source](https://github.com/json-everything/json-everything/blob/3520d7ac43e5c6c9b91abeac5af992eb82ffbf63/src/JsonSchema/SchemaRegistry.cs)
confirm this shared mutable path; the [author's documentation](https://docs.json-everything.net/schema/basics/)
recommends separate registries for concurrent builds or conflicting registrations.
The test harness now supplies a fresh SchemaRegistry per schema, matching the
existing production SchemaLoader pattern. No CLI/product, schema, dependency,
workflow, coverage or scheduling policy changes are involved.

The race is intermittent: the unchanged local 48-case BDD run with four workers
passed, and a 512-build global-registry probe did not reproduce the CI failure.
The actual CI failure remains the red evidence. After isolation, all 309 tooling
tests pass with four workers, including all 48 configuration subprocess cases,
and an isolated 512-build probe passes. Test compilation, formatting and
whitespace checks pass. Successful receipts are
`schema-registry-tooling-full.log` and `schema-registry-probe-isolated.log`;
original baseline receipts remain separate. The earlier integrated full-suite
and native qualifications still apply because this fix only changes the test
schema-construction boundary. Fresh current-head CI remains required.
