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
- [x] 2.2 Add `tests/Morphir.Extensions.Tests` (Expecto, Expecto.FsCheck) to `Morphir.slnx` and to the unit test targets in `build/Build.Testing.cs`; verify `./build.sh --target Test` runs it.
  - `./build.sh --target Test --configuration Release` ran both new projects: `Morphir.Extensions.Tests` 57 passed, `Morphir.Tests` 11 passed. The run reported one failure, `Morphir.SDK.Tests`, caused by the local environment: its package-migration tests start `pwsh`, which resolved to the mise shim, and mise refused this worktree's untrusted `mise.toml`. With the real `pwsh` first on `PATH`, `Morphir.SDK.Tests` passes 403 of 403. This change touches no SDK file.
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
- [x] 4.2 Remove the C# `TargetId` struct, use the F# `TargetId`, and parse `--target` with `TargetId.TryParse` in `CliGenerationSettingsResolver`; verify 4.1 passes and the existing generate tests pass unchanged.
  - Done with a `RequestedTarget` union (`Valid(TargetId)` or `Malformed(input)`) instead of failing in the settings resolver: a malformed target still fails in `RegisteredBackendResolver`, so the order of errors and the messages do not change, and only a valid `TargetId` reaches a backend. Backends compare targets with `Equals`. Tests that built `TargetId` or targets with `new(...)` now use `TargetId.Create` and `RequestedTarget.Parse`; their assertions did not change. The F# consumer snippet type-checks `RequestedTarget.Parse` but runs a `Malformed` target, because `dotnet fsi` holds the SDK's FSharp.Core 10.x and cannot load Morphir.Extensions (FSharp.Core 11). Full `Morphir.Tooling.Tests`: 580 passed.
- [x] 4.3 Change `BackendCapability.Versions` to `NonEmptySet<SourceFormatVersion>` and `Supports` to compare `SourceFormatVersion` values; verify the existing resolver tests for exact versions, `3` against `3.0.0`, and pre-releases pass unchanged.
  - `BackendCapability` is a positional record over `NonEmptySet<SourceFormatVersion>`; `SourceFormatVersions.Create(...)` builds the declared literals. Existing version assertions now read `Text`.
- [x] 4.4 Give `BackendDescriptor` an `ExtensionId Id`, an `ExtensionName Name`, an optional `ExtensionDescription` and a `NonEmptySet<BackendCapability>`; set `morphir.csharp` / "C# generator" and `morphir.fsharp` / "F# generator" with `Create`; verify the `backend` report field is unchanged, `tests/Morphir.Tooling.Tests/Features/Generate/verify-csharp-cli.ps1` and `verify-csharp-cli-contracts.ps1` pass, and a test calls every literal.
  - `Capabilities` stays an `ImmutableArray` with a non-empty constructor guard: `BackendCapability` has no order, which `NonEmptySet` needs. The two PowerShell scripts are manual Windows evidence scripts (hard-coded `C:\` paths and a Windows `dotnet.exe`) that CI does not run; they were not run here. The `backend` report field is covered by `CSharpCliGenerationTests` (`morphir.csharp`), which passes. `BothBackendDescriptorsBuildFromValidLiterals` calls every literal.
- [x] 4.5 Update `src/Morphir.Tooling/Features/Generate/README.md` (its `TargetId` example and the descriptor contract); verify the documented snippets compile in the existing consumer test.
  - The README states the `RequestedTarget` and parsed descriptor contracts, the `Equals` rule and the fsi limit; `GenerationContractsTests` compiles the C# provider and the F# settings snippet.

## 5. Registry (REG-1, REG-2, REG-3)

- [x] 5.1 Write failing Expecto tests for the REG-1 unions and `ExtensionEntry` (types derived from capabilities; `IsAvailableIn` for `AllHosts` and `Only`; each `WireValue`) and for `ExtensionCatalog.create` (ordinal order by id; `DuplicateIds` that names every duplicate id, with its diagnostics); verify they fail, then implement in `Morphir.Extensions` and verify they pass.
  - `Registry.fs`: the REG-1 unions with `WireValue` members, `HostIdentity`, `BackendClaim`, `ExtensionIdentity` (with `Create` and `DescriptionOrNull` for C#), `ExtensionEntry` (with `BuiltInBackend`, `WithHosts` and `BackendClaimOrNull` for C#, because C# cannot use `with` or branch on unions), `Diagnostic`, `RegistryError`, `IExtensionSource` and `ExtensionCatalog` (`Build`, the `ExtensionCatalog.create` module function and `TryCreate`). 7 Expecto tests.
- [x] 5.2 Add `IExtensionSource`, `ExtensionCatalog.TryCreate` and `ExtensionEntry.BuiltInBackend` for C# callers (FFI-1); verify a C# TUnit test builds a catalog through them with no `switch` on an F# union.
- [x] 5.3 Write failing TUnit tests for the C# `BackendExtensionSource` (one entry per backend, identity from the descriptor and the host version, `BuiltIn`, `AllHosts`, one backend capability with the target and the union of declared IR versions); implement it with no parsing and verify the tests pass.
  - `src/Morphir.Tooling/Features/Extensions/BackendExtensionSource.cs` and `ExtensionRegistry.cs` (`TryRead` with `MaybeNullWhen`). Group 4 review fixes in the same commit: `SourceFormatVersions.Create` moved to F# (`Morphir.Extensions/FormatVersions.fs`) so C# does not branch on an F# `Result` and the error keeps the parse detail; `SourceFormatVersion.ToString` in Morphir.Models returns the declaration instead of reflection text; a test compares `ModelFamily` with `Equals`. Turning on nullness in all of Morphir.Models is left for a separate change.
- [x] 5.4 Add the agreement test: every registered backend has an entry, and every backend target in the catalog resolves through `RegisteredBackendResolver` for at least one declared model; verify it passes, and fails when a test registers a backend that the source does not see.
  - `ExtensionGenerationAgreementTests`: agrees for the `GenerationScenario` backends; the check reports `morphir.fsharp` when the source sees only the C# backend. Full `Morphir.Tooling.Tests`: 592 passed.
- [x] 5.5 Write XML doc comments on the public F# surface (the static-registration rule, the one-host rule from HOST-2, the unique-id guarantee, and which members are for C# callers); verify the build has no documentation warnings.
  - Every public F# declaration in Morphir.Extensions has an XML doc comment (checked with a script; interface implementations inherit); the build has no documentation warnings.

## 6. Host identity (HOST-1)

- [x] 6.1 Add `HostIdentity` to the shared composition through the HOST-1 path recorded in 1.2: `morphir` passes `MorphirHost.Native` and `dotnet-morphir` passes `MorphirHost.DotnetTool`, each with `ReleaseVersion.Create(VersionInfo.Version)`; verify a unit test resolves `HostIdentity` from the container of each composition.
  - `Morphir.Program` has `Host`, `Run(args, host)` and `CreateApp(args, host)`; `Morphir.Tool.Program.Host` states `DotnetTool`. The host is a singleton in the shared composition. Tests in `tests/Morphir.Tests/Hosts/` (that project had no tests; it is now in the Nuke unit test list and references both entry points).
- [x] 6.2 Add a build test that runs `--version` on each host and parses the output with `ReleaseVersion.TryParse`; verify it passes for a versioned build and for `0.0.0-dev`.
  - `HostVersionTests` runs `--version` on the built `morphir.dll` and `dotnet-morphir.dll` and parses the output, which has build metadata (`0.4.0-alpha.1+<sha>`); `0.0.0-dev` is parsed directly, because changing the build version inside a unit test is not practical.
- [x] 6.3 Register `BackendExtensionSource` and `ExtensionRegistry` with `TryAddEnumerable` and `TryAddSingleton`; verify `--version` and `--help` output are unchanged in both hosts.
  - Registered in `ManagementScenario`. `HostRegistryTests` reads both generators for each host. `--version` and `--help` of both built hosts match the baseline taken before the change.

## 7. `extension list` command (CLI-1, CLN-1)

- [x] 7.1 Write failing command tests for text output (one row for each entry, sorted, with the six columns), `--json` output (one document with `host` and `extensions`, MEP wire names in `capabilities.backend`, no `description` member when absent), `--local` and `--platform` still showing built-ins, and exit code 2 naming each duplicate id; verify they fail.
  - `ExtensionListCommandTests` (core: text, JSON, absent description, other-host entry, duplicate id) and `ExtensionListCliTests` (the real CLI: `--json`, `--local --json`, `--platform linux-x64 --json`, text).
- [x] 7.2 Add `ExtensionListCommand`, `ExtensionListReport.From(ExtensionCatalog)` as the only typed-to-text mapping (through `ToString` and `WireValue`, with no `switch` on an F# union), and the source-generated `ExtensionListJsonContext`; verify the 7.1 tests pass and stdout holds only command output.
  - `ExtensionListCommand.Execute(registry, json, stdout, stderr)` is the testable core; `ExtensionListReport.From` and `Failure` are the only typed-to-text mapping; `ExtensionListJsonContext` leaves out nulls. In JSON, `+` in a version is written as `\u002B`, the System.Text.Json default, as in `generate --json`.
- [x] 7.3 Wire `list` into the `extension` command through the service provider and keep `install`, `use`, `remove` and `which` on `NotImplemented` with the #492 link; remove the `extension list` row from `tests/Morphir.Tooling.Tests/Integration/CLI/CliManagementCommands.feature` and verify the other rows still pass.
  - `MorphirCliBuilder.ConfigureHostedCommands` (new) runs after the host is built; `ManagementScenario` uses it to insert `list` first, so `extension --help` differs from the baseline only in the `list` description. The other four subcommands still exit 1 with the #492 link; the 14 remaining `CliManagementCommands.feature` rows pass.
- [x] 7.4 Remove `ExtensionList`, `ExtensionListResult`, `ExtensionInfo` and `ExtensionHandlers.HandleList`; verify the solution builds and no reference remains.
  - No reference remains; the solution builds.
- [x] 7.5 Update `docs/content/docs/cli/management.md` with the `extension list` text and JSON output, the id rule (including the dotted superset), the meaning of delivery, hosts and `available`, and the note that `--platform` and `--local` apply to installed extensions from PRD step 3; verify the documented commands give the documented output.
  - `TextOutputMatchesTheDocumentation` asserts the documented table exactly; the JSON example is checked field by field by `JsonOutputIsOneDocumentWithMepWireNames`. Full `Morphir.Tooling.Tests`: 603 passed.

## 8. Both hosts and AOT

- [x] 8.1 Add a `tool` executable type to `tests/Morphir.E2E.Tests/Infrastructure/ExecutableLocator.cs` that runs the built `dotnet-morphir.dll` through `dotnet`; verify an `ExecutableRunnerTests` case runs `--version` through it.
  - Done with the app host that the `Morphir.Tool` build writes (`src/Morphir.Tool/bin/<configuration>/net10.0/dotnet-morphir`), which runs `dotnet-morphir.dll` on the installed runtime, so `ExecutableRunner` needs no change. A `tool` run never falls back to a `morphir` executable. `scripts/run-e2e-tests.cs` accepts `tool`, includes it in `all`, and builds `Morphir.Tool` on every run. `ToolTypeRunsTheBuiltDotnetMorphir` passes.
- [x] 8.2 Add `tests/Morphir.E2E.Tests/Features/ExecutableExtensionList.feature` with steps that run `extension list --json` and check the entries `morphir.csharp` and `morphir.fsharp`, the `host` value and stdout purity; verify it passes against the AOT `morphir` executable and against `dotnet-morphir`.
  - E2E run with `MORPHIR_EXECUTABLE_TYPE=aot` (the published AOT executable) and `tool`: 27 passed, 0 failed, 1 skipped (`Verify with quiet mode`, an existing `@Ignore`), in each. `dotnet run scripts/run-e2e-tests.cs -- tool Release` passes.
- [x] 8.3 Add a scenario that compares the shared entries of the two hosts' results (spec: "Both hosts list the same shared built-ins"); verify it passes.
  - `Both hosts list the same shared built-ins` runs the other host (the tool for a `morphir` type; else the published or built `morphir`) and compares the shared entries, which carry the build version, so both hosts must come from the same commit. It passes in both runs.
- [x] 8.4 Publish the AOT `morphir` executable and record the trim and AOT warning count and the binary size before and after; verify there are no new warnings, including from `Morphir.Extensions`, and that the only new assembly in the closure is `Morphir.Extensions`.
  - Measured on osx-arm64 against `main` (`a5f91af`), both published with `./build.sh --target PublishExecutable --configuration Release`: the same 54 warning lines (18 IL2026, 4 IL2104, 30 IL3050, 2 IL3053; 27 unique), 0 new and 0 removed, none from `Morphir.Extensions`; the only new assembly is `Morphir.Extensions`; the executable grows from 17,100,952 to 17,382,456 bytes (+281,504, +1.6%).

## 9. Closeout: PRD and upstream

- [x] 9.1 Update PRD step 0 with LANG-1 (F# domain project), PLG-1 (the pipeline plugins register in step 1) and ID-1 (dotted ids; step 3 refuses them in shared upstream files until upstream accepts them), and set the feature status row "Extension registry and built-in list"; verify the PRD index row matches.
  - PRD step 0 states that the plugins register in step 1, the F# domain project and the dotted id rule; step 1 says `TypeValidator` registers as a `validator` entry; step 3 refuses dotted ids in shared upstream files; the status row says implemented, with the MCK suite still to come; the PRD index row is updated.
- [ ] 9.2 Draft the upstream proposal for dotted extension ids (the segment grammar, the 238-character limit, the file-name safety argument, and `morphir.csharp` as the example) against the `finos/morphir` extension identity rules and `finos/morphir-rust` `crates/morphir-distribution/src/domain/identity.rs`; get the user's approval, then open it upstream and link it in the PRD and in `proposal.md`; verify the links resolve.
  - Drafted; waiting for the user's approval before it is opened upstream.

## Workflow follow-up

- Follow AGENTS.md for review, CLA-compliant commits, explicit merge approval, archive and issue closure.
- When upstream decides on dotted ids, update ID-1 in the archived design notes and PRD step 3.
- After archive, check that `openspec/specs/extension-registry/spec.md` has the Purpose text from this change.
