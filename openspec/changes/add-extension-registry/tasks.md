# Tasks

Parent: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 0. Follow red/green/refactor for each behavior change. Each group lands its own tests and documentation. Decision ids refer to [design.html](design.html). Parse, don't validate (TYP-1): no task adds a contract member of type `string`, a C# `enum`, or a check that runs after construction.

## 1. Tracking and baseline

- [ ] 1.1 Create the GitHub issue for PRD step 0 and link it in `proposal.md` and in the PRD feature status table; verify both links resolve.
- [ ] 1.2 Check whether PR #495 (`add-fsharp-compile`) has landed `Morphir.Program.CreateApp`; record in this task which HOST-1 path applies, and verify the branch builds with `./build.sh` before any production change.

## 2. Parsed value types (TYP-1, ID-1)

- [ ] 2.1 Write failing tests in `tests/Morphir.Tooling.Tests/Features/Extensions/` for `Parsed<T>`, `ParseFailure` and `NonEmptySet<T>` (no empty set can be built, order and equality are stable, `Map` keeps the set non-empty); verify they fail, then implement the types and verify they pass.
- [ ] 2.2 Write failing property and table tests for `ExtensionId`: generated valid ids parse and print back unchanged; each spec example in "An extension id is a dotted portable token" is accepted or refused with the matching `ParseFailure`; then implement it and verify the tests pass.
- [ ] 2.3 Do the same for `ExtensionName`, `ExtensionDescription` and `ReleaseVersion` (`ReleaseVersion` wraps `Versioning.SemanticVersion.parse`); verify the limits (128, 1024), the white-space and control-character cases, and that `0.0.0-dev` and `0.4.0-rc.1` parse.
- [ ] 2.4 Add the closed unions `MorphirHost`, `HostAvailability`, `ExtensionDelivery`, `ExtensionType` and `ExtensionCapability`, each an abstract record with a private constructor; verify a test maps every case to its wire value through an exhaustive `switch`, and that the build has no new warnings.

## 3. Typed generation contracts (GEN-1)

- [ ] 3.1 Write a failing test that a malformed `--target` (for example `C#`) gives the same `unknown-target` invocation failure, message and list of available targets as a well-formed unknown target; verify it fails for the expected reason.
- [ ] 3.2 Replace `TargetId` with the parsed type, and parse `--target` in `CliGenerationSettingsResolver`; verify 3.1 passes and the existing generate tests pass unchanged.
- [ ] 3.3 Change `BackendCapability.Versions` to `NonEmptySet<SourceFormatVersion>` and `Supports` to compare `SourceFormatVersion` values; verify the existing resolver tests for exact versions, `3` against `3.0.0`, and pre-releases pass unchanged.
- [ ] 3.4 Give `BackendDescriptor` an `ExtensionId Id`, an `ExtensionName Name`, an optional `ExtensionDescription` and a `NonEmptySet<BackendCapability>`; set `morphir.csharp` / "C# generator" and `morphir.fsharp` / "F# generator" with `Create`; verify the `backend` report field is unchanged, `tests/Morphir.Tooling.Tests/Features/Generate/verify-csharp-cli.ps1` and `verify-csharp-cli-contracts.ps1` pass, and a test calls every literal.
- [ ] 3.5 Update `src/Morphir.Tooling/Features/Generate/README.md` (its `TargetId` example and the descriptor contract); verify the documented snippets compile in the existing consumer test.

## 4. Registry and backend source (REG-1, REG-2, REG-3)

- [ ] 4.1 Write failing tests for `ExtensionEntry` (types derived from capabilities; `IsAvailableIn` for `AllHosts` and `Only`) and for `ExtensionCatalog.Create` (ordinal order by id; `DuplicateIds` that names every duplicate id); verify they fail, then implement and verify they pass.
- [ ] 4.2 Write failing tests for `BackendExtensionSource` (one entry per backend, identity from the descriptor and the host version, `BuiltIn`, `AllHosts`, one backend capability with the target and the union of declared IR versions); implement it with no parsing and verify the tests pass.
- [ ] 4.3 Add the agreement test: every registered backend has an entry, and every backend target in the catalog resolves through `RegisteredBackendResolver` for at least one declared model; verify it passes, and fails when a test registers a backend that the source does not see.
- [ ] 4.4 Write XML doc comments on `IExtensionSource` and `ExtensionCatalog` that state the static-registration rule, the one-host rule (HOST-2) and the unique-id guarantee; verify the build has no new documentation warnings.

## 5. Host identity (HOST-1)

- [ ] 5.1 Add `HostIdentity` to the shared composition through the HOST-1 path recorded in 1.2: `morphir` passes `Native` and `dotnet-morphir` passes `DotnetTool`, each with `ReleaseVersion.Create(VersionInfo.Version)`; verify a unit test resolves `HostIdentity` from the container of each composition.
- [ ] 5.2 Add a build test that runs `--version` on each host and parses the output with `ReleaseVersion.Parse`; verify it passes for a versioned build and for `0.0.0-dev`.
- [ ] 5.3 Register `BackendExtensionSource` and `ExtensionRegistry` with `TryAddEnumerable` and `TryAddSingleton`; verify `--version` and `--help` output are unchanged in both hosts.

## 6. `extension list` command (CLI-1, CLN-1)

- [ ] 6.1 Write failing command tests for text output (one row for each entry, sorted, with the six columns), `--json` output (one document with `host` and `extensions`, MEP wire names in `capabilities.backend`, no `description` member when absent), `--local` and `--platform` still showing built-ins, and exit code 2 naming each duplicate id; verify they fail.
- [ ] 6.2 Add `ExtensionListCommand`, `ExtensionListReport.From(ExtensionCatalog)` as the only typed-to-text mapping, and the source-generated `ExtensionListJsonContext`; verify the 6.1 tests pass and stdout holds only command output.
- [ ] 6.3 Wire `list` into the `extension` command through the service provider and keep `install`, `use`, `remove` and `which` unchanged; verify their help text and stub output are unchanged.
- [ ] 6.4 Remove `ExtensionList`, `ExtensionListResult`, `ExtensionInfo` and `ExtensionHandlers.HandleList`; verify the solution builds and no reference remains.
- [ ] 6.5 Update `docs/content/docs/cli/management.md` with the `extension list` text and JSON output, the id rule (including the dotted superset), the meaning of delivery, hosts and `available`, and the note that `--platform` and `--local` apply to installed extensions from PRD step 3; verify the documented commands give the documented output.

## 7. Both hosts and AOT

- [ ] 7.1 Add a `tool` executable type to `tests/Morphir.E2E.Tests/Infrastructure/ExecutableLocator.cs` that runs the built `dotnet-morphir.dll` through `dotnet`; verify an `ExecutableRunnerTests` case runs `--version` through it.
- [ ] 7.2 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionList.feature` with steps that run `extension list --json` and check the entries `morphir.csharp` and `morphir.fsharp`, the `host` value and stdout purity; verify it passes against the AOT `morphir` executable and against `dotnet-morphir`.
- [ ] 7.3 Add a scenario that compares the shared entries of the two hosts' results (spec: "Both hosts list the same shared built-ins"); verify it passes.
- [ ] 7.4 Publish the AOT `morphir` executable and record the trim and AOT warning count and the binary size before and after; verify there are no new warnings and no new assembly in the closure.

## 8. Closeout: PRD and upstream

- [ ] 8.1 Update PRD step 0 with PLG-1 (the pipeline plugins register in step 1) and ID-1 (dotted ids; step 3 refuses them in shared upstream files until upstream accepts them), and set the feature status row "Extension registry and built-in list"; verify the PRD index row matches.
- [ ] 8.2 Draft the upstream proposal for dotted extension ids (the segment grammar, the 238-character limit, the file-name safety argument, and `morphir.csharp` as the example) against the `finos/morphir` extension identity rules and `finos/morphir-rust` `crates/morphir-distribution/src/domain/identity.rs`; get the user's approval, then open it upstream and link it in the PRD and in `proposal.md`; verify the links resolve.

## Workflow follow-up

- Follow AGENTS.md for review, CLA-compliant commits, explicit merge approval, archive and issue closure.
- When upstream decides on dotted ids, update ID-1 in the archived design notes and PRD step 3.
- After archive, check that `openspec/specs/extension-registry/spec.md` has the Purpose text from this change.
