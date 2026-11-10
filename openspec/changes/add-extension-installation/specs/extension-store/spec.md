# Spec Delta

## Purpose

Keeps installed extensions under Morphir Home in the upstream store, catalog and lock formats, so that a reader never sees partial state. The .NET and Rust CLIs share the format of these files. They share use of an installed extension only when both can run it, so the shared catalog admits only entries that every supported reader can read and run, and a .NET global layer holds the rest.

## ADDED Requirements

### Requirement: Morphir Home locates all extension state
The installer SHALL keep extension state under Morphir Home: a nonempty `MORPHIR_HOME`, otherwise `.morphir` in the user's home directory. A relative `MORPHIR_HOME` SHALL resolve against the current directory. Extension state SHALL NOT be written to the platform configuration directories that `OsPaths` names.

#### Scenario: Relocated home
- **WHEN** `MORPHIR_HOME` is an absolute directory and a user installs an extension
- **THEN** every file the install writes is below that directory

#### Scenario: Native Windows home
- **WHEN** on Windows `MORPHIR_HOME` is `C:\Users\dana\.morphir-test` written with backslashes
- **THEN** it is used as an absolute path and is not joined onto the current directory

### Requirement: Verified artifacts are stored by content digest
The installer SHALL publish each verified artifact at `store/extensions/sha256/<digest>/<filename>` below Morphir Home, where `<digest>` is the lowercase hexadecimal SHA-256 of its bytes and `<filename>` is the artifact filename of its record. This layout is the parent contract's, and objects in it are shared by every layer and every CLI.

#### Scenario: Stored path
- **WHEN** an artifact `acme-backend.exe` with SHA-256 `ab12…` (64 digits) is installed
- **THEN** its bytes are at `store/extensions/sha256/ab12…/acme-backend.exe` below Morphir Home
- **AND** the installed record's `storePath` is `store/extensions/sha256/ab12…/acme-backend.exe`, with `/` separators

### Requirement: The copy into the store is verified again
The installer SHALL hash the bytes while it copies them from staging into the store and SHALL compare that digest with the recorded digest before it publishes the object, so that a staged copy changed during the probe is never stored. A mismatch SHALL fail with `extension.digest_mismatch` and leave the store, catalog, locks and inventory unchanged.

#### Scenario: Guest rewrites its staged file during describe
- **WHEN** a staged guest overwrites its own staged executable while it answers `describe`
- **THEN** the install fails with `extension.digest_mismatch` and no store object, catalog entry, lock or inventory record is written

### Requirement: A stored object is never replaced
Before it reuses an existing store object, the installer SHALL hash it. An object whose digest differs SHALL stop the install with `extension.store_object_mismatch` and SHALL be left unchanged. A new object SHALL be written to a temporary file in its own directory and published without replacing a file that appeared meanwhile.

#### Scenario: Tampered object
- **WHEN** the file at the store path of a digest holds other bytes, and an install selects that digest
- **THEN** the install fails with `extension.store_object_mismatch` naming the path
- **AND** the file, the catalog and the locks are unchanged

#### Scenario: Two installs of one object
- **WHEN** two processes publish the same verified artifact at the same time
- **THEN** both succeed and the store holds one object with the expected digest

### Requirement: Store paths stay inside Morphir Home
The installer and the host SHALL refuse a store root, digest directory or stored object whose canonical path is outside Morphir Home, for example through a symbolic link, with `extension.path_escape`, and SHALL write nothing through it.

#### Scenario: Linked digest directory
- **WHEN** a digest directory in the store is a symbolic link to a directory outside Morphir Home
- **THEN** an install or activation that uses it fails with `extension.path_escape` and writes nothing

### Requirement: The shared catalog admits only entries every supported reader can run
Provisional pending design question Q-SHARED. A global install SHALL write a shared catalog entry and lock only when the entry's `mepVersions` holds a version the pinned reference reader supports (`0.1` at `c9f79da`), its id is valid at the catalog version that reader reads, and the catalog stays at a version that reader reads. Otherwise it SHALL install into the .NET global layer and write no shared entry or lock.

#### Scenario: A guest that speaks only MEP 0.2
- **WHEN** a user installs globally a process extension whose `mepVersions` is `["0.2.0-draft.1"]`
- **THEN** the catalog and `locks/extensions/` are unchanged, the .NET global layer selects it, and the report says `layer` `dotnet-global` with the reason `mep-version`
- **AND** a Rust `morphir extension list` on the same Morphir Home succeeds and does not list it

#### Scenario: A guest that speaks both versions
- **WHEN** a user installs globally an extension whose `mepVersions` is `["0.1", "0.2.0-draft.1"]`
- **THEN** the shared catalog and its lock hold the entry, and both CLIs list it

#### Scenario: A dotted id
- **WHEN** a user installs globally an extension with the id `org.finos.morphir.fsharp-backend` from a descriptor version that allows dotted ids
- **THEN** the shared catalog is unchanged and the .NET global layer selects it, with the reason `id-version`

### Requirement: The .NET global layer selects what the shared catalog cannot hold
Provisional pending Q-SHARED. The installer SHALL keep the .NET global layer in `data/morphir-dotnet/extensions/global.json` below Morphir Home, mapping each id to one inventory record by digest, sorted by id, under the state lock. A global install SHALL refuse an id already selected in the shared catalog or this layer with `extension.already_installed`.

#### Scenario: Same id in the shared catalog
- **WHEN** the Rust CLI installed `acme-backend` into the shared catalog and a user installs a 0.2-only `acme-backend` globally with the .NET host
- **THEN** the install fails with `extension.already_installed` naming the shared catalog, and nothing is written

#### Scenario: No shared file changes
- **WHEN** an install writes the .NET global layer
- **THEN** no file below `catalog/` or `locks/extensions/` changes

### Requirement: The catalog holds one active record per extension
The shared catalog SHALL be `catalog/extensions.json` below Morphir Home, with `schemaVersion` and `extensions` sorted by id, one entry per id, and the members the reference writer writes. `critical` and `requires` SHALL be written when the release has `requires.host`, with `critical` naming `requires.host`. An entry whose claims were only converted from a version 1 record SHALL have no `claims`, `claimCheck` or `probeSource`.

#### Scenario: Catalog entry members
- **WHEN** a process extension with a supplied claim set is installed and probed through `describe`
- **THEN** its catalog entry has `extensionId`, `name`, `version`, `runtime`, `platform`, `args`, `digest`, `storePath`, `capabilities`, `mepVersions`, `index`, `executable`, `claims`, `claimCheck` `probed` and `probeSource` `describe`

#### Scenario: A host requirement
- **WHEN** the installed release has `requires.host` `>=0.4.0`
- **THEN** the entry has `requires` with that range and `critical` `["requires.host"]`

#### Scenario: A converted version 1 record
- **WHEN** the installed record is a version 1 index record without a claim set, installed without a probe
- **THEN** the entry has no `claims`, `claimCheck` or `probeSource` member

#### Scenario: Two ids in order
- **WHEN** `zeta-backend` and then `alpha-frontend` are installed
- **THEN** the catalog lists `alpha-frontend` before `zeta-backend`

### Requirement: The catalog schema version follows its entries
The installer SHALL write catalog `schemaVersion` `"2.0.0-draft.2"` when any entry carries a supplied claim set, and `"1.0"` otherwise, as the reference implementation does (Q-CATVER, follows the reference).

#### Scenario: Only converted records
- **WHEN** every installed entry came from a version 1 index record without a claim set
- **THEN** the catalog `schemaVersion` is `"1.0"`

#### Scenario: A supplied claim set
- **WHEN** one installed entry has a claim set supplied by its descriptor
- **THEN** the catalog `schemaVersion` is `"2.0.0-draft.2"`

### Requirement: Each shared lock has exactly the upstream members
For each shared catalog entry the installer SHALL write `locks/extensions/<id>.json` below Morphir Home with `schemaVersion` `"1.0"` and only the members `schemaVersion`, `selection`, `extensionId`, `name`, `version`, `index`, `source`, `runtime`, `platform`, `args`, `digest`, `capabilities`, `mepVersions`, `frontend`, `backend` and `executable`, because upstream lock readers refuse other members (Q-STRICTLOCK, follows the reference).

#### Scenario: No extra member
- **WHEN** an extension is installed from a package archive
- **THEN** its shared lock has no member beyond that list, and the archive's own digest is not in it

#### Scenario: Claims pinned under index
- **WHEN** the installed artifact has a supplied claim set
- **THEN** the lock's `index.claims` holds that claim set, and the lock has no top-level `claims` member

#### Scenario: Portable WASM platform
- **WHEN** a WASM artifact is installed into the shared catalog
- **THEN** its lock has `"platform": null`, `"args": []` and `"executable": false`

### Requirement: Upstream records are read with the parent compatibility rules
The installer and the host SHALL read catalogs and records with the parent rules: ignore unknown optional members, refuse an unknown `critical` path and name it, accept released major 1 and the exact drafts `2.0.0-draft.1` and `2.0.0-draft.2`, convert a draft.1 record, and refuse a record that mixes draft.1 and draft.2 names.

#### Scenario: A newer optional member
- **WHEN** the catalog written by another CLI has an entry member this host does not know and that `critical` does not list
- **THEN** the host reads the catalog and keeps the entry

#### Scenario: A draft.1 entry
- **WHEN** a catalog entry has `statement` and `statementSource` `declared`
- **THEN** the host reads it as `claims` with `claimCheck` `unchecked`

#### Scenario: Mixed drafts
- **WHEN** a catalog entry has both `statement` and `claimCheck`
- **THEN** reading fails and names the entry

### Requirement: A shared entry this host cannot run does not hide the others
When a shared catalog entry's `mepVersions` holds no version this host speaks, the host SHALL keep reading the catalog, list the entry as unavailable with a `reason` that names its MEP versions, and refuse to activate it with `extension.no_compatible_mep_version`. Other entries SHALL resolve as usual.

#### Scenario: A Rust-installed legacy extension
- **WHEN** the Rust CLI installed `morphir-elm` with `mepVersions` `["0.1"]` and another entry speaks `0.2.0-draft.1`
- **THEN** `extension list --json` succeeds, shows `morphir-elm` with `available` false and its reason, and shows the other entry as available
- **AND** a command that selects `morphir-elm` fails with `extension.no_compatible_mep_version` and starts no process

### Requirement: A catalog entry and its lock agree
A host SHALL use a shared entry only when its lock exists and agrees with it on id, name, version, runtime, platform, arguments, digest, capabilities, MEP versions, index provenance, frontend and backend metadata, executable mode and pinned claims. Otherwise it SHALL report `extension.state_mismatch` naming the id and SHALL start no guest.

#### Scenario: Lock edited by hand
- **WHEN** the lock of `acme-backend` names another digest than its catalog entry
- **THEN** `extension list` reports `extension.state_mismatch` for that id
- **AND** no command starts that extension

### Requirement: One interprocess lock serializes extension state
Every read and write of the catalog, shared locks, the .NET global layer and the .NET inventory SHALL hold an exclusive operating-system lock on `locks/extensions.state.lock` below Morphir Home, opened for read and write without truncation and taken as the reference implementation takes it, so that a .NET and a Rust process exclude each other (Q-LOCKPRIM, follows the reference).

#### Scenario: Another process holds the lock
- **WHEN** a helper process holds the state lock with the reference implementation's primitive and a user runs `morphir extension install`
- **THEN** the install writes one line to stderr saying that it waits for another Morphir run, and waits
- **AND** it continues after the helper releases the lock

#### Scenario: The .NET host holds the lock
- **WHEN** a .NET install holds the state lock and a helper process tries to take it with the reference implementation's primitive
- **THEN** the helper blocks until the install releases it, and does not fail

#### Scenario: Cancelled wait
- **WHEN** the user cancels a command while it waits for the state lock
- **THEN** the command exits without changing any extension state

### Requirement: Lock and catalog change as one transaction
The installer SHALL change a shared lock and the catalog together through the reference journal `locks/extensions/<id>.json.transaction`, with `schemaVersion` 1, `nextLock` and `nextCatalog` as byte arrays or `null` for a deletion, and no other member. Each holder of the state lock SHALL first apply every journal it finds, whichever CLI wrote it, and then remove it (Q-JOURNAL, follows the reference).

#### Scenario: Interrupted install
- **WHEN** an install is stopped after it wrote the new lock and before it wrote the catalog
- **THEN** the next command that takes the state lock applies the journal, and the lock and catalog agree

#### Scenario: Journal left by the Rust CLI
- **WHEN** a journal in the reference format is present and a .NET command takes the state lock
- **THEN** the .NET command applies its bytes before it reads the catalog, and removes it

#### Scenario: Uninstall journal
- **WHEN** an uninstall is interrupted after it wrote its journal
- **THEN** the journal has `nextLock` `null`, and applying it deletes the lock and writes the catalog without the entry

### Requirement: Shared files are replaced atomically
The installer SHALL write each catalog, lock, journal, .NET global layer, inventory and pin file to a temporary file in the same directory, flush it, and rename it over the target, so that a reader sees the old or the new file and never a partial one.

#### Scenario: Reader during a write
- **WHEN** a host reads the catalog while an install replaces it
- **THEN** the host reads either the complete previous catalog or the complete new one

### Requirement: Removal never deletes store objects
Uninstall, update and selection changes SHALL NOT delete objects from the store. This change adds no store cleanup (Q-GC, follows the reference).

#### Scenario: Uninstall keeps bytes
- **WHEN** a user uninstalls an extension
- **THEN** its catalog entry and lock are gone and its store object remains

### Requirement: The .NET inventory records every installed version
The installer SHALL record each installed artifact in `data/morphir-dotnet/extensions/installed/<id>/<digest>.json` below Morphir Home, holding the entry and lock it would write globally and its source. A later install of the same id and digest with other arguments, claims or index SHALL replace the record and warn. For a shared entry the catalog and lock SHALL win over the record.

#### Scenario: Project-level install
- **WHEN** a user installs version 0.2.0 at the project level while 0.1.0 is installed globally
- **THEN** the inventory has a record for each of the two digests
- **AND** the global selection still names 0.1.0

#### Scenario: Record rebuilt after a Rust install
- **WHEN** the shared catalog has an entry that the inventory lacks
- **THEN** the host rebuilds the record from the catalog entry and lock, with source kind `shared-catalog`, and never reports a source it cannot know

### Requirement: Private run copies belong to the host that made them
A .NET host SHALL make its private run copies in directories named `morphir-dotnet-run-*` below `tmp/extensions/` in Morphir Home, SHALL remove its copy after the process exits, and before an activation SHALL remove its own copies left by an earlier run that it can delete. It SHALL NOT remove other directories there, such as the Rust host's `morphir-extension-*` copies.

#### Scenario: Copy left by a crash
- **WHEN** a .NET run copy from a crashed compile remains and no process uses it
- **THEN** the next .NET activation removes it and leaves every `morphir-extension-*` directory in place
</content>
</invoke>
