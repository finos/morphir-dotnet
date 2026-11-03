# morphir-csharp-transpilation Specification

## Purpose

Generate deterministic, executable and immutable C# libraries from supported typed Morphir models, with explicit target-language capabilities and reusable in-memory outputs.

## Requirements

### Requirement: Typed versioned library input

Generation SHALL accept already decoded classic Library models at versions 1, 2, 3, 3.0.0 and 3.1.0, and v4 Library models at 4 and 4.0.0. It SHALL preserve the model family and source version without serialization bridges, file reads or implicit migration.

#### Scenario: Equivalent model families

- **WHEN** equivalent supported classic and v4 libraries are generated
- **THEN** their C# consumers observe equivalent admitted behavior through their respective typed generation paths

#### Scenario: Unsupported distribution

- **WHEN** a Specs, Application or unsupported source version is supplied
- **THEN** generation returns a located incompatibility diagnostic and no successful sources

#### Scenario: Empty library

- **WHEN** a supported library contains no modules
- **THEN** generation succeeds with no source files

### Requirement: Explicit language profiles

Generation SHALL support explicit C# 14 and C# 15 profiles, defaulting to 14 independently of the installed SDK. Profiles SHALL declare required runtime support and use the same SDK API and public case representation. Unknown versions SHALL fail before emission.

#### Scenario: Supported toolchains

- **WHEN** a supported model is generated for each language profile
- **THEN** its sources compile and execute using the pinned matching .NET 10 or .NET 11 RC toolchain

#### Scenario: No ambient preview

- **WHEN** the machine has a newer compiler or preview language features
- **THEN** default generation remains C# 14 and does not enable unrelated preview syntax

#### Scenario: Public representation stability

- **WHEN** the same model is generated for C# 14 and 15
- **THEN** public type, case, member and payload identities remain equivalent rather than changing to a different union representation

### Requirement: Immutable type translation

Generation SHALL preserve supported aliases, generic parameters, custom-type cases, tuples, closed records, function types, unit and visibility. Generated values SHALL be immutable and retain Morphir equality semantics for supported payloads. Open extensible records SHALL fail explicitly.

#### Scenario: Generic data and nested collections

- **WHEN** a model exports generic records and cases containing SDK collections
- **THEN** the C# consumer constructs and compares values structurally without mutable backing data

#### Scenario: Alias and visibility fidelity

- **WHEN** a model uses transparent aliases and private helpers or constructors
- **THEN** supported aliases retain their semantic identity and inaccessible declarations remain inaccessible to a consumer

### Requirement: Executable initial expressions

Generation SHALL support literals, variables, top-level references and self recursion, constructors, tuples, lists, closed records, fields, application, lambdas, nonrecursive lets, conditionals, record updates and ordered patterns. It SHALL preserve scope, evaluation order and partial application.

#### Scenario: Function values and lexical capture

- **WHEN** a function is partially applied and a lambda captures a shadowed local name
- **THEN** executing the generated program uses the correct binding and argument order

#### Scenario: Pattern and record behavior

- **WHEN** a model uses constructor, tuple, list, wildcard, literal and alias patterns followed by a record update
- **THEN** the first matching branch executes and the original record remains unchanged

#### Scenario: Unsupported local recursion

- **WHEN** a model uses a local recursive binding group or an unmapped expression
- **THEN** generation reports its model location rather than omitting it or emitting a runtime stub

### Requirement: Explicit SDK resolution

SDK references SHALL resolve through the admitted SDK inventory and Morphir.SDK.CSharp. Unmapped SDK operations, unresolved dependencies and required semantics that the generator cannot preserve SHALL fail explicitly. Result error/success payload meaning SHALL be preserved.

#### Scenario: Initial executable SDK slice

- **WHEN** a supported library uses arithmetic, comparisons, List.map, Maybe.map and Result.map
- **THEN** the generated consumer produces the shared conformance expectations

#### Scenario: Unavailable implementation

- **WHEN** a value depends on an unmapped SDK operation or a declaration without an admitted implementation
- **THEN** generation identifies the qualified reference and returns no success-shaped placeholder

### Requirement: Deterministic names and source artifacts

Generation SHALL return relative C# source paths and stable bytes independent of host paths, culture, time and unordered map enumeration. Names SHALL preserve qualification, escape keywords and avoid capturing locals. Unresolvable symbol or portable path collisions SHALL fail explicitly.

#### Scenario: Reordered maps and cultures

- **WHEN** the same model is generated from different host paths and cultures with reordered unordered declarations
- **THEN** file paths and source bytes are identical while branch and argument order remain unchanged

#### Scenario: Keyword and collision

- **WHEN** a declaration is a C# keyword or two public names map to one target identity
- **THEN** the keyword is escaped and an unresolvable collision produces a diagnostic naming both owners

### Requirement: In-memory failure and cancellation

Generation SHALL return sources and structured diagnostics without host configuration, publication or compiler processes. Unsupported input, type inconsistencies and cancellation SHALL return no successful partial artifact set. Diagnostics SHALL identify a stable rule and available model location.

#### Scenario: Cancellation during a module

- **WHEN** the caller cancels while generation is processing a nonempty model
- **THEN** generation stops promptly with cancellation and no successful partial output

#### Scenario: Invalid later declaration

- **WHEN** valid declarations precede a malformed or unsupported declaration
- **THEN** the result fails for the whole model without writable or success-shaped partial sources

### Requirement: Compiler-host reuse evidence

The generation entry point SHALL be callable from a minimal modern .NET compiler-host adapter using in-memory model input and caller cancellation. Compatibility claims SHALL name the tested host/runtime and dependency closure; untested IDE or legacy compiler hosts SHALL NOT be claimed.

#### Scenario: Minimal generator-driver smoke

- **WHEN** a packaged test adapter invokes generation from a supplied model and adds its sources to a .NET 10 compiler-host compilation
- **THEN** the compilation succeeds without loading Tooling, reading host files or spawning the CLI

#### Scenario: Untested host

- **WHEN** the dependency closure has not been demonstrated in a legacy or different IDE compiler host
- **THEN** the support matrix identifies that limitation rather than assuming generated language compatibility proves host compatibility
