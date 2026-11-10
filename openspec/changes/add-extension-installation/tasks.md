# Tasks

This is the implementation breakdown of the .NET extension installer (extension-model PRD step 3). Each checkbox is one small pull request. The indented notes under it name the first failing test, the test project that carries it and the narrowest command; they are planning detail and are not tracked.

GitHub issue: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort.
<!-- TRACKING: link the dedicated installer issue here once it exists. -->
No Beads id is recorded; do not invent one.

Dependencies:

- Can start now: group 1, group 2, group 3 (after 2.7), group 4 (after group 3), 5.1.
- Waits on a maintainer answer in the design: 3.1 (Q-LOCKPRIM), 5.2 (Q-ARCHIVE, Q-PROVENANCE, Q-DESCNAME), 5.3 (Q-AUTOPROCESS), group 6 (Q-LEVELS; 6.3 also Q-OVERLAY; 6.5 also Q-CONFIGREF). An answer that differs from the recommendation changes the named specs first, through task 1.1.
- Waits on other work: 5.4 on PRD step 2a (a WASM runtime in this host); group 8 on the finos/morphir change that allows dotted ids in the descriptor contract (not written yet); 7.2 on the adoption change's group 4 and the separately evaluated TUF client. Q-MULTIFILE has no task here; its answer gates approved task 3.3, not this change.
- The adoption change consumes this one: approved 3.3 needs 5.2 and 7.1, approved 3.4 needs 6.4, approved 4.4 needs 6.3.

Commands follow the repository's build settings. Build one project with `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`. Run Expecto assemblies with `dotnet exec <dll> --filter-test-list "<list>"` and TUnit assemblies with `dotnet exec <dll> --treenode-filter "<filter>"`, as `build/Build.Testing.cs` does. `<inst>` below stands for `tests/Morphir.Extensions.Installation.Tests/bin/Release/net10.0/Morphir.Extensions.Installation.Tests.dll` and `<tooling>` for `tests/Morphir.Tooling.Tests/bin/Release/net10.0/Morphir.Tooling.Tests.dll`.

## 1. Decisions and upstream inputs, no code

- [ ] 1.1 Record the maintainer's answer to each question in `design.html` (Q-LOCKPRIM to Q-FIXTURES) beside the question, and change the specs and tasks where an answer differs from the recommendation; verify with `mise exec -- openspec validate add-extension-installation --strict` and `mise run openspec:verify`.
- [ ] 1.2 Raise the upstream gaps in finos/morphir (Q-LOCKPRIM, Q-JOURNAL, Q-STRICTLOCK, Q-CATVER, Q-ARCHIVE, Q-MULTIFILE, Q-PROVENANCE, the device-name part of Q-IDRULE, Q-PLATFORM, Q-CLAIMCHECK, Q-REQHOST, Q-NUMBERS, Q-FIXTURES) and the PRD superset proposal (levels, staged folders, package archive, install mode); verify that each question in `design.html` links its issue or proposal.
  - Do not write issue numbers before they exist.

## 2. Installation library and pure readers

- [ ] 2.1 Create `src/Morphir.Extensions.Installation` (F#, referencing `Morphir.Extensions` and `Morphir.Mep`, not `Morphir.Tooling`), `tests/Morphir.Extensions.Installation.Tests` (Expecto) and an AOT probe `tests/Morphir.Extensions.Installation.AotProbe` run in the pull request lane like `TestMepAot`; verify the solution builds and the probe publishes with Native AOT with zero IL2026 and IL3050 warnings.
  - Files: `Morphir.slnx`, `build/Build.Testing.cs` (test project lists), the probe target beside `TestMepAot`.
  - Follow the `fsharp-csharp-interop` skill for every public member C# will call.
- [ ] 2.2 Add distribution identifiers: the id rule by document version, artifact file names, declared relative paths, channels, selections and SHA-256 digests; verify the `Identifiers` tests pass.
  - First failing test: `con`, `com1`, `a-`, `A`, 239 characters and the dotted `org.finos.morphir.fsharp-frontend` under `2.0.0-draft.2` are refused with `extension.id_not_portable`; `morphir-elm` is accepted. Cases from the toolchain identifier and target path tables.
  - Command: build `tests/Morphir.Extensions.Installation.Tests/Morphir.Extensions.Installation.Tests.fsproj`, then `dotnet exec <inst> --filter-test-list "Identifiers"`.
- [ ] 2.3 Add the platform table: RID to descriptor triple to index `os` and `arch`, with musl refused; verify the `Platforms` tests pass.
  - First failing test: each row of the toolchain platform table maps both ways; `linux-musl-x64` gives `extension.platform_unrepresentable`.
  - Command: `dotnet exec <inst> --filter-test-list "Platforms"` after the build above.
- [ ] 2.4 Add claim sets that keep their JSON, compare as parsed values with ordered arrays and Rust number equality, convert draft.1, check `critical` and evaluate `requires.host` comparators; verify the `Claims` tests pass.
  - First failing test: `{"a":1}` differs from `{"a":1.0}`; `1.0` equals `1.00`; key order is ignored; array order is not; a draft.1 `statement` with `statementSource` `declared` reads as `claims` with `claimCheck` `unchecked`; a mixed record is refused.
  - Reuse the `Morphir.Mep` claim set type for the typed view; keep the raw JSON beside it.
  - Command: `dotnet exec <inst> --filter-test-list "Claims"`.
- [ ] 2.5 Add index records, JSONL histories and pure resolution; verify the `Resolution` tests pass.
  - First failing test: a history with `1.0.0+a` and `1.0.0+b` is refused naming both; stable skips a prerelease listed on `stable`; a release whose `requires.host` excludes the host is skipped for an older one; only-`0.1` releases give `extension.no_compatible_mep_version`; two artifacts for one platform are refused.
  - Command: `dotnet exec <inst> --filter-test-list "Resolution"`.
- [ ] 2.6 Add release descriptor reading for version 1 and the exact version 2 drafts, with the platform-differences check; verify the `Descriptors` tests pass.
  - First failing test: two artifacts whose claims differ at `capabilities.backend.targets` with `platformDifferences` `none` are refused naming that member; a descriptor mixing `wasm` and `process` is refused; integer `schemaVersion` 1 reads as version 1.
  - Command: `dotnet exec <inst> --filter-test-list "Descriptors"`.
- [ ] 2.7 Add catalog, global lock and journal codecs that write the upstream member sets and read with must-ignore, including the catalog version switch; pin interoperability fixtures with provenance; verify the `StateCodecs` tests pass.
  - Fixtures: `tests/Fixtures/ExtensionAcquisition/` with `frontend-catalog-v1.json` and `frontend-lock-v1.json` copied from `ecosystem/morphir-rust/crates/morphir-distribution/tests/fixtures` at submodule `c9f79da`, and a catalog, lock, journal, history and descriptor written by the pinned Rust CLI. The README names each source, commit and SHA-256 and the capture commands. Replace them with finos/morphir data when Q-FIXTURES lands.
  - First failing test: reading `frontend-lock-v1.json` and writing it back gives the same member set and values, with `"platform": null`; the lock writer never adds a top-level member outside the upstream list; a catalog with a supplied claim set is written as `"2.0.0-draft.2"`.
  - Command: `dotnet exec <inst> --filter-test-list "StateCodecs"`.

## 3. Shared state on disk

- [ ] 3.1 Implement the state lock on `locks/extensions.state.lock` with the reference primitive (Q-LOCKPRIM), its waiting line and cancellation; verify the `StateLock` tests pass on Windows and on Linux or macOS.
  - Add a small helper process, `tests/Morphir.Extensions.Installation.LockHelper`, that takes the lock with `LockFileEx` on Windows and `flock` elsewhere, as `fs2` does.
  - First failing test: while the helper holds the lock, an acquisition prints one waiting line and blocks; while .NET holds it, the helper's non-blocking attempt reports contention, not an I/O error.
  - Command: build the helper and the tests, then `dotnet exec <inst> --filter-test-list "StateLock"`.
- [ ] 3.2 Implement atomic file replacement and the transaction journal with rollback and recovery at every lock acquisition; verify the `Transactions` tests pass.
  - First failing test: with a writer seam that fails after the lock write, the next acquisition finishes the journal and the pair agrees; a journal in the reference format from the 2.7 fixtures is applied and removed.
  - Command: `dotnet exec <inst> --filter-test-list "Transactions"`.
- [ ] 3.3 Implement the content-addressed store: staged temporary file, no-clobber publication, rehash before reuse, containment inside Morphir Home, the 256 MiB limit and the Unix owner execute bit; verify the `Store` tests pass.
  - First failing test: a tampered object fails with `extension.store_object_mismatch` and is unchanged; a linked digest directory fails with `extension.path_escape`.
  - Command: `dotnet exec <inst> --filter-test-list "Store"`.
- [ ] 3.4 Implement the .NET inventory below `data/morphir-dotnet/extensions/installed/` and its rebuild from a catalog and lock pair; verify the `Inventory` tests pass.
  - First failing test: after a project-level install of 0.2.0 over a global 0.1.0, the inventory has both digests and the global pair still names 0.1.0.
  - Command: `dotnet exec <inst> --filter-test-list "Inventory"`.

## 4. Install from a named repository, and the commands

- [ ] 4.1 Read and write the upstream repositories file under its lock, and add `extension repository add|list|remove`; verify the repository command tests pass.
  - First failing test: `RepositoryCommandTests` in `tests/Morphir.Tooling.Tests/Features/Extensions`: `add` records the canonical directory as an enabled `local-directory` endpoint; a directory without `extensions/` is refused and the file is unchanged.
  - Command: build `tests/Morphir.Tooling.Tests/Morphir.Tooling.Tests.csproj`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/RepositoryCommandTests/*"`.
- [ ] 4.2 Implement the pipeline without a probe: resolve, check repository containment, stage and hash, store, commit, report with `authentication` kind `local-developer` and `claimCheck` `unchecked`; verify the `Pipeline` tests pass.
  - First failing test: a repository artifact whose bytes differ from its record fails with `extension.digest_mismatch` and nothing is written to the store, catalog, locks or inventory; a linked artifact fails with `extension.path_escape`.
  - Tests pass `MORPHIR_HOME` explicitly to a temporary directory; none reads the user's home.
  - Command: `dotnet exec <inst> --filter-test-list "Pipeline"`.
- [ ] 4.3 Add the bounded describe probe and the session fallback, and record `claimCheck` from this install only; verify the `Probe` tests pass.
  - Add a test guest, `tests/Morphir.Extensions.TestGuest`, built on the `Morphir.Mep` guest loop, with modes: agree, lie, hang, junk on stdout, answer `-32601`, and spawn a child.
  - First failing test: the lying guest fails with `extension.claims_mismatch` naming `claims.capabilities.backend.targets`; the hanging guest and its child are ended after the deadline with `extension.probe_failed`; an index record that says `probed` installed with `--no-probe` is recorded `unchecked`.
  - Record the deadline and environment allow-list in the design's open questions when chosen.
  - Command: build the test guest and the tests, then `dotnet exec <inst> --filter-test-list "Probe"`.
- [ ] 4.4 Wire `extension install`, `update`, `uninstall`, `remove`, `info` in both hosts, with JSON reports on stdout, messages on stderr and exit codes 0, 1 and 2; remove the extension handlers, `ExtensionCommands.cs` and the extension use of `ArtifactPathResolver` and `OsPaths`; verify the command tests and the E2E feature pass in `morphir` and `dotnet-morphir`.
  - Files: `src/Morphir.Tooling/Scenarios/ManagementScenario.cs`, `src/Morphir.Tooling/Features/Extensions/`, `src/Morphir.Tooling/Features/Management/` and its tests in `tests/Morphir.Tooling.Tests/Features/Management`.
  - First failing tests: `ExtensionInstallCommandTests` (a URL argument exits 2; `--json` stdout parses as one document with the report members; install twice fails with `extension.already_installed`; a failed update probe keeps the old record), and `tests/Morphir.E2E.Tests/Features/ExecutableExtensionInstall.feature` installing the test guest from a repository in both hosts.
  - Commands: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionInstallCommandTests/*"`; for E2E, build the executables as `tests/Morphir.E2E.Tests` documents, then `dotnet exec tests/Morphir.E2E.Tests/bin/Release/net10.0/Morphir.E2E.Tests.dll --treenode-filter "/*/*/ExecutableExtensionInstallFeature/*"` (Reqnroll names the class after the feature title).
  - Docs: rewrite `docs/content/docs/cli/management.md` for extensions; mark step 3 in progress in the extension-model PRD with the corrections in the design's findings 6; recommend closing #492 as superseded.
- [ ] 4.5 List installed extensions in `extension list` with deliveries `process` and `wasm`, the `installation` member and `--local`; verify the registry tests and the list E2E feature pass.
  - Files: `src/Morphir.Extensions/Registry.fs` (delivery cases, installed claims), `src/Morphir.Tooling/Features/Extensions/ExtensionListReport.cs`. Run the `fsharp-csharp-interop-reviewer` agent on the F# changes.
  - First failing test: in `tests/Morphir.Extensions.Tests/RegistryTests.fs`, an installed entry whose id equals `morphir.fsharp` is refused with `extension.duplicate_id`; an installed process entry has delivery `process` and both hosts.
  - Command: build `tests/Morphir.Extensions.Tests/Morphir.Extensions.Tests.fsproj`, then `dotnet exec tests/Morphir.Extensions.Tests/bin/Release/net10.0/Morphir.Extensions.Tests.dll --filter-test-list "Registry"`.
  - Keep `ExecutableExtensionList.feature` green: with nothing installed its id list does not change.

## 5. Package archives and staged folders

- [ ] 5.1 Read zip and tar gzip package archives with the entry rules and recorded limits, detecting the format from content; verify the `Archives` tests pass in both formats and the AOT probe covers both readers with no new warning.
  - First failing test: for each format, archives built in the test with `../../evil.exe`, an absolute path, a drive letter, a symbolic link, a hard link, a FIFO (tar), a GNU long name adding `..`, a case alias, 1025 entries and a ratio bomb are each refused before any file is written; a gzip of one file is `extension.unsupported_package`.
  - Command: `dotnet exec <inst> --filter-test-list "Archives"`.
- [ ] 5.2 Install a package archive given as a file: descriptor and checksum checks, single-executable target archives, the Windows `.exe` rule, WASM packages unchecked, bare `.wasm` refused, and generated local repositories for provenance; verify the `PackageInstall` tests pass.
  - Waits on Q-ARCHIVE, Q-PROVENANCE and Q-DESCNAME.
  - First failing test: a zip of release assets for the current RID installs, and the global lock has `index.kind` `local-directory` naming a directory below `data/morphir-dotnet/extensions/repositories/`; a target archive with two files fails with `extension.multi_file_artifact`; a replaced artifact with a matching replaced `.sha256` but the original descriptor digest fails with `extension.digest_mismatch`.
  - Command: `dotnet exec <inst> --filter-test-list "PackageInstall"`.
  - Docs: a "Install an extension" page under `docs/content/docs/cli/` with both archive formats and the trust note for process guests.
- [ ] 5.3 Add the staged folders at the four levels, the `[extension_install] staged` setting and the automatic pass for WASM packages only, plus `extension install --staged`; verify the configuration, staged-pass and E2E tests pass.
  - Waits on Q-AUTOPROCESS.
  - Files: `src/Morphir.Configuration/Schema.fs` and its tests in `tests/Morphir.Configuration.Tests`; the pass in `src/Morphir.Tooling/Features/Extensions/`.
  - First failing tests: `manual` mode leaves a staged WASM package uninstalled on compile; a staged process package in a project prints the install note on stderr, runs no guest and installs nothing; a broken staged archive leaves `compile --json` stdout unchanged.
  - Commands: the configuration test list in `tests/Morphir.Configuration.Tests`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/StagedPassTests/*"`, then an E2E feature `ExecutableExtensionStaged.feature`.
- [ ] 5.4 Install a bare `.wasm` module through `describe` in the WASM runtime; verify the `BareWasm` tests pass.
  - Waits on PRD step 2a. Until then 5.2's refusal `extension.wasm_host_unavailable` stays.

## 6. Selection layers and activation

- [ ] 6.1 Read and write pin files (`0.1.0-draft.1`) with exact draft matching, critical members and per-platform digests; verify the `Pins` tests pass.
  - Waits on Q-LEVELS.
  - First failing test: a pin with `0.1.0-draft.2` is refused with `extension.unsupported_contract_version`; a pin with an absolute path member value or a credential member is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Pins"`.
- [ ] 6.2 Resolve through the layers with the discovery boundary and isolation, refuse duplicates within a layer, report shadowing, and add `extension use` and `which` with pin locks; verify the `Layers` tests and the command tests pass.
  - First failing test: a project pin of 0.2.0 over a global 0.1.0 selects 0.2.0 only inside the project; a pin for a version not installed fails with `extension.not_installed` and does not fall back; `MORPHIR_ISOLATED` omits the global catalog.
  - Commands: `dotnet exec <inst> --filter-test-list "Layers"`; `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionUseCommandTests/*"`.
- [ ] 6.3 Read the generation overlay from `MORPHIR_EXTENSION_OVERLAY`, refuse paths outside `toolchains/generations/`, and never write there; verify the `Overlay` tests pass.
  - Waits on Q-OVERLAY. Approved task 4.4 consumes this seam.
  - First failing test: two overlays selecting 0.2.0 and 0.3.0 of one id resolve to their own versions while the global catalog still names 0.1.0; an overlay of `/tmp/gen` fails with `extension.overlay_invalid`; a `"1.0"` global catalog from the 2.7 fixtures resolves the other ids.
  - Command: `dotnet exec <inst> --filter-test-list "Overlay"`.
  - Docs: the overlay contract for `morphirup` in `src/Morphir.Extensions.Installation/README.md`.
- [ ] 6.4 Activate offline: pair agreement, containment, rehash, mode and MEP version checks, then launch a private copy below `tmp/extensions/`; on Windows check the launch path length; verify the `Activation` tests pass on Windows and Linux.
  - Q-WINLAUNCH: first confirm with a test whether process creation refuses an executable path over 260 characters; drop the length check if it does not.
  - First failing test: a store object changed after install fails with `extension.digest_mismatch` and starts no process; on Windows, uninstall while the guest runs succeeds and the guest keeps running.
  - Command: `dotnet exec <inst> --filter-test-list "Activation"`.
- [ ] 6.5 Add `[frontend.<language>] extension_id` and let compile start the selected installed frontend through the private copy; verify the configuration, compile and E2E tests pass.
  - Waits on Q-CONFIGREF.
  - Files: `src/Morphir.Configuration/Schema.fs`, `src/Morphir.Tooling/Features/Compile/MepFrontendExtensionConnector.cs`.
  - First failing test: setting both `extension` and `extension_id` fails configuration loading naming both; with the test guest installed as a frontend, `morphir compile` starts its verified copy.
  - Commands: the configuration test list, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Compile/*/*"`.

## 7. Hand-over for morphirup

- [ ] 7.1 Add the in-process hand-over of a descriptor, one artifact and an authentication statement, installing through the same checks and returning the statement unchanged; verify the `HandOver` tests pass.
  - First failing test: a hand-over with `recipe-digest` whose artifact bytes differ from the descriptor digest fails with `extension.digest_mismatch`; a successful one reports the caller's statement and the probe outcome separately.
  - Command: `dotnet exec <inst> --filter-test-list "HandOver"`.
  - Docs: the hand-over contract in `src/Morphir.Extensions.Installation/README.md`, including that the installer checks no signature.
- [ ] 7.2 Exercise the hand-over from `morphirup setup` with descriptors and artifacts authenticated by the tool release metadata client; verify the adoption change's task 4.3 tests pass against this installer.
  - Waits on the adoption change's group 4 and the separately evaluated TUF client.

## 8. Dotted ids in shared files

- [ ] 8.1 Accept dotted ids from the descriptor version that the finos/morphir change defines, and write them to shared files only at that version; verify the `Identifiers` tests gain the new version's cases from that change's fixtures and pass.
  - Waits on that change, which is not written yet.

## 9. Integration checks

- [ ] 9.1 In the manual or release lane, on Windows x64 and Linux x64, check interoperability with the Rust CLI at the pinned finos/morphir revision: each CLI lists, activates and uninstalls what the other installed; concurrent installs from both serialize on the state lock; a journal left by either is recovered by the other; record the outcome and the revisions in the pull request.
  - This runs the Rust CLI as a separate executable in a qualification workflow; no .NET code links or calls Rust code.

## Workflow follow-up

- Link the dedicated installer issue, when it exists, in the proposal and here before claiming tasks.
- Follow AGENTS.md and `openspec/config.yaml` for review and explicit approval before merge.
- Archive only after the implemented scope passes; split groups 5.4, 7.2 and 8 into a follow-up change if they are still waiting.
- Rebase the `extension-registry` MODIFIED block onto whichever of this change and `add-deployment-and-acquisition` archives first.
