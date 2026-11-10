# Spec Delta

## MODIFIED Requirements

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and at least one host it runs in. The delivery value SHALL be `built-in` or `wasm`. The host values SHALL be `morphir` and `dotnet-morphir`.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in either host
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry whose only host is `dotnet-morphir`, and a user runs `morphir extension list --json`
- **THEN** that entry is in the result with `available` set to false
- **AND** in `dotnet-morphir extension list --json` the same entry has `available` set to true

#### Scenario: A WASM delivery
- **WHEN** the registry has an entry that a WASM module delivers
- **THEN** that entry has the delivery `wasm`
- **AND** every built-in entry still has the delivery `built-in`

## ADDED Requirements

### Requirement: A WASM entry is available only where the WASM runtime is
An entry with the delivery `wasm` SHALL have `available` set to false in a host whose WASM runtime is unavailable, and that host SHALL still list the entry. The availability SHALL agree with the runtime report of the same executable.

#### Scenario: Host without the engine
- **WHEN** the registry has a `wasm` entry for both hosts, and a user runs `extension list --json` in a host whose runtime report lists `wasm` as unavailable
- **THEN** that entry is in the result with `available` set to false
- **AND** the built-in entries keep `available` set to true

#### Scenario: Host with the engine
- **WHEN** the same registry is listed in a host whose runtime report lists `wasm` as available
- **THEN** the `wasm` entry has `available` set to true
