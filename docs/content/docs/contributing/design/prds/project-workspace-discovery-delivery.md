# Project and workspace discovery delivery

Campaign 2 tracks [issue #466](https://github.com/finos/morphir-dotnet/issues/466).
Owned checkout: `/Users/damian/.t3/worktrees/morphir-dotnet/t3-2ee0222b`.
Branch: `t3/add-project-workspace-discovery`.
Prepared and current main baseline: `3d5b41c0f839c7b2648403ad6aba5c5644f7dfb5`.
The initial checkout was clean. Its tree is
`66609771da2eb88ac9e3cab3c21b6e8d201a6748`, the verified squash tree of
[prerequisite PR #461](https://github.com/finos/morphir-dotnet/pull/461).
The [parent completion receipt](https://github.com/finos/morphir-dotnet/pull/461#issuecomment-6598647710)
records campaign 1 landing and cleanup, including its 33 reproducible temporary
artifacts. That tree is a tree object, not a commit ancestry identifier.

The parent owns PR review/check watching, merge and cleanup. This worker delivers
one reviewable PR, without watching, merging, deleting branches/worktrees,
force-pushing, publishing or deploying. Campaign completion remains pending until
parent landing and cleanup receipts exist. Future campaigns remain active.

## Validation checkpoints

- Initial BDD scenario describes a member primary below a workspace. The capped
  adapter test build fails with CS0246 for the absent `InMemoryProjectTree` and
  `ProjectDiscoveryResult` APIs. No discovery implementation exists at this red
  checkpoint. Logs are in `/private/tmp/morphir-discovery-tests/`.
- All builds use `DOTNET_PROCESSOR_COUNT=2`, single-node MSBuild, disabled
  parallel restore/build, and one heavy job at a time. Executable TUnit/Reqnroll
  uses one worker and physical macOS temporary paths. Core uses Expecto.
- `e849db3` records initial red; `7299331` records first passing API slice.
- Root ambiguity was observed red with all 15 layout/extension cases green.
  Boundary group then exposed outside-start host access and missing diagnostics
  for a directory at a primary filename. Both regressions were made green.
  An incomplete classic test fixture was corrected to a canonical project after
  error propagation revealed its required `exposedModules` field was absent.
- Membership group was observed red: 15/110 failed for globs/exclusions,
  root/virtual identity, workspace.root, invalid/missing literals, duplicate/path
  selection, defaults and bounds. After implementation adapter 110/110 passes,
  zero build warnings/errors. Native adapter, scoped loading and qualification
  remain pending. Evidence logs use `red-members-*` and `green-members-*`.
- Machine planner first failed compilation for absent APIs, then Linux 1/1
  passed. Expanded platform tests ran 14 cases with 4 pass, 7 fail, 3 error;
  after implementation 14/14 passed. Overlay/ambiguity missing APIs were observed
  red before implementation. Builds are serialized by the primary worker.

## Adapter and loading checkpoints

- `bd325c2` is an external checkpoint of the then-current files, followed by owned
  `db17ca1`. Both are preserved. The source-planner and documentation helpers
  confirm they ran no staging or commit commands.
- Native adapter parity/link tests first failed compilation for the missing
  filesystem adapter; after implementation adapter 112/112 passed. Tests compare
  complete immutable discovery and selection results across native and memory
  hosts, then compare complete loading results as well.
- Loading first failed compilation for the absent API and options. After
  implementation adapter 121/121 passed. Pure core passed 124/124, including the
  new platform/overlay planner's 28 cases and a resolver ordering regression.
  That regression failed at runtime with workspace personal TOML beating member
  personal YAML. The separate member-personal stage now wins across formats.
  The earlier substring-only Expecto filter selected zero tests; the recorded
  red evidence reran the full `Resolution` list and ran 31 cases, 30 pass/1 fail.
- Canonical schema failures, requesting below member links and member-personal
  workspace output relocation produced 4 red failures, then 125/125 passed.
  Isolation with supplied machine sources produced 1 red failure, then 127/127
  passed. Directory enumeration failure lacked source evidence in a 1/128 red
  run; after implementation adapter 128/128 passed. Focused builds have zero
  warnings/errors. Native directory probes never open discovered links.
- Implementation tasks 2.1 through 3.2 are fulfilled. Apply instructions were
  refreshed after every checkbox update. Broader compatibility, runtime,
  coverage, formatting and final archive qualification remain pending.

## Broad verification and semantic audit

- `b6ff7c5` protects bounded native/mounted loading and its initial red/green
  regressions. Full solution build succeeds with the same 20 existing package
  and generated-code warnings and zero errors.
- All 14 configured Nuke-target suites pass serially using compiled Debug outputs:
  C# core 237, tooling 245, configuration core 124, adapter/BDD 128 at that
  checkpoint, internal generation 25, models/roundtrips 791, pipeline 299, SDK
  175, SDK conformance 27, pipeline plugins 141, F# backend 333, Ion 463,
  Myriad 4, and Myriad integration 12. See `suite-results.json` and each suite's
  log under the owned temporary root. TUnit uses one worker, Expecto sequenced.
- A read-only semantic audit then produced concrete regression tests. Adapter
  run `red-audit-run.log` has 9 failures out of 137: explicit disabled profiles
  inherited premature discovery errors, host warning suppression leaked discovery
  warnings, sibling literal members with exclusions crashed, sibling globs were
  missed, UNC authority was lost, build-named projects were implicitly pruned,
  supplied default layers were falsely ambiguous, and two permitted owner-output
  overlays left stale context. All nine are fixed; adapter 137/137 passes.
- A follow-up invalid owner-output overlay failed from a member load, then passed
  after retaining workspace validation errors in the owning context. Adapter
  138/138 and core 124/124 pass. Discovery validates canonical declarations without
  interpreting profile activation or precomputing suppressible format warnings.
  Member inventories now honor within-boundary sibling literals/globs and declared
  build-named directories. UNC paths retain their authority. Root personal/profile
  output settings produce the same output context for root and member loads.
- Fantomas formats owned F# files; C# whitespace formatting covers owned C# files.
  Full `dotnet format Morphir.slnx --no-restore --verify-no-changes --verbosity quiet`
  passes; unsupported F# projects are checked separately with Fantomas.
- Final full-solution rebuild after these fixes succeeds with zero errors and the
  same 20 existing warnings. Configured coverage runners pass core 124/124 and
  adapter/BDD 153/153. The additional boundary cases cover selected-file misses,
  unsupported formats, outside paths, personal access/link evidence, absent machine
  authorization, relative Morphir home, invalid selection and native permission
  failures. Their source-line union is core 3010/3172 = 94.89% and authored
  adapter 624/647 = 96.45%, above campaign 1's 94.13% and 94.23% rates.
  Reproduce with `scripts/configuration/summarize-coverage.py` over
  `core-coverage/*/coverage.cobertura.xml` and `io-coverage.cobertura.xml`.
- Consumer audit confirms ManagementScenario and ConfigResolver still reference
  the unchanged legacy WorkspaceDiscovery. No existing caller was migrated to
  DiscoveryPolicy or ProjectSystem. Task 3.3 is fulfilled and apply reports 7/8.
