---
title: "C# language support spike, 2026-11-15"
linkTitle: "C# language support spike"
description: >
  Probes of Roslyn under Native AOT, of C# 15 unions and closed types, of a source generator with an F# core, and of the size of the Native AOT host, for the C# language support PRD.
---

# C# language support spike, 2026-11-15

Status: Feasible. The decisions are in the [C# language support PRD](../prds/csharp-language-support.md). This report is step 0 of that PRD: it records the probes and the steps to run them again. The probe sources are in `spikes/csharp-language-support/`.

Three items of step 0 are not done; see [Not tested](#not-tested).

## Results

| Question | Result | PRD decision |
| --- | --- | --- |
| Does C# output with `SyntaxFactory` run under Native AOT? | Yes. The output is the same as with JIT. | D1 |
| Does an analyzer run in process under Native AOT? | Yes. The same 7 diagnostics as with JIT. | D1, D5 |
| Can flow analysis classify a loop? | Yes, 10 of 10 loops, the same under Native AOT. | D6 |
| How much does Roslyn add to the Native AOT `morphir`? | 28.2 MB (47.9 MB to 76.0 MB) on `osx-arm64`. The publish gate passes. | D13 |
| Do `closed record` and `union` give exhaustive matches? | Yes. A missing arm is `CS8509`. C# 14 rejects the two forms. | D7 |
| Do they work for a target below `net11.0`? | Yes, with five `internal` polyfill types, for `net8.0`, `netstandard2.0` and `net10.0`. | D12 |
| Can a frontend read a `union`? | Yes. Roslyn has the cases, the conversion and the patterns. | D15 |
| How do plain records and plain unions compare? | As the table in [Plain equality](#plain-equality) shows. | D8 |
| Does a source generator with an F# core load? | Yes, in the compilers of SDK 10.0.100, 10.0.403 and 11.0.100, when it is compiled against the oldest Roslyn. | D10 |

## Environment

macOS 26.6.2, arm64 (Apple M5 Max). .NET SDK 11.0.100, runtimes 11.0.0 and 10.0.14, ILCompiler 11.0.0, RID `osx-arm64`. `Microsoft.CodeAnalysis.CSharp` 5.12.0. The source generator probe also uses SDK 10.0.100 (Roslyn 5.0.0) and SDK 10.0.403 (Roslyn 5.9).

All commands run from `spikes/csharp-language-support/`, unless a step says a different folder. On a machine where `dotnet` is a version manager shim, set `DOTNET_ROOT` to the folder of the real `dotnet` for the steps that run outside the repository.

```bash
REF="$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0"   # the reference assemblies that the probes bind against
```

## Roslyn under Native AOT

Each of the three probes prints a text. `expected-output.txt` in each folder is the output with JIT on 2026-11-15.

```bash
for p in codegen analyzer loop; do
  (cd roslyn-$p && dotnet run -c Release -- "$REF" | diff - expected-output.txt && echo "roslyn-$p: JIT output is equal")
done

# Native AOT. The code generation probe has a second variant that also binds its output.
(cd roslyn-codegen  && dotnet publish -c Release -r osx-arm64 -o out/gen                     && out/gen/RoslynCodegenProbe "$REF"  | diff - expected-output.txt)
(cd roslyn-codegen  && dotnet publish -c Release -r osx-arm64 -p:ProbeBind=true -o out/bind  && out/bind/RoslynCodegenProbe "$REF" | diff - expected-output.txt)
(cd roslyn-analyzer && dotnet publish -c Release -r osx-arm64 -o out/aot                     && out/aot/RoslynAnalyzerProbe "$REF" | diff - expected-output.txt)
(cd roslyn-loop     && dotnet publish -c Release -r osx-arm64 -o out/aot                     && out/aot/RoslynLoopProbe "$REF"     | diff - expected-output.txt)
```

| Probe | IL warnings | Size | Output |
| --- | --- | --- | --- |
| Generate with `SyntaxFactory` and parse back | 0 | 12.0 MB (12,037,264 bytes) | Same as JIT |
| Generate, parse and bind | 4 `IL2091` | 32.1 MB (32,110,736 bytes) | Same as JIT |
| One admission analyzer in process, 4 rules | 4 `IL2091` | 34.6 MB (34,613,664 bytes) | Same 7 diagnostics as JIT |
| Loop classification, 10 loops | 4 `IL2091` | 33.2 MB (33,171,760 bytes) | Same as JIT |

The probes use only `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp`. They do not use the Workspaces layer, which fails under Native AOT (see the [Roslyn under Native AOT spike](extension-model/roslyn-aot.md)).

The loop probe gives this classification.

```text
Total           ForEach  => List.foldl
Notionals       ForEach  => List.map
Large           ForEach  => List.filter
LargeNotionals  ForEach  => List.filterMap
SumAndCount     ForEach  => List.foldl (state is a tuple of 2)
Factorial       While    => LetRecursion (parameters: acc,n)
FirstIndexOver  For      => LetRecursion (parameters: i; exits: 1 return)
Counted         ForEach  => REFUSE (writes counter)
UntilNegative   ForEach  => LetRecursion (parameters: total; exits: 1 break)
Fill            For      => REFUSE (writes array element)
```

## The size of the Native AOT host

The probe adds Roslyn and a small frontend and backend path to `src/Morphir` for one publish. Run these steps from the repository root. Do not commit between `apply.py` and `revert.sh`.

```bash
H=spikes/csharp-language-support/host-size
export MORPHIR_SPIKE_OUT="$(mktemp -d)"
$H/measure.sh base       # the host as it is
python3 $H/apply.py      # adds RoslynProbe.cs, one line in Program.cs and one package reference
$H/measure.sh roslyn     # the host with Roslyn; also runs the probe in the host
$H/revert.sh             # restores src/Morphir
```

| Build | Size | `--version` start |
| --- | --- | --- |
| `morphir` today | 47.9 MB (47,893,064 bytes) | 12.8 ms |
| `morphir` with Roslyn | 76.0 MB (76,048,280 bytes) | 16.0 ms |

The start time is the median of 15 runs. The first measurement of the day gave 12.5 ms and 15.2 ms with the same sizes.

The publish gate of `src/Morphir/Morphir.csproj` passes in the two builds. The link step reports 2 `IL2104` and 1 `IL3053` for the host today, and 3 `IL2104` and 1 `IL3053` with Roslyn: Roslyn adds one `IL2104`, for `Microsoft.CodeAnalysis`. The log of the first build also has 9 `IL2026` and 15 `IL3050` from the compile of the projects; the second build did not compile those projects again.

In the host with Roslyn, the probe parses and binds C# 15 source, runs one analyzer, runs data-flow and control-flow analysis on two loops, and writes a record with `SyntaxFactory`. It reports 0 compile errors and the expected diagnostic.

The embedded reference assemblies are not in these numbers: the probe reads them from the SDK folder. The spike of 2026-11-07 measured about 6.3 MB for them.

## C# 15: closed types and unions

```bash
(cd cs15/language && dotnet run -c Release)                          # 20, 3, Tom
(cd cs15/language && dotnet build -c Release -p:LangVersion=14)      # fails: C# 14 does not have the two forms
(cd cs15/equality && dotnet run -c Release)                          # a record with its own equality members
(cd cs15/api && dotnet run -c Release -- ../language/Program.cs "$REF")   # what Roslyn has for the two forms
```

- `closed record` and `union` compile with SDK 11.0.100 and the default language version of `net11.0`.
- A `switch` with no discard arm is exhaustive. A missing arm gives `CS8509`.
- Roslyn has `ITypeSymbol.IsClosed`, `IsUnion`, `UnionCaseTypes`, `SyntaxFactory.UnionDeclaration` and `SyntaxKind.ClosedKeyword`.
- The `api` probe compiles its input with no implicit `using` lines, so it reports 3 errors for `Console`. They do not change the symbol list.

### Lower targets

```bash
(cd lower-tfm && dotnet build -c Release)                # net8.0 and netstandard2.0
(cd cross-assembly/App && dotnet run -c Release)         # a net10.0 library and a net10.0 application
```

With SDK 11.0.100 and `LangVersion` 15, the two forms compile for `net8.0` and `netstandard2.0` when the assembly has the `internal` polyfill types of `Polyfill.cs`. In `cross-assembly`, the library and the application each have their own polyfill types. The build has 0 warnings, the `switch` on the union and on the closed record of the library is exhaustive in the application, and the program runs on .NET 10.0.14. When an arm is removed, the compiler reports `CS8509`.

### Unions for a frontend

```bash
cd union/api
dotnet build -c Release
dotnet run -c Release --no-build -- inputs/shapes.cs      # the case types that the compiler accepts
dotnet run -c Release --no-build -- inputs/usage.cs       # construction, patterns, and the code that the frontend must refuse
dotnet run -c Release --no-build -- inputs/equality.cs    # a union with an interface list and equality members
```

- A union is a `struct` with `IsUnion`, `UnionCaseTypes`, a `Value` property and the interface `IUnion`.
- A conversion from a case to its union is an `IConversionOperation` whose conversion is `Union`. `new Shape(circle)` and `(Shape)circle` compile.
- A pattern on a union has the union as its input type and the case as its matched type.
- `default` of a union, a `null` arm and the `Value` property compile, so the frontend must refuse them.
- The compiler accepts these case types: a record, `int`, `string`, `int?`, a tuple, a type parameter, a constructed generic type and a different union. A case type that is written two times is the compile error `CS0111`.
- A union compiles with an interface list, equality members, the operators `==` and `!=`, and a `switch` on `this` in its body.

### Plain equality

```bash
(cd union/equality && dotnet run -c Release)
(cd union/equality && dotnet build -c Release -p:ProbeOperator=true)   # fails with CS0019: a plain union has no ==
```

| Test | Result |
| --- | --- |
| `double.NaN == double.NaN` | `false` |
| Two plain records with a `NaN` field | Equal |
| Two plain records with `0.0` and `-0.0` | Equal, with equal hashes |
| Two plain unions with the same case and value | Equal with `Equals` and with `EqualityComparer<T>.Default`, with equal hashes |
| Two plain unions with different cases | Not equal |
| `==` on two plain unions | Compile error `CS0019` |
| Two records with their own equality members and a `NaN` field (`cs15/equality`) | Not equal |

## Source generator with an F# core

The probe builds the sources of `Morphir.Models` and of `Morphir.CodeGeneration.CSharp` for `netstandard2.0`, with `FSharp.Core`, behind a C# `IIncrementalGenerator`. The repository sources stay as they are: `prepare.sh` makes a copy and adds one `open` line to ten files, and `Core/Polyfill.fs` has the members that `netstandard2.0` does not have.

```bash
cd source-generator
./prepare.sh
(cd Generator && dotnet build -c Release)
(cd Consumer && dotnet run -c Release)                # daily rate from generated code: 5000

# The same generator in the compilers of three SDKs. Each SDK must be installed.
./sdk-matrix.sh 5.0.0 10.0.100 10.0.403 11.0.100
# A generator that is compiled against a newer Roslyn.
./sdk-matrix.sh 5.12.0 10.0.403 11.0.100
```

| Generator compiled against | SDK 10.0.100 (Roslyn 5.0.0) | SDK 10.0.403 (Roslyn 5.9) | SDK 11.0.100 (Roslyn 5.12) |
| --- | --- | --- | --- |
| Roslyn 5.0.0 | Loads and generates | Loads and generates | Loads and generates |
| Roslyn 5.12.0 | Not run | Does not load; the consumer fails with `CS0103` | Loads and generates |

A generator must thus compile against the oldest Roslyn that it supports. The polyfill file has these members: `MaybeNullWhen`, `NotNullWhen`, `ArrayBufferWriter`, `Char.IsAsciiDigit`, `Double.IsFinite`, `String.Split(string)`, `String.Contains(char)` and `Dictionary.TryAdd`. `System.Text.Json` comes as a package.

This probe builds only in the repository, because it reads `src/`.

## Not tested

- The source generator in Visual Studio, Rider and VS Code. The probe ran only under `dotnet build`. `FSharp.Core` ships beside the generator, and a conflict in the .NET Framework process of Visual Studio is possible.
- Generated code on a `net8.0` runtime. The `net8.0` build compiles; no program ran on that runtime.
- Plain union equality under Native AOT. The equality probe ran with JIT.
- Linux and Windows. All results are from `osx-arm64`.
- `Generator="MSBuild:Compile"` and the design-time build of the MSBuild target.

## Notes for the repository

Renovate restores each tracked project with the `NuGet.config` of the repository root. Each probe project restores that way: `dotnet restore <project> --force-evaluate --configfile NuGet.config` (checked on macOS, not in a Linux container).
