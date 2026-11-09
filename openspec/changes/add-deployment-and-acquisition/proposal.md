# Proposal

## Why

The user approved the finos/morphir OpenSpec change `add-deployment-and-acquisition` on 9 November 2026. It defines how people acquire Morphir .NET and its F# tools without assembling runtimes, extension packages and configuration by hand. That change is the source of truth. It leaves the implementation to this repository, requires the work to follow this repository's OpenSpec and review policy, and requires this repository's `extension-registry` spec to change, because an executable name alone no longer identifies a deployment profile.

This change adopts that plan. It maps the approved decisions onto the projects, build targets and tests of this repository as they are at `d819bac`. It makes no new product decision. Where the code today complicates the approved plan, the design names the place and the question to settle.

## What Changes

The approved narrative stays the guide. Ada installs Morphir on a machine without .NET, selects the F# toolset, compiles a small package and generates F#. Ben already has a .NET runtime and runs the framework-dependent `morphir`. Chen keeps `dotnet tool install Morphir.Tool`. A colleague restores the checked-in toolchain selection in CI, offline after acquisition, and gets the same providers and exact artifact identities.

```text
bootstrap morphirup -> select host + deployment + toolset
                   -> verify and stage artifacts -> activate locked toolchain
                   -> morphir compile -> model -> morphir generate
```

- Four .NET offerings (approved DEP-1): `native-aot`, `self-contained` and `framework-dependent`, each exposing `morphir`, and `dotnet-tool` (the existing `Morphir.Tool` package and its `dotnet-morphir` command). The shared F#-enabled managed composition moves out of the `src/Morphir` executable project, which the AOT entry point keeps and `Morphir.Tool` references today. A new managed entry point gives the two managed `morphir` offerings that composition. The AOT entry point keeps excluding FSharp.Compiler.Service.
- Named release profiles in the Nuke build replace the ad hoc `PublishSingleFile` and `PublishSingleFileUntrimmed` targets and the stale `scripts/publish-single-file*.cs` scripts. Existing release assets keep their names and links. Metadata records their real identity.
- Offering identity and prerequisite reporting in `extension list` and the version report (approved DEP-1, task 2.3). The `host` strings `morphir` and `dotnet-morphir` keep their meaning as command names. A new `offering` value says which of the four offerings runs. The in-process F# frontend becomes a registry entry, so the AOT offering can say why it is unavailable there.
- F# process deliveries (approved FS-1). `Morphir.Frontend.FSharp.Extension` gains a self-contained process archive beside its existing .NET tool. A new, independently versioned F# backend guest reuses `Morphir.CodeGeneration.FSharp`. It needs the `generateModel` wire types that the pinned MEP contract defines and `Morphir.Mep` does not implement yet, and a host-side MEP backend adapter.
- The standalone Native AOT `morphirup` manager (approved UP-1, UP-2, LOCK-1, PATH-1) as a new project, with pure plan and resolution logic and bounded adapters. It reuses the interprocess `TaskLock`, the safe tree publication path and the configuration discovery boundaries. It depends on the .NET extension installation work (extension-model PRD step 3, issue #492) and on an authenticated tool release metadata client, neither of which exists in this repository yet.
- Acquired-release qualification (approved QUAL-1) through the existing E2E, build and conformance suites and the manual and release lanes, not new per-offering pull request jobs.

New contracts (toolset recipe, toolchain request, portable lock, installation receipt and manager result) start at `0.1.0-draft.1` and are defined in finos/morphir (approved group 1). This repository reads and writes them and defines none of their members. Tool release metadata, MEP (`0.2.0-draft.1`, claims `0.1.0-draft.2`) and IR versions keep their own version authorities. Nothing described here is shipped or qualified yet.

Not in this change: the shared contracts and fixed examples (approved group 1, owned by finos/morphir), the join with the separate WASM-host change (approved group 7, a later follow-up), MoonBit and Rust adoption, Linux musl, package-manager integrations and management of an external SDK.

## Capabilities

### New Capabilities

- `dotnet-deployment-profiles`: the four .NET offerings, their prerequisites, shared command behavior, capability availability, release artifact identity and support matrix. Adopted from the approved `deployment/dotnet-profiles`.
- `toolchain-management`: `morphirup` bootstrap, setup plans, artifact resolution, use of the existing acquisition authorities, transactional activation, ownership, lifecycle operations, shell setup and diagnostic results. Adopted from the approved `acquisition/toolchain-management`.
- `project-toolchains`: project requests and exact locks, selection precedence, frozen and offline restore, command dispatch and the split between toolchain state and build outputs. Adopted from the approved `acquisition/project-toolchains`.
- `toolset-setup`: capability-based toolsets, the preserved built-in F# backend, the standalone F# frontend and the independently acquirable F# backend, prerequisites, readiness evidence and cross-offering qualification. Adopted from the approved `extensions/toolset-setup`, without its WASM readiness requirement, which joins with group 7.

This repository keeps a flat spec layout, so the approved nested paths become flat names. The specs carry the approved requirement and scenario text. Where the approved requirement defines a shared document, the spec points at the finos/morphir contract instead of restating it.

### Modified Capabilities

- `extension-registry`: host strings stay command names. Results name the running offering. Entries list the offerings that provide them, state why they are unavailable and state delivery prerequisites. Built-in frontends become registered entries.
- `cli-model-compilation`: the in-process F# frontend ships with all three managed offerings, not only `dotnet-morphir`, and the AOT refusal message names them.

## Tracking

- Source of truth: finos/morphir `openspec/changes/add-deployment-and-acquisition/`, approved 9 November 2026. Its decisions are DEP-1, UP-1, UP-2, FS-1, LOCK-1, PATH-1 and QUAL-1.
- GitHub issue: **TODO (orchestrator): link the owning finos/morphir-dotnet issue here before implementation starts.**
- The owning finos/morphir tracking is pending. This change invents no issue or Beads id.
- Related open issues in this repository: #208 (deployment architecture epic), #212 (its phase 4: deployment and distribution), #264 (AOT tests on platforms said to lack Native AOT, which conflicts with the approved Windows and Linux ARM64 targets), #490, #491 and #492 (wiring `dist`, `tool` and `extension`, which `morphirup` and toolset setup depend on).
- Related active changes: `add-discovery-containment` (discovery boundary and isolation that project selection reuses), `establish-morphir-compatibility-kit` (the MCK suites and adapters that F# readiness and qualification use), `add-fsharp-morphir-analyzers` (FCS version pins shared with the frontend archive). This change edits none of them.
- Baseline: main at `d819bac`. The approved design inspected `4e3321f`. Since then the frontend guest project was renamed to `Morphir.Frontend.FSharp.Extension` (#552), `v0.4.0-alpha.3` was prepared (#555) and single-file executables load `System.Native` by the runtime's name (#556).

## Impact

- Entry points and composition: `src/Morphir` (`Program.cs`, `Morphir.csproj`), `src/Morphir.Tool` (`Program.cs`, `FSharpFrontendScenario.cs`), a new shared composition library and a new managed `morphir` entry point. The public `Morphir.Program` type in the `Morphir` NuGet package moves, which is a breaking change to that package's API and needs maintainer review.
- Registry and reports: `src/Morphir.Extensions/Registry.fs` (`MorphirHost`, `HostIdentity`, `ExtensionCapability`), `src/Morphir.Tooling/Features/Extensions/`, the `--version` path in `src/Morphir/Program.cs`.
- Build and release: `build/Build.cs`, `build/Build.Packaging.cs`, `build/Build.Publishing.cs`, `scripts/publish-single-file*.cs`, `scripts/install-*`, `.github/workflows/deployment.yml`, `manual-aot-test.yml`, `manual-e2e-test.yml`.
- F# deliveries: `src/Morphir.Frontend.FSharp.Extension`, `src/Morphir.Frontend.FSharp/CompilerEdge.fs` (compiler reference selection), `src/Morphir.Mep/Wire.fs`, `src/Morphir.Tooling/Features/Generate/`, a new backend guest project.
- Manager: a new `morphirup` project and its tests, reusing `src/Morphir.Configuration.IO` discovery and `src/Morphir.Tooling/Features/TaskRecords/TaskLock.cs` patterns.
- Tests: `tests/Morphir.Tests/Hosts`, `tests/Morphir.Build.Tests`, `tests/Morphir.E2E.Tests`, `tests/Morphir.Frontend.FSharp.Extension.Tests`, `tests/Morphir.Mep.Tests`, `tests/Morphir.Tooling.Tests`, `tests/Morphir.Mep.AotProbe`, `src/Morphir.Conformance` and `conformance/`, plus new test projects for the backend guest and the manager.
- Docs: `docs/content/docs/getting-started/installation.md`, `docs/content/docs/cli/management.md`, `docs/contributing/aot-trimming-guide.md`, the extension-model PRD and the release notes.
