# Proposal

## Why

The maintainer decided on 10 November 2026 that the Morphir CLI is the .NET one, and that its official delivery moves to
finos/morphir. finos/morphir pins this repository as `ecosystem/morphir-dotnet` and owns building, qualifying,
publishing and installing the CLI. This repository keeps the CLI source and the build and profile logic. Two
publishers of one CLI would compete, so this repository has to stop publishing the CLI and offer a stable build
interface instead.

The decision also separates the Morphir Compatibility Kit (MCK) from the CLI. finos/morphir provides `morphir-mck` as a
standalone, development-only tool, under modified Decision 16A. It keeps the existing compatibility gates available
until the .NET runner reaches parity. Users reach it as `morphir mck`, the way `dotnet morphir` reaches
`dotnet-morphir`, without the kit being linked into the CLI under test.

## What Changes

- `morphir mck <args>` runs the separately installed `morphir-mck` with the same arguments, working directory and
  standard streams, and exits with its exit code. It works the same in all three entry points: the Native AOT
  `morphir`, the managed `morphir` and `dotnet-morphir`.
- **Resolution.** The executable is resolved safely:
  - `MORPHIR_MCK` names an absolute path;
  - otherwise the first absolute `PATH` folder that holds `morphir-mck` (`morphir-mck.exe` on Windows) is used;
  - there is no shell, no relative or current-directory lookup, and no implicit installation.

  A missing tool is a clear diagnostic with exit code 127.
- **Cancellation and cleanup.** Ctrl+C reaches the tool, which decides how to stop. Cancelling the CLI stops the
  tool's whole process tree. On Windows the tool runs in a job object that ends its tree when the CLI process ends.
  `mck` is the only routed command; this is not a general plugin mechanism.
- **The CLI no longer embeds an MCK engine.** `Morphir.Conformance` stays a library that tests use; the CLI does not
  reference it. No second checker is created, and full command parity with the parent's `morphir-mck` is not claimed.
- **BREAKING (release process).** The deployment workflow no longer publishes the official CLI by default: the
  `morphir-<rid>` executables, the `morphirup-<rid>` assets and the `Morphir.Tool` package. Library packages are still
  published. The repository variable `MORPHIR_DOTNET_CLI_PUBLICATION` set to `enabled` restores the earlier behavior,
  as a rollback while the parent's channel is not yet live.
- **A reusable workflow for the parent.** `cli-offering.yml` checks out a given commit of this repository, builds one
  of the four offerings for one platform with the maintained release profiles, starts the produced executable, and
  uploads the archive, its launch metadata and its SHA-256. The four offerings, the `Morphir.Tool` package identity and
  the `dotnet-morphir` command stay as they are.
  The upload also carries `.build-contents.json` and the workflow exposes the release version. Its shared
  `install-tool-package.ps1` helper verifies an isolated install of the built bytes, with default tool-path and
  `-Local` manifest modes and optional success-only `-ResultFile` evidence. This helper is a build check, not an
  acquisition installer.

## Capabilities

### New Capabilities

- `cli-tool-dispatch`: routing `morphir mck` to the standalone `morphir-mck` executable. This covers resolution,
  arguments, streams, working directory, exit status, cancellation, process-tree cleanup, the missing-tool diagnostic,
  and the same behavior in every entry point.
- `cli-build-interface`: the build interface that the parent's official CLI delivery consumes. It is the targets, the
  reusable workflow, its inputs and outputs, and the qualification of produced executables. It also covers the
  boundary between this repository's library publication and the parent's CLI publication.

### Modified Capabilities

None. The offerings themselves (`dotnet-deployment-profiles` in `add-deployment-and-acquisition`) do not change.

## Tracking

- finos/morphir-dotnet#589 (this repository's work).
- finos/morphir#1153 (the parent's work: `ecosystem/morphir-dotnet`, its workflows and docs, `morphir-mck`, and the
  removal of the Rust product CLI).
- Decisions 15 and 16, and modified 16A, of finos/morphir#1146.

The parent owns the shared contracts. finos/morphir#1147 is merged at `836deccc5abfdf2a67fb555ec69d2bd561a13c4d`,
and #1152 at `776744f2cc709ff8c6ecf3c3fea2cf055d8cc2c9` sets one version per contract family, a required published
schema URL and exact prerelease matching without conversion. Any valid SemVer prerelease label is allowed; the
extension state family keeps `0.1.0-preview.1`. MEP 0.2, IR and tool release metadata v1 remain separate authorities.
This change renames or converts no contract and adds no Rust acquisition crate.

finos/morphir#1150, merged at `ec363c61dd955d6979e1c9f7cf94592d19f348ad`, owns shared extension state, locks and
old-state backups. #1151, merged at `cd391b07564823c14389d42cd14f05e89824e4f8`, owns package planning. Its VERIFY-B
decision checks manifest digest, names, sizes and modes at activation, with full file hashes at install and on
request. LAUNCH-A uses shared-store launch with a process-tree lease and explicit collection, never uninstall, to
delete bytes. Package-version timing remains open under its task 1.1. Those requirements belong to the installer
and package follow-ups. They do not add package leases or installation to the `mck` dispatcher.

## Impact

- **Code:**
  - `src/Morphir.Tooling` (the shared command layer: `MorphirApplication`, a new `Features/Mck` slice);
  - `tests/Morphir.Tooling.Tests` (the resolution unit tests);
  - `tests/Morphir.Build.Tests` (publication, workflow and tool-package helper checks);
  - a new test stub `tests/Morphir.Mck.Stub`;
  - `tests/Morphir.Tests` and `tests/Morphir.Managed.Tests`, which run produced apphosts as subprocesses.
- **Workflows:** `.github/workflows/deployment.yml` (the CLI publication gate) and a new
  `.github/workflows/cli-offering.yml`.
- **Docs:** `docs/content/docs/cli/mck.md`, the CLI reference index, a contributing page on the parent build
  interface, and the CHANGELOG.
- **No change** to the four offerings, the release profiles, `Morphir.Tool`'s id or command, the WASM engine, or the
  CET mitigation. No production release is made.
