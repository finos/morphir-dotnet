# Proposal

## Why

morphir-dotnet cannot install an external extension. The `extension install`, `use`, `remove` and `which` commands are stubs (`src/Morphir.Tooling/Scenarios/ManagementScenario.cs`), and the handlers behind them download a URL into a folder that is not the upstream store and check no digest. The extension-model PRD plans the installer as step 3.

The finos/morphir change `add-deployment-and-acquisition`, approved on 9 November 2026, makes the same installer a dependency of `morphirup`: "Extension acquisition SHALL use extension descriptors, claims, store and lock contracts", and setup "SHALL use the host's extension configuration, store, claims and locks". Its tasks 3.3, 3.4 and 4.4 cannot start without it.

On 9 November 2026 the maintainer decided that the .NET installer is built inside that effort, as its own OpenSpec change in this repository, following the parent contract. This is that change.

## What Changes

A developer on Windows adds a local extension repository and installs a process backend from it. A second project pins a newer version of the same backend without changing the global selection, and a `morphirup` toolchain generation selects its own versions over the shared store. A teammate who clones a repository with an extension package archive in `.morphir/extensions/staged/` gets a clear message, and no code from the clone runs until they install it.

This host does not support MEP `0.1` (maintainer decision, 9 November 2026): "we don't have users to support the cost of maintaining 2 versions". The maintainer said the same day what is supported instead: "We should support 0.2.0 all prerelease and final 0.2.x releases".

- **The supported protocol is the whole 0.2 line.** That is every prerelease of `0.2.0` and every final `0.2.x`, written as the comparators `>=0.2.0-0` and `<0.3.0` under the npm and Cargo prerelease rule.
- **What exists today.** The only version of that line is `0.2.0-draft.1`, and it is what this host implements.
- **Later versions are added, not swapped.** As later versions of the line appear, the host keeps the earlier ones.
- **This is an explicit exception.** The general rule is that a prerelease matches only exactly.

The same day the maintainer decided to stop reading transient pre-1.0 document formats: "we are paying too much of a cost to support transient pre 1.0 contracts". This host reads index records, descriptors and catalog entries only at the current draft, `2.0.0-draft.2` until finos/morphir#1149 moves every document to `2.0.0-draft.3` (lock `2.0.0-draft.1`), with claim sets at `0.1.0-draft.2`. It does not read or convert version 1 records and descriptors, `2.0.0-draft.1` or claim set draft.1. A Rust-written catalog entry in those formats is listed as unavailable and carried through unchanged.

Every global install goes to the shared catalog, `catalog/extensions.json`, so the current hosts (.NET now, MoonBit later) share what is installed. The maintainer accepted the cost on 9 November 2026: a released Rust CLI cannot list a Morphir Home the .NET host has installed into, because one catalog entry it cannot run fails every Rust `compile`, `generate` and `extension list`. "For now we are treating the rust cli as legacy." A user who needs the Rust CLI gives it its own `MORPHIR_HOME`.

```text
source (named repository | package archive | staged folder | manager handoff)
  -> read descriptor or index record -> select the artifact for this platform
  -> verify digests while staging -> describe probe (process, unless --no-probe)
  -> copy into MORPHIR_HOME/store, hashing again
  -> write lock and catalog, or a pin, under the state lock
  -> resolve through selection layers -> verify again offline -> run a private copy
```

- **The installer.** It writes the shared store, catalog and locks under Morphir Home, with the members and values the shared contract defines, under one interprocess lock.
  - Where only the Rust source specifies the contract today, the installer follows it and asks upstream to write it down. It does not copy Rust quirks the contract does not need.
  - A shared entry this host cannot run, such as one the Rust CLI installed that speaks only `0.1`, is listed as unavailable instead of breaking the list.
  - A .NET inventory keeps the .NET-only facts that the closed catalog and lock member sets cannot carry.
  - It never downloads anything. Compile, generate and validate never install.
- **Install sources.** Each source becomes the same resolved record before any byte is stored:
  - a named local-directory repository (the upstream path);
  - an extension package archive or a bare `.wasm` file given on the command line;
  - the `staged/` folders;
  - authenticated files that `morphirup` hands over.
- **Claim checks.** They happen at install time, and are recorded as `probed` or `unchecked` by what this install did. That is a deliberate divergence from the reference `update`. WASM is not probed, as upstream decided. Publisher authentication is a separate fact, reported separately, and a checksum is never called authentication.
- **Selection layers.** The layers are a generation-scoped overlay for `morphirup`, project and workspace pins, the shared catalog, and a system pin. One id resolves to one installed record, offline, with the bytes hashed again in the read that makes the private run copy. The overlay and the pins serve a contract that belongs in finos/morphir's toolchain specifications. Until that contract exists, they are provisional and raised upstream.
- **Package archives.** They are read in zip and tar gzip, using only the base class library, with the PRD's entry rules plus ASCII-only names, zip header consistency and recorded size, count and ratio limits.
- **Commands.**
  - The verbs are `extension install`, `update`, `uninstall` (with `remove` as an alias), `list`, `info`, `use`, `which` and `extension repository add|list|remove`.
  - Each has `--json` on stdout and messages on stderr.
  - Only `list` keeps `--platform`.
- **BREAKING** for a command that never worked: the `extension install <name> <url> <version>` arguments go away. The stub never installed anything, and no release documented it as working. The download handlers in `src/Morphir.Tooling/Features/Management` stop being used for extensions.

The design groups its questions by what they need:

- **Follows today's reference behavior.** The specs follow what the Rust source does where the contract is unwritten. The ask is to write it into the contract pages, and no maintainer answer is needed to start.
- **Needs the maintainer.** The requirements that depend on these questions are marked provisional in the spec text, and each question names the tasks it blocks.

The decision that mattered most, what may enter the shared catalog (Q-SHARED), is settled: every global install. The design records how the question went three turns, so the .NET-only layer is not restored again. Nothing here is shipped, released or qualified.

## Capabilities

### New Capabilities

- `extension-installation`: the install, update, uninstall and info pipeline from each source: record reading, the id rule of the contract version, artifact selection, digest checks, claim probes, reports and diagnostics.
- `extension-store`: the shared Morphir Home state: content-addressed store, catalog, locks, the .NET inventory, the interprocess state lock, transaction recovery and compatibility with upstream readers.
- `extension-package-archives`: the extension package archive and bare `.wasm` formats, archive safety rules and limits, and the `staged/` folders with their install policy.
- `extension-selection`: level pins, the generation overlay, precedence, offline activation checks and launching only verified bytes.

### Modified Capabilities

- `extension-registry`: `extension list` reports installed extensions beside built-ins, with deliveries `process` and `wasm`, `deployments` and `reason` for installed entries, and a `--platform` filter over recorded artifacts. "Extension ids are unique" extends to selection layers. The MODIFIED delivery block is written against the text of the adoption change `add-deployment-and-acquisition`, which is active on main.

## Tracking

- GitHub: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort that this change serves.
- <!-- TRACKING: a dedicated issue for the .NET extension installer (PRD step 3) is recommended; none exists yet. Link it here when it is created. -->
- Beads: no id is recorded. Do not invent one.
- Parent contract: finos/morphir `docs/design/draft/extensions/distribution-and-acquisition.md` and the reference implementation `ecosystem/morphir-rust/crates/morphir-distribution`, read at finos/morphir `c247aad39` (submodule `c9f79da`). The toolchain pages were read at finos/morphir `efdbc34da`.
- Approved change: finos/morphir `openspec/changes/add-deployment-and-acquisition/` (tasks 3.3, 3.4, 4.4). Its adoption in this repository is on main (`b8ebd12cd`).
- Dotted ids: finos/morphir#1149, read at `123bbffc5` (the one-catalog rewrite, not yet reviewed). The maintainer chose one catalog: every id goes in `catalog/extensions.json`, `locks/extensions/` and a repository's `extensions/`, at new drafts, and its task 2.3 is for this installer. Toolchain contract pages: finos/morphir#1147 at `60d363059`.
- Issues: #492 wires the existing handlers, which this change replaces for extensions; the design recommends closing #492 as superseded. #490 (`dist`) and #491 (`tool`) are not part of this change.

## Impact

- New F# library `src/Morphir.Extensions.Installation` (AOT-safe, no reference to `Morphir.Tooling`), so both hosts and `morphirup` can use it, and its Expecto tests.
- `src/Morphir.Extensions` (registry entries for installed extensions), `src/Morphir.Tooling/Features/Extensions` and `Scenarios/ManagementScenario.cs` (commands), `src/Morphir.Configuration/Schema.fs` (`[extension_install]`), `src/Morphir.Compilation.Mep` and `src/Morphir.Mep` (the describe probe).
- A workflow job that runs the pinned Rust CLI as a separate executable against Morphir Homes written by the .NET code, from task 3.5 on.
- Removal from the extension path: `Features/Management/ExtensionHandlers.cs`, `ExtensionCommands.cs` and the extension use of `ArtifactPathResolver` and `Configuration/OsPaths.cs`.
- Docs: `docs/content/docs/cli/management.md`, the extension-model PRD (step 3 status and corrections), and a new installation guide.
- Upstream: issues and review comments for the asks the design lists, and language-neutral fixtures for the acquisition documents in finos/morphir.
</content>
