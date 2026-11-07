# Tasks

Parent: [Morphir extension model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md), step 0. Follow red/green/refactor for each behavior change. Each group lands its own tests and documentation. Decision ids refer to [design.html](design.html).

- Parse, don't validate (TYP-1): no task adds a contract member of type `string`, a C# `enum`, or a check that runs after construction.
- F# domain, C# edge (LANG-1): load the `fsharp-csharp-interop` skill before writing F# code that C# calls, and run the `fsharp-csharp-interop-reviewer` agent on each group's diff before the group is complete (FFI-1).

## 1. Tracking and baseline

- [x] 1.1 Link step 0 to issue #492 (the `morphir extension` commands): comment on #492 with a link to this change, and add #492 to the PRD feature status table; verify both links resolve.
- [x] 1.2 Check whether PR #495 (`add-fsharp-compile`) has landed `Morphir.Program.CreateApp`; record in this task which HOST-1 path applies, and verify the branch builds with `./build.sh` before any production change.
  - Recorded 2026-11-07: #495 landed only the plan (`3a33379`); neither entry point has `CreateApp`. HOST-1 uses the `Morphir.Program.Run(args, HostIdentity)` path. Baseline `./build.sh` on `a5f91af` succeeded (Restore and Compile, 0:54), with existing NU1608, NU1510 and CS0436 warnings only.

## 2. F# project setup (LANG-1)

- [x] 2.1 Add `src/Morphir.Extensions` (F#, `net10.0`) with `Nullable`, `TreatWarningsAsErrors`, `IsTrimmable`, `EnableTrimAnalyzer`, `IsAotCompatible` and `GenerateDocumentationFile`, a reference to `Morphir.Models`, the same packing settings as `Morphir.Tooling`, and an entry in `Morphir.slnx`; verify `dotnet build` of the project has zero warnings.
- [ ] 2.2 Add `tests/Morphir.Extensions.Tests` (Expecto, Expecto.FsCheck) to `Morphir.slnx` and to the unit test targets in `build/Build.Testing.cs`; verify `./build.sh --target Test` runs it.
- [x] 2.3 Reference `Morphir.Extensions` from `Morphir.Tooling`, and add the interop check from TST-1: a C# call that passes `null` to a non-nullable F# parameter fails to compile; verify the check fails when F# nullness is turned off in the project and passes when it is on.
  - Done 2026-11-07 with `tests/Morphir.Tooling.Tests/Features/Extensions/ExtensionsNullabilityContractTests.cs`. It reads the nullable metadata that the C# compiler reads (`NullabilityInfoContext` and `MaybeNullWhen`), not a Roslyn compilation, so no new package is needed. With F# nullness off, 7 of 7 tests fail; with it on, 7 of 7 pass.
  - Found: F# does not write the nullness of a byref element (`byref<T | null>`) into the metadata, so C# read `TryParse` out values as never null and gave no CS8602 on the unsafe branch. `TryParse` and `TryCreate` now use non-nullable out types with `[<MaybeNullWhen>]` (a probe gives CS8602 on each unsafe read and no warning on the safe ones). FFI-1, REG-2 and the `fsharp-csharp-interop` skill are corrected.

## 3. Parsed value types in F# (TYP-1, ID-1, FFI-1)

- [x] 3.1 Write failing Expecto tests for `NonEmptySet<'T>` (no empty set can be built, order and equality are stable, `map` keeps the set non-empty, it implements `IReadOnlyCollection<'T>`) and for `ParseFailure`; verify they fail, then implement and verify they pass.
- [x] 3.2 Write failing FsCheck properties and Expecto tests for `ExtensionId`: generated valid ids parse and print back unchanged; each spec example in "An extension id is a dotted portable token" is accepted or refused with the matching `ParseFailure`; ordinal equality and order; then implement it with `parse`, `TryParse`, `Create` and a `ToString` override, and verify the tests pass.
- [x] 3.3 Do the same for `ExtensionName`, `ExtensionDescription`, `ReleaseVersion` (wraps `Versioning.SemanticVersion.parse`) and `TargetId`; verify the limits (128, 1024), the white-space and control-character cases, and that `0.0.0-dev` and `0.4.0-rc.1` parse.
- [x] 3.4 Add C# TUnit tests in `tests/Morphir.Tooling.Tests` that call each `TryParse` and `Create` from C#, with `null` input and nullable flow analysis; verify they pass with no nullable warnings.

## 4. Typed generation contracts (GEN-1)

- [x] 4.1 Write a failing test that a malformed `--target` (for example `C#`) gives the same `unknown-target` invocation failure, message and list of available targets as a well-formed unknown target; verify it fails for the expected reason.
  - Done as a characterization test (`GenerationHandlerTests.MalformedTargetFailsLikeAnUnknownTarget`, cases `C#`, `CSharp`, `a..b`). It passes before the change, because today a malformed target already fails in the backend resolver; it pins that behavior through GEN-1. Locally it needs a real `TMPDIR` on macOS, as CI sets.
- [ ] 4.2 Remove the C# `TargetId` struct, use the F# `TargetId`, and parse `--target` with `TargetId.TryParse` in `CliGenerationSettingsResolver`; verify 4.1 passes and the existing generate tests pass unchanged.
- [ ] 4.3 Change `BackendCapability.Versions` to `NonEmptySet<SourceFormatVersion>` and `Supports` to compare `SourceFormatVersion` values; verify the existing resolver tests for exact versions, `3` against `3.0.0`, and pre-releases pass unchanged.
- [ ] 4.4 Give `BackendDescriptor` an `ExtensionId Id`, an `ExtensionName Name`, an optional `ExtensionDescription` and a `NonEmptySet<BackendCapability>`; set `morphir.csharp` / "C# generator" and `morphir.fsharp` / "F# generator" with `Create`; verify the `backend` report field is unchanged, `tests/Morphir.Tooling.Tests/Features/Generate/verify-csharp-cli.ps1` and `verify-csharp-cli-contracts.ps1` pass, and a test calls every literal.
- [ ] 4.5 Update `src/Morphir.Tooling/Features/Generate/README.md` (its `TargetId` example and the descriptor contract); verify the documented snippets compile in the existing consumer test.

## 5. Registry (REG-1, REG-2, REG-3)

- [ ] 5.1 Write failing Expecto tests for the REG-1 unions and `ExtensionEntry` (types derived from capabilities; `IsAvailableIn` for `AllHosts` and `Only`; each `WireValue`) and for `ExtensionCatalog.create` (ordinal order by id; `DuplicateIds` that names every duplicate id, with its diagnostics); verify they fail, then implement in `Morphir.Extensions` and verify they pass.
- [ ] 5.2 Add `IExtensionSource`, `ExtensionCatalog.TryCreate` and `ExtensionEntry.BuiltInBackend` for C# callers (FFI-1); verify a C# TUnit test builds a catalog through them with no `switch` on an F# union.
- [ ] 5.3 Write failing TUnit tests for the C# `BackendExtensionSource` (one entry per backend, identity from the descriptor and the host version, `BuiltIn`, `AllHosts`, one backend capability with the target and the union of declared IR versions); implement it with no parsing and verify the tests pass.
- [ ] 5.4 Add the agreement test: every registered backend has an entry, and every backend target in the catalog resolves through `RegisteredBackendResolver` for at least one declared model; verify it passes, and fails when a test registers a backend that the source does not see.
- [ ] 5.5 Write XML doc comments on the public F# surface (the static-registration rule, the one-host rule from HOST-2, the unique-id guarantee, and which members are for C# callers); verify the build has no documentation warnings.

## 6. Host identity (HOST-1)

- [ ] 6.1 Add `HostIdentity` to the shared composition through the HOST-1 path recorded in 1.2: `morphir` passes `MorphirHost.Native` and `dotnet-morphir` passes `MorphirHost.DotnetTool`, each with `ReleaseVersion.Create(VersionInfo.Version)`; verify a unit test resolves `HostIdentity` from the container of each composition.
- [ ] 6.2 Add a build test that runs `--version` on each host and parses the output with `ReleaseVersion.TryParse`; verify it passes for a versioned build and for `0.0.0-dev`.
- [ ] 6.3 Register `BackendExtensionSource` and `ExtensionRegistry` with `TryAddEnumerable` and `TryAddSingleton`; verify `--version` and `--help` output are unchanged in both hosts.

## 7. `extension list` command (CLI-1, CLN-1)

- [ ] 7.1 Write failing command tests for text output (one row for each entry, sorted, with the six columns), `--json` output (one document with `host` and `extensions`, MEP wire names in `capabilities.backend`, no `description` member when absent), `--local` and `--platform` still showing built-ins, and exit code 2 naming each duplicate id; verify they fail.
- [ ] 7.2 Add `ExtensionListCommand`, `ExtensionListReport.From(ExtensionCatalog)` as the only typed-to-text mapping (through `ToString` and `WireValue`, with no `switch` on an F# union), and the source-generated `ExtensionListJsonContext`; verify the 7.1 tests pass and stdout holds only command output.
- [ ] 7.3 Wire `list` into the `extension` command through the service provider and keep `install`, `use`, `remove` and `which` on `NotImplemented` with the #492 link; remove the `extension list` row from `tests/Morphir.Tooling.Tests/Integration/CLI/CliManagementCommands.feature` and verify the other rows still pass.
- [ ] 7.4 Remove `ExtensionList`, `ExtensionListResult`, `ExtensionInfo` and `ExtensionHandlers.HandleList`; verify the solution builds and no reference remains.
- [ ] 7.5 Update `docs/content/docs/cli/management.md` with the `extension list` text and JSON output, the id rule (including the dotted superset), the meaning of delivery, hosts and `available`, and the note that `--platform` and `--local` apply to installed extensions from PRD step 3; verify the documented commands give the documented output.

## 8. Both hosts and AOT

- [ ] 8.1 Add a `tool` executable type to `tests/Morphir.E2E.Tests/Infrastructure/ExecutableLocator.cs` that runs the built `dotnet-morphir.dll` through `dotnet`; verify an `ExecutableRunnerTests` case runs `--version` through it.
- [ ] 8.2 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionList.feature` with steps that run `extension list --json` and check the entries `morphir.csharp` and `morphir.fsharp`, the `host` value and stdout purity; verify it passes against the AOT `morphir` executable and against `dotnet-morphir`.
- [ ] 8.3 Add a scenario that compares the shared entries of the two hosts' results (spec: "Both hosts list the same shared built-ins"); verify it passes.
- [ ] 8.4 Publish the AOT `morphir` executable and record the trim and AOT warning count and the binary size before and after; verify there are no new warnings, including from `Morphir.Extensions`, and that the only new assembly in the closure is `Morphir.Extensions`.

## 9. Closeout: PRD and upstream

- [ ] 9.1 Update PRD step 0 with LANG-1 (F# domain project), PLG-1 (the pipeline plugins register in step 1) and ID-1 (dotted ids; step 3 refuses them in shared upstream files until upstream accepts them), and set the feature status row "Extension registry and built-in list"; verify the PRD index row matches.
- [ ] 9.2 Draft the upstream proposal for dotted extension ids (the segment grammar, the 238-character limit, the file-name safety argument, and `morphir.csharp` as the example) against the `finos/morphir` extension identity rules and `finos/morphir-rust` `crates/morphir-distribution/src/domain/identity.rs`; get the user's approval, then open it upstream and link it in the PRD and in `proposal.md`; verify the links resolve.

## Workflow follow-up

- Follow AGENTS.md for review, CLA-compliant commits, explicit merge approval, archive and issue closure.
- When upstream decides on dotted ids, update ID-1 in the archived design notes and PRD step 3.
- After archive, check that `openspec/specs/extension-registry/spec.md` has the Purpose text from this change.
