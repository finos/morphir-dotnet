# Spec Delta

## Purpose

Keeps installed extensions in the shared Morphir Home state that every Morphir CLI reads, in the upstream store, catalog and lock formats, so that a reader never sees partial state and the .NET and Rust CLIs share global installs.

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
The installer SHALL publish each verified artifact at `store/extensions/sha256/<digest>/<filename>` below Morphir Home, where `<digest>` is the lowercase hexadecimal SHA-256 of its bytes and `<filename>` is the artifact filename of its record. This layout is the parent contract's.

#### Scenario: Stored path
- **WHEN** an artifact `morphir-fsharp-backend.exe` with SHA-256 `ab12…` (64 digits) is installed
- **THEN** its bytes are at `store/extensions/sha256/ab12…/morphir-fsharp-backend.exe` below Morphir Home
- **AND** the catalog entry's `storePath` is `store/extensions/sha256/ab12…/morphir-fsharp-backend.exe`, with `/` separators

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

### Requirement: The catalog holds one active record per extension
The global catalog SHALL be `catalog/extensions.json` below Morphir Home, with `schemaVersion` and `extensions` sorted by id, one entry per id. Each entry SHALL have the members the parent contract's installed record lists, including `storePath`, `index`, `claims`, `claimCheck` and, when probed, `probeSource`.

#### Scenario: Catalog entry members
- **WHEN** a process extension with a supplied claim set is installed and probed through `describe`
- **THEN** its catalog entry has `extensionId`, `name`, `version`, `runtime`, `platform`, `args`, `digest`, `storePath`, `capabilities`, `mepVersions`, `index`, `executable`, `claims`, `claimCheck` `probed` and `probeSource` `describe`

#### Scenario: Two ids in order
- **WHEN** `zeta-backend` and then `alpha-frontend` are installed
- **THEN** the catalog lists `alpha-frontend` before `zeta-backend`

### Requirement: The catalog schema version follows its entries
The installer SHALL write catalog `schemaVersion` `"2.0.0-draft.2"` when any entry carries a supplied claim set, and `"1.0"` otherwise, as the reference implementation does (design question Q-CATVER).

#### Scenario: Only converted records
- **WHEN** every installed entry came from a version 1 index record without a claim set
- **THEN** the catalog `schemaVersion` is `"1.0"`

#### Scenario: A supplied claim set
- **WHEN** one installed entry has a claim set supplied by its descriptor
- **THEN** the catalog `schemaVersion` is `"2.0.0-draft.2"`

### Requirement: Each global lock has exactly the upstream members
For each catalog entry the installer SHALL write `locks/extensions/<id>.json` below Morphir Home with `schemaVersion` `"1.0"` and only the members `schemaVersion`, `selection`, `extensionId`, `name`, `version`, `index`, `source`, `runtime`, `platform`, `args`, `digest`, `capabilities`, `mepVersions`, `frontend`, `backend` and `executable`, because upstream lock readers refuse other members (Q-STRICTLOCK).

#### Scenario: No extra member
- **WHEN** an extension is installed from a package archive
- **THEN** its global lock has no member beyond that list, and the archive's own digest is not in it

#### Scenario: Claims pinned under index
- **WHEN** the installed artifact has a supplied claim set
- **THEN** the lock's `index.claims` holds that claim set, and the lock has no top-level `claims` member

#### Scenario: Portable WASM platform
- **WHEN** a WASM artifact is installed
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

### Requirement: A catalog entry and its lock agree
A host SHALL use a global entry only when its lock exists and agrees with it on id, name, version, runtime, platform, arguments, digest, capabilities, MEP versions, index provenance, frontend and backend metadata, executable mode and pinned claims. Otherwise it SHALL report `extension.state_mismatch` naming the id and SHALL start no guest.

#### Scenario: Lock edited by hand
- **WHEN** the lock of `morphir-fsharp-backend` names another digest than its catalog entry
- **THEN** `extension list` reports `extension.state_mismatch` for that id
- **AND** no command starts that extension

### Requirement: One interprocess lock serializes extension state
Every read and write of the catalog, global locks and the .NET inventory SHALL hold an exclusive operating-system lock on `locks/extensions.state.lock` below Morphir Home, taken in the way the reference implementation takes it so that a .NET and a Rust process exclude each other (Q-LOCKPRIM).

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
The installer SHALL change a lock and the catalog together through the reference implementation's transaction journal `locks/extensions/<id>.json.transaction`. Each holder of the state lock SHALL first complete or roll back every journal it finds, whichever CLI wrote it (Q-JOURNAL).

#### Scenario: Interrupted install
- **WHEN** an install is stopped after it wrote the new lock and before it wrote the catalog
- **THEN** the next command that takes the state lock finishes the journal, and the lock and catalog agree

#### Scenario: Journal left by the Rust CLI
- **WHEN** a journal in the reference format is present and a .NET command takes the state lock
- **THEN** the .NET command applies it before it reads the catalog, and removes it

### Requirement: Shared files are replaced atomically
The installer SHALL write each catalog, lock, journal, inventory and pin file to a temporary file in the same directory, flush it, and rename it over the target, so that a reader sees the old or the new file and never a partial one.

#### Scenario: Reader during a write
- **WHEN** a host reads the catalog while an install replaces it
- **THEN** the host reads either the complete previous catalog or the complete new one

### Requirement: Removal never deletes store objects
Uninstall, update and selection changes SHALL NOT delete objects from the store. This change adds no store cleanup (Q-GC).

#### Scenario: Uninstall keeps bytes
- **WHEN** a user uninstalls an extension
- **THEN** its catalog entry and lock are gone and its store object remains

### Requirement: The .NET inventory records every installed version
The installer SHALL record each installed artifact, at any level, in `data/morphir-dotnet/extensions/installed/<id>/<digest>.json` below Morphir Home, holding the catalog entry and lock it would write globally and the true source kind. Upstream CLIs do not read this inventory.

#### Scenario: Project-level install
- **WHEN** a user installs version 0.2.0 at the project level while 0.1.0 is installed globally
- **THEN** the inventory has a record for each of the two digests
- **AND** the global catalog and lock still name 0.1.0
