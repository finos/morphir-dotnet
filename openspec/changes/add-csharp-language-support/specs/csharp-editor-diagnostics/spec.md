# Spec Delta

## Purpose

Shows a C# author the refusals of the Morphir frontend where the author works: the compiler, the IDE and `morphir compile`. Each refusal is a Roslyn analyzer diagnostic with one code, message and range everywhere. The same analyzers report an older encoding of a sum type, and a code fix changes it to the preferred form.

## ADDED Requirements

### Requirement: Refusals are analyzer diagnostics

Each admission rule of the C# frontend SHALL be a Roslyn analyzer rule. The package SHALL put the analyzer assembly where the compiler and the IDE load it. `morphir compile` SHALL run the same analyzers in process.

#### Scenario: Build with the package

- **WHEN** a project references the package and has a `foreach` that the subset refuses
- **THEN** `dotnet build` reports the rule as an error with file, line and column

#### Scenario: Compile command

- **WHEN** `morphir compile` runs on the same source
- **THEN** it reports the same rule with the same message and range

### Requirement: Rules have a short id and an IRI

A rule SHALL have a short Roslyn id such as `MORPHIR1001`. The absolute IRI of the rule SHALL be in the help link of the diagnostic.

#### Scenario: Help link

- **WHEN** the analyzer reports a rule
- **THEN** the help link of the diagnostic is the absolute IRI of the rule

### Requirement: Rules cannot be suppressed

A rule SHALL NOT be configurable: `#pragma warning disable`, an `.editorconfig` severity and `NoWarn` SHALL NOT hide it. A `SuppressMessage` or `UnconditionalSuppressMessage` attribute that names a rule SHALL be refused with its own rule. `morphir compile` SHALL refuse a construct that a rule refuses also when the IDE does not report it.

#### Scenario: Editorconfig entry

- **WHEN** an `.editorconfig` sets one rule id to `none`
- **THEN** the compiler still reports that rule as an error

#### Scenario: Pragma

- **WHEN** a `#pragma warning disable MORPHIR1006` comes before a class
- **THEN** the analyzer still reports the class

#### Scenario: Suppression attribute

- **WHEN** a type has `[SuppressMessage("Morphir", "MORPHIR1001")]`
- **THEN** the analyzer reports `MORPHIR1008` at the attribute

### Requirement: The analyzer reports only in a model project

The analyzer SHALL report only in a project that is a Morphir model project. A project is a model project when it has the assembly attribute `MorphirModel` or sets the MSBuild property for it.

#### Scenario: Ordinary project

- **WHEN** a project references the package and has no model project switch
- **THEN** the analyzer reports no diagnostic

### Requirement: The IDE and the compile command agree

The package SHALL give a model project fixed compile options, and an admission rule SHALL refuse a symbol from an assembly outside the fixed reference set. A construct that `morphir compile` cannot bind SHALL be reported by the analyzer.

#### Scenario: Package outside the reference set

- **WHEN** a model project uses a type from a NuGet package that the fixed reference set does not have
- **THEN** the analyzer reports an error at that use

### Requirement: Analyzer assemblies are portable

The analyzer assembly SHALL target `netstandard2.0`, depend only on Roslyn, and compile against the oldest Roslyn that it supports. The assembly that holds code fixes SHALL be loaded only by an IDE, and no host SHALL load it.

#### Scenario: Host closure

- **WHEN** the Native AOT `morphir` is published
- **THEN** it holds no Workspaces assembly and no code fix assembly

### Requirement: Older sum type encodings are reported

The analyzer SHALL report a sum type that is not in the preferred form, such as an `abstract record` with a private constructor and nested `sealed record` cases. The frontend SHALL refuse the older encoding with the same diagnostic.

#### Scenario: Abstract record encoding

- **WHEN** a model project has an `abstract record` with a private constructor and nested `sealed record` cases
- **THEN** the analyzer reports the older encoding, and `morphir compile` refuses it with the same code

### Requirement: A code fix changes a sum type to the preferred form

A code fix SHALL change an older sum type encoding to a `closed record` with `sealed record` cases, and SHALL keep the case names and fields. The fix SHALL be available in an IDE that loads the code fix assembly.

#### Scenario: Apply the fix

- **WHEN** the fix runs on the abstract record encoding
- **THEN** the result is a `closed record` with the same cases, and the model that the frontend reads has the same custom type

### Requirement: Unions are not reported as an older form

The analyzer SHALL NOT report a `closed record` or a convertible union as an older encoding. The rewriter SHALL NOT change a union to a `closed record`.

#### Scenario: Union stays

- **WHEN** a model project has `union Shape(Circle, Rect)`
- **THEN** the analyzer reports no sum type diagnostic
