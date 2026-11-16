# Spec Delta

## Purpose

Generates C# from a Morphir model inside a user build, with two entry points: an MSBuild target that writes files, and a Roslyn source generator that needs no separate build step. Both call one pipeline function, so the CLI, the target and the generator give the same C#.

## ADDED Requirements

### Requirement: One pipeline function with three callers

The upgrade, the target-side passes and the backend SHALL be one pure function with no file or process access. The CLI, the MSBuild target and the source generator SHALL call it, and the same model SHALL give the same C# bytes through the three.

#### Scenario: Same bytes

- **WHEN** one model is generated with the CLI, the target and the generator
- **THEN** the C# paths and bytes are equal

### Requirement: The source generator reads model files

The source generator SHALL read each `*.morphir.json` additional file, run the pipeline function, and add the generated files to the compilation. It SHALL report a failure as a diagnostic and SHALL NOT throw.

#### Scenario: Generator in a build

- **WHEN** a project has a `car-rental.morphir.json` additional file and takes the generator as an analyzer
- **THEN** `dotnet build` compiles the generated code and a program can call it

#### Scenario: Invalid model

- **WHEN** the additional file is not a valid model
- **THEN** the build reports an error diagnostic with the reason

### Requirement: The generator loads in the supported compilers

The generator SHALL target `netstandard2.0`, SHALL compile against the oldest Roslyn that it supports, and SHALL ship `FSharp.Core` beside it. It SHALL load and generate in the compilers of each supported .NET SDK. The oldest supported Roslyn is 5.12.0, the compiler of .NET SDK 11.0.100, because the generated code is C# 15.

#### Scenario: SDK matrix

- **WHEN** a consumer of the package builds with SDK 10.0.100, 10.0.403 and 11.0.100
- **THEN** the build with SDK 11.0.100 generates the code, and the builds with SDK 10.0.100 and 10.0.403 report the error that names SDK 11

#### Scenario: A compiler older than the Roslyn of the generator

- **WHEN** the compiler of SDK 10.0.100 or 10.0.403 gets the generator as an analyzer, with no file of the package
- **THEN** the compiler does not load the generator and reports `CS9057`

### Requirement: Untested hosts are not claimed

A compatibility claim SHALL name the tested host, runtime and dependency closure. The support matrix SHALL list Visual Studio, Rider and VS Code as untested until a test runs the generator in them.

#### Scenario: Support matrix

- **WHEN** a user reads the support matrix
- **THEN** each IDE that has no test is marked as not tested, and no claim says that it works

### Requirement: The MSBuild target writes files

The package SHALL have an opt-in MSBuild target that generates C# into the project before compilation and adds the files to the `Compile` items. The target SHALL set `LangVersion` to 15 for the project.

#### Scenario: Target in a build

- **WHEN** a project opts in and has a model file
- **THEN** the generated files exist in the intermediate output folder and the project compiles them

### Requirement: Older target frameworks get a polyfill file

For a target framework below `net11.0`, the output SHALL have one polyfill file with `internal` types behind framework guards: `IsExternalInit`, `CompilerFeatureRequiredAttribute`, `UnionAttribute`, `IUnion` and `IsClosedTypeAttribute`. The file SHALL be absent for a framework that has the types.

#### Scenario: net10.0 target

- **WHEN** a project that targets `net10.0` builds generated code
- **THEN** the polyfill file is present and the build has no warning

#### Scenario: Exhaustive match across assemblies

- **WHEN** a library and an application each have their own polyfill file and the application matches on a union of the library
- **THEN** the compiler checks the match as exhaustive

### Requirement: The build needs .NET SDK 11

A project that uses the package SHALL need .NET SDK 11 to build, and MAY target a lower framework. A build with an older SDK SHALL report an error that names the SDK that it needs.

#### Scenario: Older SDK

- **WHEN** a model project builds with SDK 10
- **THEN** the build reports an error that names SDK 11
