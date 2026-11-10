# Spec Delta

## Purpose

Lets a user install an extension from one file, the extension package archive or a bare `.wasm` module, and from the `staged/` folders, and reads archives so that no entry can escape its directory, exhaust resources or substitute an artifact.

## ADDED Requirements

### Requirement: The format comes from the file content
The installer SHALL read the format from the first bytes: `50 4B 03 04` is a zip archive, `1F 8B` a gzip stream that SHALL hold a tar archive, and `00 61 73 6D` a WASM module. A gzip stream that holds no tar archive, an uncompressed tar archive and any other content SHALL be refused with `extension.unsupported_package`.

#### Scenario: Misleading name
- **WHEN** a zip archive is named `ext.tar.gz`
- **THEN** it is read as a zip archive

#### Scenario: Gzip of one file
- **WHEN** a file is a gzip stream of a single `release.json`
- **THEN** it is refused with `extension.unsupported_package`

### Requirement: A package archive has one descriptor and checksums
A package archive SHALL hold at its root exactly one `*.release.json` descriptor, its `.sha256` file, the artifact for the host's platform, and a `.sha256` file for each artifact present. Artifacts of other platforms are optional. Any other entry SHALL be refused with `extension.unexpected_entry` naming it.

#### Scenario: Release assets zipped as published
- **WHEN** a zip holds `morphir-fsharp-backend-0.1.0.release.json`, its `.sha256`, `morphir-fsharp-backend-0.1.0-x86_64-pc-windows-msvc.zip` and its `.sha256`
- **THEN** the archive is accepted on `win-x64`

#### Scenario: Two descriptors
- **WHEN** the archive holds two `*.release.json` files
- **THEN** it is refused and both names are reported

#### Scenario: An extra file
- **WHEN** the archive also holds `README.md`
- **THEN** it is refused with `extension.unexpected_entry` naming `README.md`

### Requirement: Archive entries cannot escape or alias
The installer SHALL read the entry list before it extracts anything, and SHALL refuse an entry with an absolute path, a drive letter, a `\`, a `..` segment, a name that is not a portable file name, or a name equal to another entry after Unicode normalization and case folding. Tar path rules SHALL apply after PAX and GNU long-name headers.

#### Scenario: Traversal
- **WHEN** an archive has the entry `../../evil.exe`
- **THEN** it is refused with `extension.unsafe_archive` naming the entry, and no file is written anywhere

#### Scenario: Long-name header
- **WHEN** a tar entry's GNU long-name header changes its path to `a/../../b`
- **THEN** it is refused with `extension.unsafe_archive`

#### Scenario: Case alias
- **WHEN** an archive has `Ext.exe` and `ext.exe`
- **THEN** it is refused on every platform

### Requirement: Only regular files and folders are accepted
The installer SHALL accept only regular file and folder entries. It SHALL refuse symbolic links, hard links, device files and FIFOs, and SHALL NOT copy permission bits or owners from entries.

#### Scenario: Symbolic link entry
- **WHEN** a tar archive has a symbolic link entry
- **THEN** it is refused with `extension.unsafe_archive`

### Requirement: Archive reading has recorded limits
The installer SHALL refuse an archive file over 1 GiB, more than 1024 entries, an entry over 256 MiB, more than 1 GiB in total extracted, and an entry over 1 MiB whose extracted size is more than 200 times its compressed size. The limits SHALL apply to zip entries and to the gzip stream alike, and SHALL be checked while reading, not after.

#### Scenario: Compression bomb
- **WHEN** a 2 MiB zip entry claims a small compressed size but expands beyond 200 times it
- **THEN** reading stops at the limit and the archive is refused with `extension.archive_limit` naming the limit

#### Scenario: Too many entries
- **WHEN** an archive has 1025 entries
- **THEN** it is refused before any entry is extracted

### Requirement: Descriptor and artifacts match their checksums
The installer SHALL check the descriptor against its `.sha256` file, and each artifact it uses against its `.sha256` file and the digest that the descriptor gives it. A checksum file SHALL hold 64 lowercase hexadecimal digits, white space and the entry's file name. Any mismatch SHALL stop the install before the store changes.

#### Scenario: Substituted artifact in the archive
- **WHEN** the artifact's bytes and its `.sha256` file were both replaced, but the descriptor still names the original digest
- **THEN** the install fails with `extension.digest_mismatch`

### Requirement: A descriptor is read with the parent descriptor rules
The installer SHALL read the descriptor as version 1 or exact `2.0.0-draft.1` or `2.0.0-draft.2`, require claims on every version 2 artifact, require a platform triple for process artifacts and none for WASM, refuse a descriptor that mixes runtimes, and refuse claims that differ between artifacts unless `platformDifferences` is `declared`.

#### Scenario: Undeclared difference
- **WHEN** two artifacts' claim sets differ and `platformDifferences` is `none`
- **THEN** the install fails and names the differing members

#### Scenario: Mixed runtimes
- **WHEN** a descriptor lists a `wasm` and a `process` artifact
- **THEN** the install fails with `extension.unsupported_package`

### Requirement: A target archive holds exactly one executable
When a process artifact is a `.zip`, `.tgz` or `.tar.gz` target archive, the installer SHALL read it with the same entry rules and limits, require exactly one regular file, and install that file as the executable (Q-ARCHIVE). A target archive with more than one file SHALL be refused with `extension.multi_file_artifact` (Q-MULTIFILE).

#### Scenario: One executable
- **WHEN** the selected target archive holds only `morphir-fsharp-backend.exe`
- **THEN** that file is staged, its digest is recorded as the installed digest, and the archive's digest is recorded in the .NET inventory

#### Scenario: Self-contained folder
- **WHEN** the selected target archive holds an executable and its runtime folder
- **THEN** the install fails with `extension.multi_file_artifact`

#### Scenario: Unix permissions
- **WHEN** the executable is installed on Linux
- **THEN** its owner execute bit is set, whatever mode its archive entry had

### Requirement: Windows process artifacts are launchable
On Windows the installer SHALL refuse a process executable whose file name does not end in `.exe`, with `extension.not_launchable` (Q-WINLAUNCH).

#### Scenario: Missing extension
- **WHEN** a Windows artifact's executable is named `morphir-fsharp-backend`
- **THEN** the install fails with `extension.not_launchable`

### Requirement: A WASM package installs without running the guest
A package archive with a WASM artifact SHALL install with the descriptor's claims and `claimCheck` `unchecked`, and SHALL NOT run the guest. Until this host has a WASM runtime, the installed entry SHALL be listed as not available in this host (Q-WASMDESCRIBE).

#### Scenario: WASM package before the WASM host
- **WHEN** a user installs a WASM package archive in a host without a WASM runtime
- **THEN** the install succeeds with `claimCheck` `unchecked`
- **AND** `extension list --json` shows the entry with `available` false

### Requirement: A bare WASM module needs the guest's describe
A bare `.wasm` file has no descriptor, so its identity, version and claims SHALL come only from `describe` run in the host's WASM runtime. A host without a WASM runtime SHALL refuse a bare module with `extension.wasm_host_unavailable` (Q-WASMDESCRIBE).

#### Scenario: Bare module today
- **WHEN** a user runs `morphir extension install ./analyzer.wasm` in a host without a WASM runtime
- **THEN** the command fails with `extension.wasm_host_unavailable` and nothing is written

### Requirement: A file argument installs like a staged file
`extension install <file>` SHALL install a package archive or a bare `.wasm` file with the rules of this capability, at the global level unless `--level` names another, and SHALL run the process probe unless `--no-probe` is given.

#### Scenario: Install a package archive
- **WHEN** a user runs `morphir extension install ./morphir-fsharp-backend-0.1.0.zip --level project`
- **THEN** the backend is installed, the project pin names it, and the global catalog is unchanged

### Requirement: Staged folders are install sources, never run directly
Each level SHALL have a `staged/` folder: `<project>/.morphir/extensions/staged/`, `<workspace>/.morphir/extensions/staged/`, `extensions/staged/` below Morphir Home for the global level, and `extensions/staged/` below the system Morphir location. Morphir SHALL never run a file from a staged folder; it installs a copy at that folder's level.

#### Scenario: Project staged file
- **WHEN** a package archive is in `<project>/.morphir/extensions/staged/` and is installed
- **THEN** the project pin names the installed version and the staged file stays where it is

#### Scenario: Other files
- **WHEN** a staged folder also holds `notes.txt`
- **THEN** install ignores it and writes one warning naming it on stderr

### Requirement: The staged install mode is explicit
The setting `[extension_install] staged` SHALL be `auto` or `manual`, default `auto`. With `auto`, compile, generate, validate and the `extension` commands that read selections SHALL first install new staged WASM packages. With `manual`, staged files install only through `extension install --staged`.

#### Scenario: Manual mode
- **WHEN** the setting is `manual` and a new staged WASM package exists
- **THEN** `morphir compile` does not install it, and `morphir extension install --staged` does

#### Scenario: Already installed
- **WHEN** a staged file's artifact is already selected at its level with the same digest
- **THEN** the automatic pass writes nothing

### Requirement: Staged process guests are never installed automatically
The automatic pass SHALL NOT install a staged package that contains a process artifact, and SHALL NOT run any staged guest. It SHALL write one note on stderr naming the file and the `extension install` command that installs it (Q-AUTOPROCESS).

#### Scenario: Cloned repository with a process guest
- **WHEN** a user clones a repository whose project staged folder holds a process package, and runs `morphir compile` with the default `auto` mode
- **THEN** no code from that package runs, compile does not install it, and stderr names the command to install it

### Requirement: The automatic pass does not change command output
The automatic pass SHALL write nothing on stdout. A staged file it cannot install SHALL produce a warning on stderr and SHALL NOT by itself change the exit code of the command that ran the pass.

#### Scenario: Broken staged archive
- **WHEN** a staged archive has a traversal entry and a user runs `morphir compile --json`
- **THEN** stdout holds only the compile report, stderr has a warning with `extension.unsafe_archive`, and the store is unchanged

### Requirement: Staged and file installs are representable upstream
For an install from a file or staged folder, the installer SHALL write the global catalog and lock with `index.kind` `local-directory`, naming a .NET-owned repository directory below `data/morphir-dotnet/extensions/repositories/` that holds the generated history and artifact, and SHALL record the true source in the .NET inventory (Q-PROVENANCE).

#### Scenario: Rust CLI reads a staged install
- **WHEN** a WASM package from the global staged folder is installed and the Rust CLI runs `morphir extension list`
- **THEN** the Rust CLI lists the extension and reads its lock without error
