# Spec Delta

## Purpose

Gives each Morphir host one registry of the extensions it knows, with their identity, types, delivery, hosts and capability claims, and reports it through `morphir extension list`.

## ADDED Requirements

### Requirement: Built-in generators are registered extensions
Each host SHALL register one built-in extension for each generation target that `morphir generate` can resolve, and no built-in backend extension for a target that `generate` cannot resolve.

#### Scenario: The C# and F# generators are listed
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the result has an entry with id `morphir.csharp` and an entry with id `morphir.fsharp`
- **AND** each of the two entries has the type `backend` and the delivery `built-in`

#### Scenario: The list agrees with generation
- **WHEN** the set of backend targets in `extension list --json` is compared with the targets that `morphir generate --target` accepts
- **THEN** the two sets are equal

### Requirement: An entry has an identity in the shape of the MEP extension record
Each entry SHALL have an `id`, a `name`, a `version`, an optional `description` and a non-empty list of `types`. Each type SHALL be one of the MEP wire values `frontend`, `backend`, `transform`, `validator` or `workspace`.

#### Scenario: Required identity fields are present
- **WHEN** a user runs `morphir extension list --json`
- **THEN** each entry has a non-empty `id`, `name` and `version`, and at least one value in `types`
- **AND** each value in `types` is one of `frontend`, `backend`, `transform`, `validator` or `workspace`

#### Scenario: A built-in has the version of its host
- **WHEN** a user runs `morphir --version` and `morphir extension list --json`
- **THEN** the `version` of each built-in entry is equal to the host version

### Requirement: Extension ids are unique
A host SHALL NOT register two extensions with the same id. If the composition has a duplicate id, `extension list` SHALL fail and name the duplicate id.

#### Scenario: A duplicate id stops the list
- **WHEN** a host composition registers two extensions with the id `morphir.csharp`
- **THEN** `extension list` exits with code 2
- **AND** the diagnostic on stderr names `morphir.csharp`

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and the hosts it runs in. The delivery values for this capability SHALL be `built-in`. The host values SHALL be `morphir` and `dotnet-morphir`.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in either host
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry whose hosts are only `dotnet-morphir`, and a user runs `morphir extension list --json`
- **THEN** that entry is in the result with `available` set to false
- **AND** in `dotnet-morphir extension list --json` the same entry has `available` set to true

### Requirement: The result names the current host
The `extension list` result SHALL name the host that produced it.

#### Scenario: Each host names itself
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the top-level `host` value is `morphir`
- **AND** when a user runs `dotnet-morphir extension list --json`, the top-level `host` value is `dotnet-morphir`

### Requirement: Backend entries claim their targets and IR versions
A `backend` entry SHALL include a backend capability claim with its target ids, the IR versions it accepts as declared by the backend, and `generate` set to true.

#### Scenario: The C# backend claim
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the `morphir.csharp` entry has `capabilities.backend.targets` equal to `["csharp"]`
- **AND** `capabilities.backend.irVersions` holds each IR version that the C# backend declares, including `4.0.0`
- **AND** `capabilities.backend.generate` is true

### Requirement: Both hosts list the same shared built-ins
For each entry whose hosts include both `morphir` and `dotnet-morphir`, the two hosts SHALL report the same id, name, version, types, delivery, hosts and capabilities.

#### Scenario: The same entries in both hosts
- **WHEN** a user runs `extension list --json` in the AOT `morphir` executable and in `dotnet-morphir` of the same build
- **THEN** the entries that are available in both hosts are equal, apart from the top-level `host` value

### Requirement: The list writes command output only on stdout
`extension list` SHALL write its result on stdout and SHALL write logs and diagnostics only on stderr. The entries SHALL be sorted by id with ordinal comparison. A successful list SHALL exit with code 0.

#### Scenario: Text output
- **WHEN** a user runs `morphir extension list`
- **THEN** stdout has one row for each entry, sorted by id, with the id, name, version, types, delivery and hosts
- **AND** the exit code is 0

#### Scenario: JSON output is one parseable document
- **WHEN** a user runs `morphir extension list --json`
- **THEN** stdout holds exactly one JSON document with the members `host` and `extensions`
- **AND** the `extensions` array is sorted by `id`

### Requirement: Installed-extension options do not hide built-ins
The `--platform` and `--local` options of `extension list` SHALL apply only to installed extensions. Built-in entries SHALL appear in the result for every value of these options.

#### Scenario: Local listing still shows built-ins
- **WHEN** a user runs `morphir extension list --local --json`
- **THEN** the result has the entries `morphir.csharp` and `morphir.fsharp`
