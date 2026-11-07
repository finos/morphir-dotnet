# Tasks

Parent: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 0. Follow red/green/refactor for each behavior change. Each group lands its own tests and documentation. Decision ids refer to [design.html](design.html).

## 1. Tracking and baseline

- [ ] 1.1 Create the GitHub issue for PRD step 0 and link it in `proposal.md` and in the PRD feature status table; verify both links resolve.
- [ ] 1.2 Check whether PR #495 (`add-fsharp-compile`) has landed `Morphir.Program.CreateApp`; record in this task which HOST-1 path applies, and verify the branch builds with `./build.sh` before any production change.

## 2. Registry types and backend source

- [ ] 2.1 Write failing unit tests in `tests/Morphir.Tooling.Tests/Features/Extensions/` for the registry: ordinal sort by id, the `extension.duplicate_id` failure that names each duplicate id, and `available` for an entry whose hosts are only `dotnet-morphir`; verify they fail for the expected reason.
- [ ] 2.2 Add `ExtensionType`, `ExtensionDelivery`, `MorphirHostKind`, `MorphirHost`, `ExtensionIdentity`, `BackendClaim`, `ExtensionClaims`, `ExtensionEntry`, `IExtensionSource`, `ExtensionRegistry` and `ExtensionCatalog` in `src/Morphir.Tooling/Features/Extensions/` (REG-1, REG-2); verify the 2.1 tests pass.
- [ ] 2.3 Write failing tests for `BackendExtensionSource`: one entry for each registered backend, id from `Descriptor.Identity`, type `backend`, delivery `built-in`, both hosts, host version, targets and the ordinal union of declared IR versions (REG-3); then implement it and verify the tests pass.
- [ ] 2.4 Add optional `Name` and `Description` `init` properties to `BackendDescriptor` and set "C# generator" and "F# generator" in the two backends; verify the existing generation tests still pass with no change to `RegisteredBackendResolver`.
- [ ] 2.5 Add the agreement test: every registered backend has an entry, and every backend target in the registry resolves through `RegisteredBackendResolver` for at least one model that the backend declares; verify it passes, and fails when a test registers a backend that the source does not see.
- [ ] 2.6 Write an XML doc comment on `IExtensionSource` that states the static-registration rule and the one-host rule (HOST-2); verify the build has no new documentation warnings.

## 3. Host identity

- [ ] 3.1 Add `MorphirHost` to the shared composition through the HOST-1 path recorded in 1.2: `morphir` passes `Native` and `dotnet-morphir` passes `DotnetTool`, each with its own `VersionInfo.Version`; verify a unit test resolves `MorphirHost` from the container of each composition.
- [ ] 3.2 Register `BackendExtensionSource` and `ExtensionRegistry` in `GenerationScenario` or a new `ExtensionScenario` with `TryAddEnumerable` and `TryAddSingleton`; verify `--version` and `--help` output are unchanged in both hosts.

## 4. `extension list` command

- [ ] 4.1 Write failing command tests for text output (one row for each entry, sorted, with the six columns), `--json` output (one document with `host` and `extensions`, MEP wire names in `capabilities.backend`, `description` left out when null), `--local` and `--platform` still showing built-ins, and exit code 2 with a stderr diagnostic for a duplicate id; verify they fail.
- [ ] 4.2 Add `ExtensionListCommand`, its report record and the source-generated `ExtensionListJsonContext` with explicit enum-to-string mapping (CLI-1); verify the 4.1 tests pass and stdout holds only command output.
- [ ] 4.3 Wire `list` into the `extension` command through the service provider and keep `install`, `use`, `remove` and `which` unchanged (CLI-1); verify their help text and stub output are unchanged.
- [ ] 4.4 Remove `ExtensionList`, `ExtensionListResult`, `ExtensionInfo` and `ExtensionHandlers.HandleList` (CLN-1); verify the solution builds and no reference remains.
- [ ] 4.5 Update `docs/content/docs/cli/management.md` with the `extension list` text and JSON output, the meaning of delivery, hosts and `available`, and the note that `--platform` and `--local` apply to installed extensions from PRD step 3; verify the documented commands give the documented output.

## 5. Both hosts and AOT

- [ ] 5.1 Add a `tool` executable type to `tests/Morphir.E2E.Tests/Infrastructure/ExecutableLocator.cs` that runs the built `dotnet-morphir.dll` through `dotnet`; verify an `ExecutableRunnerTests` case runs `--version` through it.
- [ ] 5.2 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionList.feature` with steps that run `extension list --json` and check the entries `morphir.csharp` and `morphir.fsharp`, the `host` value and stdout purity; verify it passes against the AOT `morphir` executable and against `dotnet-morphir`.
- [ ] 5.3 Add a scenario that compares the shared entries of the two hosts' results (spec: "Both hosts list the same shared built-ins"); verify it passes.
- [ ] 5.4 Publish the AOT `morphir` executable and record the trim and AOT warning count and the binary size before and after; verify there are no new warnings and no new assembly in the closure.

## 6. PRD alignment

- [ ] 6.1 Update PRD step 0 with PLG-1 (the pipeline plugins register in step 1, not step 0) and set the feature status row "Extension registry and built-in list" to the result of this change; verify the PRD index row matches.

## Workflow follow-up

- Follow AGENTS.md for review, CLA-compliant commits, explicit merge approval, archive and issue closure.
- After archive, check that `openspec/specs/extension-registry/spec.md` has the Purpose text from this change.
