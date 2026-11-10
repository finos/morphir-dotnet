# Spec Delta

## MODIFIED Requirements

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and at least one host it runs in. The delivery of a built-in entry SHALL be `built-in`, and of an installed entry its runtime, `process` or `wasm`. The host values SHALL be `morphir` and `dotnet-morphir`. A host value SHALL name the command that runs. It SHALL NOT identify the deployment offering, because the `native-aot`, `self-contained` and `framework-dependent` offerings all run as `morphir`. An installed entry SHALL list both hosts.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in any offering
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry that only the managed offerings provide, so that `native-aot` `morphir` cannot run it, and a user runs `extension list --json` in the `native-aot` `morphir`
- **THEN** that entry is in the result with `available` set to false
- **AND** in the `self-contained` `morphir`, the `framework-dependent` `morphir` and `dotnet-morphir`, the same entry has `available` set to true

#### Scenario: An installed process extension
- **WHEN** a process extension that speaks an MEP version of this release is installed and a user runs `extension list --json` in any offering
- **THEN** its entry has the delivery `process`, the hosts `morphir` and `dotnet-morphir`, and `available` set to true

#### Scenario: An installed WASM extension before the WASM host
- **WHEN** a WASM extension is installed and the running release has no WASM runtime
- **THEN** its entry has the delivery `wasm` and `available` set to false

### Requirement: Extension ids are unique
A host SHALL NOT register two extensions with the same id. If the composition has a duplicate id, `extension list` SHALL fail with exit code 2 and name each duplicate id. After selection precedence each installed id SHALL appear once, and lower layers that select it SHALL be reported as shadowed. Two selections of one id inside one layer, or an installed id equal to a built-in id, SHALL be refused with `extension.duplicate_id` and exit code 1.

#### Scenario: A duplicate id stops the list
- **WHEN** a host composition registers two extensions with the id `morphir.csharp`
- **THEN** `extension list` exits with code 2
- **AND** the diagnostic names `morphir.csharp`

#### Scenario: Duplicate in a pin file
- **WHEN** a project pin file lists `acme-backend` twice
- **THEN** `extension list` fails with `extension.duplicate_id` naming the pin file and `acme-backend`, exit code 1

#### Scenario: Shadowed global version
- **WHEN** the project pin and the shared catalog both select `acme-backend`
- **THEN** `extension list --json` has one entry for it, at level `project`, and `extension which` names the global selection as shadowed

## ADDED Requirements

### Requirement: Installed extensions are listed with their selection
`extension list` SHALL include one entry for each installed extension that the selection layers select, beside the built-ins. An installed entry SHALL take its id, name, version, description, types and claims from its installed claim set, and SHALL have an `installation` member with `level`, `layer`, `digest`, `storePath`, `claimCheck`, `selection` and `source`.

#### Scenario: Installed backend in the list
- **WHEN** `acme-backend` 0.1.0 is installed globally from repository `local-dev` and a user runs `morphir extension list --json`
- **THEN** the result has an entry with id `acme-backend`, version `0.1.0`, type `backend`, delivery `process`, and `installation.level` `global`

#### Scenario: Built-ins still listed
- **WHEN** extensions are installed
- **THEN** `extension list --json` still has the entries `morphir.csharp`, `morphir.fsharp` and `morphir.type-validator`, unchanged

### Requirement: An installed entry states its deployments and why it is unavailable
An installed entry's `deployments` SHALL list the offerings of the running release that can run it: all four for a process entry with an MEP version this release speaks, and none otherwise. An unavailable installed entry SHALL have a `reason` that names what it needs, the WASM runtime or the MEP versions it speaks.

#### Scenario: Process entry in every offering
- **WHEN** an installed process extension speaks `0.2.0-draft.1` and a user runs `extension list --json` in the `native-aot` `morphir`
- **THEN** its `deployments` are `native-aot`, `self-contained`, `framework-dependent` and `dotnet-tool`, and `available` is true

#### Scenario: A legacy entry from the Rust CLI
- **WHEN** the shared catalog holds an entry that speaks only MEP `0.1`
- **THEN** its entry has `deployments` `[]`, `available` false and a `reason` saying that it speaks only MEP `0.1`, which is not supported, and the list exits with code 0

#### Scenario: WASM entry
- **WHEN** a WASM extension is installed and the running release has no WASM runtime
- **THEN** its entry has `deployments` `[]` and a `reason` that names the missing WASM runtime

### Requirement: The platform option filters installed entries
Without `--platform`, installed entries SHALL be those selected for the running platform. With `--platform <rid>`, where `<rid>` is a platform identifier of the toolchain contracts' table, they SHALL be the selections that record an artifact for that platform. Another value SHALL be a usage error with exit code 2.

#### Scenario: Pins for another platform
- **WHEN** a project pin has digests for `win-x64` and `linux-x64` and a user on Windows runs `morphir extension list --platform linux-x64 --json`
- **THEN** the pinned entry is in the result with its `linux-x64` digest

#### Scenario: Unknown platform
- **WHEN** a user runs `morphir extension list --platform amiga`
- **THEN** the command fails with exit code 2

### Requirement: Local listing shows project and workspace selections
`extension list --local` SHALL limit the installed entries to those that a project or workspace pin selects. Built-in entries SHALL still appear.

#### Scenario: Local listing
- **WHEN** one extension is selected by the project pin and another only by the shared catalog, and a user runs `morphir extension list --local --json`
- **THEN** the installed entries in the result are only the project one, and the built-ins are present
</content>
