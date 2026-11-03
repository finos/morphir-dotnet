# Coordinator integration proposals

These are the reviewed shared-edit proposals for issue #465. After package
RED and canonical project creation, the coordinator applied the seven paths
directly in the F# checkout. Integrated solution, PackAll and TestBuild checks
passed. The coordinator subsequently added the covered Fixed changelog entry
and the approved bounded Restore/Compile arguments. The lead remains sole Git
writer. The proposal text below is historical; actual diffs and validation
are recorded in the feature PRD. Preserve concurrent C# additions on integration.

## Solution and build project paths

In `Morphir.slnx`, retain the legacy project and add this adjacent project:

```xml
<Project Path="src\Morphir.SDK.FSharp\Morphir.SDK.FSharp.fsproj" />
```

In `build/Build.cs`, retain `MorphirSDKProject` and add:

```csharp
AbsolutePath MorphirSDKFSharpProject => SourceDirectory / "Morphir.SDK.FSharp" / "Morphir.SDK.FSharp.fsproj";
```

No root SDK, language-version, central package version or global build-property
change is requested. Both runtime test suites keep their existing project
identities and point their project references to the canonical project.

## PackLibs

In `build/Build.Packaging.cs`, insert immediately before the existing
`Packing Morphir.SDK...` block:

```csharp
Serilog.Log.Information("Packing Morphir.SDK.FSharp...");
DotNetPack(s => s
    .SetProject(MorphirSDKFSharpProject)
    .SetConfiguration(Configuration)
    .SetOutputDirectory(OutputDir)
    .SetProperty("Version", versionString));
```

Retain the existing `MorphirSDKProject` packing block for the legacy metapackage.
In the `libraryProjects` array, replace the `"Morphir.SDK"` entry with
`"Morphir.SDK.FSharp"`; preserve every other entry including concurrent C# work.
The existing `ValidateLibraryPackage` requires a runtime DLL and therefore must
not validate the dependency-only legacy package. The F# package-consumer suite
will assert its exact dependency graph and absence of DLL assets. Update the
PackLibs XML summary to mention both canonical and compatibility packages.

No new test project or sequential suite-array entry is proposed: conformance
and package-consumer tests will live in existing `Morphir.SDK.Tests`, already
executed by `build/Build.Testing.cs`. The package checks will run sequentially
inside that suite and use bounded captured child processes. Existing
`Morphir.Build.Tests` wildcard metadata checks do not claim legacy asset-layout
validation; the new suite separately checks both SDK package IDs.

## PRD index

Append this row to the existing table in
`docs/content/contributing/design/prds/_index.md`, preserving other rows:

```markdown
| [Introduce F# SDK package](introduce-fsharp-sdk-package.md) | In Progress | Package compatibility | #465; untouched legacy package/binary captured; package-graph RED next |
```

The baseline evidence is already captured. This row must change as integrated
verification progresses; neither an index row nor a task checkbox proves delivery.

## README

After the offline shared-validator command block in `README.md`'s Developing
section, insert:

```markdown
F# runtime consumers use `Morphir.SDK.FSharp`. Its namespace and runtime assembly
remain `Morphir.SDK`, so existing generated source and runtime type identities
remain valid. The legacy `Morphir.SDK` package depends on the canonical package
and ships no runtime assembly. See the [F# runtime migration guide](src/Morphir.SDK.FSharp/README.md)
for dependency selection and covered behavior corrections.
```

This describes the intended verified result. Integrate it with the implementation,
not as a claim that packaging is already delivered.

## Changelog

Under Unreleased/Added in `CHANGELOG.md`, add:

```markdown
- Canonical `Morphir.SDK.FSharp` runtime package and dependency-only `Morphir.SDK` compatibility package, preserving existing runtime identities, with packaged source/binary checks and the frozen F# conformance adapter ([#465](https://github.com/finos/morphir-dotnet/issues/465))
```

Under Unreleased/Fixed, add after covered RED/GREEN confirms the changes:

```markdown
- F# SDK dictionary union keeps the first dictionary's conflicting value; covered Int arithmetic checks overflow, Decimal division has an optional-result entry point, and decimal output is invariant and canonical. Existing public entry points remain available; see the F# runtime migration notes ([#465](https://github.com/finos/morphir-dotnet/issues/465))
```

If the actual covered correction set differs, revise this text before integration.

## Contribution notice

Append to `NOTICE.txt` after existing notices:

```text
Canonical F# SDK package migration (#465)

This contribution reorganizes the existing FINOS Morphir F# runtime packaging,
adds compatibility checks and corrects covered runtime behavior against the
already attributed shared .NET SDK evidence. It adds no third-party source,
license or restriction. Existing FINOS, Morgan Stanley and upstream evidence
notices and licenses remain applicable and are retained.
```

## Legacy pack-only project implementation

Owned implementation proposal: canonical project compiles the existing runtime
files with `AssemblyName=Morphir.SDK`, `RootNamespace=Morphir.SDK` and the existing
version/key policy. The old project packs a project dependency on the canonical
project with `IncludeBuildOutput=false`. It must override its build work after
SDK imports so it never invokes the compiler or emits an empty runtime assembly.
Package tests must establish this behavior; merely excluding an emitted DLL from
the nupkg is insufficient. No new Microsoft.Build.NoTargets dependency is proposed.

## Retained binary fixture

For repeatable CI binary checks, propose retaining the genuine pre-migration
`LegacyConsumer.dll`, its `.deps.json`/`.runtimeconfig.json` and the 281-line API
baseline under `tests/Morphir.SDK.Tests/Packaging/Baseline/`, together with source
and provenance hashes. These are test inputs captured from the untouched project,
not rebuilt by migration tests. The old package and runtime DLL stay in the
owned C:/Temp evidence directories and are not shipping package assets. New
graphs supply only the runtime and its declared dependencies to a copy of the
retained consumer; the retained binary itself is never changed or recompiled.
