---
title: "C# generation in a build"
linkTitle: "Build integration"
description: >
  Generate C# from a Morphir model when a C# project builds: the source generator and the MSBuild target of the package Morphir.Lang.CSharp, their settings, their diagnostics and the support matrix.
---

The package `Morphir.Lang.CSharp` generates C# from the Morphir models of a C# project when the project builds. A model is
an `AdditionalFiles` item with a file name that ends in `.morphir.json`. The package has two entry points:

- The **source generator** (the default). It adds the generated code to the compilation. The code is not a file of the
  project.
- The **MSBuild target** (opt-in). It writes the generated files into the intermediate output folder before the
  compilation, and the project compiles them.

Both entry points call one pipeline function, `CSharpPipeline.Generate`. It upgrades a classic model to the current IR,
runs the C# passes and the backend, and has no file or process access. `morphir generate --target csharp` runs the same
stages, so one model gives the same paths and bytes through the CLI, the target and the generator.

## Requirements

- The **.NET SDK 11.0.100 or later**. The generated code is C# 15, and the package sets `LangVersion` to 15. A build
  with an older SDK stops with the error `MORPHIR2010`, which names the SDK that it needs.
- The project can target a lower framework, for example `net10.0`. Below `net11.0` the output has one polyfill file,
  `Morphir.Polyfill.g.cs`, with the `internal` runtime types that C# 15 needs (see [C# generation](_index.md#the-output)).
- The generated code calls `Morphir.SDK.CSharp`. Add that package too.

## Generate in a build

Make a console project, and add the two packages:

```bash
$ dotnet new console --output RentalApp
$ cd RentalApp
$ dotnet add package Morphir.Lang.CSharp --prerelease
$ dotnet add package Morphir.SDK.CSharp --prerelease
```

Put the model in the project folder as `car-rental.morphir.json`, and add it to `RentalApp.csproj` as an additional
file:

```xml
<ItemGroup>
  <AdditionalFiles Include="car-rental.morphir.json" />
</ItemGroup>
```

The model of this example is the package `car/rental` with the module `inventory` and the value `daily-rate`. Its code
is the static class `Car.Rental.Inventory`. Replace the text of `Program.cs`:

```csharp
System.Console.WriteLine($"daily rate: {Car.Rental.Inventory.DailyRate()}");
```

Build and run the project:

```bash
$ dotnet run
daily rate: 5000
```

To see the generated files, set `EmitCompilerGeneratedFiles` to `true`. The compiler then writes them below
`obj/<configuration>/<framework>/generated/Morphir.Lang.CSharp.Generator/`.

## Write the generated files with the MSBuild target

Set `MorphirGenerateCSharpFiles` to `true`. The target `MorphirGenerateCSharpFiles` then writes the files into
`obj/<configuration>/<framework>/morphir/csharp/` before the compilation and adds them to the `Compile` items, and the
source generator does nothing:

```bash
$ dotnet run -p:MorphirGenerateCSharpFiles=true
daily rate: 5000
```

In the project file:

```xml
<PropertyGroup>
  <MorphirGenerateCSharpFiles>true</MorphirGenerateCSharpFiles>
</PropertyGroup>
```

The target writes a file only when its bytes change, and it deletes the C# files of its folder that no model writes
now. `dotnet clean` removes the folder.

## Settings

| MSBuild property | Value | Effect |
| --- | --- | --- |
| `MorphirGenerateCSharpFiles` | `true` | The MSBuild target writes the files, and the source generator does nothing. |
| `MorphirCSharpNamespace` | A dotted C# namespace | The namespace of all generated files. Empty: the namespace of the package of each model. |
| `MorphirModel` | `true` | The project is a model project: the admission analyzers (`MORPHIR1001` and the others) report its C# outside the Morphir subset. |

## Diagnostics

| Id | Severity | Reason |
| --- | --- | --- |
| `MORPHIR2001` | Error | The generation of a model failed. The message has the model file, the code (for example `invalid-json` or `unsupported-version`) and the reason. The project gets no file of that model. |
| `MORPHIR2002` | Warning | A warning of the pipeline, with the model file and the code. |
| `MORPHIR2003` | Error | Two models write a file with the same path and different text. One polyfill file for two models is not an error. |
| `MORPHIR2010` | Error | The .NET SDK is older than 11.0.100. |

## The package

| Folder | Contents |
| --- | --- |
| `lib/net11.0`, `lib/net10.0` | The frontend, the passes and the backend, with the analyzer and attribute assemblies. |
| `lib/netstandard2.0` | The pipeline function only, with the attribute assembly. |
| `analyzers/dotnet/cs` | The source generator, the admission analyzers and their closure: `Morphir.Lang.CSharp`, `Morphir.Models`, `Morphir.Compilation` and `Morphir.MiddleEnd` for `netstandard2.0`, `FSharp.Core`, and `System.Text.Json` with its dependencies. It has no compiler assembly (`Microsoft.CodeAnalysis*`). |
| `tasks/netstandard2.0` | The MSBuild task `Morphir.Lang.CSharp.Build` and its closure, with `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp` 5.12.0. |
| `build` | `Morphir.Lang.CSharp.props` and `Morphir.Lang.CSharp.targets`. |

The source generator compiles against Roslyn 5.12.0, the compiler of .NET SDK 11.0.100, so it loads in that compiler and
in later compilers. The compiler of SDK 10 does not load it (`CS9057`), and the package stops the build before the
compiler with `MORPHIR2010`.

## Support matrix

A claim names the host, the runtime and the closure that a test ran. A host with no test is not tested, and this page
makes no claim that it works.

| Host | Status | Test |
| --- | --- | --- |
| `dotnet build` and `dotnet run`, .NET SDK 11.0.100, with the closure of the table above | Tested: the source generator and the MSBuild target generate, a `net10.0` program runs | `tests/Morphir.BlackBox.Tests/EndToEnd/CSharpBuildGeneration.feature` (packages lane) and `scripts/csharp-sdk-matrix.sh` |
| `dotnet build`, .NET SDK 10.0.100 and 10.0.403 | Not supported: the build stops with `MORPHIR2010` | `scripts/csharp-sdk-matrix.sh` |
| Generated code on the .NET 10 runtime | Tested: a `net10.0` library and a `net10.0` application, each with its own polyfill file, 0 warnings | `CSharpBuildGeneration.feature` |
| Generated code on the .NET 11 runtime | Tested: no polyfill file | The commands of this page |
| Generated code on a `net8.0` runtime | Not tested | None |
| Visual Studio | Not tested. `FSharp.Core` is beside the generator, and it can conflict in the .NET Framework process of Visual Studio. The MSBuild task is `netstandard2.0`, but it did not run in the MSBuild of Visual Studio. | None |
| JetBrains Rider | Not tested | None |
| Visual Studio Code with the C# Dev Kit | Not tested | None |

The packages lane runs on each operating system of the CI matrix. The SDK matrix check runs on Linux. The commands of this
page ran on macOS arm64.
