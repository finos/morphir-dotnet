# Spec Delta

## MODIFIED Requirements

### Requirement: An entry states its delivery and hosts
Each entry SHALL state its delivery and at least one host it runs in. The delivery value for this capability SHALL be `built-in`. The host values SHALL be `morphir` and `dotnet-morphir`. A host value SHALL name the command that runs. It SHALL NOT identify the deployment offering, because the `native-aot`, `self-contained` and `framework-dependent` offerings all run as `morphir`.

#### Scenario: A built-in in both hosts
- **WHEN** a user runs `extension list --json` in any offering
- **THEN** the entries `morphir.csharp` and `morphir.fsharp` have the hosts `morphir` and `dotnet-morphir`

#### Scenario: An entry for the other host only
- **WHEN** the registry has an entry that only the managed offerings provide, so that `native-aot` `morphir` cannot run it, and a user runs `extension list --json` in the `native-aot` `morphir`
- **THEN** that entry is in the result with `available` set to false
- **AND** in the `self-contained` `morphir`, the `framework-dependent` `morphir` and `dotnet-morphir`, the same entry has `available` set to true

### Requirement: The result names the current host
The `extension list` result SHALL name the host that produced it and its deployment offering. The `host` value SHALL keep its meaning as the command name. The `offering` value SHALL be one of `native-aot`, `self-contained`, `framework-dependent` and `dotnet-tool`. It SHALL come from the build of the running executable, not from its file name.

#### Scenario: Each host names itself
- **WHEN** a user runs `morphir extension list --json`
- **THEN** the top-level `host` value is `morphir`
- **AND** when a user runs `dotnet-morphir extension list --json`, the top-level `host` value is `dotnet-morphir`

#### Scenario: Each offering names itself
- **WHEN** a user runs `extension list --json` in the `native-aot` and in the `self-contained` `morphir` of the same release
- **THEN** both results have the top-level `host` value `morphir`
- **AND** their top-level `offering` values are `native-aot` and `self-contained`

#### Scenario: A renamed executable
- **WHEN** a user copies the `native-aot` executable to another file name and runs `extension list --json` with it
- **THEN** the top-level `offering` value is still `native-aot`

### Requirement: Both hosts list the same shared built-ins
For each entry available in two offerings of the same build, the two offerings SHALL report the same id, name, version, types, delivery, hosts, offerings and capabilities. This holds between `morphir` and `dotnet-morphir`, and between the three offerings that run as `morphir`.

#### Scenario: The same entries in both hosts
- **WHEN** a user runs `extension list --json` in the `native-aot` `morphir` executable and in `dotnet-morphir` of the same build
- **THEN** the entries that are available in both hosts are equal, apart from the top-level `host` and `offering` values

#### Scenario: The same entries in every offering
- **WHEN** a user runs `extension list --json` in each of the four offerings of the same build
- **THEN** each entry that is available in two offerings is equal in both results

## ADDED Requirements

### Requirement: An entry states the offerings that provide it
Each entry SHALL list the offerings of its release in which it is available, in the fixed order `native-aot`, `self-contained`, `framework-dependent`, `dotnet-tool`. The `available` value of an entry SHALL be true if and only if the running offering is in that list.

#### Scenario: Generators and analyzers in every offering
- **WHEN** a user runs `extension list --json` in any offering
- **THEN** the entries `morphir.csharp`, `morphir.fsharp` and `morphir.type-validator` each list the offerings `native-aot`, `self-contained`, `framework-dependent` and `dotnet-tool`

#### Scenario: Availability follows the offering
- **WHEN** an entry lists the offerings `self-contained`, `framework-dependent` and `dotnet-tool`, and a user runs `extension list --json` in the `native-aot` `morphir`
- **THEN** the entry has `available` set to false

### Requirement: An unavailable entry states why
An entry with `available` set to false SHALL state a reason. When another delivery of the same capability can serve the running offering, the reason SHALL name that delivery and the step that makes it available, such as installing or configuring a process extension. An unavailable entry SHALL NOT change the exit code of a successful list.

#### Scenario: The F# frontend in the AOT offering
- **WHEN** a user runs `extension list --json` in the `native-aot` `morphir`
- **THEN** the built-in F# frontend entry has `available` set to false and a reason that names the F# frontend process delivery `morphir-fsharp-frontend`
- **AND** the exit code is 0

### Requirement: Built-in frontends are registered extensions
Each offering SHALL register one built-in extension with the type `frontend` for each in-process frontend that `morphir compile` can select in any offering of the release. Its frontend claim SHALL state the language ids, file extensions and IR versions that the frontend advertises. The entry id SHALL be the frontend identity that the compile report names.

#### Scenario: The in-process F# frontend is listed
- **WHEN** a user runs `dotnet-morphir extension list --json`
- **THEN** one entry has the type `frontend`, the delivery `built-in`, `available` set to true, and a frontend claim for the language `fsharp` with the file extension `.fs`
- **AND** its id equals the `frontend` value of a successful `dotnet-morphir compile --json` of F# source with no extension configured

#### Scenario: The list agrees with compile
- **WHEN** in any offering the available `frontend` entries are compared with the in-process frontends that `compile` can select with no extension configured
- **THEN** the two sets of ids are equal

### Requirement: The result states delivery prerequisites
The result SHALL state the runtime prerequisite of the running offering. Each entry SHALL state the runtime, SDK and native-library prerequisites of its delivery as separate members. A built-in entry SHALL have no prerequisite beyond those of its offering. An SDK needed only to install a delivery SHALL be reported as an installation prerequisite, not as a runtime prerequisite.

#### Scenario: Standalone and framework-dependent offerings
- **WHEN** a user runs `extension list --json` in the `native-aot`, `self-contained` and `framework-dependent` `morphir`
- **THEN** the `native-aot` and `self-contained` results state no runtime prerequisite
- **AND** the `framework-dependent` result states the .NET runtime that its runtime configuration names

#### Scenario: The dotnet tool
- **WHEN** a user runs `dotnet-morphir extension list --json`
- **THEN** the result states the .NET runtime as its runtime prerequisite and the .NET SDK as an installation prerequisite
- **AND** the built-in entries state no prerequisite of their own
