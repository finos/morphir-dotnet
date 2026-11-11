# Spec Delta

## Purpose

Installs, updates, removes and describes external extensions from local sources only, and checks each artifact's identity, platform, digest and claims before any shared state changes.

## ADDED Requirements

### Requirement: Install reads a named local repository
`extension install --repository <name> <id>` SHALL resolve `<id>` from the repository `<name>` configured in `config/extensions/repositories.json` below Morphir Home, reading `extensions/<id>.jsonl` below its directory for every id, dotted or not, as finos/morphir#1149 (merged) defines. A disabled or unknown repository SHALL be refused with `extension.repository_unavailable`.

#### Scenario: Repository configured by another host
- **WHEN** another current host configured repository `local-dev` and a user runs `morphir extension install --repository local-dev acme-backend`
- **THEN** the .NET host reads that configuration and installs from `<directory>/extensions/acme-backend.jsonl`

#### Scenario: Disabled repository
- **WHEN** repository `local-dev` is disabled
- **THEN** the install fails with `extension.repository_unavailable` naming `local-dev`, and nothing is written

### Requirement: Repositories are configured in the upstream format
`extension repository add <name> --directory <dir>` SHALL record the directory as an enabled `local-directory` endpoint in the repositories configuration, under `locks/extensions.repositories.lock` with the state lock primitive, never holding both lock files at once. The file SHALL carry its family `$schema` and only the members the shared contract defines. A name SHALL be a portable token of at most 64 characters.

#### Scenario: Add a repository
- **WHEN** a user runs `morphir extension repository add local-dev --directory ./ext-repo`
- **THEN** the repositories file has an enabled entry `local-dev` whose path is the canonical absolute directory

#### Scenario: Directory without metadata
- **WHEN** the directory has no `extensions` folder
- **THEN** the command fails and the repositories file is unchanged

### Requirement: Repository identities are ordinary absolute paths
The installer SHALL write repository paths and lock `index.identity` as final OS paths of open roots. It SHALL strip `\\?\`, rewrite `\\?\UNC\server\share` as `\\server\share`, uppercase drive letters and omit trailing separators except at volume roots. Readers SHALL refuse verbatim paths and compare paths by bytes. Merged finos/morphir#1150 defines these rules; implementation waits for parent group 1 publication.

#### Scenario: Verbatim final path
- **WHEN** on Windows a user adds `C:\ext-repo` and the operating system reports its final path as `\\?\C:\ext-repo`
- **THEN** the repositories configuration names `C:\ext-repo`

#### Scenario: A verbatim path in a current file
- **WHEN** a current repositories configuration names a repository at `\\?\C:\ext-repo`
- **THEN** the host refuses the file and names the path

### Requirement: Selection is a channel or an exact version
Install and update SHALL take `--channel <channel>` or `--version <semver>`, not both, and default to channel `stable`. `stable` SHALL select only releases without a prerelease label that list `stable`. `preview` and `insiders` SHALL select from all preview channels; `preview/<segment>` only from that one.

#### Scenario: Both options
- **WHEN** a user passes `--channel preview --version 1.0.0`
- **THEN** the command fails with a usage error, exit code 2

#### Scenario: Prerelease on stable
- **WHEN** a history lists `0.2.0-rc.1` on channel `stable` and `0.1.0` on `stable`
- **THEN** a stable install selects `0.1.0`

### Requirement: Resolution picks the newest release this host can run
Among releases matching the selection, the installer SHALL take the highest SemVer precedence whose artifacts speak an MEP version this host implements, skipping releases whose `requires.host` excludes this host. It SHALL refuse two matching artifacts for one platform, and SHALL name the platform when none matches.

#### Scenario: Newer release needs a newer host
- **WHEN** release 0.3.0 requires `>=9.0.0` of the host and 0.2.0 has no requirement
- **THEN** a stable install of a 0.4.0 host selects 0.2.0

#### Scenario: No artifact for this platform
- **WHEN** the newest release has process artifacts only for `x86_64-unknown-linux-gnu` and the host runs on `win-x64`
- **THEN** the install fails with `extension.no_matching_artifact` naming `win-x64`

#### Scenario: Only MEP 0.1
- **WHEN** every matching release speaks only MEP `0.1`, which this host does not support
- **THEN** the install fails with `extension.no_compatible_mep_version`, naming the versions the host supports and saying that MEP `0.1` is not supported

### Requirement: The host supports the MEP 0.2 line
The supported MEP versions SHALL be those the comparators `>=0.2.0-0` and `<0.3.0` match, where a prerelease matches only when a comparator names the same major, minor and patch with a prerelease: every prerelease of `0.2.0` and every final `0.2.x` (maintainer decision MEP-1). MEP `0.1` SHALL NOT be supported. A host SHALL implement only versions of this line, and SHALL keep each one it has implemented. Today it implements `0.2.0-draft.1`.

#### Scenario: In the line
- **WHEN** the supported line is evaluated for `0.2.0-draft.1`, `0.2.0-rc.1`, `0.2.0` and `0.2.7`
- **THEN** each one is in the line

#### Scenario: Outside the line
- **WHEN** the supported line is evaluated for `0.1`, `0.1.0`, `0.2.1-rc.1`, `0.3.0-draft.1` and `0.3.0`
- **THEN** none is in the line

### Requirement: An entry runs only over a version this host implements
An entry or index record SHALL be runnable only when its `mepVersions` holds, exactly, a version this host implements. A version of the 0.2 line that the host does not implement yet SHALL make it unavailable with `extension.no_compatible_mep_version` and a reason saying the version is not implemented yet. A version outside the line SHALL be named as not supported.

#### Scenario: A later draft of the line
- **WHEN** an installed entry's `mepVersions` is `["0.2.0-draft.2"]` and this host implements only `0.2.0-draft.1`
- **THEN** `extension list` shows it unavailable with a reason saying MEP `0.2.0-draft.2` is not implemented by this host yet
- **AND** a command that selects it fails with `extension.no_compatible_mep_version` and starts no process

#### Scenario: Outside the line
- **WHEN** an entry's `mepVersions` is `["0.3.0"]`
- **THEN** it is unavailable with a reason saying MEP `0.3.0` is not supported

### Requirement: Repository paths stay inside the repository
The installer SHALL canonicalize the history file and each artifact source path, and SHALL refuse one that resolves outside the repository directory, for example through a symbolic link or `..`, with `extension.path_escape`. A declared source path SHALL be relative, use `/`, and have at most 512 UTF-8 bytes and 240 UTF-16 code units.

#### Scenario: Linked artifact
- **WHEN** `artifacts/backend.exe` in a repository is a symbolic link to a file outside the repository
- **THEN** the install fails with `extension.path_escape` and reads no bytes through the link

### Requirement: A release history is consistent
The installer SHALL refuse a history whose records name different ids, repeat a version, or contain two versions of equal SemVer precedence, and SHALL record the SHA-256 of the exact history bytes as the index revision. A history is read whole and refused when any line is not current (store capability, decision DOC-1).

#### Scenario: Two equal precedences
- **WHEN** a history lists `1.0.0+a` and `1.0.0+b`
- **THEN** resolution fails and names both versions

### Requirement: The id rule is the one finos/morphir#1149 defines
The installer SHALL check every id with the rule finos/morphir#1149 (merged) defines: one or more lowercase portable tokens joined by `.`, at most 238 characters, with a first segment that is not a Windows device name. A document at an earlier version SHALL be refused by version before its id is read (decision DOC-1).

#### Scenario: A dotted id in a current descriptor
- **WHEN** a current descriptor has `extensionId` `org.finos.morphir.fsharp-frontend`
- **THEN** the id is accepted

#### Scenario: Historical example, a dotted id at draft.2
- **WHEN** a descriptor has `schemaVersion` `2.0.0-draft.2` and `extensionId` `org.finos.morphir.fsharp-frontend`
- **THEN** the install fails with `extension.unsupported_contract_version` for the version, and nothing is written

#### Scenario: Device name
- **WHEN** a record has the id `con` or `com1`
- **THEN** reading refuses it with `extension.id_not_portable`

### Requirement: An installed id never reuses a built-in id
The installer SHALL refuse an extension whose id equals the id of a built-in registry entry of this release, with `extension.duplicate_id`. It SHALL refuse every id that starts with `morphir.`, a prefix reserved for built-in frontends and backends, with `extension.reserved_id` (maintainer decision, 9 November 2026). Neither refusal SHALL grant any id trust, precedence or built-in status.

#### Scenario: Impersonated generator
- **WHEN** a current descriptor names the extension `morphir.fsharp`
- **THEN** the install fails with `extension.duplicate_id` and the built-in stays the only `morphir.fsharp` entry

#### Scenario: Reserved prefix
- **WHEN** a current descriptor names the extension `morphir.example`, which no built-in uses
- **THEN** the install fails with `extension.reserved_id`

#### Scenario: Not reserved
- **WHEN** a descriptor names `org.finos.morphir.fsharp-frontend`
- **THEN** the prefix rule does not apply, and the id gains no trust from its segments

### Requirement: The host platform maps to one artifact platform
The installer SHALL map the host's runtime identifier to the descriptor target triple and to the index `os` and `arch` with the platform table of the toolchain contracts, whose "Tool release v1 platform" column gives the index spelling. A musl host, which that column cannot name, SHALL get `extension.no_matching_artifact` naming its runtime identifier for process artifacts (Q-PLATFORM, follows the reference).

#### Scenario: Windows Arm64
- **WHEN** the host runs as `win-arm64`
- **THEN** it selects the artifact with triple `aarch64-pc-windows-msvc`, or index platform `windows` and `aarch64`

#### Scenario: musl host
- **WHEN** the host runs on `linux-musl-x64` and the release has only process artifacts
- **THEN** the install fails with `extension.no_matching_artifact` naming `linux-musl-x64`

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
For a `process` artifact the installer SHALL start the staged copy and call `morphir.extension.describe` before shared state changes. The answer and the recorded claim set SHALL be compared over the typed claim set projection, without their schema members (Q-NUMBERS). A refusal with `-32601` or `-32014` SHALL fall back to a session. Lifecycle refusals that older guests signal only by message SHALL NOT trigger a fallback, because those guests predate `0.2.0-draft.1`.

#### Scenario: Claims agree
- **WHEN** the staged guest answers `describe` with a claim set equal to the descriptor's
- **THEN** the install records `claimCheck` `probed` and `probeSource` `describe`

#### Scenario: Claims disagree
- **WHEN** the guest answers with `backend.targets` `["fsharp","csharp"]` and the record has `["fsharp"]`
- **THEN** the install fails with `extension.claims_mismatch` naming `claims.capabilities.backend.targets`
- **AND** no store object, catalog entry, lock, inventory record or pin is written

#### Scenario: Projection, not raw JSON
- **WHEN** the answer has an unknown top-level member and omits `critical`, and the record has `critical` `[]`
- **THEN** the claim sets agree

#### Scenario: describe not implemented
- **WHEN** the guest answers `describe` with error `-32601`
- **THEN** the installer runs a session, checks it with the agreement rule, and records `probeSource` `session-fallback`

#### Scenario: A refusal by message only
- **WHEN** the guest answers `describe` with code `-32600` and the message `not initialized`
- **THEN** the install fails with `extension.probe_failed`, which deliberately differs from the reference

### Requirement: A describe answer carries the claims schema
The plan in finos/morphir#1150 is merged; implementation waits for parent group 1's published tables and cases. A claim set that `describe` returns SHALL carry the `$schema` of the claims schema at the family version, in place of `claimsVersion` (Decision 15A). An answer without it, or with another URL, SHALL fail the probe with `extension.unsupported_contract_version`, naming the URL this host reads.

#### Scenario: An answer at an earlier claims version
- **WHEN** the staged guest answers `describe` with `claimsVersion` `0.1.0-draft.2` and no `$schema`
- **THEN** the install fails with `extension.unsupported_contract_version` and writes nothing

### Requirement: The claim check records what this install did
An installed record's `claimCheck` SHALL be `probed` only when this install or update ran the probe, and `unchecked` otherwise, whatever the index record says. This deliberately differs from the reference `update` (Q-CLAIMCHECK). `--no-probe` SHALL skip the probe and print a note on stderr. WASM artifacts SHALL NOT be probed and SHALL be recorded `unchecked`.

#### Scenario: Probed in the index, not probed here
- **WHEN** the index record says `claimCheck` `probed` and the user installs with `--no-probe`
- **THEN** the installed record says `claimCheck` `unchecked` and has no `probeSource`

#### Scenario: WASM install
- **WHEN** a WASM artifact is installed
- **THEN** no guest code runs and the record says `claimCheck` `unchecked`

### Requirement: A probe runs with bounded rights and resources
A probe SHALL start the guest by absolute path with an argument vector and no shell, in a private working directory, with a cleared environment holding only `PATH`, `SYSTEMROOT` on Windows, and `TMP`, `TEMP` and `TMPDIR` naming a private directory, no console window on Windows, MEP framing limits, and a 30 second request deadline after which the whole process tree is ended (Q-PROBELIMITS).

#### Scenario: Guest hangs
- **WHEN** the staged guest never answers `describe`
- **THEN** after the deadline the installer ends the guest and its child processes and fails with `extension.probe_failed`
- **AND** no shared state changes

#### Scenario: Guest writes junk on stdout
- **WHEN** the guest writes bytes that are not a framed MEP message
- **THEN** the install fails with `extension.probe_failed`

#### Scenario: Environment filtered
- **WHEN** the user's environment holds `GITHUB_TOKEN` and the probed guest reports its environment
- **THEN** the guest saw only the allowed variables, and not `GITHUB_TOKEN`

### Requirement: Host requirements and critical members are honored
The installer SHALL refuse a release or claim set whose `requires.host` comparators exclude this host's release version, naming the range, and a record whose `critical` names a path it does not understand, naming the path (Q-REQHOST, follows the reference).

#### Scenario: Unknown critical member
- **WHEN** a record lists `critical: ["artifacts.sandbox"]`
- **THEN** the install fails with `extension.unsupported_critical_member` naming `artifacts.sandbox`

### Requirement: Install and update are distinct
`extension install` SHALL refuse an id already installed at the target level with `extension.already_installed`. `extension update` SHALL take its source as install does, with `--repository <name>` required for a repository, SHALL refuse an id not installed at the level, and SHALL replace the record only through the same resolution, verification and probe steps.

#### Scenario: Install twice
- **WHEN** `acme-backend` is installed globally and a user installs it globally again
- **THEN** the command fails with `extension.already_installed`, names `extension update`, and the global selection is unchanged

#### Scenario: Update keeps the old version on failure
- **WHEN** an update's probe fails
- **THEN** the previously installed version stays selected and its record is unchanged

#### Scenario: Update without a source
- **WHEN** a user runs `morphir extension update acme-backend` without `--repository`, a file or `--staged`
- **THEN** the command fails with a usage error, exit code 2

### Requirement: Uninstall removes one selection
`extension uninstall <id>`, and its alias `extension remove <id>`, SHALL remove the selection of `<id>` at the target level: at the global level the catalog entry and lock in one transaction; at another level the pin. An id not selected there SHALL fail with `extension.not_installed`.

#### Scenario: Global uninstall
- **WHEN** a user runs `morphir extension uninstall acme-backend`
- **THEN** the catalog entry and `locks/extensions/acme-backend.json` are removed together
- **AND** no other host that reads the catalog lists it

### Requirement: Installation never uses the network
No `extension` command SHALL download. An argument that is a URL SHALL be a usage error with exit code 2. `compile`, `generate` and `validate` SHALL NOT install an extension from a repository or a network.

#### Scenario: URL argument
- **WHEN** a user runs `morphir extension install https://example.org/ext.zip`
- **THEN** the command fails with exit code 2 and says that installation reads only local files and repositories

#### Scenario: Missing provider during compile
- **WHEN** a project selects an extension id that is not installed and a user runs `morphir compile`
- **THEN** compile fails with `extension.not_installed` and the install command to run, and opens no network connection

### Requirement: Authentication and claim checks are reported separately
Provisional pending Q-DEVPOLICY. Each install report SHALL state `authentication` apart from `claimCheck`. An install from a local repository, a file or a staged folder SHALL report kind `local-developer` with `policy` `developer`. A matching checksum SHALL be reported as integrity, never as authentication. The label SHALL appear only in the installer's reports and inventory, never in a receipt.

#### Scenario: Local repository install
- **WHEN** a user installs from a local repository and the probe passes
- **THEN** the report has `authentication` `{"kind": "local-developer", "policy": "developer"}`, `integrity` `verified` and `claimCheck` `probed`

### Requirement: A caller can hand over authenticated files
The installer SHALL accept from an in-process caller, such as `morphirup`, a release descriptor, one artifact file and a statement of how the caller authenticated them (`repository-target` or `recipe-digest`), install them through the same checks, and report the statement unchanged. It SHALL refuse any other kind, such as `local-developer`, and SHALL NOT check signatures or repository identities itself.

#### Scenario: Recipe-bound artifact
- **WHEN** a caller hands over a descriptor and artifact with authentication `recipe-digest` and the artifact's bytes differ from the descriptor's digest
- **THEN** the install fails with `extension.digest_mismatch`, as for any other source

#### Scenario: Statement kept
- **WHEN** the hand-over succeeds
- **THEN** the report's `authentication` is the caller's statement and `claimCheck` is the probe outcome

#### Scenario: A developer statement in a hand-over
- **WHEN** a caller hands over files with authentication `local-developer`
- **THEN** the hand-over is refused and nothing is written

### Requirement: A caller can learn what an install would write
Provisional pending the shared toolchain contract. Before it writes anything, the installer SHALL tell an in-process caller what a hand-over install would write: the store path, the catalog and lock or pin it would change, the inventory record, and whether the guest would run with the user's rights. The install that follows SHALL write nothing else, so that a manager's plan covers everything its command writes.

#### Scenario: Plan then install
- **WHEN** a caller asks what a hand-over would write and then performs it
- **THEN** the files the install changes are exactly those the answer named

### Requirement: Platform differences are reported
When the release that an install reads has artifacts whose claim sets differ from the installed artifact's, the install and `extension info` SHALL name the members that differ and the platforms they differ on.

#### Scenario: Declared difference
- **WHEN** a descriptor with `platformDifferences` `declared` gives `win-x64` a claim member that `linux-x64` lacks
- **THEN** the install report on `linux-x64` names that member and `x86_64-pc-windows-msvc`

### Requirement: Info describes one installed extension
`extension info <id>` SHALL report the selected record for `<id>`: level, version, runtime, platform, digest, store path, claims, `claimCheck`, `probeSource`, source and selection, and the levels it shadows.

#### Scenario: Info as JSON
- **WHEN** a user runs `morphir extension info acme-backend --json`
- **THEN** stdout holds one JSON document with those members

#### Scenario: Not installed
- **WHEN** a user runs `morphir extension info missing-ext`
- **THEN** the command fails with `extension.not_installed`, exit code 1

### Requirement: Commands write results on stdout and messages on stderr
Provisional pending Q-COMMANDS and Q-DIAG. Every `extension` command SHALL write its result on stdout, and progress, notes, warnings and diagnostics only on stderr. With `--json` stdout SHALL hold one JSON document. Exit codes SHALL be 0 on success, 1 for a refused operation and 2 for a usage error. Only `extension list` SHALL accept `--platform`.

#### Scenario: JSON install
- **WHEN** a user runs `morphir extension install --repository local-dev acme-backend --json`
- **THEN** stdout parses as one JSON document with `operation` `install`, `extension`, `level`, `digest`, `storePath`, `source`, `authentication`, `integrity`, `claimCheck` and `diagnostics`
- **AND** the waiting and probe notes are on stderr

#### Scenario: Platform on install
- **WHEN** a user runs `morphir extension install --repository local-dev acme-backend --platform linux-x64`
- **THEN** the command fails with a usage error, exit code 2, saying that an install is for the running platform only

### Requirement: Refusals use stable codes
Provisional pending Q-DIAG. Each refusal SHALL carry one stable code from this capability and its sibling capabilities, the id it concerns when there is one, and a message for a person. Codes SHALL begin with `extension.` and SHALL NOT change meaning between releases.

#### Scenario: Code in the JSON report
- **WHEN** an install fails because the digest differs
- **THEN** the JSON report's `diagnostics` has one entry with `code` `extension.digest_mismatch`, `severity` `error` and the extension id
