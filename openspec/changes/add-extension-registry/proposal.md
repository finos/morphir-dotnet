# Proposal

## Why

The [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md) delivers extensions in small steps. Step 0 makes the built-in capabilities visible as extensions before any external runtime exists. Today `morphir extension list` is a stub that prints "No extensions installed.", although both hosts already contain two generators. The later steps (selection rule, analyzers, installation) need one registry that knows each extension, its capabilities, its delivery and the hosts it runs in.

## What Changes

- Add an extension registry to `Morphir.Tooling`. Each entry has an identity in the shape of the MEP `ExtensionInfo` record (`id`, `name`, `version`, optional `description`, `types`), a delivery, the hosts it runs in, and capability claims in the shape of the MEP capability records.
- Built-ins register through static dependency injection only. There is no assembly scanning, no reflection-based discovery and no assembly loading.
- The registry makes one entry for each registered `IGenerationBackend`, from its `BackendDescriptor`. So the list always agrees with the targets that `morphir generate` can resolve. The first two entries are `morphir.csharp` and `morphir.fsharp`, with type `backend`.
- Each host states which host it is. `morphir` (Native AOT) and `dotnet-morphir` (JIT dotnet tool) share `Morphir.Program`, and each entry point passes its host identity to the shared composition.
- `morphir extension list` shows the registered extensions with their id, name, version, types, delivery and hosts, in text form on stdout. With `--json`, it writes one JSON document on stdout from a source-generated serializer context.
- The `--platform` and `--local` options of `extension list` stay valid. They apply only to installed extensions, and there are none until PRD step 3. Built-ins always appear.
- No other command changes. `extension install`, `use`, `remove` and `which` stay as they are. `generate` keeps its backend resolution unchanged.

Out of scope for this change: the MEP `Morphir.Mep` types (planned in `add-fsharp-compile`, PR #495), the selection rule between built-in and configured extensions, external runtimes, installation, and the pipeline plugins in `src/Morphir.IR.Pipeline.Plugins` (see `design.html`, decision PLG-1).

## Capabilities

### New Capabilities

- `extension-registry`: The set of extensions that a host knows, with identity, types, delivery, hosts and capability claims, and how `morphir extension list` reports it.

### Modified Capabilities

None. No existing spec covers `morphir extension`.

## Tracking

- Parent: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 0.
- No GitHub issue exists yet. Create one and link it here before implementation starts, as `openspec/config.yaml` requires.
- Related: PR [#495](https://github.com/finos/morphir-dotnet/pull/495) (`add-fsharp-compile`) plans `Morphir.Program.CreateApp(args, extend)` and `Morphir.Mep`. This change must fit both (design decisions HOST-1 and REG-1).

## Impact

- New: `src/Morphir.Tooling/Features/Extensions/` (registry types, the backend adapter, the list query and report, the JSON context).
- Changed: `src/Morphir.Tooling/Scenarios/ManagementScenario.cs` (`extension list` handler), `src/Morphir.Tooling/Features/Generate/GenerationContracts.cs` (optional `Name` and `Description` on `BackendDescriptor`) and the two backends, `src/Morphir/Program.cs` and `src/Morphir.Tool/Program.cs` (host identity).
- Removed: the unused `ExtensionList`, `ExtensionListResult` and `ExtensionInfo` records and `ExtensionHandlers.HandleList` in `src/Morphir.Tooling/Features/Management/`, which have no callers. The other management records stay.
- Documentation: `docs/content/docs/cli/management.md` (`extension list` output).
- Tests: unit tests in `tests/Morphir.Tooling.Tests`, and an executable test that runs `extension list --json` against the AOT `morphir` binary and against `dotnet-morphir`.
- No new package dependency. No change to the AOT closure.
