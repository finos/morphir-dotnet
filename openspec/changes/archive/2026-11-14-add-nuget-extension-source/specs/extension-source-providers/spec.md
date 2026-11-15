# Spec Delta

## Purpose

Defines what every extension source provider has in common: how a request names its provider, that every provider installs through the same installer, how the installed artifact's origin is recorded, how each payload shape is pinned, and how WASM artifacts are installed and run.

A source provider only finds a release and fetches its bytes. For example, `github:finos/morphir-python@0.6.2` and `nuget:Acme.Morphir.Python@1.2.3` select different providers, but the same installer checks the bytes, writes the package tree or module, and records the catalog, lock and journal entries.

## ADDED Requirements

### Requirement: A source request names its provider
`morphir extension install` SHALL read a positional source of the form `<scheme>:<rest>` and select the provider by its scheme: `github` or `nuget`. A local declaration keeps its own options. An unknown scheme, or a source with no scheme, SHALL fail with `extension.invalid_source` and name the known schemes.

#### Scenario: NuGet source
- **WHEN** a user runs `morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home <home> --no-probe`
- **THEN** the NuGet provider resolves the request

#### Scenario: Unknown scheme
- **WHEN** a user runs `morphir extension install npm:acme-python --home <home> --no-probe`
- **THEN** the command fails with `extension.invalid_source`
- **AND** the message names `github` and `nuget`

### Requirement: Every provider installs through one installer
The installer SHALL apply the same preflight, host requirement checks, package tree or module checks, store publication and catalog, lock and journal writes to every provider. A provider SHALL NOT write to the store, catalog, lock or journal itself.

#### Scenario: Same failure for every provider
- **WHEN** a release from a NuGet feed and a release from a GitHub release both require a host version this host does not satisfy
- **THEN** both installs fail with `extension.host_incompatible`
- **AND** neither writes a catalog, lock or journal entry

### Requirement: The origin of an artifact is recorded by provider
The lock and catalog SHALL record the provenance and the source of an installed artifact as provider-tagged members of the extension family: `local-directory` and `local-file`, `github-release` and `github-release-asset`, `nuget-package` and `nuget-package-entry`. They SHALL NOT record credentials.

#### Scenario: NuGet origin
- **WHEN** a user installs `nuget:Acme.Morphir.Python@1.2.3` from nuget.org
- **THEN** the lock records provenance `nuget-package` with the feed, the id `acme.morphir.python` in lower case, the version `1.2.3` and the SHA-256 of the `release.json` bytes
- **AND** the lock records source `nuget-package-entry` with the feed, id, version and package path of the installed artifact

#### Scenario: Provenance and source of one provider
- **WHEN** a lock pairs a `nuget-package-entry` source with `github-release` provenance
- **THEN** reading it fails, because a source is recorded only with the provenance of the same provider

### Requirement: Each payload shape has an integrity pin
The installer SHALL check a payload against its pin before it publishes anything: a `zip` or `tar.gz` archive against its `sha256` and then its `treeSha256`; a `nupkg` payload against its `treeSha256`; a WASM module against its `sha256`. A mismatch SHALL install nothing and fail with `package.archive_digest_mismatch`, `package.tree_digest_mismatch` or, for a WASM module, `extension.digest_mismatch`.

#### Scenario: Changed package payload
- **WHEN** a file under the declared `root` of a `nupkg` payload differs from what `treeSha256` describes
- **THEN** the install fails with `package.tree_digest_mismatch`
- **AND** the Home's store, catalog, lock and journal are unchanged

#### Scenario: Changed WASM module
- **WHEN** the bytes of a WASM artifact do not match its descriptor `sha256`
- **THEN** the install fails with `extension.digest_mismatch`

### Requirement: A WASM artifact installs from every provider without running the guest
When the selected artifact has runtime `wasm`, the installer SHALL store the verified module in the single-file store and record the descriptor's claims with `claimCheck` `unchecked`, for the local, GitHub and NuGet providers alike. It SHALL NOT run the guest during installation.

#### Scenario: WASM from a GitHub release
- **WHEN** a user installs a GitHub release whose descriptor has only a WASM artifact
- **THEN** the install succeeds and the lock records runtime `wasm` with `claimCheck` `unchecked`
- **AND** no guest code runs

### Requirement: An installed WASM extension runs in compile and generate
When configuration selects an installed extension by id and its lock records runtime `wasm`, `morphir compile` and `morphir generate` SHALL verify the stored module against the lock offline and run it through the host's WASM runtime, with the same session claim check as an installed process extension. A host without a WASM runtime SHALL fail with `extension.runtime_unavailable`.

#### Scenario: Installed WASM frontend
- **WHEN** a project selects the installed WASM frontend `com.acme.analyzer` and the host has a WASM runtime
- **THEN** `morphir compile` runs the stored module and checks the session against the installed claims

#### Scenario: Changed stored module
- **WHEN** the stored module's bytes differ from the lock's `sha256`
- **THEN** the command fails before the guest runs

#### Scenario: Host without a WASM runtime
- **WHEN** a host without a WASM runtime runs `morphir generate` with an installed WASM backend
- **THEN** the command fails with `extension.runtime_unavailable`

### Requirement: The installer selects a process artifact before a WASM artifact
For every provider, the installer SHALL select the one `process` artifact whose platform matches the host, else the first `wasm` artifact, else fail with `extension.no_matching_artifact` and install nothing. The lock SHALL record the selected artifact with its `runtime`, and activation SHALL NOT select again.

#### Scenario: Mixed NuGet release on a matching platform
- **WHEN** the `morphir/release.json` of a NuGet package lists a `wasm` artifact and a `process` artifact for the host platform
- **THEN** the installer selects the `process` artifact and the lock records runtime `process`

#### Scenario: Mixed release on another platform
- **WHEN** the descriptor lists a `process` artifact for `x86_64-unknown-linux-gnu` and a `wasm` artifact, and the host is `aarch64-apple-darwin`
- **THEN** the installer selects the `wasm` artifact

#### Scenario: No matching artifact
- **WHEN** the descriptor lists only a `process` artifact for another platform
- **THEN** the install fails with `extension.no_matching_artifact` and the Home is unchanged

### Requirement: A descriptor may offer both runtimes
The descriptor reader SHALL NOT refuse a release descriptor that lists `process` and `wasm` artifacts.

#### Scenario: Mixed descriptor
- **WHEN** a descriptor lists a `process` artifact and a `wasm` artifact
- **THEN** the reader accepts it

### Requirement: A dry run changes nothing
`--dry-run` SHALL resolve the release and select the artifact, and SHALL NOT write to the Home's store, catalog, lock or journal or download any artifact that the selection does not need to read the descriptor.

#### Scenario: Dry run from NuGet
- **WHEN** a user runs `morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home <home> --no-probe --dry-run`
- **THEN** the command reports the planned extension id, version and artifact
- **AND** the Home's store, catalog, lock and journal are unchanged
