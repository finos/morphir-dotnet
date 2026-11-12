# Spec Delta

## Purpose

Keeps installed extensions under Morphir Home in the shared store, catalog and lock files, with the members and values the shared contract defines, so that a reader never sees partial state and every current host shares global installs. The documents belong to the extension state family, versioned once by a required `$schema` (maintainer Decision 15A, 10 November 2026). finos/morphir#1149 (merged) puts every id in one catalog; the shared-file rules marked provisional below come from finos/morphir#1150 (merged, original inspection at `6e15650`). The Rust product CLI is to be deleted (Decision 16), and keeping it working on a shared home is not a goal.

## ADDED Requirements

### Requirement: Morphir Home locates all extension state
The installer SHALL keep extension state under Morphir Home: a nonempty `MORPHIR_HOME`, otherwise `.morphir` in the user's home directory. A relative `MORPHIR_HOME` SHALL resolve against the current directory. Extension state SHALL NOT be written to the platform configuration directories that `OsPaths` names.

#### Scenario: Relocated home
- **WHEN** `MORPHIR_HOME` is an absolute directory and a user installs an extension
- **THEN** every file the install writes is below that directory

#### Scenario: Native Windows home
- **WHEN** on Windows `MORPHIR_HOME` is `C:\Users\dana\.morphir-test` written with backslashes
- **THEN** it is used as an absolute path and is not joined onto the current directory

### Requirement: A home on a network file system is refused
Parent group 1 is published at `0439c4c0`; its schema/member tables and corpus are authoritative. A home on a network file system SHALL be outside the contract, because the lock primitive does not keep its guarantees there. When the host can tell that Morphir Home is on a network file system, it SHALL refuse every command that reads or writes extension state, naming the home. A host is not required to detect it.

#### Scenario: Home on NFS
- **WHEN** on Linux `statfs` reports that `MORPHIR_HOME` is on NFS and a user runs an install
- **THEN** the install fails, names the home, says a home on a network file system is not supported, and writes nothing

### Requirement: Verified artifacts are stored by content digest
The installer SHALL publish each verified artifact at `store/extensions/sha256/<digest>/<filename>` below Morphir Home, where `<digest>` is the lowercase hexadecimal SHA-256 of its bytes and `<filename>` is the artifact filename of its record. This layout is the parent contract's, and objects in it are shared by every layer and every host.

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

### Requirement: Publication preserves the verified object identity
The installer SHALL keep the verified file descriptor open through a platform operation bound to that descriptor. A stage pathname precheck followed by pathname publication SHALL NOT suffice. A substituted stage name SHALL never produce corrupt success. Cleanup SHALL retain foreign replacement objects; unsupported descriptor-bound publication SHALL fail closed.

#### Scenario: Stage name replaced after hashing
- **WHEN** another writer replaces the staging name after hashing and before publication
- **THEN** publication uses the verified object or refuses without corrupt success
- **AND** foreign objects are not overwritten or removed by cleanup

### Requirement: Supported writers serialize and detect directory tampering
Morphir Home SHALL be user-owned. Supported concurrent managers SHALL honor stable interprocess locks. The installer SHALL check opened directory identities against their expected ancestry before mutation and commit, and SHALL refuse detected rename-out or replacement. This contract does not promise isolation from arbitrary malicious same-UID actors, and does not require a broker or sandbox.

#### Scenario: Opened directory renamed out
- **WHEN** a test interleaving moves an opened digest directory outside Morphir Home before commit
- **THEN** the installer refuses with `extension.path_escape` and does not publish through the moved descriptor

#### Scenario: Two installs of one object
- **WHEN** two processes publish the same verified artifact at the same time
- **THEN** both succeed and the store holds one object with the expected digest

### Requirement: Store paths stay inside Morphir Home
The installer and the host SHALL refuse a store root, digest directory or stored object whose canonical path is outside Morphir Home, for example through a symbolic link, with `extension.path_escape`, and SHALL write nothing through it.

#### Scenario: Linked digest directory
- **WHEN** a digest directory in the store is a symbolic link to a directory outside Morphir Home
- **THEN** an install or activation that uses it fails with `extension.path_escape` and writes nothing

### Requirement: Every id shares one catalog
As finos/morphir#1149 (merged) defines, the installer SHALL record every global install, whatever the form of its id, in the one catalog `catalog/extensions.json` and the one lock directory `locks/extensions/`. The `extensions` entries SHALL be in ascending byte order of `extensionId`, each id once. A catalog out of order or naming an id twice SHALL be refused, naming the first entry out of place (provisional pending finos/morphir#1150).

#### Scenario: A dotted install beside a single-segment one
- **WHEN** a user installs `org.finos.morphir.fsharp-backend` into a home that holds `acme-backend`
- **THEN** `catalog/extensions.json` holds both entries, `acme-backend` first, and the new lock is `locks/extensions/org.finos.morphir.fsharp-backend.json`

#### Scenario: A repeated id
- **WHEN** a host reads a current catalog whose entries are `acme-backend`, `acme-backend`
- **THEN** it refuses the catalog and names its second entry

### Requirement: A catalog entry holds the members the shared contract defines
A catalog entry SHALL hold the members the shared contract defines for it; finos/morphir#1149's task 1.4 makes `distribution-and-acquisition.md` their authority, and until it does, today's reference members are the evidence. `critical` and `requires` SHALL be written when the release has `requires.host`, with `critical` naming `requires.host`.

#### Scenario: Catalog entry members
- **WHEN** a process extension with a supplied claim set is installed and probed through `describe`
- **THEN** its catalog entry has `extensionId`, `name`, `version`, `runtime`, `platform`, `args`, `digest`, `storePath`, `capabilities`, `mepVersions`, `index`, `executable`, `claims`, `claimCheck` `probed` and `probeSource` `describe`

#### Scenario: A host requirement
- **WHEN** the installed release has `requires.host` `>=0.4.0`
- **THEN** the entry has `requires` with that range and `critical` `["requires.host"]`

### Requirement: Every document of the family carries its current schema URL
Writers SHALL write every extension state document (descriptor, index record, catalog, lock, journal, repositories configuration, standalone claim set) with a required `$schema` naming that file's published schema at `0.1.0-preview.1`, and no `schemaVersion` (Decision 15A). Each history line SHALL carry its own `$schema`. A claim set embedded in a document SHALL carry none. The exact URLs are provisional pending finos/morphir#1150.

#### Scenario: A lock at the family version
- **WHEN** a user installs `acme-backend`
- **THEN** `locks/extensions/acme-backend.json` starts with a `$schema` naming the lock schema at `0.1.0-preview.1`, as merged finos/morphir#1150 defines it (`https://morphir.finos.org/schemas/extensions/0.1.0-preview.1/lock.schema.json`), and has no `schemaVersion`

#### Scenario: Embedded claims
- **WHEN** a catalog entry holds the installed claim set
- **THEN** the claim set has no `$schema` and no `claimsVersion`

### Requirement: Only current family documents are read
A reader SHALL accept a family document only when its `$schema` is that file's current URL, compared as exact text. It SHALL NOT read, replay or convert any other document, including one with `schemaVersion` and no `$schema` as earlier hosts wrote (decision DOC-1). A repository history SHALL be read whole and refused when any line is not current (provisional pending finos/morphir#1150).

#### Scenario: Historical example, a descriptor at an earlier draft
- **WHEN** a release's descriptor has `schemaVersion` `2.0.0-draft.2` and no `$schema`, as releases published before Decision 15A have
- **THEN** the install fails with `extension.unsupported_contract_version`, naming the descriptor and the URL this host reads, and installs nothing

#### Scenario: A history with an earlier line
- **WHEN** lines 1 and 2 of `extensions/acme-backend.jsonl` have `schemaVersion` `2.0.0-draft.2` and line 3 is a current record
- **THEN** the reader refuses the whole file, names it and line 1, and resolves no release from it

#### Scenario: A newer optional member
- **WHEN** a current catalog entry holds a member this host does not know and that `critical` does not list
- **THEN** the host reads the catalog and keeps the entry

### Requirement: A home an older CLI wrote is moved aside, never read
Under the state lock and before any recovery, a host SHALL detect old state: a catalog, lock, journal or repositories configuration without its current `$schema`, or more than one journal. When a command needing extension state finds it, the host SHALL show what would move, ask, and on acceptance move it to a marked backup below `backups/` and go on; `--yes` SHALL skip the question (Decisions 10 and 11).

#### Scenario: Accepted at the prompt
- **WHEN** a user at a terminal installs `acme-backend` in a home whose catalog an older CLI wrote at `schemaVersion` `2.0.0-draft.2`, and accepts
- **THEN** the host moves the old catalog, locks and journals to a backup directory, writes the notice naming it on stderr, and installs `acme-backend` into a new current catalog

#### Scenario: With --yes
- **WHEN** the same install runs with `--yes`
- **THEN** the host makes the same move without asking and installs `acme-backend`

### Requirement: A declined or unanswerable prompt changes nothing
When the user declines, or standard input is not a terminal and `--yes` is not given, the command SHALL fail and change nothing, naming the first old file, `morphir doctor --fix` and `--yes` (Decision 11). The host SHALL ask without holding the state lock, detect old state again after acceptance, and make the move and its own change in one hold. The backup layout is provisional pending finos/morphir#1150.

#### Scenario: In a script
- **WHEN** an install runs without a terminal and without `--yes` in a home with old state
- **THEN** it asks nothing, fails, changes nothing, and names `catalog/extensions.json`, `morphir doctor --fix` and `--yes`

### Requirement: morphir doctor reports and fixes old state
`morphir doctor` SHALL report old state and what `--fix` would move, change nothing, and exit 1 when there is old state. `morphir doctor --fix` SHALL make the same move as an accepted prompt, through one implementation, asking unless `--yes` is given (Decisions 10 and 11).

#### Scenario: Report only
- **WHEN** a user runs `morphir doctor` in a home whose catalog an older CLI wrote
- **THEN** it lists the catalog and each lock and journal `--fix` would move, exits 1, and changes no file

#### Scenario: Fix twice
- **WHEN** a user runs `morphir doctor --fix --yes` in that home, and then runs it again
- **THEN** the first run moves the old state to a backup and exits 0, and the second finds nothing to fix and exits 0

### Requirement: Each lock has exactly the members the shared contract defines
For each catalog entry the installer SHALL write `locks/extensions/<id>.json` with only the members the shared contract defines, a closed set: a reader SHALL refuse a lock with another member, naming it (Q-STRICTLOCK). Today's reference members are the evidence until finos/morphir#1149's task 1.4 defines them. .NET-only facts SHALL go to the .NET inventory.

#### Scenario: No extra member
- **WHEN** an extension is installed from a package archive
- **THEN** its lock has `$schema` and the members `selection`, `extensionId`, `name`, `version`, `index`, `source`, `runtime`, `platform`, `args`, `digest`, `capabilities`, `mepVersions`, `frontend`, `backend` and `executable` that the evidence lists, and not the archive's own digest

#### Scenario: Claims pinned under index
- **WHEN** the installed artifact has a supplied claim set
- **THEN** the lock's `index.claims` holds that claim set, and the lock has no top-level `claims` member

#### Scenario: Portable WASM platform
- **WHEN** a WASM artifact is installed globally
- **THEN** its lock has `"platform": null`, `"args": []` and `"executable": false`

### Requirement: A shared entry this host cannot run does not hide the others
A host SHALL keep reading a current catalog past an entry whose `mepVersions` holds no version it implements. It SHALL list the entry as unavailable with a `reason`, and refuse to activate it with `extension.no_compatible_mep_version`. A reason about MEP `0.1` SHALL say it is not supported.

#### Scenario: An entry another host installed
- **WHEN** a current catalog holds `acme-legacy` with `mepVersions` `["0.1"]` and another entry speaks `0.2.0-draft.1`
- **THEN** `extension list --json` succeeds, shows `acme-legacy` with `available` false and a reason saying it speaks only MEP `0.1`, which is not supported, and shows the other entry as available
- **AND** a command that selects `acme-legacy` fails with `extension.no_compatible_mep_version` and starts no process

### Requirement: A catalog entry and its lock agree
A host SHALL use a catalog entry only when its lock exists and agrees with it on id, name, version, runtime, platform, arguments, digest, capabilities, MEP versions, index provenance, frontend and backend metadata, executable mode and pinned claims. Otherwise it SHALL report `extension.state_mismatch` naming the id and SHALL start no guest.

#### Scenario: Lock edited by hand
- **WHEN** the lock of `acme-backend` names another digest than its catalog entry
- **THEN** `extension list` reports `extension.state_mismatch` for that id
- **AND** no command starts that extension

### Requirement: One interprocess lock serializes extension state
Every read and write of the catalog, locks, journal and .NET inventory SHALL hold `locks/extensions.state.lock`, taken with the contract's whole-file exclusive primitive: blocking `flock` `LOCK_EX` on Unix, blocking `LockFileEx` exclusive over the whole range on Windows, on a file opened to share read, write and delete (Q-LOCKPRIM; exact parameters provisional pending finos/morphir#1150). A host SHALL never write or remove a lock file.

#### Scenario: Another host holds the lock
- **WHEN** a helper process holds the state lock with that primitive and a user runs `morphir extension install`
- **THEN** the install's open of the lock file succeeds, it writes one line to stderr saying that it waits for another Morphir run, and waits
- **AND** it continues after the helper releases the lock

#### Scenario: The .NET host holds the lock
- **WHEN** a .NET install holds the state lock and a helper process tries to take it with that primitive
- **THEN** the helper waits until the install releases it, and does not fail

#### Scenario: Cancelled wait
- **WHEN** the user cancels a command while it waits for the state lock
- **THEN** the command exits without changing any extension state

### Requirement: Lock and catalog change as one transaction
The installer SHALL change a lock and the catalog together through one journal `locks/extensions/<id>.json.transaction` holding exactly `$schema`, `nextLock` and `nextCatalog`, each `null` for an absent file or the file's bytes as an array of integers. One journal SHALL exist at a time. Recovery SHALL validate the saved documents in full before it writes the lock, then the catalog, then removes the journal (Q-JOURNAL; provisional pending finos/morphir#1150).

#### Scenario: Interrupted install
- **WHEN** an install is stopped after it wrote the new lock and before it wrote the catalog
- **THEN** the next command that takes the state lock applies the journal, and the lock and catalog agree

#### Scenario: A journal an older CLI left
- **WHEN** `locks/extensions/acme-backend.json.transaction` has `schemaVersion` integer `1` and no `$schema`
- **THEN** the host writes nothing from it and treats the home as holding old state

#### Scenario: Uninstall journal
- **WHEN** an uninstall is interrupted after it wrote its journal
- **THEN** the journal has `nextLock` `null`, and applying it deletes the lock and writes the catalog without the entry

### Requirement: Shared files are replaced atomically
The installer SHALL write each catalog, lock, journal, repositories configuration, inventory and pin file to a temporary file named `.stage-…` in the same directory, flush it, and rename it over the target. A reader that lists those directories SHALL ignore entries whose name starts with `.` and entries that are not regular files.

#### Scenario: Reader during a write
- **WHEN** a host reads the catalog while an install replaces it
- **THEN** the host reads either the complete previous catalog or the complete new one

#### Scenario: A stale stage file
- **WHEN** recovery finds `locks/extensions/.stage-3f2a` beside a journal
- **THEN** it considers only the journal and leaves `.stage-3f2a` in place

### Requirement: Shared documents are I-JSON compared by parsed values
Parent group 1 is published at `0439c4c0`; its schema/member tables and corpus are authoritative. Every shared document SHALL be UTF-8 without a byte order mark and I-JSON, with no repeated member names and integer numbers only. An absent member SHALL differ from `null`. Two writers SHALL conform when their documents are equal as parsed values; bytes, member order and spacing need not be equal.

#### Scenario: A fraction in a journal
- **WHEN** a host reads a journal whose `nextLock` holds `123.0`
- **THEN** it refuses the journal and names the file

### Requirement: Removal never deletes store objects
Uninstall, update and selection changes SHALL NOT delete objects from the store. This change adds no store cleanup (Q-GC).

#### Scenario: Uninstall keeps bytes
- **WHEN** a user uninstalls an extension
- **THEN** its catalog entry and lock are gone and its store object remains

### Requirement: The .NET inventory records every installed version
The installer SHALL record each installed artifact in `data/morphir-dotnet/extensions/installed/<id>/<digest>.json` below Morphir Home, holding the entry and lock it would write globally and its source. A later install of the same id and digest with other arguments, claims or index SHALL replace the record and warn. For a global entry the catalog and lock SHALL win over the record.

#### Scenario: Project-level install
- **WHEN** a user installs version 0.2.0 at the project level while 0.1.0 is installed globally
- **THEN** the inventory has a record for each of the two digests
- **AND** the global catalog and lock still name 0.1.0

#### Scenario: Record rebuilt after another host's install
- **WHEN** the catalog has an entry that the inventory lacks
- **THEN** the host rebuilds the record from the catalog entry and lock, with source kind `shared-catalog`, and never reports a source it cannot know

### Requirement: Private run copies belong to the host that made them
A .NET host SHALL make its private run copies in directories named `morphir-dotnet-run-*` below `tmp/extensions/` in Morphir Home, SHALL remove its copy after the process exits, and before an activation SHALL remove its own copies left by an earlier run that it can delete. It SHALL NOT remove other hosts' directories there, such as `morphir-extension-*`.

#### Scenario: Copy left by a crash
- **WHEN** a .NET run copy from a crashed compile remains and no process uses it
- **THEN** the next .NET activation removes it and leaves every `morphir-extension-*` directory in place

### Requirement: Run creation and sweeping preserve ownership identity
Creation and sweeping SHALL serialize across processes until the new run directory has its owner lease. A sweep SHALL skip a busy creator or owner without waiting. Disposal and removal SHALL check the original directory and owner identities and SHALL refuse detected substitutions. Process-local serialization alone SHALL NOT satisfy this requirement.

#### Scenario: Sweep before owner creation
- **WHEN** an independent process sweeps after a run directory is created but before its owner lease is acquired
- **THEN** the creator's directory survives until ownership is established

#### Scenario: Replaced owner during disposal
- **WHEN** a test replaces the owned directory or owner file before disposal
- **THEN** disposal leaves the replacement objects untouched
