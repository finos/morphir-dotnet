# Proposal

## Why

The [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md) delivers extensions in small steps. Step 0 makes the built-in capabilities visible as extensions before any external runtime exists. Today `morphir extension list` fails with "not implemented yet" and links issue [#492](https://github.com/finos/morphir-dotnet/issues/492) (since #489), although both hosts already contain two generators. The later steps (selection rule, analyzers, installation) need one registry that knows each extension, its capabilities, its delivery and the hosts it runs in.

## What Changes

- Add an extension registry. Its domain types live in a new F# project, `src/Morphir.Extensions`, with C# callers in mind (nullable metadata, C#-friendly members). `Morphir.Tooling` (C#) keeps the CLI edge: DI registration, the command and the JSON report. Each entry has an identity in the shape of the MEP `ExtensionInfo` record (`id`, `name`, `version`, optional `description`), a delivery, the hosts it runs in, and a non-empty set of capability claims in the shape of the MEP capability records. The entry types come from the claims.
- Parse, don't validate. Every registry value is a parsed type: an id, name, description, version and target cannot be constructed from invalid text, and closed unions replace enums and flags. An entry with no capability, no host or types that disagree with its claims cannot exist, and a catalog cannot hold a duplicate id. Strings exist only at the input boundary and in the command output.
- An extension id is one or more lowercase portable tokens joined by dots. Each token follows the upstream `ExtensionId` rule. Dots are a morphir-dotnet superset, so the published generate `backend` identities `morphir.csharp` and `morphir.fsharp` stay valid. The closeout of this change proposes dotted ids upstream.
- The generation contracts that the registry reads become parsed types too: `TargetId`, the IR versions of `BackendCapability` (`SourceFormatVersion`) and the backend identity (`ExtensionId`). `morphir generate` keeps its behavior, its diagnostics and its report.
- Built-ins register through static dependency injection only. There is no assembly scanning, no reflection-based discovery and no assembly loading.
- The registry makes one entry for each registered `IGenerationBackend`, from its typed `BackendDescriptor`. So the list always agrees with the targets that `morphir generate` can resolve. The first two entries are `morphir.csharp` and `morphir.fsharp`, with type `backend`.
- Each host states which host it is. `morphir` (Native AOT) and `dotnet-morphir` (JIT dotnet tool) share `Morphir.Program`, and each entry point passes its host identity to the shared composition.
- `morphir extension list` shows the registered extensions with their id, name, version, types, delivery and hosts, in text form on stdout. With `--json`, it writes one JSON document on stdout from a source-generated serializer context.
- The `--platform` and `--local` options of `extension list` stay valid. They apply only to installed extensions, and there are none until PRD step 3. Built-ins always appear.
- No other command changes its behavior. `extension install`, `use`, `remove` and `which` stay as they are. `generate` keeps its backend resolution, diagnostics and report; only its contract types change.

Out of scope for this change: the MEP `Morphir.Mep` types (planned in `add-fsharp-compile`, PR #495), the selection rule between built-in and configured extensions, external runtimes, installation, and the pipeline plugins in `src/Morphir.IR.Pipeline.Plugins` (see `design.html`, decision PLG-1).

## Capabilities

### New Capabilities

- `extension-registry`: The set of extensions that a host knows, with identity, types, delivery, hosts and capability claims, and how `morphir extension list` reports it.

### Modified Capabilities

None. No existing spec covers `morphir extension`.

## Tracking

- Parent: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 0.
- Issue: [#492](https://github.com/finos/morphir-dotnet/issues/492) tracks the `morphir extension` commands. This change implements `extension list` for built-ins; `install`, `use`, `remove` and `which` stay open under #492 until PRD step 3.
- Related: PR [#495](https://github.com/finos/morphir-dotnet/pull/495) (`add-fsharp-compile`) plans `Morphir.Program.CreateApp(args, extend)` and `Morphir.Mep`. This change must fit both (design decisions HOST-1 and REG-1).
- Upstream: dotted extension ids are proposed in [finos/morphir#1137](https://github.com/finos/morphir/issues/1137), with the implementation in [finos/morphir-rust#329](https://github.com/finos/morphir-rust/issues/329), as design decision ID-1 records.

## Impact

- New: `src/Morphir.Extensions` (F#: the parsed types, `NonEmptySet`, the registry unions and catalog, `IExtensionSource`, `TargetId`) and `tests/Morphir.Extensions.Tests` (Expecto and FsCheck), both in `Morphir.slnx`, with the test project in the Nuke test targets.
- New in `Morphir.Tooling`: `Features/Extensions/` (C#: the backend adapter, the registry service, the list command and report, the JSON context).
- Changed: `src/Morphir.Tooling/Scenarios/ManagementScenario.cs` (`extension list` is no longer a not-implemented command), `tests/Morphir.Tooling.Tests/Integration/CLI/CliManagementCommands.feature` (its `extension list` row), `src/Morphir.Tooling/Features/Generate/GenerationContracts.cs` (the F# `TargetId` replaces the C# struct, `SourceFormatVersion` sets in `BackendCapability`, and `ExtensionId`, `ExtensionName` and an optional `ExtensionDescription` on `BackendDescriptor`), `CliGenerationSettingsResolver.cs` and `RegisteredBackendResolver.cs` (typed inputs, same behavior), the two backends, `src/Morphir/Program.cs` and `src/Morphir.Tool/Program.cs` (host identity).
- Removed: the unused `ExtensionList`, `ExtensionListResult` and `ExtensionInfo` records and `ExtensionHandlers.HandleList` in `src/Morphir.Tooling/Features/Management/`, which have no callers. The other management records stay.
- Documentation: `docs/content/docs/cli/management.md` (`extension list` output).
- Tests: parser property tests and unit tests in `tests/Morphir.Tooling.Tests`, the unchanged generate tests and contract scripts, and an executable test that runs `extension list --json` against the AOT `morphir` binary and against `dotnet-morphir`.
- New package dependency in `Morphir.Tooling`: Spectre.Console (core only, already pinned in `Directory.Packages.props` for repository scripts) for TUI-styled tables. The AOT closure gains `Morphir.Extensions` and `Spectre.Console`; task 7.6 measures the effect.
- Contributor tooling: the work uses the project skill `fsharp-csharp-interop` and the agent `fsharp-csharp-interop-reviewer`, which are added to the repository with this proposal.
