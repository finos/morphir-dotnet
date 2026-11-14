# Spec Delta

## Purpose

Lets users install Morphir extensions from NuGet feeds (nuget.org, V3 HTTP feeds and local folder feeds) with the sources, credentials and signature policy they already keep in `nuget.config`, and lets extension authors publish one package, one package per platform, or a RID-specific .NET tool.

Running example. The feed has three packages at version 1.2.3: `Acme.Morphir.Python` (the main package, with `morphir/release.json`) and `Acme.Morphir.Python.osx-arm64` and `Acme.Morphir.Python.linux-x64` (RID packages of a self-contained .NET tool). On macOS arm64, the descriptor's process artifact for `aarch64-apple-darwin` names `acme.morphir.python.osx-arm64.1.2.3.nupkg` with `root` `tools/net11.0/osx-arm64`, so the installer reads that package and installs the files under that folder.

```
Acme.Morphir.Python 1.2.3                  morphir/release.json
Acme.Morphir.Python.osx-arm64 1.2.3        tools/net11.0/osx-arm64/acme-python, ...
Acme.Morphir.Python.linux-x64 1.2.3        tools/net11.0/linux-x64/acme-python, ...
```

## ADDED Requirements

### Requirement: A NuGet source names a package and an optional version
The NuGet provider SHALL read `nuget:<package-id>[@<version>]`. The id SHALL follow NuGet package id rules and the version SHALL be a NuGet version, compared in normalized form. A malformed id or version SHALL fail with `extension.invalid_source`.

#### Scenario: Exact version
- **WHEN** a user requests `nuget:Acme.Morphir.Python@1.2.3`
- **THEN** the provider looks for version `1.2.3` of `Acme.Morphir.Python`

#### Scenario: Malformed version
- **WHEN** a user requests `nuget:Acme.Morphir.Python@1.x`
- **THEN** the command fails with `extension.invalid_source`

### Requirement: Without a version the latest stable listed version is selected
Without `@<version>`, the provider SHALL select the highest stable version that any applicable source lists, as `dotnet tool install` without `--version` does. It SHALL NOT select a prerelease or an unlisted version. A named version SHALL be selected even when it is a prerelease or unlisted.

#### Scenario: Prerelease is newer
- **WHEN** the feed lists `1.2.3` and `1.3.0-beta.1` and the user requests `nuget:Acme.Morphir.Python`
- **THEN** the provider selects `1.2.3`

#### Scenario: Named prerelease
- **WHEN** the user requests `nuget:Acme.Morphir.Python@1.3.0-beta.1`
- **THEN** the provider selects `1.3.0-beta.1`

### Requirement: Sources come from nuget.config
Without `--source`, the provider SHALL use the enabled package sources of the `nuget.config` hierarchy from the current directory, or of the file `--configfile` names, and SHALL apply its package source mapping when one is configured. `--source` SHALL name one source: a V3 `index.json` URL, a folder path, or a configured source name.

#### Scenario: Package source mapping
- **WHEN** `nuget.config` maps `Acme.*` to the source `acme` only
- **THEN** the provider looks for `Acme.Morphir.Python` in `acme` and in no other source

#### Scenario: Local folder feed
- **WHEN** a user adds `--source ./artifacts/nupkg`
- **THEN** the provider reads the packages in that folder and in no configured source

#### Scenario: HTTP source
- **WHEN** the only applicable source is `http://feed.example/v3/index.json`
- **THEN** the command fails with `extension.invalid_source`, because an installed artifact records only an `https` feed or a folder

### Requirement: Credentials come from nuget.config and credential providers
The provider SHALL authenticate with the `packageSourceCredentials` of a source and with NuGet credential provider plugins, found as NuGet finds them. It SHALL run non-interactively unless `--interactive` is given, SHALL send a credential only to its own source, and SHALL NOT log or record it. A refused credential SHALL fail with `extension.nuget_unauthorized` naming the source.

#### Scenario: Azure Artifacts feed with a credential provider
- **WHEN** the source is an Azure Artifacts feed and the Azure Artifacts Credential Provider is installed
- **THEN** the provider gets the credential from the plugin and installs the package

#### Scenario: No credential
- **WHEN** a private feed answers 401 and no credential is available
- **THEN** the command fails with `extension.nuget_unauthorized`
- **AND** the message names the source and `--interactive`

### Requirement: The main package carries the release descriptor
The selected package SHALL contain `morphir/release.json`, its `.nuspec` id and version SHALL equal the requested id and the selected version, and the descriptor `version` SHALL equal the package version. A package without the descriptor SHALL fail with `extension.release_descriptor_missing`; a version that differs SHALL fail with `extension.version_mismatch`.

#### Scenario: Ordinary NuGet package
- **WHEN** a user requests `nuget:Newtonsoft.Json@13.0.3`
- **THEN** the command fails with `extension.release_descriptor_missing`
- **AND** nothing is written to the Home

### Requirement: A process artifact is a subtree of a package of the same release
An artifact with package format `nupkg` SHALL name in `filename` the package `<id>.<version>.nupkg` whose id is the main package id or starts with `<main id>.`, and whose version is the main package version; the provider SHALL fetch it from the source of the main package. Only the files under `package.root` SHALL become package files.

#### Scenario: Layout B
- **WHEN** the selected artifact names `acme.morphir.python.osx-arm64.1.2.3.nupkg` with `root` `tools/net11.0/osx-arm64`
- **THEN** the provider fetches `Acme.Morphir.Python.osx-arm64` 1.2.3 from the same source
- **AND** the installed tree has the files under `tools/net11.0/osx-arm64` and no `.nuspec`, `_rels/`, `[Content_Types].xml`, `.signature.p7s` or `morphir/` file

#### Scenario: Package of another publisher
- **WHEN** an artifact names `contoso.tools.1.2.3.nupkg` in the descriptor of `Acme.Morphir.Python`
- **THEN** the install fails with `extension.invalid_descriptor`

#### Scenario: Missing artifact package
- **WHEN** the named artifact package is not in the source at that version
- **THEN** the install fails with `extension.artifact_missing`

### Requirement: Package entries follow the archive safety rules
Every entry of a package the provider reads SHALL pass the archive safety rules and limits, also when it is outside `package.root`. File modes SHALL come from the descriptor's `entryPoint` and `executables`, never from entry metadata.

#### Scenario: Escaping entry outside the root
- **WHEN** an artifact package has an entry `../evil` outside `package.root`
- **THEN** the install fails with the archive safety failure and nothing is published

#### Scenario: Executable entry point
- **WHEN** the `entryPoint` is `acme-python` and the package stores no Unix mode
- **THEN** the installed `acme-python` is executable on Unix

### Requirement: A WASM artifact is an entry of the main package
A WASM artifact SHALL be the entry `morphir/<filename>` of the main package, pinned by its descriptor `sha256`. A missing entry SHALL fail with `extension.artifact_missing`.

#### Scenario: WASM artifact from NuGet
- **WHEN** the descriptor of `Acme.Morphir.Analyzer` 2.0.0 has only the WASM artifact `acme-analyzer.wasm`
- **THEN** the installer stores the entry `morphir/acme-analyzer.wasm` of that package

### Requirement: A self-contained RID-specific .NET tool installs as an extension
A RID-specific .NET tool package SHALL install when the descriptor's `root` is its `tools/<tfm>/<rid>` folder and the payload is self-contained or Native AOT. An `entryPoint` ending in `.dll`, or a payload whose `<entry point>.runtimeconfig.json` names a shared framework, SHALL fail with `extension.unsupported_package`, because such a tool needs an installed .NET runtime to start.

#### Scenario: The same packages as a tool and an extension
- **WHEN** `Acme.Morphir.Python` has the package types `DotnetTool` and `MorphirExtension` and self-contained RID packages
- **THEN** `morphir extension install nuget:Acme.Morphir.Python@1.2.3` and `dotnet tool install Acme.Morphir.Python --version 1.2.3` both succeed

#### Scenario: Framework-dependent tool
- **WHEN** the payload's `acme-python.runtimeconfig.json` names the framework `Microsoft.NETCore.App`
- **THEN** the install fails with `extension.unsupported_package`

### Requirement: Package signatures follow the dotnet CLI defaults and the nuget.config policy
The provider SHALL verify package signatures when the dotnet CLI would on the same OS: by default on Windows and Linux, not on macOS, and as `DOTNET_NUGET_SIGNATURE_VERIFICATION` sets. It SHALL apply the `nuget.config` client policy (`accept`, or `require` with `trustedSigners`) to every package it reads, and SHALL fail a refused package with `extension.nuget_signature_invalid`.

#### Scenario: Require mode with an unsigned package
- **WHEN** `nuget.config` sets `signatureValidationMode` to `require` and the artifact package is unsigned
- **THEN** the install fails with `extension.nuget_signature_invalid`

#### Scenario: nuget.org package on Linux
- **WHEN** the Native AOT `morphir` on Linux installs a package from nuget.org with the default policy
- **THEN** the signature check passes with the trusted root bundle that ships with `morphir`

#### Scenario: macOS default
- **WHEN** `DOTNET_NUGET_SIGNATURE_VERIFICATION` is not set on macOS
- **THEN** the provider does not verify signatures, as the dotnet CLI does not

### Requirement: NuGet failures have stable codes
The provider SHALL fail with `extension.nuget_not_found` when no applicable source has the package or the version, and with `extension.nuget_unreachable` when a source cannot be read. The message SHALL name the package, the version when one is known, and the sources it tried.

#### Scenario: Unknown package
- **WHEN** no source has `Acme.Morphir.Missing`
- **THEN** the command fails with `extension.nuget_not_found` and names the sources it tried

#### Scenario: Feed down
- **WHEN** the only source does not answer
- **THEN** the command fails with `extension.nuget_unreachable`

### Requirement: The provider writes only to the Home
The provider SHALL NOT write to NuGet's HTTP cache, global packages folder or any other folder outside the Home; it SHALL download into private staging under the Home. Files that a credential provider plugin writes for itself are the plugin's.

#### Scenario: No NuGet cache writes
- **WHEN** a user installs `nuget:Acme.Morphir.Python@1.2.3` with `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` set to empty folders
- **THEN** both folders are still empty after the install

### Requirement: NuGet options apply only to NuGet sources
`--source`, `--configfile` and `--interactive` SHALL apply only to a `nuget:` source. With any other source they SHALL fail with `extension.invalid_source`.

#### Scenario: Source option with GitHub
- **WHEN** a user runs `morphir extension install github:finos/morphir-python --source ./feed --home <home> --no-probe`
- **THEN** the command fails with `extension.invalid_source`

### Requirement: The Native AOT host installs from NuGet
The Native AOT `morphir` SHALL install from local folder feeds and V3 HTTP feeds, with credential provider plugins, and its publish SHALL report no trim or AOT warning from the NuGet client packages.

#### Scenario: AOT publish
- **WHEN** `morphir` is published as Native AOT
- **THEN** the publish reports no IL2xxx or IL3xxx warning from an assembly whose name starts with `NuGet.` or from `Newtonsoft.Json`
