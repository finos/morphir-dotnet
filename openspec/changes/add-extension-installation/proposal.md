# Proposal

## Why

morphir-dotnet cannot install an external extension. The `extension install`, `use`, `remove` and `which` commands are stubs (`src/Morphir.Tooling/Scenarios/ManagementScenario.cs`), and the handlers behind them download a URL into a folder that is not the upstream store and check no digest. The extension-model PRD plans the installer as step 3. The finos/morphir change `add-deployment-and-acquisition`, approved on 9 November 2026, makes the same installer a dependency of `morphirup`: "Extension acquisition SHALL use extension descriptors, claims, store and lock contracts", and setup "SHALL use the host's extension configuration, store, claims and locks". Its tasks 3.3, 3.4 and 4.4 cannot start without it.

On 9 November 2026 the maintainer decided that the .NET installer is built inside that effort, as its own OpenSpec change in this repository, following the parent contract. This is that change.

## What Changes

A developer on Windows adds a local extension repository, installs a process backend from it, and sees the same installed extension in the Rust CLI. A second project pins a newer version of the same backend without changing the global selection, and a `morphirup` toolchain generation selects its own versions over the shared store. A teammate who clones a repository with an extension package archive in `.morphir/extensions/staged/` gets a clear message, and no code from the clone runs until they install it.

```text
source (named repository | package archive | staged folder | manager handoff)
  -> read descriptor or index record -> select the artifact for this platform
  -> verify digests while staging -> describe probe (process, unless --no-probe)
  -> publish to MORPHIR_HOME/store -> write lock and catalog under the state lock
  -> resolve through selection layers -> verify again offline -> run a private copy
```

- An installer that writes the upstream store, catalog and global locks under Morphir Home in the format and with the interprocess lock that the Rust CLI uses, so each CLI sees the other's global installs. It never downloads anything. Compile, generate and validate never install.
- Install sources: a named local-directory repository (the upstream path), an extension package archive or a bare `.wasm` file given on the command line, the `staged/` folders, and authenticated files that `morphirup` hands over. Each source becomes the same resolved record before any byte is stored.
- Claim checks at install time, recorded as `probed` or `unchecked` by what this install did. WASM is not probed, as upstream decided. Publisher authentication is a separate fact, reported separately, and a checksum is never called authentication.
- Selection layers: the global catalog, .NET level pins at the system, workspace and project levels, and a generation-scoped overlay for `morphirup`. One id resolves to one installed record, offline, with the bytes hashed again before they run.
- Package archive reading with the PRD's entry rules and recorded size, count and ratio limits, in zip and tar gzip, using only the base class library.
- Commands: `extension install`, `update`, `uninstall` (with `remove` as an alias), `list`, `info`, `use`, `which` and `extension repository add|list|remove`, each with `--json` on stdout and messages on stderr.
- **BREAKING** for a command that never worked: the `extension install <name> <url> <version>` arguments go away. The stub never installed anything, and no release documented it as working. The download handlers in `src/Morphir.Tooling/Features/Management` stop being used for extensions.

The design lists every place where the parent contract is silent, ambiguous or specified only by the Rust code, as named questions with a recommendation. The specs follow the recommendations and name the question each one depends on. Nothing here is shipped, released or qualified.

## Capabilities

### New Capabilities

- `extension-installation`: the install, update, uninstall and info pipeline from each source: record reading, the id rule of the contract version, artifact selection, digest checks, claim probes, reports and diagnostics.
- `extension-store`: the shared Morphir Home state: content-addressed store, catalog, global locks, the interprocess state lock, transaction recovery and compatibility with upstream readers.
- `extension-package-archives`: the extension package archive and bare `.wasm` formats, archive safety rules and limits, and the `staged/` folders with their install policy.
- `extension-selection`: level pins, the generation overlay, precedence, offline activation checks and launching only verified bytes.

### Modified Capabilities

- `extension-registry`: `extension list` reports installed extensions beside built-ins, with deliveries `process` and `wasm`, and the `--platform` and `--local` options change meaning to filter installed entries by platform and level.

## Tracking

- GitHub: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort that this change serves.
- <!-- TRACKING: a dedicated issue for the .NET extension installer (PRD step 3) is recommended; none exists yet. Link it here when it is created. -->
- Beads: no id is recorded. Do not invent one.
- Parent contract: finos/morphir `docs/design/draft/extensions/distribution-and-acquisition.md` and the reference implementation `ecosystem/morphir-rust/crates/morphir-distribution`, read at finos/morphir `c247aad39` (submodule `c9f79da`).
- Approved change: finos/morphir `openspec/changes/add-deployment-and-acquisition/` (tasks 3.3, 3.4, 4.4). Adoption change in this repository: `add-deployment-and-acquisition` on `docs/openspec-add-dotnet-deployment-and-acquisition`.
- Issues: #492 wires the existing handlers, which this change replaces for extensions; the design recommends closing #492 as superseded. #490 (`dist`) and #491 (`tool`) are not part of this change.
- Dependency: dotted extension ids in shared files wait for a separately reviewed change to the descriptor contract in finos/morphir, which is not written yet.

## Impact

- New F# library `src/Morphir.Extensions.Installation` (AOT-safe, no reference to `Morphir.Tooling`), so both hosts and `morphirup` can use it, and its Expecto tests.
- `src/Morphir.Extensions` (registry entries for installed extensions), `src/Morphir.Tooling/Features/Extensions` and `Scenarios/ManagementScenario.cs` (commands), `src/Morphir.Configuration/Schema.fs` (`[extension_install]`), `src/Morphir.Compilation.Mep` and `src/Morphir.Mep` (the describe probe).
- Removal from the extension path: `Features/Management/ExtensionHandlers.cs`, `ExtensionCommands.cs` and the extension use of `ArtifactPathResolver` and `Configuration/OsPaths.cs`.
- Docs: `docs/content/docs/cli/management.md`, the extension-model PRD (step 3 status and corrections), and a new installation guide.
- Upstream: issues and a proposal for the gaps the design names, and language-neutral fixtures for the acquisition documents in finos/morphir.
