# Spec Delta

## Purpose

Defines what this repository offers finos/morphir so that it can build, qualify and publish the official Morphir CLI
from a pinned commit, and keeps this repository from publishing that CLI itself.

## ADDED Requirements

### Requirement: This repository does not publish the official CLI by default
The deployment workflow SHALL publish the library packages and SHALL NOT publish the official CLI: the `morphir-<rid>`
executables, the `morphirup-<rid>` assets and the `Morphir.Tool` package. This holds unless the repository variable
`MORPHIR_DOTNET_CLI_PUBLICATION` is `enabled`, which restores the earlier CLI publication as a rollback.

#### Scenario: A release tag without the variable
- **WHEN** a version tag is pushed and `MORPHIR_DOTNET_CLI_PUBLICATION` is not `enabled`
- **THEN** the library packages are published to NuGet, and no CLI executable, morphirup asset or `Morphir.Tool` package is published

#### Scenario: The rollback switch
- **WHEN** `MORPHIR_DOTNET_CLI_PUBLICATION` is `enabled`
- **THEN** the workflow builds, checks and publishes the CLI assets and the `Morphir.Tool` package as before

### Requirement: A reusable build of one offering for one platform
The repository SHALL provide a reusable workflow that takes a repository, a commit, an offering and a runtime
identifier, and builds that offering for that platform with the maintained release profiles. It SHALL upload the
archive, its build launch metadata, its build contents metadata and a SHA-256 file. It SHALL expose the release version
alongside the artifact name, file name and SHA-256. The offering is one of `native-aot`, `self-contained`,
`framework-dependent` or `dotnet-tool`.

#### Scenario: The parent builds the Native AOT morphir for Linux
- **WHEN** finos/morphir calls the workflow with this repository's commit, `native-aot` and `linux-x64`
- **THEN** it receives an artifact with the `morphir-dotnet-<version>-native-aot-linux-x64.tar.gz` archive, its `.build-launch.json`, `.build-contents.json` and archive SHA-256, built from exactly that commit

### Requirement: The build qualifies the produced executable
For every offering except `dotnet-tool`, the reusable workflow SHALL extract the archive and start the produced
executable, not `dotnet exec` of an assembly. It SHALL check that the executable prints the release version. For
`dotnet-tool` it SHALL install the package into a folder and start the installed `dotnet-morphir`.

#### Scenario: A produced apphost is started
- **WHEN** the workflow has built the self-contained offering for a platform
- **THEN** it runs the `morphir` executable from the extracted archive with `--version`, and fails the job if that does not print the release version

### Requirement: Tool-package checks use only the built package
The build's shared tool-package helper SHALL isolate its feed, NuGet configuration and caches and verify the installed
package and extracted tool files against the built bytes. It SHALL support a default tool-path install and a `-Local`
manifest install. Its optional `-ResultFile` SHALL hold internal run evidence only after a successful check; a failed
run SHALL leave no result file. This is a build check, not a shared acquisition or package-state contract.

#### Scenario: Another feed or cache holds the same tool version
- **WHEN** another feed or cache holds a different package with the same id and version
- **THEN** both helper modes install and verify only the built package, and refuse different installed bytes

#### Scenario: A caller requests local-install evidence
- **WHEN** the parent calls the helper with `-Local` and `-ResultFile`
- **THEN** the result records the verified files and the `dotnet morphir` invocation from the isolated manifest folder only after the checks pass, so the caller can run that invocation

### Requirement: The offerings and the tool identity are kept
The build interface SHALL keep the four offerings, the `Morphir.Tool` package id and its `dotnet-morphir` command,
which `dotnet morphir` runs.

#### Scenario: The tool package
- **WHEN** the parent builds the `dotnet-tool` offering
- **THEN** the package is `Morphir.Tool.<version>.nupkg`, and installing it gives the command `dotnet-morphir`
