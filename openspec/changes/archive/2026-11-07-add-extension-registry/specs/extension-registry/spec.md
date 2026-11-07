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

#### Scenario: An extension id is the generate backend identity
- **WHEN** `morphir generate --target csharp --json` succeeds
- **THEN** the report's `backend` value is `morphir.csharp`, the id of the C# entry in `extension list --json`

### Requirement: An extension id is a dotted portable token
An extension id SHALL be one or more segments joined by `.`. Each segment SHALL start with a lowercase ASCII letter, contain only lowercase ASCII letters, digits and `-`, and end with a letter or digit. An id SHALL have at most 238 characters. A one-segment id is valid upstream; a dotted id is a morphir-dotnet superset.

#### Scenario: Valid ids
- **WHEN** a host reads the ids `morphir-elm`, `a`, `morphir.csharp` and `acme.tools.lint-2`
- **THEN** each one is accepted as an extension id

#### Scenario: Invalid ids
- **WHEN** a host reads the ids `""`, `.`, `..`, `a..b`, `.a`, `a.`, `A`, `1a`, `a-`, `a_b` and an id of 239 characters
- **THEN** each one is refused with a diagnostic that names the id and the rule it breaks

### Requirement: An entry has an identity in the shape of the MEP extension record
Each entry SHALL have an extension id, a name of 1 to 128 characters with no leading or trailing white space and no control characters, a SemVer 2.0.0 version, and an optional description. When a description is present, it SHALL be 1 to 1024 characters with the same character rules as a name.

#### Scenario: Identity fields in the result
- **WHEN** a user runs `morphir extension list --json`
- **THEN** each entry has an `id`, a `name` and a `version` that follow these rules
- **AND** an entry with no description has no `description` member, and never an empty string

#### Scenario: A built-in has the version of its host
- **WHEN** a user runs `morphir --version` and `morphir extension list --json`
- **THEN** the `version` of each built-in entry is equal to the host version

### Requirement: Entry types come from the capability claims
Each entry SHALL have at least one capability claim. The `types` of an entry SHALL be exactly the MEP extension types of its claims, with the wire values `frontend`, `backend`, `transform`, `validator` or `workspace`.

#### Scenario: Types agree with claims
- **WHEN** a user runs `morphir extension list --json`
- **THEN** each entry has at least one member in `capabilities` and at least one value in `types`
- **AND** an entry has the type `backend` if and only if it has `capabilities.backend`

### Requirement: Extension ids are unique
A host SHALL NOT register two extensions with the same id. If the composition has a duplicate id, `extension list` SHALL fail and name each duplicate id.

#### Scenario: A duplicate id stops the list
- **WHEN** a host composition registers two extensions with the id `morphir.csharp`
- **THEN** `extension list` exits with code 2
- **AND** the diagnostic names `morphir.csharp`

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and at least one host it runs in. The delivery value for this capability SHALL be `built-in`. The host values SHALL be `morphir` and `dotnet-morphir`.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in either host
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry whose only host is `dotnet-morphir`, and a user runs `morphir extension list --json`
- **THEN** that entry is in the result with `available` set to false
- **AND** in `dotnet-morphir extension list --json` the same entry has `available` set to true

### Requirement: The result names the current host
The `extension list` result SHALL name the host that produced it.

#### Scenario: Each host names itself
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the top-level `host` value is `morphir`
- **AND** when a user runs `dotnet-morphir extension list --json`, the top-level `host` value is `dotnet-morphir`

### Requirement: Backend entries claim their targets and IR versions
A `backend` entry SHALL include a backend capability claim with at least one target id, at least one IR version as declared by the backend, and `generate` set to true. A target id SHALL follow the extension id rule.

#### Scenario: The C# backend claim
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the `morphir.csharp` entry has `capabilities.backend.targets` equal to `["csharp"]`
- **AND** `capabilities.backend.irVersions` holds each IR version that the C# backend declares, in its declared spelling, including `3`, `3.0.0` and `4.0.0`
- **AND** `capabilities.backend.generate` is true

### Requirement: Both hosts list the same shared built-ins
For each entry available in both `morphir` and `dotnet-morphir`, the two hosts SHALL report the same id, name, version, types, delivery, hosts and capabilities.

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
