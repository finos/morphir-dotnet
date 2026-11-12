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

### Requirement: Static delivery support and current availability are distinct
Each entry SHALL have a `deployments` member that lists the offerings that provide its delivery, in the fixed order `native-aot`, `self-contained`, `framework-dependent`, `dotnet-tool`. This static declaration SHALL NOT change when the running installation's WASM runtime is unavailable. An entry SHALL be available only when its host and deployment declarations permit the running host and its delivery's runtime requirements are satisfied.

#### Scenario: An engine failure does not rewrite delivery support
- **WHEN** an otherwise eligible WASM entry declares all four deployments and the running installation's engine cannot load
- **THEN** its `deployments` still lists all four declared offerings
- **AND** its `available` is false with the common reason for that runtime failure

### Requirement: A WASM entry is available only where the WASM runtime is
An entry with the delivery `wasm` SHALL have `available` set to false in a host whose WASM runtime is unavailable, and that host SHALL still list the entry. For an otherwise eligible entry, availability SHALL agree with the runtime report of the same executable.

#### Scenario: Host without the engine
- **WHEN** the registry has a `wasm` entry for both hosts, and a user runs `extension list --json` in a host whose runtime report lists `wasm` as unavailable
- **THEN** that entry is in the result with `available` set to false
- **AND** its `reason` is the same common unavailable reason returned by the registry query for that host and runtime
- **AND** the built-in entries keep `available` set to true

#### Scenario: Host with the engine
- **WHEN** the same registry is listed in a host whose runtime report lists `wasm` as available
- **THEN** the `wasm` entry has `available` set to true
- **AND** the combined outcome and common reason query have no unavailable reason

### Requirement: Extension availability has one common reason
One combined availability outcome SHALL supply both the availability flag and a common validated, human-readable unavailable reason. Available entries SHALL have no reason. Host and entry-deployment refusals SHALL take precedence over runtime availability. Built-in and statically ineligible entries SHALL NOT probe the engine; eligible WASM entries SHALL share the process's lazy probe.

#### Scenario: Each runtime refusal supplies the common reason
- **WHEN** the runtime refuses an otherwise eligible WASM entry with `platform_unsupported`, `engine_missing` or `engine_load_failed`
- **THEN** the registry maps that typed runtime refusal once to a validated human-readable unavailable reason
- **AND** the listing uses that same reason while the runtime report keeps its existing wire code

#### Scenario: Static refusal needs no engine probe
- **WHEN** an entry's host or declared deployments exclude the running host
- **THEN** the combined outcome and common reason query return the static refusal without probing the engine
- **AND** built-in entries never probe the engine

#### Scenario: Eligible WASM entries share the lazy probe
- **WHEN** several eligible WASM entries are listed using the process runtime
- **THEN** they read the same lazily evaluated runtime availability and the engine is probed at most once
