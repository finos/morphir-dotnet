---
title: "Roslyn under Native AOT spike, 2026-11-07"
linkTitle: "Roslyn under Native AOT spike"
description: >
  A Native AOT probe of Microsoft.CodeAnalysis.CSharp, and the decision whether a C# frontend can be a built-in of the AOT `morphir` executable.
---

# Roslyn under Native AOT spike, 2026-11-07

Status: Feasible. A C# frontend built on Roslyn can technically be a built-in in both hosts (`morphir` and `dotnet-morphir`). Building it into `morphir` adds about 31 to 38 MB for every user. Decided 2026-11-07: a C# frontend ships as an optional extension for now, not as a built-in. The conditions below apply if it becomes a built-in later. It must use only `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp`: syntax, `CSharpCompilation`, the semantic model and `IOperation`. It must not use the Workspaces layer, IL emit or analyzer loading. The `morphir` build must accept a fixed baseline of 6 known Roslyn warnings and fail on all other warnings. The fallback is a JIT-only built-in in `dotnet-morphir`.

This spike is the "Roslyn in the AOT host" item in [Spikes before step 2](../../prds/morphir-extension-model.md#spikes-before-step-2) of the Morphir extension model PRD. The PRD rule is: "The language does not decide whether an extension can be a built-in. The dependency set decides: a built-in for the AOT host must pass the AOT checks." The probe sources are in `spikes/extension-model/roslyn-aot/`.

## Reproduction

The probe builds outside the repository. The probe project turns off central package management and lock files, so it also builds in the repository with the same result (checked once).

Environment: macOS 26.6.2, arm64 (Apple M5 Max), .NET SDK 10.0.403, runtime 10.0.14, ILCompiler 10.0.14, RID `osx-arm64`. Packages: `Microsoft.CodeAnalysis.CSharp` 5.9.0 (Roslyn commit `35d9211b`), `Basic.Reference.Assemblies.Net100` 1.8.14.

```bash
export DOTNET_ROOT="$HOME/.local/share/mise/installs/dotnet/10.0.403"
export PATH="$DOTNET_ROOT:$PATH"
WORK="$(mktemp -d)"
cp -R spikes/extension-model/roslyn-aot "$WORK/"
cd "$WORK/roslyn-aot"

# AOT variants. ProbeVariant: frontend (default), extensions, full, workspaces.
for v in frontend extensions full workspaces; do
  rm -rf obj bin
  dotnet publish -c Release -r osx-arm64 -p:ProbeVariant=$v -o out/$v > $v.log 2>&1
  echo "$v: $(grep 'warning' $v.log | sed -E 's/ \[.*//' | sort -u | wc -l) warnings, $(stat -f %z out/$v/RoslynAotProbe) bytes"
  grep 'warning' $v.log | sed -E 's/ \[.*//' | sort -u | grep -oE 'warning IL[0-9]+' | sort | uniq -c
done
rm -rf obj bin && dotnet publish -c Release -r osx-arm64 -p:ProbeRefs=sdk -o out/frontend-sdk
rm -rf obj bin && dotnet publish -c Release -r osx-arm64 -p:RoslynSingleWarn=true -o out/singlewarn

# JIT comparison builds.
rm -rf obj bin && dotnet build -c Release -o out/jit
rm -rf obj bin && dotnet publish -c Release -r osx-arm64 --self-contained false \
  -p:PublishAot=false -p:PublishReadyToRun=true -o out/jit-r2r

# Correct results: the AOT output must be the same as the JIT output.
dotnet out/jit/RoslynAotProbe.dll walk > jit.txt
out/frontend/RoslynAotProbe walk > aot.txt
diff jit.txt aot.txt
out/frontend-sdk/RoslynAotProbe walk --refs sdk | diff jit.txt -
out/frontend/RoslynAotProbe diag
out/frontend/RoslynAotProbe walk --refs tpa
out/full/RoslynAotProbe walk --refs location
out/extensions/RoslynAotProbe emit
out/full/RoslynAotProbe emit
out/extensions/RoslynAotProbe analyzer
out/extensions/RoslynAotProbe generator
out/full/RoslynAotProbe analyzer-file \
  "$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref/10.0.14/analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll"
out/full/RoslynAotProbe analyzer-file \
  "$DOTNET_ROOT/sdk/10.0.403/Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll"
out/workspaces/RoslynAotProbe workspace
```

Start time is the median wall time of 20 runs of `walk`, after one warm-up run, measured from a parent process with `time.perf_counter`. The `--scale 200` runs use 5 runs. Peak memory is the "maximum resident set size" from `/usr/bin/time -l`.

The probe does what a frontend does to make Morphir IR. It parses a small C# domain (a record struct, an enum, records, an abstract record with nested sealed records as a union, a static class with functions, generics, nullable types, LINQ and a switch expression with patterns). It makes a `CSharpCompilation` with nullable enabled. Then it uses the semantic model to walk each type declaration and member, and it resolves each type to a fully qualified symbol. For each method body, it gets the `IOperation` tree and resolves calls, patterns and binary operators to symbols. It does not emit IL in the `frontend` variant.

The variants put each feature set in its own binary, so each publish shows the warnings of one feature set:

| Variant | Contents |
| --- | --- |
| `frontend` | `walk` and `diag`. What a C# frontend needs. |
| `extensions` | `frontend`, plus emit to a memory stream, one in-process `DiagnosticAnalyzer` and one in-process `IIncrementalGenerator`. |
| `full` | `extensions`, plus `Assembly.Load` of the emitted bytes, `AnalyzerFileReference` loading from a `.dll`, and `--refs location`. |
| `workspaces` | `frontend`, plus `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0: an `AdhocWorkspace` and `Formatter`. |

Reference modes for `walk --refs`:

| Mode | Source of the reference assemblies |
| --- | --- |
| `basic` | `Basic.Reference.Assemblies.Net100`, which embeds the .NET 10 reference assemblies as resources. |
| `sdk` | The targeting pack of the installed SDK: `$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref/<version>/ref/net10.0`. |
| `tpa` | `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")`. |
| `location` | The folder of `typeof(object).Assembly.Location`. |

## Results

### Warnings

All counts are distinct warnings, with `TrimmerSingleWarn=false`. The probe sets `IsAotCompatible`, `EnableTrimAnalyzer`, `EnableAotAnalyzer` and `EnableSingleFileAnalyzer`. The build step (Roslyn analyzers on the probe code) gives no IL warnings. All warnings come from ILC at publish.

| Variant | Total | Codes | Assemblies |
| --- | --- | --- | --- |
| `frontend` | 6 | 5 IL2091, 1 IL3000 | `Microsoft.CodeAnalysis` only |
| `frontend`, `--refs sdk` build | 6 | 5 IL2091, 1 IL3000 | `Microsoft.CodeAnalysis` only |
| `extensions` | 13 | `frontend` + 6 IL2050, 1 IL2072 | `Microsoft.CodeAnalysis` only |
| `full` | 20 | `extensions` + 5 IL2026, 1 IL2072, 1 IL3000 | `Microsoft.CodeAnalysis` (3), probe code (4, intentional) |
| `workspaces` | 67 | 14 IL2091, 9 IL3050, 7 IL2026, 6 IL2050, 6 IL2060, 6 IL2111, 5 IL2072, 4 IL2067, 3 IL2055, 2 IL2070, 2 IL2090, 1 IL2075, 1 IL2080, 1 IL3000 | `Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.Workspaces`, `System.Composition.*` (49) |

`Microsoft.CodeAnalysis.CSharp` gives no warnings in any variant. `Basic.Reference.Assemblies.Net100` gives no warnings.

The 6 `frontend` warnings:

| Code | Site (Roslyn source) | What it is |
| --- | --- | --- |
| IL2091 | `Roslyn.Utilities.RoslynLazyInitializer.EnsureInitialized<T>(ref T)` (`InternalUtilities/RoslynLazyInitializer.cs`, line 15) | It calls `LazyInitializer.EnsureInitialized<T>(ref T)`, which has a `PublicParameterlessConstructor` annotation on `T`. Roslyn does not copy the annotation. |
| IL2091 (4) | `Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.GetPooledCreateValueCallback` and `CreateValueCallbackWithBoundArgument<,,>` (type, constructor, `Bind`) (`Dependencies/PooledObjects/PooledDelegates.cs`) | They use the `ConditionalWeakTable<,>.CreateValueCallback` delegate type, whose `TValue` has a `PublicParameterlessConstructor` annotation. Roslyn does not copy the annotation. |
| IL3000 | `Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type)` (`CommandLine/CommonCompiler.cs`, line 175) | It reads `Assembly.Location`. The code already changes an empty value to `"<unknown>"`. |

These are annotation-flow warnings. They do not show a reflection call on the frontend path. The extra warnings in `extensions` are in `Microsoft.DiaSymReader.SymUnmanagedFactory.CreateObject`, the COM-based native (Windows) PDB writer that the emit path brings in. The Roslyn warnings in `full` are in `AnalyzerFileReference`, which loads analyzer assemblies with reflection.

With `TrimmerSingleWarn=true` set only on the `Microsoft.CodeAnalysis*` assemblies (`-p:RoslynSingleWarn=true`), the `frontend` publish gives 2 warnings: one `IL2104` ("Assembly 'Microsoft.CodeAnalysis' produced trim warnings") and the `IL3000` above. All other assemblies keep one warning for each site.

### Runs and correct results

| Check | JIT | Native AOT |
| --- | --- | --- |
| `walk --refs basic` | 0 errors, 167 references | Same output as JIT, with one difference (see the next paragraph) |
| `walk --refs sdk` | 0 errors, 167 references | Same as `basic` |
| `walk --refs tpa` | 0 errors, 177 references | Fails: `TRUSTED_PLATFORM_ASSEMBLIES` is null |
| `walk --refs location` | 0 errors, 165 references | Fails: `Assembly.Location` is empty |
| `diag` (an error in the source) | `error CS0103: The name 'missing' does not exist in the current context` | Same text. Roslyn diagnostic messages come from resources, and they work. |
| `emit` to a memory stream | 13,312 bytes | 13,312 bytes |
| `Assembly.Load` of the emitted bytes | 11 types | `PlatformNotSupportedException` |
| In-process `DiagnosticAnalyzer` (`CompilationWithAnalyzers`) | 1 diagnostic | Same |
| In-process `IIncrementalGenerator` (`CSharpGeneratorDriver`) | 1 generated tree, 0 errors | Same |
| `AnalyzerFileReference`, `System.Text.Json.SourceGeneration.dll` | 1 generator | 0 generators, `UnableToLoadAnalyzer`: `PlatformNotSupportedException` |
| `AnalyzerFileReference`, `Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll` | 43 analyzers | 0 analyzers, `UnableToLoadAnalyzer`: `PlatformNotSupportedException` |
| `AdhocWorkspace` and `Formatter` (`workspaces` variant) | 0 errors, formatted | Fails: `System.Composition.Hosting.CompositionFailedException: Exported contract type 'IAsynchronousOperationListenerProvider' is not assignable from part 'AsynchronousOperationListenerProvider'.` |

The one difference in `walk`: the probe prints `OperationKind` names with `ToString()`. JIT prints `Binary` and AOT prints `BinaryOperator`. Both names have the value `0x20`. `BinaryOperator` is an old alias with `[EditorBrowsable(Never)]` ([source](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Generated/OperationKind.Generated.cs)). `Enum.ToString()` can give either name when two names have the same value. All symbols, types, patterns, calls and operator kinds are the same.

### Size

Sizes are the published `osx-arm64` executable, without the `.dSYM` folder.

| Build | Bytes | MB |
| --- | --- | --- |
| Hello world, Native AOT (floor) | 1,102,744 | 1.1 |
| `morphir` today (`artifacts/executables/osx-arm64/morphir`, for comparison) | 18,111,224 | 18.1 |
| `frontend`, `basic` references | 38,990,640 | 39.0 |
| `frontend`, `basic`, `OptimizationPreference=Size` | 38,163,152 | 38.2 |
| `frontend`, `sdk` references (no embedded references) | 32,682,688 | 32.7 |
| `frontend`, `sdk`, `OptimizationPreference=Size`, `StackTraceSupport=false` | 29,714,144 | 29.7 |
| `extensions` | 42,971,152 | 43.0 |
| `full` | 43,153,280 | 43.2 |
| `workspaces` | 44,994,272 | 45.0 |

Roslyn (syntax, binding, `IOperation` and the framework code that they use) adds about 31.6 MB of native code. The embedded reference assemblies add 6.3 MB. A `morphir` binary with a C# frontend built-in is about 50 to 56 MB. The upper value is the sum, and some framework code is shared with what `morphir` already has. This is past the 20 MB guideline in `.agents/aot-optimization.md`. The PRD says that size alone does not reject a native dependency, but each user pays it, also users who do not use C#.

For comparison, the framework-dependent JIT output is 22 MB on disk. The IL assemblies are `Microsoft.CodeAnalysis.dll` 3.1 MB, `Microsoft.CodeAnalysis.CSharp.dll` 7.1 MB, and `Basic.Reference.Assemblies.Net100.dll` 6.3 MB. ReadyToRun makes the two Roslyn assemblies 9.4 MB and 22.0 MB.

### Start time and run time

| Run | Median | Peak memory |
| --- | --- | --- |
| Hello world, AOT | 3.3 ms | |
| `walk`, AOT, `basic` | 21.4 ms | 58.5 MB |
| `walk`, AOT, `sdk` | 21.2 ms | |
| `walk`, AOT, `sdk`, size-optimized | 21.4 ms | |
| `walk`, JIT (IL only) | 379.9 ms | 112.4 MB |
| `walk`, JIT, `sdk` | 370.2 ms | |
| `walk`, JIT, ReadyToRun | 182.7 ms | 128.1 MB |
| `walk --scale 200` (200 files), AOT | 268.2 ms | |
| `walk --scale 200`, JIT | 1,080.5 ms | |
| `walk --scale 200`, JIT, ReadyToRun | 701.2 ms | |

The AOT host runs the frontend walk about 18 times faster than JIT for one file and about 4 times faster for 200 files. Reference loading is 6 to 23 ms in all modes.

### Upstream status

- No Roslyn package declares trim or AOT support. `Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` 5.9.0 have no `IsTrimmable` or `IsAotCompatible` assembly metadata.
- [dotnet/roslyn#68154](https://github.com/dotnet/roslyn/issues/68154), "Microsoft.CodeAnalysis.* trimming support", is open since 2023-05-10 (Area-Compilers, Feature Request).
- [dotnet/roslyn#66519](https://github.com/dotnet/roslyn/pull/66519), "Annotate MS.CA and MS.CA.C# for trimming", is an open draft since 2023-01-22 (milestone 18.4, last update 2026-04-03). Its description says: "Almost all of MS.CA and MS.CA.CSharp are already trimming compatible, it is only native PDB writing which has problems". The probe results agree.
- The PR author wrote on 2026-03-13 ([comment](https://github.com/dotnet/roslyn/pull/66519#issuecomment-4052439615)) that the PR waits for [dotnet/symreader#340](https://github.com/dotnet/symreader/pull/340), "Enable AOT and platform compatibility analyzers". It merged on 2026-03-13.
- Precedent: the CsWinRT 3.0 projection generator, `cswinrtprojectiongen`, is a Native AOT tool that uses `Microsoft.CodeAnalysis.CSharp`. Its project file sets `<NoWarn>$(NoWarn);IL2091;IL2050;IL2072;IL2075;IL2026;</NoWarn>` with the comment "Temporarily suppress warnings for 'Microsoft.CodeAnalysis.CSharp'" ([project file at `848515f`](https://github.com/microsoft/CsWinRT/blob/848515ff48b817d34056910f6d9b1c2d6f1efefd/src/WinRT.Projection.Generator/WinRT.Projection.Generator.csproj#L41-L42), and the [comment on #66519](https://github.com/dotnet/roslyn/pull/66519#issuecomment-3963213980) from 2026-02-26).

## Decision

A C# frontend built on Roslyn passes the AOT checks, with conditions. Its delivery in the PRD is "Built-in, both hosts".

1. **Dependency set.** Use `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp` only. Do not use `Microsoft.CodeAnalysis.Workspaces`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.CodeAnalysis.Scripting` or `System.Composition`. The Workspaces layer fails at run time under AOT, and it adds 61 warnings.
2. **Warning baseline, not a global suppression.** The `morphir` publish accepts exactly the 6 warnings above (or, with `TrimmerSingleWarn` on the Roslyn assemblies only, one `IL2104` for `Microsoft.CodeAnalysis` and the one `IL3000`). Any other warning fails the build. Do not copy the CsWinRT `NoWarn` list: a project-wide `NoWarn` for IL2091, IL2026 or IL2072 also hides new warnings in Morphir code.
3. **No emit, no assembly loading.** The frontend does not call `Emit`, `Assembly.Load` or `AnalyzerFileReference`.
4. **References without the runtime folder.** The AOT host has no `TRUSTED_PLATFORM_ASSEMBLIES` and no `Assembly.Location`. The frontend gets framework references from embedded reference assemblies (`Basic.Reference.Assemblies.Net100`, 6.3 MB) or from the targeting pack of an installed SDK.
5. **Recheck on each Roslyn update.** A new Roslyn version can add warnings. The baseline check finds them. If [dotnet/roslyn#66519](https://github.com/dotnet/roslyn/pull/66519) merges, the baseline can become empty.

**Size is the open cost.** The built-in takes `morphir` from 18.1 MB to about 50 to 56 MB. If the maintainers do not accept this size, use the fallback.

**Fallback: JIT-only built-in in `dotnet-morphir`.** The same code runs in the JIT host with no warnings to manage. It costs 180 to 380 ms of start time (ReadyToRun or IL) and no size in `morphir`. Then the AOT host follows the selection rule: it fails with "not available in this build" and names `dotnet-morphir` or a process guest. A `process` guest is not necessary for the frontend itself. Use a `process` guest only for work that must load user assemblies (see the next section).

## Consequences for a C# frontend

- Build the frontend on `CSharpCompilation`, `SemanticModel`, `ISymbol` and `IOperation`. These worked under AOT with the same results as JIT.
- Do not use `Enum.ToString()` on Roslyn enums (`OperationKind`, `SyntaxKind`) to make IR or diagnostic codes. Some values have two names, and AOT and JIT can give different names. Use the enum values in a `switch`.
- Roslyn diagnostic messages work under AOT. The frontend can pass C# compile errors to the user as MEP diagnostics.
- Framework references: embed `Basic.Reference.Assemblies.Net100` to work without an SDK, or read the SDK targeting pack to save 6.3 MB. This is a builder's call. Package references of the user project (for example `Morphir.SDK.CSharp`) need a separate source, because the frontend cannot use `MSBuildWorkspace`. Options: read `obj/project.assets.json`, or run `dotnet msbuild -getItem:ReferencePath` as a child process.
- The AOT host cannot load analyzers or source generators from `.dll` files. If a user project uses a source generator, the AOT frontend sees a compilation without the generated code. The options are: read the generated files from `obj/` when the project sets `EmitCompilerGeneratedFiles`, run the frontend in `dotnet-morphir`, or run it as a `process` guest. In-process analyzers and generators that are part of `morphir` work.
- Third-party C# analyzers that are Roslyn `DiagnosticAnalyzer` assemblies cannot run in the AOT host. They stay `process` guests, as the PRD already says.
- Update the AOT closure check in the PRD testing approach: allow `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp`, and reject `Microsoft.CodeAnalysis.Workspaces*`, `Microsoft.CodeAnalysis.Scripting` and `System.Composition*` in the `morphir` executable.
- The AOT frontend is fast: about 21 ms for a small model and 268 ms for 200 files, with 58.5 MB peak memory. If the frontend is JIT-only, consider ReadyToRun for the Roslyn assemblies in `dotnet-morphir`. It halves the start time.
