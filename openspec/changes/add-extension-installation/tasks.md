# Tasks

This is the implementation breakdown of the .NET extension installer (extension-model PRD step 3). Each checkbox is one small pull request. The indented notes under it name the first failing test, the test project that carries it and the narrowest command; they are planning detail and are not tracked.

GitHub issue: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort.
<!-- TRACKING: link the dedicated installer issue here once it exists. -->
No Beads id is recorded; do not invent one.

Dependencies. The design groups its questions in two. One group follows today's reference behavior, which stands in for the shared contract until finos/morphir writes it down; those need no maintainer answer to start. The other group needs the maintainer. A task waits only on the second group.

- Can start now: group 1; 2.1 to 2.8 and 2.10; 3.1 to 3.5; 4.1 to 4.3; 5.1; 6.5 (the shared catalog first).
- Waits on a maintainer answer:
  - 4.4 to 4.8 on Q-COMMANDS, Q-DIAG and Q-DEVPOLICY.
  - 5.2 on Q-ARCHIVE. 5.3 on Q-ARCHIVE, Q-MULTIFILE and Q-WINLAUNCH. 5.4 on Q-PROVENANCE.
  - 6.1 to 6.3 on Q-LEVELS. 6.4 on Q-OVERLAY. 6.6 on Q-WINLAUNCH. 6.7 on Q-CONFIGREF.
  - An answer that differs from the recommendation changes the named specs first, through task 1.1.
- Waits on the shared toolchain contract in finos/morphir (raised in 1.2): 6.1 and 6.4, as well as their maintainer answers. Without that contract, the pin and overlay formats would be a .NET-only parallel design.
- Waits on other work:
  - 5.6 on PRD step 2a, a WASM runtime in this host.
  - 7.2 on the adoption change's group 4 and the separately evaluated TUF client.
  - 8.1 on finos/morphir#1149 being approved and on the maintainer's answer to its question 8 (dotted partition or one catalog that switches version home-wide).
- The adoption change (`openspec/changes/add-deployment-and-acquisition`, on main) consumes this one:
  - its task 3.3 needs 3.5, 5.2, 5.3 and 7.1, and the Q-MULTIFILE answer;
  - its 3.4 needs 6.5;
  - its 4.4 needs 6.4.

Commands follow the repository's build settings. Build one project with `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`. Run Expecto assemblies with `dotnet exec <dll> --filter-test-list "<list>"` and TUnit assemblies with `dotnet exec <dll> --treenode-filter "<filter>"`, as `build/Build.Testing.cs` does. In the notes below:

- `<inst>` stands for `tests/Morphir.Extensions.Installation.Tests/bin/Release/net10.0/Morphir.Extensions.Installation.Tests.dll`;
- `<tooling>` stands for `tests/Morphir.Tooling.Tests/bin/Release/net10.0/Morphir.Tooling.Tests.dll`;
- `<e2e>` stands for `tests/Morphir.E2E.Tests/bin/Release/net10.0/Morphir.E2E.Tests.dll`, run after the executables are built as `tests/Morphir.E2E.Tests` documents. Reqnroll names each feature class after its feature title.

## 1. Decisions and upstream inputs, no code

- [ ] 1.1 Record the maintainer's answer to each question in the design's "needs the maintainer" group beside the question, and change the specs and tasks where an answer differs from the recommendation; verify with `mise exec -- openspec validate add-extension-installation --strict` and `mise run openspec:verify`.
- [ ] 1.2 Raise the upstream asks of the design's "Raised upstream" list in finos/morphir, and the PRD superset proposal; verify that each question in `design.html` links its issue or proposal.
  - The asks:
    - the maintainer decision MEP-1: MEP `0.1` is not supported, and the supported protocol is the whole 0.2 line (`>=0.2.0-0`, `<0.3.0`, under the npm and Cargo prerelease rule) as an explicit exception to the exact-match rule for prereleases. Say so in `protocol.md` and the extension README, and move the Rust host to the 0.2 line;
    - review comments on finos/morphir#1149, listed in the design's Q-SHARED entry: answer question 8 with the partition; new readers list and skip entries they cannot run; a host may drop earlier transient drafts (DOC-1); equal members and values instead of byte identity; and the mixed-home check;
    - the overlay, the level pins and the receipt launch path, for the toolchain contract;
    - the rules that only the Rust source specifies today, to be written into the contract pages (finos/morphir#1149 task 1.4 covers the catalog, lock, journal, state lock and paths): Q-LOCKPRIM, Q-JOURNAL, Q-STRICTLOCK, Q-CATVER, Q-NUMBERS, Q-PLATFORM, Q-REQHOST, Q-PROBELIMITS, Q-GC and Q-FIXTURES, plus the repository identity spelling;
    - target and package archives (Q-ARCHIVE), multi-file packages (Q-MULTIFILE), and a provenance kind (Q-PROVENANCE);
    - the claim check after `update` (Q-CLAIMCHECK).
  - Do not write issue numbers before they exist.

## 2. Installation library and pure readers

- [ ] 2.1 Create `src/Morphir.Extensions.Installation` (F#, referencing `Morphir.Extensions` and `Morphir.Mep`, not `Morphir.Tooling`), `tests/Morphir.Extensions.Installation.Tests` (Expecto) and an AOT probe `tests/Morphir.Extensions.Installation.AotProbe` run in the pull request lane like `TestMepAot`; verify the solution builds and the probe publishes with Native AOT with zero IL2026 and IL3050 warnings.
  - Files: `Morphir.slnx`, `build/Build.Testing.cs` (test project lists), the probe target beside `TestMepAot`.
  - Follow the `fsharp-csharp-interop` skill for every public member C# will call.
- [ ] 2.2 Add distribution identifiers: the id rule by document version with the reference device-name rule, artifact file names, declared relative paths, channels, selections and SHA-256 digests; verify the `Identifiers` tests pass.
  - First failing test: `con`, `com1`, `a-`, `A`, 239 characters and the dotted `org.finos.morphir.fsharp-frontend` under `2.0.0-draft.2` are refused with `extension.id_not_portable`; `morphir-elm` is accepted; a declared path of 513 UTF-8 bytes or 241 UTF-16 units is refused. Cases from the toolchain identifier and target path tables.
  - Command: build `tests/Morphir.Extensions.Installation.Tests/Morphir.Extensions.Installation.Tests.fsproj`, then `dotnet exec <inst> --filter-test-list "Identifiers"`.
- [ ] 2.3 Add the platform table from the toolchain contracts: RID to descriptor triple to the "Tool release v1 platform" `os` and `arch`, with musl giving no index platform; verify the `Platforms` tests pass.
  - First failing test: each row of the table maps both ways; `linux-musl-x64` has no index platform, and resolution names it in `extension.no_matching_artifact`.
  - Command: `dotnet exec <inst> --filter-test-list "Platforms"` after the build above.
- [ ] 2.4 Add claim sets that keep their JSON beside the typed `Morphir.Mep` view, compare over the typed projection with the reference number equality, check `critical` and evaluate `requires.host`; verify the `Claims` tests pass.
  - First failing tests:
    - `{"a":1}` differs from `{"a":1.0}`, and `1.0` equals `1.00`;
    - key order is ignored and array order is not;
    - an unknown top-level member is ignored, and an absent `critical` equals `[]`;
    - a claim set at `0.1.0-draft.1` (`statement`, `statementSource`) is refused with `extension.unsupported_contract_version` and is not converted (DOC-1);
    - a mixed record is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Claims"`.
- [ ] 2.5 Add index records, JSONL histories and pure resolution; verify the `Resolution` tests pass.
  - First failing tests:
    - a history with `1.0.0+a` and `1.0.0+b` is refused naming both;
    - stable skips a prerelease listed on `stable`;
    - a release whose `requires.host` excludes the host is skipped for an older one;
    - only-`0.1` releases give `extension.no_compatible_mep_version`;
    - two artifacts for one platform are refused;
    - a WASM record with `platform` `null` is refused, as the reference index reader refuses it.
    - a history with version 1 lines for 0.1.0 and 0.2.0 and a `2.0.0-draft.2` line for 0.3.0 selects 0.3.0 on stable, and refuses `--version 0.2.0` with `extension.unsupported_contract_version`.
  - Command: `dotnet exec <inst> --filter-test-list "Resolution"`.
- [ ] 2.6 Add release descriptor reading at exactly `2.0.0-draft.2`, with the platform-differences check; verify the `Descriptors` tests pass.
  - First failing test: two artifacts whose claims differ at `capabilities.backend.targets` with `platformDifferences` `none` are refused naming that member; a descriptor mixing `wasm` and `process` is refused; integer `schemaVersion` 1, `"1.0"` and `2.0.0-draft.1` are refused with `extension.unsupported_contract_version` (DOC-1).
  - Command: `dotnet exec <inst> --filter-test-list "Descriptors"`.
- [ ] 2.7 Add catalog and lock codecs that write the members and values the shared contract defines and read with must-ignore, including the catalog version switch, `critical` and `requires`; pin evidence fixtures with provenance; verify the `StateCodecs` tests pass.
  - Fixtures: `tests/Fixtures/ExtensionAcquisition/` holds:
    - `frontend-catalog-v1.json` and `frontend-lock-v1.json`, copied from `ecosystem/morphir-rust/crates/morphir-distribution/tests/fixtures` at submodule `c9f79da`;
    - a catalog, lock, journal, repositories file, history and descriptor written by the pinned Rust CLI, including an entry with `requires.host` and one installed from a version 1 record.
  - The README names each fixture's source, commit and SHA-256, and the capture commands. These fixtures are evidence of today's reference behavior, not a target to match byte for byte. Replace them with finos/morphir data when the contract pages and Q-FIXTURES land.
  - First failing tests:
    - reading `frontend-lock-v1.json` and writing it back gives the same member set and values, with `"platform": null`;
    - the lock writer never adds a top-level member outside the contract's list;
    - a catalog with a supplied claim set is written as `"2.0.0-draft.2"`;
    - an entry for a release with `requires.host` is written with `critical` `["requires.host"]`;
    - a Rust-written entry converted from a version 1 record is carried through unchanged when .NET rewrites the catalog, and is listed as unavailable.
  - Command: `dotnet exec <inst> --filter-test-list "StateCodecs"`.
- [ ] 2.8 Add the journal and repositories-file codecs; verify the `StateCodecs` tests pass.
  - Journal: `schemaVersion` 1, and `nextLock` and `nextCatalog` as byte arrays or `null`.
  - Repositories file: integer `schemaVersion`, no unknown members, and identities as canonical absolute paths in the platform's ordinary syntax.
  - First failing tests:
    - a journal with `nextLock` `null` reads as a deletion;
    - the writer never adds a member;
    - the Rust-written repositories file from 2.7 reads, and writes back with the same members;
    - on Windows, `D:\ext-repo` and `\\?\D:\ext-repo` compare equal, and the .NET writer emits `D:\ext-repo`.
  - Command: `dotnet exec <inst> --filter-test-list "StateCodecs"`.
- [ ] 2.10 Add the supported MEP line and the implemented set: the comparators `>=0.2.0-0` and `<0.3.0` under the npm and Cargo prerelease rule, the implemented versions (today `0.2.0-draft.1`), and the runnable check with its two reasons; verify the `MepLine` tests pass.
  - Library: the repository has no SemVer range library in product code. `Morphir.Models/IR/Versioning.fs` parses and prints versions but has no precedence or ranges, and `NuGet.Versioning` reaches only the build projects, transitively.
    - Use the `Semver` NuGet package's `SemVersionRange` with its default options, which follow that prerelease rule, after the AOT probe from 2.1 shows no new warning.
    - Do not use `NuGet.Versioning`. Its ranges compare by precedence alone, so `[0.2.0-0, 0.3.0)` would also admit `0.2.1-rc.1` and `0.3.0-draft.1` (Assumed; one test case pins it).
    - If the package is not admitted, implement the two comparators and SemVer precedence over the in-house `SemanticVersion`.
    - The test matrix below is the contract either way.
  - First failing tests:
    - `0.2.0-draft.1`, `0.2.0-rc.1`, `0.2.0` and `0.2.7` are in the line;
    - `0.1`, `0.1.0`, `0.2.1-rc.1`, `0.3.0-draft.1` and `0.3.0` are not;
    - an entry with `["0.2.0-draft.2"]` is unavailable as "not implemented yet", one with `["0.3.0"]` as "not supported", and one with `["0.1", "0.2.0-draft.1"]` is runnable over `0.2.0-draft.1`.
  - Command: `dotnet exec <inst> --filter-test-list "MepLine"`.

## 3. Shared state on disk

- [ ] 3.1 Resolve Morphir Home and implement the state lock on `locks/extensions.state.lock` as a whole-file exclusive operating-system lock, the primitive the reference uses today (Q-LOCKPRIM), with its waiting line and cancellation; verify the `StateLock` tests pass on Windows and on Linux or macOS.
  - Add a small helper process, `tests/Morphir.Extensions.Installation.LockHelper`, that takes the lock with `LockFileEx` on Windows and `flock` elsewhere, standing in for any other host, such as the MoonBit host, that shares the home.
  - First failing tests:
    - while the helper holds the lock, an acquisition prints one waiting line and blocks;
    - while .NET holds it, the helper's non-blocking attempt reports contention, not an I/O error;
    - a relative `MORPHIR_HOME` resolves against the current directory.
  - Command: build the helper and the tests, then `dotnet exec <inst> --filter-test-list "StateLock"`.
- [ ] 3.2 Implement atomic file replacement and the transaction journal with rollback and recovery at every lock acquisition; verify the `Transactions` tests pass.
  - First failing tests:
    - with a writer seam that fails after the lock write, the next acquisition applies the journal and the pair agrees;
    - a journal in the format of the 2.7 evidence fixtures is applied and removed;
    - a journal with `nextLock` `null` deletes the lock;
    - a reader during a replacement sees the old or the new file.
  - Command: `dotnet exec <inst> --filter-test-list "Transactions"`.
- [ ] 3.3 Implement the content-addressed store; verify the `Store` tests pass.
  - The store covers: a staged temporary file, hash-while-copy from staging compared with the recorded digest, no-clobber publication, a rehash before reuse, containment inside Morphir Home, the 256 MiB limit and the Unix owner execute bit.
  - First failing tests:
    - a tampered object fails with `extension.store_object_mismatch` and is unchanged;
    - a linked digest directory fails with `extension.path_escape`;
    - a staged file changed after its first hash fails with `extension.digest_mismatch` and nothing is published.
  - Command: `dotnet exec <inst> --filter-test-list "Store"`.
- [ ] 3.4 Implement the .NET inventory below `data/morphir-dotnet/extensions/installed/`, its rebuild from a shared pair, and the ownership of .NET run copies below `tmp/extensions/`; verify the `Inventory` tests pass.
  - The inventory holds the .NET-only facts that the closed catalog and lock member sets cannot carry: the true source, the archive digest, the level, and every installed version for pins and overlays. It is needed whatever Q-LEVELS decides.
  - First failing tests:
    - a second record for the same id and digest with other `args` replaces the first and warns;
    - a record rebuilt from a pair another host wrote has source kind `shared-catalog`;
    - the copy sweep removes a stale `morphir-dotnet-run-*` directory and leaves `morphir-extension-*` directories in place.
  - Command: `dotnet exec <inst> --filter-test-list "Inventory"`.
- [ ] 3.5 Implement the commit path: every global install writes the catalog entry and lock through the journal, and an install at another level writes a pin; read a catalog that holds entries this host cannot run. Verify the `Commit` tests pass.
  - First failing tests (`Commit`):
    - a global install writes the entry and lock in one journal, and the pair agrees;
    - a second global install of an id the catalog holds fails with `extension.already_installed`;
    - a catalog holding a 0.1-only entry lists that entry as unavailable, saying that MEP `0.1` is not supported, and lists the others as usual.
  - Command: `dotnet exec <inst> --filter-test-list "Commit"`.
## 4. Install from a named repository, and the commands

- [ ] 4.1 Read and write the upstream repositories file under `locks/extensions.repositories.lock`, and add `extension repository add|list|remove`; verify the repository command tests pass.
  - First failing test: `RepositoryCommandTests` in `tests/Morphir.Tooling.Tests/Features/Extensions`: `add` records the canonical directory as an enabled `local-directory` endpoint; a directory without `extensions/` is refused and the file is unchanged; while the helper from 3.1 holds the repositories lock, `add` waits.
  - Command: build `tests/Morphir.Tooling.Tests/Morphir.Tooling.Tests.csproj`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/RepositoryCommandTests/*"`.
- [ ] 4.2 Implement the pipeline without a probe; verify the `Pipeline` tests pass.
  - Steps: resolve, check repository containment, stage and hash, store, commit through 3.5, and report with `authentication` kind `local-developer` and `claimCheck` `unchecked`.
  - First failing test: a repository artifact whose bytes differ from its record fails with `extension.digest_mismatch`, and nothing is written to the store, catalog, locks or inventory. A linked artifact fails with `extension.path_escape`.
  - Tests pass `MORPHIR_HOME` explicitly to a temporary directory; none reads the user's home.
  - Command: `dotnet exec <inst> --filter-test-list "Pipeline"`.
- [ ] 4.3 Add the bounded describe probe, the session fallback on `-32601` and `-32014` only, and the environment filter, and record `claimCheck` from this install only; verify the `Probe` tests pass.
  - Add a test guest, `tests/Morphir.Extensions.TestGuest`, built on the `Morphir.Mep` guest loop. Its modes:
    - agree, lie and hang;
    - write junk on stdout;
    - answer `-32601`, or answer `-32600` with `not initialized`;
    - spawn a child;
    - report its environment;
    - rewrite its staged file.
  - First failing tests:
    - the lying guest fails with `extension.claims_mismatch` naming `claims.capabilities.backend.targets`;
    - the hanging guest and its child are ended after the deadline with `extension.probe_failed`;
    - the `-32600` guest fails with `extension.probe_failed`, with no fallback;
    - the environment guest sees no `GITHUB_TOKEN`;
    - the rewriting guest fails with `extension.digest_mismatch`;
    - an index record that says `probed`, installed with `--no-probe`, is recorded `unchecked`.
  - Command: build the test guest and the tests, then `dotnet exec <inst> --filter-test-list "Probe"`.
- [ ] 4.4 Wire `extension install --repository` and `extension uninstall` with its alias `remove` in both hosts, with JSON reports on stdout, messages on stderr, exit codes 0, 1 and 2, and stable codes; verify the command tests pass.
  - Files: `src/Morphir.Tooling/Scenarios/ManagementScenario.cs`, `src/Morphir.Tooling/Features/Extensions/`.
  - First failing tests, in `ExtensionInstallCommandTests`:
    - a URL argument exits 2;
    - `--platform` exits 2;
    - `--json` stdout parses as one document with the report members;
    - installing twice fails with `extension.already_installed`;
    - a global uninstall removes the entry and lock in one journal and keeps the store object.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionInstallCommandTests/*"`.
- [ ] 4.5 Wire `extension update` and `extension info`, including the platform-differences report; verify the command tests pass.
  - First failing tests, in `ExtensionUpdateCommandTests` and `ExtensionInfoCommandTests`:
    - `update` without a source exits 2;
    - a failed update probe keeps the old record;
    - `info --json` has the members of the requirement;
    - `info` of an id that is not installed fails with `extension.not_installed`;
    - a declared platform difference is named in the install report and in `info`.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionUpdateCommandTests/*"`, then the same filter with `ExtensionInfoCommandTests`.
- [ ] 4.6 List installed extensions in `extension list`, with deliveries `process` and `wasm`, `deployments`, `reason`, the `installation` member with `layer`, and `--platform`; refuse an installed id equal to a built-in id. Verify the registry and list tests pass.
  - Files: `src/Morphir.Extensions/Registry.fs` (delivery cases, installed claims), `src/Morphir.Tooling/Features/Extensions/ExtensionListReport.cs`. Run the `fsharp-csharp-interop-reviewer` agent on the F# changes.
  - First failing tests, in `tests/Morphir.Extensions.Tests/RegistryTests.fs`:
    - an installed entry whose id equals `morphir.fsharp` is refused with `extension.duplicate_id`, exit 1;
    - an installed process entry has delivery `process`, both hosts and all four deployments;
    - a 0.1-only shared entry has `deployments` `[]` and a `reason`;
    - `--platform amiga` exits 2.
  - Command: build `tests/Morphir.Extensions.Tests/Morphir.Extensions.Tests.fsproj`, then `dotnet exec tests/Morphir.Extensions.Tests/bin/Release/net10.0/Morphir.Extensions.Tests.dll --filter-test-list "Registry"`.
  - Keep `ExecutableExtensionList.feature` green: with nothing installed, its id list does not change.
- [ ] 4.7 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionInstall.feature`: install the test guest from a repository, list it and uninstall it, in `morphir` and `dotnet-morphir`; verify the feature passes.
  - Command: `dotnet exec <e2e> --treenode-filter "/*/*/ExecutableExtensionInstallFeature/*"`.
- [ ] 4.8 Retire the download handlers for extensions, and update the docs; verify `ExecutableExtensionInstall.feature` and the remaining management tests pass.
  - Remove `ExtensionHandlers.cs`, `ExtensionCommands.cs` and the extension use of `ArtifactPathResolver` and `OsPaths`, with their tests in `tests/Morphir.Tooling.Tests/Features/Management`.
  - Docs:
    - rewrite `docs/content/docs/cli/management.md` for extensions;
    - mark step 3 in progress in the extension-model PRD, with the corrections in the design's finding 6;
    - recommend closing #492 as superseded.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Management/*/*"`.

## 5. Package archives and staged folders

- [ ] 5.1 Read zip and tar gzip package archives with the entry rules and recorded limits, detecting the format from content; verify the `Archives` tests pass in both formats and the AOT probe covers both readers with no new warning.
  - First failing test: for each format, archives built in the test are each refused before any file is written. They contain:
    - `../../evil.exe`, an absolute path or a drive letter;
    - a symbolic link, a hard link, or a FIFO (tar);
    - a GNU long name that adds `..`, or a 2 MiB PAX header;
    - a case alias, a non-ASCII name, or `CON.exe`;
    - a zip whose local and central names differ;
    - 1025 entries;
    - a ratio bomb, as a zip entry and as a gzip stream, and inside a nested archive.
  - A gzip of one file is `extension.unsupported_package`.
  - Command: `dotnet exec <inst> --filter-test-list "Archives"`.
- [ ] 5.2 Read a package archive's layout: one `*.release.json`, checksums against the descriptor, and extra entries refused; verify the `PackageLayout` tests pass.
  - Waits on Q-ARCHIVE.
  - First failing test: a zip of release assets for the current RID is accepted; two descriptors are refused naming both; a replaced artifact with a matching replaced `.sha256` but the original descriptor digest fails with `extension.digest_mismatch`.
  - Command: `dotnet exec <inst> --filter-test-list "PackageLayout"`.
- [ ] 5.3 Install a process artifact from a target archive that holds exactly one file, with the Windows `.exe` rule; verify the `TargetArchives` tests pass.
  - Waits on Q-ARCHIVE, Q-MULTIFILE and Q-WINLAUNCH.
  - First failing test: a target archive with one `.exe` installs that file's digest, and its archive digest is only in the inventory; two files fail with `extension.multi_file_artifact`; on Windows, a file without `.exe` fails with `extension.not_launchable`.
  - Command: `dotnet exec <inst> --filter-test-list "TargetArchives"`.
- [ ] 5.4 Install a package archive given as a file through the pipeline, with generated local repositories for entries that enter the shared catalog and WASM packages unchecked; verify the `PackageInstall` tests pass.
  - Waits on Q-PROVENANCE and 5.2.
  - First failing tests:
    - a global install's lock has `index.kind` `local-directory`, naming a directory below `data/morphir-dotnet/extensions/repositories/`;
    - the generated WASM history omits `platform`;
    - the repositories file is unchanged.
  - Command: `dotnet exec <inst> --filter-test-list "PackageInstall"`.
  - Docs: an "Install an extension" page under `docs/content/docs/cli/`, with both archive formats and the trust note for process guests.
- [ ] 5.5 Add the staged folders, the `[extension_install] staged` setting, the automatic pass for WASM packages only, and `extension install --staged`; verify the configuration, staged-pass and E2E tests pass.
  - The global staged folder needs only 5.4; the project, workspace and system folders also need 6.1.
  - Files: `src/Morphir.Configuration/Schema.fs` and its tests in `tests/Morphir.Configuration.Tests`; the pass in `src/Morphir.Tooling/Features/Extensions/`.
  - First failing tests:
    - `manual` mode leaves a staged WASM package uninstalled on compile;
    - a staged process package prints the install note on stderr, runs no guest and installs nothing;
    - a broken staged archive leaves `compile --json` stdout unchanged.
  - Commands: the configuration test list in `tests/Morphir.Configuration.Tests`, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/StagedPassTests/*"`, then the E2E feature `ExecutableExtensionStaged.feature`.
- [ ] 5.6 Install a bare `.wasm` module through `describe` in the WASM runtime; verify the `BareWasm` tests pass.
  - Waits on PRD step 2a. Until then, the refusal `extension.wasm_host_unavailable` stays.

## 6. Selection layers and activation

- [ ] 6.1 Read and write pin files (`0.1.0-draft.1`) with exact draft matching, critical members and per-platform digests; verify the `Pins` tests pass.
  - Waits on Q-LEVELS and on the shared toolchain contract for level pins.
  - First failing test: a pin with `0.1.0-draft.2` is refused with `extension.unsupported_contract_version`; a pin with an absolute path member value or a credential member is refused.
  - Command: `dotnet exec <inst> --filter-test-list "Pins"`.
- [ ] 6.2 Resolve through the layers with the discovery boundary and isolation, refuse duplicates within a layer, and report shadowing; verify the `Layers` tests pass.
  - First failing tests:
    - a project pin of 0.2.0 over a global 0.1.0 selects 0.2.0 only inside the project;
    - a pin for a version not installed fails with `extension.not_installed` and does not fall back;
    - a pin with only `win-x64` fails on another platform with `extension.platform_not_pinned`;
    - `MORPHIR_ISOLATED` omits the shared catalog and the system pin;
    - a pin file listing one id twice fails with `extension.duplicate_id`, exit 1.
  - Command: `dotnet exec <inst> --filter-test-list "Layers"`.
- [ ] 6.3 Add `extension use`, `extension which` and `extension list --local`, with pin writes under a lock keyed by the canonical pin path; verify the command tests pass.
  - First failing tests:
    - `use` of a version that is not installed fails and changes no pin;
    - `which --json` names the shadowed global selection;
    - `list --local` shows only pinned entries and the built-ins;
    - two concurrent `use` commands on one pin file leave the complete result of both, in some order.
  - Command: `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Extensions/ExtensionUseCommandTests/*"`.
- [ ] 6.4 Read the generation overlay from `MORPHIR_EXTENSION_OVERLAY`, refuse paths outside `toolchains/generations/`, and never write there; verify the `Overlay` tests pass.
  - Waits on Q-OVERLAY and on the shared toolchain contract for overlays. Approved task 4.4 consumes this seam.
  - First failing tests:
    - two overlays selecting 0.2.0 and 0.3.0 of one id resolve to their own versions, while the global selection still names 0.1.0;
    - an overlay of `/tmp/gen` fails with `extension.overlay_invalid`;
    - a `"1.0"` shared catalog from the 2.7 fixtures is read: the overlay's ids resolve, and its converted entries are listed as unavailable and not rewritten.
  - Command: `dotnet exec <inst> --filter-test-list "Overlay"`.
  - Docs: the overlay contract for `morphirup` in `src/Morphir.Extensions.Installation/README.md`.
- [ ] 6.5 Activate offline: pair agreement, containment, mode and MEP version checks; hash the store object in the read that writes the private copy; launch with the filtered environment and an explicit working directory; check the session against the installed claims. Verify the `Activation` tests pass on Windows and Linux.
  - Needs 3.5. It serves the shared catalog first; pins join after 6.2.
  - First failing tests:
    - a store object changed after install fails with `extension.digest_mismatch` and starts no process;
    - a test seam that changes the object mid-read fails the same way;
    - the environment guest sees no `GITHUB_TOKEN` and the project directory as its working directory;
    - a guest whose initialization offers an unclaimed target is refused before any operation;
    - on Windows, uninstall while the guest runs succeeds and the guest keeps running.
  - Command: `dotnet exec <inst> --filter-test-list "Activation"`.
- [ ] 6.6 Check the Windows launch path length; verify the `WindowsLaunch` tests pass on Windows.
  - Waits on Q-WINLAUNCH. First confirm with a test whether process creation refuses an executable path over 260 characters. If it does not, drop the provisional requirement and record that in the design.
  - Command: `dotnet exec <inst> --filter-test-list "WindowsLaunch"`.
- [ ] 6.7 Add `[frontend.<language>] extension_id`, and let compile start the selected installed frontend through the private copy; verify the configuration, compile and E2E tests pass.
  - Waits on Q-CONFIGREF.
  - Files: `src/Morphir.Configuration/Schema.fs`, `src/Morphir.Tooling/Features/Compile/MepFrontendExtensionConnector.cs`.
  - First failing tests:
    - setting both `extension` and `extension_id` fails configuration loading, naming both;
    - with the test guest installed as a frontend, `morphir compile` starts its verified copy;
    - an `extension_id` that is not installed fails with `extension.not_installed`, with the install command and no network access.
  - Commands: the configuration test list, then `dotnet exec <tooling> --treenode-filter "/*/Morphir.Tooling.Tests.Features.Compile/*/*"`.

## 7. Hand-over for morphirup

- [ ] 7.1 Add the in-process hand-over of a descriptor, one artifact and an authentication statement, installing through the same checks and returning the statement unchanged; verify the `HandOver` tests pass.
  - First failing tests:
    - a hand-over with `recipe-digest` whose artifact bytes differ from the descriptor digest fails with `extension.digest_mismatch`;
    - a successful one reports the caller's statement and the probe outcome separately;
    - a statement of kind `local-developer` is refused;
    - the "what would this write" answer names exactly the files the following install changes.
  - Command: `dotnet exec <inst> --filter-test-list "HandOver"`.
  - Docs: the hand-over contract in `src/Morphir.Extensions.Installation/README.md`. It says that the installer checks no signature and no repository identity: `repository_mismatch` is the manager's check.
- [ ] 7.2 Exercise the hand-over from `morphirup setup` with descriptors and artifacts authenticated by the tool release metadata client; verify the adoption change's task 4.3 tests pass against this installer.
  - Waits on the adoption change's group 4 and the separately evaluated TUF client.

## 8. Dotted ids

- [ ] 8.1 Read and write dotted ids at the drafts and in the home and repository layout that finos/morphir#1149 defines (its task 5.3); verify the `Identifiers`, `StateCodecs` and `Commit` tests gain #1149's shared cases, including its home-partition cases, and pass through `src/Morphir.Conformance`.
  - Waits on #1149's approval and its question 8. With the partition, the dotted extensions that pass admission go to `catalog/extensions-dotted.json` and `locks/extensions-dotted/`, with journals recovered per directory and wrong-form ids refused per file. With one catalog, they go to the existing files, which move to `2.0.0-draft.3`.

## 9. Integration checks

- [ ] 9.1 Check a shared Morphir Home with a second current host: the MoonBit host, once it installs extensions (finos/morphir#1149 group 3). Each host lists, activates and uninstalls what the other installed, concurrent installs serialize on the state lock, and a journal either leaves is applied by the other; record the outcome and the revisions in the pull request.
  - Waits on the MoonBit host. The Rust CLI is legacy and is not part of this check (maintainer decision, 9 November 2026).
## Workflow follow-up

- Link the dedicated installer issue, when it exists, in the proposal and here before claiming tasks.
- Follow AGENTS.md and `openspec/config.yaml` for review and explicit approval before merge.
- Archive only after the implemented scope passes. Split 5.6, 7.2 and 8.1 into a follow-up change if they are still waiting.
- The `extension-registry` delta is written against the text of `add-deployment-and-acquisition`, which is active on main. If that change archives first, this delta applies as written. If this change archives first, the adoption change's MODIFIED block must take this one's installed-entry sentences.
</content>
