# Spec Delta

## MODIFIED Requirements

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and at least one host it runs in. The delivery value of a built-in entry SHALL be `built-in`. The delivery value of an installed entry SHALL be its runtime, `process` or `wasm`. The host values SHALL be `morphir` and `dotnet-morphir`. An installed entry SHALL list both hosts, and its `available` value SHALL say whether the running host can run its runtime.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in either host
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry whose only host is `dotnet-morphir`, and a user runs `morphir extension list --json`
- **THEN** that entry is in the result with `available` set to false
- **AND** in `dotnet-morphir extension list --json` the same entry has `available` set to true

#### Scenario: An installed process extension
- **WHEN** a process extension is installed and a user runs `extension list --json` in either host
- **THEN** its entry has the delivery `process`, the hosts `morphir` and `dotnet-morphir`, and `available` set to true

#### Scenario: An installed WASM extension before the WASM host
- **WHEN** a WASM extension is installed and the running host has no WASM runtime
- **THEN** its entry has the delivery `wasm` and `available` set to false

## ADDED Requirements

### Requirement: Installed extensions are listed with their selection
`extension list` SHALL include one entry for each installed extension that the selection layers select, beside the built-ins. An installed entry SHALL take its id, name, version, description, types and claims from its installed claim set, and SHALL have an `installation` member with `level`, `digest`, `storePath`, `claimCheck`, `selection` and `source`.

#### Scenario: Installed backend in the list
- **WHEN** `morphir-fsharp-backend` 0.1.0 is installed globally from repository `local-dev` and a user runs `morphir extension list --json`
- **THEN** the result has an entry with id `morphir-fsharp-backend`, version `0.1.0`, type `backend`, delivery `process`, and `installation.level` `global`

#### Scenario: Built-ins still listed
- **WHEN** extensions are installed
- **THEN** `extension list --json` still has the entries `morphir.csharp`, `morphir.fsharp` and `morphir.type-validator`, unchanged

### Requirement: Local listing shows project and workspace selections
`extension list --local` SHALL limit the installed entries to those that a project or workspace pin selects. Built-in entries SHALL still appear.

#### Scenario: Local listing
- **WHEN** one extension is selected by the project pin and another only by the global catalog, and a user runs `morphir extension list --local --json`
- **THEN** the installed entries in the result are only the project one, and the built-ins are present
