# Spec Delta

## Purpose

Decides which installed version of an extension a command uses, from the global catalog, .NET level pins and a toolchain generation overlay, and checks the installed bytes again before they run, without contacting any repository.

## ADDED Requirements

### Requirement: Selection layers have a fixed precedence
For each extension id a host SHALL use the first layer that selects it, in this order: the generation overlay, the project pin, the workspace pin, the global catalog, the system pin (Q-LEVELS, Q-OVERLAY). Project and workspace SHALL be found with the project system's discovery boundary.

#### Scenario: Project pins a newer version
- **WHEN** the global catalog selects `morphir-fsharp-backend` 0.1.0 and the project pin selects 0.2.0
- **THEN** commands in that project use 0.2.0 and commands elsewhere use 0.1.0

#### Scenario: Overlay wins
- **WHEN** an overlay selects 0.3.0 and the project pin selects 0.2.0
- **THEN** the command uses 0.3.0

### Requirement: Isolation omits machine layers
When discovery is isolated, the host SHALL omit the system pin and the global catalog, and SHALL keep the overlay and the project and workspace pins, which still resolve against the inventory in Morphir Home.

#### Scenario: Isolated test run
- **WHEN** `MORPHIR_ISOLATED` is set and the global catalog selects an extension that no project pin names
- **THEN** that extension is not selected

### Requirement: A pin file is portable
A level pin file SHALL be `.morphir/extensions/selection.json` at its level, with `schemaVersion` `0.1.0-draft.1` and `extensions` sorted by id. Each entry SHALL have `extensionId`, `version`, `runtime`, and the digest per platform identifier, or one `portable` digest for WASM. It SHALL hold no absolute path and no credential.

#### Scenario: Pin for two platforms
- **WHEN** a project pins `morphir-fsharp-backend` 0.2.0 after installs on `win-x64` and `linux-x64`
- **THEN** its entry has `artifacts` with the members `linux-x64` and `win-x64`, each with a `sha256`, and no file system path

#### Scenario: Unknown members
- **WHEN** a pin file has an unknown optional member and another with a `critical` path the host does not know
- **THEN** the host ignores the first and refuses the file, naming the second path

### Requirement: Pin files follow exact draft matching
A host SHALL refuse a pin file whose `schemaVersion` is not exactly a draft it lists, with `extension.unsupported_contract_version`, before it uses any entry of that file.

#### Scenario: Later draft
- **WHEN** a pin file has `schemaVersion` `0.1.0-draft.2`
- **THEN** the host refuses it and names the version

### Requirement: Pins resolve against the local inventory only
A host SHALL resolve a pin through the .NET inventory by id and the digest for its platform. A pin with no installed record SHALL fail with `extension.not_installed`, naming the id, version and the install command. It SHALL NOT fetch content or fall back to a lower layer.

#### Scenario: Cloned project on a new machine
- **WHEN** a project pin names a version that this machine has not installed
- **THEN** a command that needs the extension fails with `extension.not_installed` and the command to install it
- **AND** it does not use the globally installed version instead

#### Scenario: Platform missing from the pin
- **WHEN** the pin has digests only for `win-x64` and the command runs on `osx-arm64`
- **THEN** the command fails with `extension.platform_not_pinned` naming `osx-arm64`

### Requirement: A generation overlay is supplied by its path
A host SHALL read a generation overlay only when `MORPHIR_EXTENSION_OVERLAY` names an absolute directory below `toolchains/generations/` in Morphir Home that holds a pin file. Another path SHALL be refused with `extension.overlay_invalid`. The host and installer SHALL never write into an overlay.

#### Scenario: Overlay outside Morphir Home
- **WHEN** `MORPHIR_EXTENSION_OVERLAY` names `/tmp/gen`
- **THEN** the command fails with `extension.overlay_invalid` before it starts any extension

#### Scenario: Global selection untouched
- **WHEN** two projects run under overlays that select versions 0.2.0 and 0.3.0 of one id
- **THEN** each uses its own version, and the global catalog still selects the version it selected before

### Requirement: Old global catalogs stay readable under an overlay
An overlay or pin SHALL NOT require a change to the global catalog or locks. A host SHALL read a global catalog written by any upstream release whose format this host reads, with or without an overlay.

#### Scenario: Catalog from an older Rust CLI
- **WHEN** the global catalog has `schemaVersion` `"1.0"` and entries converted from version 1 records, and an overlay is set
- **THEN** ids that the overlay does not select resolve from that catalog

### Requirement: One registry entry per id
After precedence, each id SHALL appear once in the host's registry. Two entries with one id inside one layer, or an installed id equal to a built-in id, SHALL fail with `extension.duplicate_id`. A lower layer's entry for a selected id SHALL be reported as shadowed, not as a second entry.

#### Scenario: Duplicate in a pin file
- **WHEN** a project pin file lists `morphir-fsharp-backend` twice
- **THEN** `extension list` fails with exit code 2 and `extension.duplicate_id`

#### Scenario: Shadowed global version
- **WHEN** the project pin and the global catalog both select `morphir-fsharp-backend`
- **THEN** `extension list --json` has one entry for it, at level `project`, and `extension which` names the global selection as shadowed

### Requirement: Activation checks the installed bytes offline
Before it starts a guest, a host SHALL load the selected record and its lock, check that they agree, check that the store path resolves inside Morphir Home, hash the bytes, check the executable mode on Unix, and check that a recorded MEP version is one it supports. Any failure SHALL stop before guest code runs. This is the parent contract's activation.

#### Scenario: Bytes changed after install
- **WHEN** the store object of a selected extension was modified after install
- **THEN** the command fails with `extension.digest_mismatch` and starts no process

### Requirement: Only verified bytes run
A host SHALL launch a process extension from a private copy of the bytes it just verified, in a new directory below `tmp/extensions/` in Morphir Home, by absolute path, with the locked arguments exactly, an argument vector, no shell and no console window on Windows. It SHALL remove the copy after the process exits.

#### Scenario: Store object replaced during a run
- **WHEN** another process changes the store object while the guest runs
- **THEN** the running guest is the verified copy, and the next activation detects the change

#### Scenario: Uninstall while running on Windows
- **WHEN** a user uninstalls an extension while a compile runs it on Windows
- **THEN** the uninstall succeeds, and the running compile is not affected

### Requirement: Windows launch paths fit the process API
On Windows, a host SHALL refuse to launch a private copy whose absolute path is longer than the process creation API accepts, with `extension.path_too_long` naming the path and the limit, before it starts the process. Store and state files SHALL use paths that the .NET file APIs accept beyond 260 characters (Q-WINLAUNCH).

#### Scenario: Deep Morphir Home
- **WHEN** Morphir Home is so deep that the private copy's path exceeds the limit
- **THEN** the command fails with `extension.path_too_long` and suggests a shorter `MORPHIR_HOME`

#### Scenario: Long store path
- **WHEN** a store object's path is longer than 260 characters on Windows
- **THEN** install and activation read and write it without error

### Requirement: The session agrees with the installed claims
A host SHALL compare the guest's initialization result with the installed claim set by the protocol's agreement rule and SHALL refuse a session that does not agree, naming the first rule that failed, before it calls any operation.

#### Scenario: Guest offers an unclaimed target
- **WHEN** the installed claims list target `fsharp` and initialization offers `fsharp` and `csharp`
- **THEN** the host refuses the session and calls no operation

### Requirement: Configuration names an installed extension by id
A frontend table SHALL name an installed extension with `[frontend.<language>] extension_id = "<id>"`, apart from the existing `extension` path. Setting both SHALL be a configuration error. A named id SHALL resolve through the selection layers, and an unresolved id SHALL fail with `extension.not_installed` (Q-CONFIGREF).

#### Scenario: Compile with an installed frontend
- **WHEN** `morphir.toml` has `[frontend.fsharp] extension_id = "morphir-fsharp-frontend"` and that id is installed
- **THEN** `morphir compile` starts the verified private copy of the selected version

#### Scenario: Path and id together
- **WHEN** a frontend table sets both `extension` and `extension_id`
- **THEN** configuration loading fails and names both members

### Requirement: Use writes a pin for an installed version
`extension use <id> --version <semver> [--level project|workspace|global|system]` SHALL select an installed version at the level, default `project`. At the global level it SHALL replace the catalog entry and lock in one transaction. It SHALL refuse a version with no installed record for this platform.

#### Scenario: Use a version that is not installed
- **WHEN** a user runs `morphir extension use morphir-fsharp-backend --version 9.9.9`
- **THEN** the command fails with `extension.not_installed` and no pin changes

### Requirement: Which explains a selection
`extension which <id>` SHALL report the layer that selects `<id>`, its version, digest, store path and claim check, and every lower layer that also selects it.

#### Scenario: Which as JSON
- **WHEN** a user runs `morphir extension which morphir-fsharp-backend --json` in a project with a pin
- **THEN** stdout has one JSON document with `level` `project`, `version`, `digest`, `storePath`, `claimCheck` and `shadowed`

### Requirement: Level pin writes are atomic and serialized
Writes of a pin file SHALL replace it atomically and SHALL be serialized by a lock below `locks/` in Morphir Home keyed by the pin file's canonical path, so that two runs that write one pin take turns.

#### Scenario: Two writers
- **WHEN** two `extension use` commands change the same project pin file at once
- **THEN** the file afterwards is the complete result of one of them followed by the other
