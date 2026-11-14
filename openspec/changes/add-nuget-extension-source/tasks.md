# Tasks

Tracking: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558). No dedicated issue yet; link it here when it is created. Before any task adds, moves or renames a test, a feature file, a step, a driver or a test project, classify it with the `test-manager` skill, and have `test-manager-reviewer` review the diff. Public F# members that C# calls follow the `fsharp-csharp-interop` skill and get an `fsharp-csharp-interop-reviewer` review. Follow TDD: each behavior's test fails first.

## 1. Upstream schema and cross-platform evidence

- [ ] 1.1 Get the extension family schema change merged in finos/morphir through its coordinator (design UP-1): package format `nupkg`, kinds `nuget-package` and `nuget-package-entry`, member tables, identity and package cases, and the "NuGet package convention" section. Verify by recording the PR and the merged commit in proposal.md, and update this change's member names in design.html and the specs if upstream renamed any.
- [ ] 1.2 Run `spikes/nuget-extension-source/run.sh` with `--online` and `CREDPROVIDER` on Linux x64 and Windows x64, and commit `results/linux-x64.md` and `results/win-x64.md`. Verify that every level gives the same warning counts and step results as osx-arm64; record any difference in design.html Risks and as a task or issue.

## 2. Pin the new family version

- [ ] 2.1 Pin the merged #1176 commit as `tests/Fixtures/ExtensionAcquisition/parent-<sha>/` (schemas, cases, manifest, README), remove the old pin, and move every family URL the installer reads and writes to the new version; refuse documents of the old version with no conversion. Verify the shared reader, identity and package case tests pass against the new pin.
- [ ] 2.2 Add the `nuget-package` and `nuget-package-entry` cases to `IndexProvenance` and `ArtifactSource` in `Sources.fs`, with their JSON readers and writers and the `nupkg` package format in the descriptor reader. Verify round-trip tests for each case and the upstream cases for the new kinds pass.

## 3. Payload seam (refactor, no behavior change)

- [ ] 3.1 Add `PackageTreeSink`, `IPackagePayload` and `PayloadIntegrity` (`Install/PackageTree.fs`), move the zip and tar.gz extraction into `ZipArchivePayload` and `TarGzArchivePayload` (`Formats/ArchivePayload.fs`), and make `PackageInstaller` and `InstalledExtensions.InstallPreparedAsync` take a payload instead of `openArchive`. Verify the existing package, archive, provider and state tests pass with no test edits.
- [ ] 3.2 Replace `PackagePlan.ArchiveDigest` with `PayloadIntegrity`, and read the artifact file name in `PackagePlan.FromState` for every source kind. Verify the existing tests pass and a lock written by the GitHub provider still re-verifies offline.
- [ ] 3.3 Move the project files into the `Core/`, `Install/`, `Formats/` and `Sources/` folders of design CODE-1 with the compile order it gives, and update the installation library README. Verify `./build.sh` and `dotnet format --verify-no-changes` pass.

## 4. WASM installation for every provider

- [ ] 4.1 Let `PackagePlan.Read` select a WASM artifact, add the `ModuleFile` payload, and publish it through `SingleFileStore` with a lock of runtime `wasm` and `claimCheck` `unchecked`, without running the guest. Verify unit tests for a WASM-only descriptor, a digest mismatch and a host requirement failure.
- [ ] 4.2 Keep the one-runtime rule for every provider (design SEL-1). Verify a test that a mixed descriptor from a NuGet package fails with `extension.unsupported_package`.
- [ ] 4.3 Install a WASM artifact from a local declaration and from a GitHub release (fake GitHub). Verify both tests pass and no guest process starts.
- [ ] 4.4 Capture an installed WASM record: verify the stored module against the lock offline and expose it through the `Wasm` case of `InstalledActivation` (design WASM-2). Verify tests for a good module and a changed module.
- [ ] 4.5 Run installed WASM frontends and backends in `InstalledExtensionConnector` through `IWasmModuleLoader` and the channel connectors, with the session claim check. Verify `morphir compile` and `morphir generate` tests with an installed WASM guest, a claim mismatch, and a host without a WASM runtime (`extension.runtime_unavailable`).
- [ ] 4.6 Update `add-extension-installation/tasks.md` task 5.4 to say its WASM part is done here, and document WASM installs in `docs/content/docs/cli/extension-installation.md`. Verify `mise run openspec:verify` passes.

## 5. NuGet provider from a local folder feed (layout A)

- [ ] 5.1 Add the five NuGet client packages to `Directory.Packages.props` and `src/Morphir.Extensions.Installation`, set `NuGet.UseSystemTextJsonDeserialization` as a trimmed runtime option in `src/Morphir/Morphir.csproj` and `src/Morphir.Tool/Morphir.Tool.csproj`, and ship it in `buildTransitive/Morphir.Extensions.Installation.props`. Verify the restore passes in locked mode and the package contains the props file.
- [ ] 5.2 Parse `nuget:<id>[@<version>]` into `SourceRequestKind.NuGet`, and add `--source`, `--configfile` and `--interactive`, refused with any other source. Verify parser tests for valid and malformed ids and versions and the option refusal (`extension.invalid_source`).
- [ ] 5.3 Add fixture packages built in the test run (main package with `morphir/release.json`, a process subtree and a WASM entry) and a local folder feed. Verify the fixture builder test checks the package contents.
- [ ] 5.4 Implement `NuGetPackageProvider` for a folder feed: download the main package into staging, check `.nuspec` id and version, read the descriptor, check `version`, locate the artifact (design NUG-2), and return a `NuGetPackagePayload` (subtree under `root`) or a WASM entry payload. Use `SourceCacheContext` with `NoCache` and `DirectDownload`. Verify tests for a layout A process install, a WASM-only release, `extension.release_descriptor_missing`, `extension.version_mismatch`, an escaping entry outside `root`, a `.dll` entry point, executable modes on Unix, and empty `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` folders after the install.
- [ ] 5.5 Record `nuget-package` provenance and `nuget-package-entry` source, and re-verify the installed tree offline from the lock. Verify the lock contents test and the offline verify test.
- [ ] 5.6 Wire `extension install nuget:` in `ManagementScenario.cs` and `ExtensionHandlers.cs`, including `--dry-run`. Verify the CLI tests for install and dry run, and document the command and its options in `docs/content/docs/cli/management.md`.

## 6. Layout B and .NET tools

- [ ] 6.1 Resolve an artifact package from `filename` with the prefix rule and the same version and source. Verify tests for layout B, a package of another publisher (`extension.invalid_descriptor`) and a missing artifact package (`extension.artifact_missing`).
- [ ] 6.2 Install a self-contained RID-specific .NET tool packed by `dotnet pack` with `ToolPackageRuntimeIdentifiers` and the package types `DotnetTool;MorphirExtension`, and refuse a framework-dependent one (`extension.unsupported_package`). Verify the installed tree holds only the files under `tools/<tfm>/<rid>`, that `dotnet tool install` from the same feed still succeeds, and the refusal test.
- [ ] 6.3 Write the extension author page: layouts A and B, .NET tools, the package type, and the two-pass publish (design DOC-1). Verify the page's commands run as written against the fixture feed, and open a follow-up issue for a tree digest command.

## 7. Feeds, version selection and signatures

- [ ] 7.1 Read sources, package source mapping and `--source` from `nuget.config` and `--configfile` (design SRC-2). Verify tests for mapping, a named source, a URL source and a folder source.
- [ ] 7.2 Select an exact version from the first source that has it and, without a version, the highest stable listed version across sources. Verify tests against a fake V3 feed (service index, flat container, registration) for a newer prerelease, an unlisted version and a named prerelease.
- [ ] 7.3 Verify package signatures as the dotnet CLI does on each OS (`DOTNET_NUGET_SIGNATURE_VERIFICATION`, `X509TrustStore.InitializeForDotNetSdk`) and with the `nuget.config` client policy for main and artifact packages. Find where the trust store looks for `trustedroots/` in the AOT binary, and ship `codesignctl.pem` and `timestampctl.pem` there from the pinned NuGet client release. Verify tests for `accept` and `require` mode with signed and unsigned fixture packages (`extension.nuget_signature_invalid`), the macOS default, and a nuget.org-signed package with the AOT binary on Linux.
- [ ] 7.4 Map NuGet failures to `extension.nuget_not_found` and `extension.nuget_unreachable` with the package, version and tried sources in the message. Verify tests with the fake feed down and an unknown package.

## 8. Credentials

- [ ] 8.1 Authenticate with `packageSourceCredentials` through NuGet's HTTP handler. Verify a fake feed test that requires basic credentials, and a test that the credential is not sent to another host and appears in no log or lock.
- [ ] 8.2 Use credential provider plugins through `NuGet.Credentials`, non-interactive unless `--interactive`, and map a refused credential to `extension.nuget_unauthorized`. Verify a test with a test plugin found through `NUGET_PLUGIN_PATHS` and one found as `nuget-plugin-*` on `PATH`, and the no-credential failure message.
- [ ] 8.3 Document credentials, plugins and the `NUGET_PLUGIN_PATHS` rule (a native executable must be a `nuget-plugin-*` on `PATH`) in `docs/content/docs/cli/extension-installation.md`. Verify the documented commands against the test plugin.

## 9. Native AOT and integration

- [ ] 9.1 Make the Native AOT publish of `morphir` fail on any IL2xxx or IL3xxx warning from `NuGet.*` or `Newtonsoft.Json` (design AOT-1). Verify that removing the feature switch makes the publish fail, and that the publish with it reports no such warning.
- [ ] 9.2 Run `morphir extension install nuget:` with the AOT binary against the fixture folder feed and the fake V3 feed, in the lane the `test-manager` skill gives. Verify the test passes on the CI platforms.
- [ ] 9.3 Update `CHANGELOG.md`, the installation library README and `docs/content/docs/cli/extension-installation.md` status. Verify `dotnet format`, `./build.sh --target Test` and `mise run openspec:verify` pass.

## Workflow follow-up

- Merge only after task 1.1's upstream commit is pinned (task 2.1).
- Follow AGENTS.md for review, archive, tracker closure and release approval.
- Archive the change after review, and verify the archived specs.
