# Proposal

## Why

The user approved the finos/morphir OpenSpec change `add-deployment-and-acquisition` on 9 November 2026. It defines how people acquire Morphir .NET and its F# tools without assembling runtimes, extension packages and configuration by hand. That change is the source of truth. It leaves the implementation to this repository, requires the work to follow this repository's OpenSpec and review policy, and requires this repository's `extension-registry` spec to change, because an executable name alone no longer identifies a deployment profile.

This change adopts that plan. It maps the approved decisions onto the projects, build targets and tests of this repository as they are at `d819bac`. It makes no decision about the approved journey, decisions or examples. It does make a few repository-level choices, listed under "Repository-level choices for review" below. Where the code today complicates the approved plan, the design names the place and the question to settle.

## What Changes

The approved narrative stays the guide. Ada installs Morphir on a machine without .NET, selects the F# toolset, compiles a small package and generates F#. Ben already has a .NET runtime and runs the framework-dependent `morphir`. Chen keeps `dotnet tool install Morphir.Tool`. A colleague restores the checked-in toolchain selection in CI, offline after acquisition, and gets the same providers and exact artifact identities.

```text
bootstrap morphirup -> select host + deployment + toolset
                   -> verify and stage artifacts -> activate locked toolchain
                   -> morphir compile -> model -> morphir generate
```

- Four .NET offerings (approved DEP-1): `native-aot`, `self-contained` and `framework-dependent`, each exposing `morphir`, and `dotnet-tool` (the existing `Morphir.Tool` package and its `dotnet-morphir` command). Today `src/Morphir` holds the shared composition without F#, and the F# part (`FSharpFrontendScenario`) lives in `src/Morphir.Tool`. The shared composition moves into a library, and the F# part into a managed composition library that the managed `morphir` entry point and `Morphir.Tool` share. `Morphir.Program` stays in `src/Morphir` with the same public signatures and delegates to the library. The AOT entry point keeps excluding FSharp.Compiler.Service.
- Named release profiles in the Nuke build replace the ad hoc `PublishSingleFile` and `PublishSingleFileUntrimmed` targets and the stale `scripts/publish-single-file*.cs` scripts. Existing release assets keep their names and links. Metadata records their real identity.
- Deployment identity and prerequisite reporting in `extension list` and the version report (approved DEP-1, task 2.3). The `host` strings `morphir` and `dotnet-morphir` keep their meaning as command names. A new `deployment` member says which of the four offerings runs, the wire name the approved documents use for this concept.
- F# process deliveries (approved FS-1). `Morphir.Frontend.FSharp.Extension` gains a self-contained process archive beside its existing .NET tool. A new, independently versioned F# backend guest reuses `Morphir.CodeGeneration.FSharp`. It needs the `generateModel` wire types that the pinned MEP contract defines and `Morphir.Mep` does not implement yet, and a host-side MEP backend adapter.
- The standalone Native AOT `morphirup` manager (approved UP-1, UP-2, LOCK-1, PATH-1) as a new project, with pure plan and resolution logic and bounded adapters. It reuses the interprocess `TaskLock` pattern, the safe tree publication path and the configuration discovery boundaries. It depends on the .NET extension installer (extension-model PRD step 3, built inside this effort as its own change, open as #567; #492 wires the existing handlers and is not step 3) and on an authenticated tool release metadata client, neither of which exists in this repository yet.
- Acquired-release qualification (approved QUAL-1) through the compatibility kit runner in `src/Morphir.Conformance` and the existing E2E, build and conformance suites in the manual and release lanes, not new per-offering pull request jobs.

New contracts (toolset recipe, toolchain request, portable lock, installation receipt and manager result) start at `0.1.0-draft.1` and are defined in finos/morphir (approved group 1). This repository reads and writes them and defines none of their members. Tool release metadata, MEP (`0.2.0-draft.1`, claims `0.1.0-draft.2`) and IR versions keep their own version authorities. Nothing described here is shipped or qualified yet.

Not in this change: the shared contracts and fixed examples (approved group 1, owned by finos/morphir), the join with the separate WASM-host change (approved group 7, a later follow-up), MoonBit and Rust adoption, Linux musl, package-manager integrations and management of an external SDK.

### Repository-level choices for review

These go beyond the approved text. Each follows approved language. The maintainer approved them with this change on 9 November 2026, so they are no longer open for review; a later change can still revise any of them without touching the approved plan.

- The registry reports the running offering in a top-level `deployment` member. Each entry lists its `deployments` in a fixed order, an unavailable entry has a `reason`, and the result and entries state their prerequisites in separate members. The approved text asks for "capability differences" and to "state why a requested delivery is unavailable"; these members are how this repository does that.
- The in-process F# frontend becomes a built-in registry entry with the id `morphir-dotnet.fsharp`, the frontend identity the compile report already shows. Without it the AOT offering has nothing to explain as unavailable. The approved change does not ask for frontend entries or name this id.
- Directed by the maintainer on 10 November 2026, after the choices above: every `morphirup` command that creates, updates, repairs or removes persistent state has `--dry-run`, which takes the command's own planning and validation path, makes no persistent change, and reports blocked actions accurately. In the plan-only build, `setup --dry-run` is `setup --plan` under another name; `doctor` only reads, and a future `doctor --fix` comes with its dry run.
- `cli-model-compilation` changes. Its requirement "The in-process F# frontend ships with the dotnet tool" and its refusal message would otherwise contradict the managed `morphir` offerings, so the requirement is renamed and modified.

## Capabilities

### New Capabilities

- `dotnet-deployment-profiles`: the four .NET offerings, their prerequisites, shared command behavior, capability availability, release artifact identity and support matrix. Adopted from the approved `deployment/dotnet-profiles`.
- `toolchain-management`: `morphirup` bootstrap, setup plans, artifact resolution, use of the existing acquisition authorities, transactional activation, ownership, lifecycle operations, shell setup and diagnostic results. Adopted from the approved `acquisition/toolchain-management`.
- `project-toolchains`: project requests and exact locks, selection precedence, frozen and offline restore, command dispatch and the split between toolchain state and build outputs. Adopted from the approved `acquisition/project-toolchains`.
- `toolset-setup`: capability-based toolsets, the preserved built-in F# backend, the standalone F# frontend and the independently acquirable F# backend, prerequisites, readiness evidence, WASM readiness as a refusal-only guard, and cross-offering qualification. Adopted from the approved `extensions/toolset-setup`.

This repository keeps a flat spec layout, so the approved nested paths become flat names. The specs carry the approved requirement and scenario text verbatim. Two added requirements, marked "Adoption-specific", say that this repository defines no members of the shared documents. A third, "Dry runs for state changes" in `toolchain-management`, records the maintainer's rule of 10 November 2026 that every `morphirup` command that changes persistent state has `--dry-run` on the same planning and validation path.

### Modified Capabilities

- `extension-registry`: host strings stay command names. Results name the running offering in `deployment`. Entries list their `deployments`, state why they are unavailable and state delivery prerequisites. Built-in frontends become registered entries. The JSON result's member list is no longer exhaustive.
- `cli-model-compilation`: the in-process F# frontend ships with all three managed offerings, not only `dotnet-morphir`, and the AOT refusal message names them. The requirement is renamed to match.

## Tracking

- Source of truth: finos/morphir `openspec/changes/add-deployment-and-acquisition/`, approved 9 November 2026, delivered in finos/morphir#1147. Its decisions are DEP-1, UP-1, UP-2, FS-1, LOCK-1, PATH-1 and QUAL-1.
- GitHub issue: [finos/morphir-dotnet#558](https://github.com/finos/morphir-dotnet/issues/558). The owning finos/morphir issue is finos/morphir#1146.
- The Beads link is still pending. This change invents no Beads id.
- Related open issues in this repository: #208 (deployment architecture epic), #212 (its phase 4: deployment and distribution), #264 (AOT tests on platforms said to lack Native AOT, which conflicts with the approved Windows and Linux ARM64 targets), #490, #491 and #492 (wiring the existing `dist`, `tool` and `extension` handlers; none of them is the PRD step 3 installer).
- Related active changes: `add-discovery-containment` (discovery boundary and isolation that project selection reuses), `establish-morphir-compatibility-kit` (the compatibility kit runner, adapters and corpus that tasks 3.2 and 6.1 use; where their new adapter and cases belong is review question 5), `add-fsharp-morphir-analyzers` (FCS version pins shared with the frontend archive). This change edits none of them.
- Baseline: main at `d819bac`. The approved design inspected `4e3321f`. Since then the frontend guest project was renamed to `Morphir.Frontend.FSharp.Extension` (#552), `v0.4.0-alpha.3` was prepared (#555) and single-file executables load `System.Native` by the runtime's name (#556).

## Impact

- Entry points and composition: `src/Morphir` (`Program.cs`, `Morphir.csproj`), `src/Morphir.Tool` (`Program.cs`, `FSharpFrontendScenario.cs`), a shared composition in `Morphir.Tooling`, a new managed composition library and a new managed `morphir` entry point. `Morphir.Program` keeps its public signatures and delegates, so the `Morphir` NuGet package keeps its API. If that arrangement proves infeasible, moving the type is a breaking API change that needs maintainer review first.
- Registry and reports: `src/Morphir.Extensions/Registry.fs` (`MorphirHost`, `HostIdentity`, `ExtensionCapability`), `src/Morphir.Tooling/Features/Extensions/`, the `--version` path.
- Build and release: `build/Build.cs`, `build/Build.Packaging.cs`, `build/Build.Publishing.cs`, `scripts/publish-single-file*.cs`, `scripts/install-*`, `.github/workflows/deployment.yml`, `manual-aot-test.yml`, `manual-e2e-test.yml`.
- F# deliveries: `src/Morphir.Frontend.FSharp.Extension`, `src/Morphir.Frontend.FSharp/CompilerEdge.fs` (compiler reference selection), `src/Morphir.Mep/Wire.fs`, `src/Morphir.Tooling/Features/Generate/`, a new backend guest project.
- Manager: a new `morphirup` project and its tests, reusing `src/Morphir.Configuration.IO` discovery and `src/Morphir.Tooling/Features/TaskRecords/TaskLock.cs` patterns.
- Tests: `tests/Morphir.Tests/Hosts`, a test project for the managed entry point, `tests/Morphir.Build.Tests`, `tests/Morphir.E2E.Tests`, `tests/Morphir.Frontend.FSharp.Extension.Tests`, `tests/Morphir.Mep.Tests`, `tests/Morphir.Tooling.Tests`, `tests/Morphir.Mep.AotProbe`, the backend adapter and fixed F# cases for the compatibility kit in `src/Morphir.Conformance` and `conformance/`, plus new test projects for the backend guest and the manager.
- Docs: `docs/content/docs/getting-started/installation.md`, `docs/content/docs/cli/management.md`, `docs/contributing/aot-trimming-guide.md`, the extension-model PRD and the release notes.
