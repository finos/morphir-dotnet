# Proposal

## Why

`morphir extension install` reads two sources today: an explicit local declaration and a GitHub release (`github:<owner>/<repo>[@<version-or-tag>]`, #654). Teams in .NET publish their tools to NuGet feeds: nuget.org, private V3 feeds such as Azure Artifacts, and local folder feeds. An extension author in .NET already has those feeds, and their credentials and policies in `nuget.config`, but cannot use them for a Morphir extension. Like mise, which installs tools from several backends, the installer should take NuGet as one more source.

Example: an author publishes `Acme.Morphir.Python` 1.2.3 as a RID-specific .NET tool that also carries `morphir/release.json`. A user runs `morphir extension install nuget:Acme.Morphir.Python@1.2.3 --home ~/.morphir --no-probe`, and the installer reads the user's feeds and credentials from `nuget.config`, installs the package payload for this platform, and records where it came from. `dotnet tool install Acme.Morphir.Python` still works on the same packages.

Why now: the provider seam from #654 exists, and a spike (`spikes/nuget-extension-source/`) showed that the Microsoft NuGet client packages run in the Native AOT `morphir` binary with no trim or AOT warnings.

## What Changes

- A NuGet source provider: `nuget:<package-id>[@<version>]`, with `--source`, `--configfile` and `--interactive`. It reads sources, package source mapping, credentials and signature policy from `nuget.config`, and supports nuget.org, V3 HTTP feeds and local folder feeds. Credential provider plugins work.
- A NuGet package convention for extensions: the main package carries `morphir/release.json`; artifacts are either in that package or in other packages of the same source and version, so one package (layout A), one package per RID (layout B) and RID-specific .NET tools all work.
- A process artifact can be a subtree of a NuGet package (package format `nupkg`), pinned by its tree digest. A NuGet package is itself the archive, so it carries no inner `.zip` or `.tar.gz`.
- WASM artifacts install from every provider (local, GitHub, NuGet): the module is stored, its claims are recorded as unchecked, and the guest does not run during installation. `morphir compile` and `morphir generate` run an installed WASM extension through the host's WASM runtime.
- The installer is split into three layers: providers fetch bytes, payload readers turn bytes of one shape into package files, and one installer core does every check and every write. Each provider owns the layouts it supports.
- The Microsoft NuGet client packages (`NuGet.Versioning`, `NuGet.Packaging`, `NuGet.Configuration`, `NuGet.Protocol`, `NuGet.Credentials`) become dependencies of `Morphir.Extensions.Installation`. Every AOT host sets the feature switch `NuGet.UseSystemTextJsonDeserialization`.
- The extension family schema gains the `nupkg` format and the `nuget-package` and `nuget-package-entry` kinds in finos/morphir first. This change pins that schema and does not merge before it.

## Capabilities

### New Capabilities

- `extension-source-providers`: what is common to every source provider: the provider-tagged request, one installer for every provider, provider-tagged provenance and source records, the integrity pin for each payload shape, the selection of a process artifact before a WASM artifact, and WASM artifact installation and activation.
- `nuget-extension-source`: installing from NuGet feeds: the request and version selection, sources, source mapping, credentials and signature policy from `nuget.config`, the NuGet package convention and its layouts, .NET tool packages, and NuGet errors.

### Modified Capabilities

None in `openspec/specs/`. The in-flight change `add-extension-installation` owns `extension-installation` and `extension-package-archives`, which are not yet main specs. This change takes over the WASM part of its task 5.4; its tasks.md is updated to point here.

## Tracking

- GitHub: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558) owns the deployment and acquisition effort. No dedicated issue exists for this change yet; link it here when it is created.
- Upstream: [finos/morphir#1176](https://github.com/finos/morphir/pull/1176), extension family `0.1.0-preview.3` with NuGet package sources, opened by the finos/morphir coordinator on 14 November 2026 and merged as `65bbbc961d9eb962ad941a859c9d09fba058f5a0`, pinned in `tests/Fixtures/ExtensionAcquisition/parent-65bbbc96/`.
- Spike: `spikes/nuget-extension-source/` and its results on osx-arm64.
- Beads: no id is recorded. Do not invent one.

## Impact

- Code: `src/Morphir.Extensions.Installation` (new `Sources/NuGet/` provider, payload seam, WASM install and capture, folder layout), `src/Morphir.Tooling` (`extension install` options and handler, installed WASM activation in `InstalledExtensionConnector`), `src/Morphir/Morphir.csproj` and the `dotnet-morphir` tool (feature switch).
- Dependencies: five NuGet client packages at the version `Directory.Packages.props` pins (7.9.0 today); about 11 MiB more in the AOT binary on osx-arm64. NuGet's trusted root bundle (`trustedroots/codesignctl.pem`, `timestampctl.pem`) ships with `morphir` for Linux signature verification.
- State: catalog, lock and journal documents move to extension family `0.1.0-preview.3`; preview.2 documents are refused with no conversion.
- Docs: `docs/content/docs/cli/management.md`, `docs/content/docs/cli/extension-installation.md`, the installation library README, and a page for extension authors on publishing to NuGet.
- Tests: unit and property tests of the provider and payload readers, fixture packages and a fake V3 feed, credential plugin tests, and the AOT publish of `morphir`.
