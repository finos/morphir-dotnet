# Spec Delta

## Purpose

Installs, updates, removes and describes external extensions from local sources only, and checks each artifact's identity, platform, digest and claims before any shared state changes.

## ADDED Requirements

### Requirement: Install reads a named local repository
`extension install --repository <name> <id>` SHALL resolve `<id>` from the repository `<name>` configured in `config/extensions/repositories.json` below Morphir Home, reading `extensions/<id>.jsonl` below its directory. A disabled or unknown repository SHALL be refused with `extension.repository_unavailable`.

#### Scenario: Repository configured by the Rust CLI
- **WHEN** the Rust CLI configured repository `local-dev` and a user runs `morphir extension install --repository local-dev morphir-fsharp-backend`
- **THEN** the .NET host reads that configuration and installs from `<directory>/extensions/morphir-fsharp-backend.jsonl`

#### Scenario: Disabled repository
- **WHEN** repository `local-dev` is disabled
- **THEN** the install fails with `extension.repository_unavailable` naming `local-dev`, and nothing is written

### Requirement: Repositories are configured in the upstream format
`extension repository add <name> --directory <dir>` SHALL record the canonical directory as a `local-directory` endpoint in the upstream repositories file, under its own lock, refusing a directory without an `extensions/` folder. `list` and `remove` SHALL read and change the same file. A name SHALL be a portable token of at most 64 characters.

#### Scenario: Add a repository
- **WHEN** a user runs `morphir extension repository add local-dev --directory ./ext-repo`
- **THEN** the repositories file has an enabled entry `local-dev` whose path is the canonical absolute directory

#### Scenario: Directory without metadata
- **WHEN** the directory has no `extensions` folder
- **THEN** the command fails and the repositories file is unchanged

### Requirement: Selection is a channel or an exact version
Install and update SHALL take `--channel <channel>` or `--version <semver>`, not both, and default to channel `stable`. `stable` SHALL select only releases without a prerelease label that list `stable`. `preview` and `insiders` SHALL select from all preview channels; `preview/<segment>` only from that one.

#### Scenario: Both options
- **WHEN** a user passes `--channel preview --version 1.0.0`
- **THEN** the command fails with a usage error, exit code 2

#### Scenario: Prerelease on stable
- **WHEN** a history lists `0.2.0-rc.1` on channel `stable` and `0.1.0` on `stable`
- **THEN** a stable install selects `0.1.0`

### Requirement: Resolution picks the newest release this host can run
Among releases matching the selection, the installer SHALL take the highest SemVer precedence whose artifacts speak an MEP version this host supports, skipping releases whose `requires.host` excludes this host. It SHALL refuse two matching artifacts for one platform, and SHALL name the platform when none matches.

#### Scenario: Newer release needs a newer host
- **WHEN** release 0.3.0 requires `>=9.0.0` of the host and 0.2.0 has no requirement
- **THEN** a stable install of a 0.4.0 host selects 0.2.0

#### Scenario: No artifact for this platform
- **WHEN** the newest release has process artifacts only for `x86_64-unknown-linux-gnu` and the host runs on `win-x64`
- **THEN** the install fails with `extension.no_matching_artifact` naming `windows-x86_64`

#### Scenario: Only legacy MEP versions
- **WHEN** every matching release speaks only MEP `0.1`, which this host does not support
- **THEN** the install fails with `extension.no_compatible_mep_version` naming the versions the host supports

### Requirement: Repository paths stay inside the repository
The installer SHALL canonicalize the history file and each artifact source path, and SHALL refuse one that resolves outside the repository directory, for example through a symbolic link or `..`, with `extension.path_escape`. A declared source path SHALL be relative, use `/`, and have at most 512 UTF-8 bytes and 240 UTF-16 code units.

#### Scenario: Linked artifact
- **WHEN** `artifacts/backend.exe` in a repository is a symbolic link to a file outside the repository
- **THEN** the install fails with `extension.path_escape` and reads no bytes through the link

### Requirement: A release history is consistent
The installer SHALL refuse a history whose records name different ids, repeat a version, or contain two versions of equal SemVer precedence, and SHALL record the SHA-256 of the exact history bytes as the index revision.

#### Scenario: Two equal precedences
- **WHEN** a history lists `1.0.0+a` and `1.0.0+b`
- **THEN** resolution fails and names both versions

### Requirement: The id rule is the rule of the contract version read
The installer SHALL check an extension id with the rule of the document version it reads. For record and descriptor versions `1.0`, `2.0.0-draft.1` and `2.0.0-draft.2` an id SHALL be one portable token of at most 238 characters that is also a portable file name. Dotted ids SHALL be accepted only from a version that allows them (Q-IDRULE).

#### Scenario: Dotted id in a draft.2 descriptor
- **WHEN** a version `2.0.0-draft.2` descriptor has `extensionId` `org.finos.morphir.fsharp-frontend`
- **THEN** the install fails with `extension.id_not_portable` naming the id and the descriptor version, and nothing is written

#### Scenario: Device name
- **WHEN** a record has the id `con`
- **THEN** reading refuses it with `extension.id_not_portable`

### Requirement: An installed id never reuses a built-in id
The installer SHALL refuse an extension whose id equals the id of a built-in registry entry of this release, with `extension.duplicate_id`.

#### Scenario: Impersonated generator
- **WHEN** a descriptor version that allows dotted ids names the extension `morphir.fsharp`
- **THEN** the install fails with `extension.duplicate_id` and the built-in stays the only `morphir.fsharp` entry

### Requirement: The host platform maps to one artifact platform
The installer SHALL map the host's runtime identifier to the descriptor target triple with the table of the toolchain contracts, and to the index platform `os` and `arch` spellings of the reference implementation. A host platform that the index cannot represent, such as `linux-musl-x64`, SHALL be refused with `extension.platform_unrepresentable` (Q-PLATFORM).

#### Scenario: Windows Arm64
- **WHEN** the host runs as `win-arm64`
- **THEN** it selects the artifact with triple `aarch64-pc-windows-msvc`, or index platform `windows` and `aarch64`

#### Scenario: musl host
- **WHEN** the host runs on `linux-musl-x64`
- **THEN** a process install fails with `extension.platform_unrepresentable`

### Requirement: Bytes are verified while they are staged
The installer SHALL copy the selected artifact into a private directory below `tmp/` in Morphir Home, hashing while it copies, refuse more than 256 MiB, and compare the digest with the record. A mismatch SHALL stop the install before the store, catalog, locks, inventory or pins change. Later steps SHALL use only the staged copy.

#### Scenario: Substituted artifact
- **WHEN** the repository file for the selected artifact has bytes whose SHA-256 differs from the record
- **THEN** the install fails with `extension.digest_mismatch` naming the expected and actual digests
- **AND** no store object, catalog entry, lock, inventory record or pin is written

#### Scenario: Oversized artifact
- **WHEN** the artifact is larger than 256 MiB
- **THEN** the install fails with `extension.artifact_too_large` after reading at most 256 MiB and one byte

### Requirement: Process artifacts are probed before they are recorded
For a `process` artifact, the installer SHALL start the staged copy and call `morphir.extension.describe` before any shared state changes. The answer SHALL equal the recorded claim set as parsed JSON values with ordered arrays (Q-NUMBERS). A refusal of `describe` with `-32601` or `-32014` SHALL fall back to a session.

#### Scenario: Claims agree
- **WHEN** the staged guest answers `describe` with a claim set equal to the descriptor's
- **THEN** the install records `claimCheck` `probed` and `probeSource` `describe`

#### Scenario: Claims disagree
- **WHEN** the guest answers with `backend.targets` `["fsharp","csharp"]` and the record has `["fsharp"]`
- **THEN** the install fails with `extension.claims_mismatch` naming `claims.capabilities.backend.targets`
- **AND** no store object, catalog entry, lock, inventory record or pin is written

#### Scenario: describe not implemented
- **WHEN** the guest answers `describe` with error `-32601`
- **THEN** the installer runs a session, checks it with the agreement rule, and records `probeSource` `session-fallback`

### Requirement: The claim check records what this install did
An installed record's `claimCheck` SHALL be `probed` only when this install or update ran the probe, and `unchecked` otherwise, whatever the index record says (Q-CLAIMCHECK). `--no-probe` SHALL skip the probe and print a note on stderr. WASM artifacts SHALL NOT be probed and SHALL be recorded `unchecked`.

#### Scenario: Probed in the index, not probed here
- **WHEN** the index record says `claimCheck` `probed` and the user installs with `--no-probe`
- **THEN** the installed record says `claimCheck` `unchecked` and has no `probeSource`

#### Scenario: WASM install
- **WHEN** a WASM artifact is installed
- **THEN** no guest code runs and the record says `claimCheck` `unchecked`

### Requirement: A probe runs with bounded rights and resources
A probe SHALL start the guest by absolute path with an argument vector and no shell, in a private working directory, with an environment limited to the variables the host passes, no console window on Windows, MEP framing limits, and a deadline after which the whole process tree is ended (Q-PROBELIMITS).

#### Scenario: Guest hangs
- **WHEN** the staged guest never answers `describe`
- **THEN** after the deadline the installer ends the guest and its child processes and fails with `extension.probe_failed`
- **AND** no shared state changes

#### Scenario: Guest writes junk on stdout
- **WHEN** the guest writes bytes that are not a framed MEP message
- **THEN** the install fails with `extension.probe_failed`

### Requirement: Host requirements and critical members are honored
The installer SHALL refuse a release or claim set whose `requires.host` comparators exclude this host's release version, naming the range, and a record whose `critical` names a path it does not understand, naming the path (Q-REQHOST).

#### Scenario: Unknown critical member
- **WHEN** a record lists `critical: ["artifacts.sandbox"]`
- **THEN** the install fails with `extension.unsupported_critical_member` naming `artifacts.sandbox`

### Requirement: Install and update are distinct
`extension install` SHALL refuse an id already installed at the target level with `extension.already_installed` and name `extension update`. `extension update` SHALL refuse an id that is not installed, and SHALL replace the installed record only through the same resolution, verification and probe steps.

#### Scenario: Install twice
- **WHEN** `morphir-fsharp-backend` is installed globally and a user installs it globally again
- **THEN** the command fails with `extension.already_installed` and the catalog is unchanged

#### Scenario: Update keeps the old version on failure
- **WHEN** an update's probe fails
- **THEN** the previously installed version stays selected and its record is unchanged

### Requirement: Uninstall removes one selection
`extension uninstall <id>`, and its alias `extension remove <id>`, SHALL remove the selection of `<id>` at the target level: at the global level the catalog entry and lock in one transaction, at another level the pin. An id not selected there SHALL fail with `extension.not_installed`.

#### Scenario: Global uninstall
- **WHEN** a user runs `morphir extension uninstall morphir-fsharp-backend`
- **THEN** the catalog entry and `locks/extensions/morphir-fsharp-backend.json` are removed together
- **AND** a Rust CLI no longer lists it

### Requirement: Installation never uses the network
No `extension` command SHALL download. An argument that is a URL SHALL be a usage error with exit code 2. `compile`, `generate` and `validate` SHALL NOT install an extension from a repository or a network.

#### Scenario: URL argument
- **WHEN** a user runs `morphir extension install https://example.org/ext.zip`
- **THEN** the command fails with exit code 2 and says that installation reads only local files and repositories

#### Scenario: Missing provider during compile
- **WHEN** a project selects an extension id that is not installed and a user runs `morphir compile`
- **THEN** compile fails with `extension.not_installed` and the install command to run, and opens no network connection

### Requirement: Authentication and claim checks are reported separately
Each install report SHALL state `authentication` apart from `claimCheck`. An install from a local repository, a file or a staged folder SHALL report `authentication` with kind `local-developer` and `policy` `developer`, the installation receipt's case for bytes no one authenticated. A matching checksum SHALL be reported as integrity, never as authentication.

#### Scenario: Local repository install
- **WHEN** a user installs from a local repository and the probe passes
- **THEN** the report has `authentication` `{"kind": "local-developer", "policy": "developer"}`, `integrity` `verified` and `claimCheck` `probed`

### Requirement: A caller can hand over authenticated files
The installer SHALL accept from an in-process caller, such as `morphirup`, a release descriptor, one artifact file and a statement of how the caller authenticated them (`repository-target` or `recipe-digest`), install them through the same checks, and report that statement unchanged. It SHALL NOT check signatures itself.

#### Scenario: Recipe-bound artifact
- **WHEN** a caller hands over a descriptor and artifact with authentication `recipe-digest` and the artifact's bytes differ from the descriptor's digest
- **THEN** the install fails with `extension.digest_mismatch`, as for any other source

#### Scenario: Statement kept
- **WHEN** the hand-over succeeds
- **THEN** the report's `authentication` is the caller's statement and `claimCheck` is the probe outcome

### Requirement: Platform differences are reported
When the release that an install reads has artifacts whose claim sets differ from the installed artifact's, the install and `extension info` SHALL name the members that differ and the platforms they differ on.

#### Scenario: Declared difference
- **WHEN** a descriptor with `platformDifferences` `declared` gives `win-x64` a claim member that `linux-x64` lacks
- **THEN** the install report on `linux-x64` names that member and `x86_64-pc-windows-msvc`

### Requirement: Info describes one installed extension
`extension info <id>` SHALL report the selected record for `<id>`: level, version, runtime, platform, digest, store path, claims, `claimCheck`, `probeSource`, source kind and selection, and the levels it shadows.

#### Scenario: Info as JSON
- **WHEN** a user runs `morphir extension info morphir-fsharp-backend --json`
- **THEN** stdout holds one JSON document with those members

### Requirement: Commands write results on stdout and messages on stderr
Every `extension` command SHALL write its result on stdout, and progress, notes, warnings and diagnostics only on stderr. With `--json` stdout SHALL hold exactly one JSON document. A command SHALL exit 0 on success, 1 when an operation is refused, and 2 for a usage error.

#### Scenario: JSON install
- **WHEN** a user runs `morphir extension install --repository local-dev morphir-fsharp-backend --json`
- **THEN** stdout parses as one JSON document with `operation` `install`, `extension`, `level`, `digest`, `storePath`, `source`, `authentication`, `integrity`, `claimCheck` and `diagnostics`
- **AND** the waiting and probe notes are on stderr

### Requirement: Refusals use stable codes
Each refusal SHALL carry one stable code from this capability and its sibling capabilities, the id it concerns when there is one, and a message for a person. Codes SHALL begin with `extension.` and SHALL NOT change meaning between releases.

#### Scenario: Code in the JSON report
- **WHEN** an install fails because the digest differs
- **THEN** the JSON report's `diagnostics` has one entry with `code` `extension.digest_mismatch`, `severity` `error` and the extension id
