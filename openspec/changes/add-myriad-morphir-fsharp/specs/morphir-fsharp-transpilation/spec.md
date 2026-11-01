# Spec delta

## Purpose

Translate versioned classic Morphir library model JSON into F# source that applications can compile and execute, while making compatibility limits and translation failures explicit.

## ADDED Requirements

### Requirement: Classic model input

The transpiler SHALL accept a Morphir library distribution JSON document with an explicit integer `formatVersion` of 1, 2 or 3 and decode it according to that version's wire format. Equivalent models across these versions SHALL produce equivalent F# declarations and behavior.

#### Scenario: Non-empty models in each supported version

- **WHEN** equivalent version 1, 2 and 3 library models containing type and value definitions are transpiled
- **THEN** each succeeds and produces F# with equivalent public types and executable results

#### Scenario: Car rental behavior across classic versions

- **WHEN** equivalent car rental model fixtures in versions 1, 2 and 3 are generated, compiled and executed
- **THEN** inventory, reservation, pickup, return and billing functions produce the same lifecycle results and business errors in each version

#### Scenario: Empty library

- **WHEN** a valid supported library has no modules
- **THEN** transpilation succeeds with an empty generated-module manifest and no `.fs` files

### Requirement: Unsupported input formats

The transpiler SHALL reject missing, malformed or unsupported versions, non-library distributions and document-tree inputs. It SHALL identify the input and explain that this delivery supports classic library JSON versions 1, 2 and 3. It MUST NOT infer version 3 for unknown input.

#### Scenario: Future IR version

- **WHEN** a model declares version 4, a semantic version string beginning with 4, or a later major version
- **THEN** transpilation fails with an unsupported-version diagnostic and produces no successful output

#### Scenario: Missing or invalid version

- **WHEN** `formatVersion` is absent, a string such as `3`, zero, negative or fractional
- **THEN** transpilation fails with a version diagnostic rather than using a default

#### Scenario: Document tree or other distribution

- **WHEN** input is a document tree or a distribution other than Library
- **THEN** transpilation fails with a diagnostic identifying the unsupported input shape

### Requirement: Type definitions

The transpiler SHALL generate F# for classic aliases, custom unions, generic variables, resolved type references, tuples, closed records, functions and unit. It SHALL preserve type parameters, field names, constructor arguments and declared visibility. It SHALL reject open extensible records rather than erase row information.

#### Scenario: Generic records and unions

- **WHEN** a model defines a generic closed record, an alias, and a union with nullary and multi-argument cases
- **THEN** the generated declarations compile and preserve their type parameters, field types and case payloads

#### Scenario: Closed structural records

- **WHEN** a closed record type occurs in a function signature without a named record alias
- **THEN** its generated F# representation supports construction, field access and updates consistently with that signature

#### Scenario: Open record cannot be preserved

- **WHEN** an open extensible record occurs in a reachable type or value definition
- **THEN** generation fails with its model location and an unsupported-open-record diagnostic

### Requirement: Executable value definitions

The transpiler SHALL translate classic literals, variables, constructors, tuples, lists, closed records, references, fields, field functions, application, lambdas, let bindings, local recursion, destructuring, conditionals, pattern matches, record updates and unit. It SHALL preserve currying, lexical scope, pattern binding and branch order for supported inputs.

#### Scenario: Curried and recursive functions

- **WHEN** a model defines a curried function and mutually recursive local functions
- **THEN** the generated F# compiles and returns the expected results for partial application and terminating recursive calls

#### Scenario: Match and destructure

- **WHEN** a function uses wildcard, alias, tuple, constructor, empty-list, head-tail, literal and unit patterns
- **THEN** generated code binds the same names and selects the same branches for representative matching inputs

#### Scenario: Record operations and lexical shadowing

- **WHEN** a function constructs a closed record, extracts and updates fields, and shadows a local binding
- **THEN** executing its generated F# returns the expected value without capturing a different binding or mutating the input record

### Requirement: Names and declaration dependencies

The transpiler SHALL preserve package and module qualification and visibility, escape F# keywords, and order declarations so forward references and supported recursive groups compile. It SHALL reject unresolvable naming collisions or dependency cycles with diagnostics rather than silently rename public symbols or omit definitions.

#### Scenario: Qualified references and reserved words

- **WHEN** a library has interdependent modules, a later-declared type, and a name that is an F# keyword
- **THEN** generated F# compiles with correct references and escaped identifiers

#### Scenario: Visibility and recursive types

- **WHEN** a model defines private helpers, private union constructors and mutually recursive types within a module
- **THEN** generated F# compiles, public operations work, and a consumer cannot access the private declarations

#### Scenario: Unrepresentable declarations

- **WHEN** two distinct public names map to the same F# identifier or a dependency cycle spans separate Morphir modules
- **THEN** transpilation fails and identifies the conflicting symbols or cycle

### Requirement: One source file per module

The transpiler SHALL produce exactly one `.fs` file per Morphir module, with a deterministic ordered manifest of those files. File paths SHALL preserve module path distinctions under a model-specific output directory and stay inside that directory. Dependencies SHALL precede dependent modules. Cross-module dependency cycles and output path collisions SHALL fail explicitly.

#### Scenario: Nested modules and compile order

- **WHEN** a model has `Domain.Types`, `Domain.Rules` depending on `Domain.Types`, and `Other.Types`
- **THEN** it generates three distinct files with `Domain.Types` before `Domain.Rules` in the compilation manifest

#### Scenario: Car rental module files

- **WHEN** the car rental example model contains shared domain types and inventory, reservation, pickup, return, billing and workflow modules
- **THEN** each module has its own generated `.fs` file and the example consumer builds using their dependency-ordered manifest

#### Scenario: Cyclic modules or colliding paths

- **WHEN** modules depend cyclically on each other or produce paths that collide on a case-insensitive filesystem
- **THEN** generation fails with the cycle or conflicting module names instead of combining files or overwriting output

### Requirement: SDK reference mapping

The transpiler SHALL resolve SDK names through a documented explicit mapping. The initial mapping SHALL cover Basics Int, Float, Bool and Order, String, Char, List, Maybe and Result types and constructors, basic arithmetic/comparisons, List.map, Maybe.map and Result.map. Unmapped SDK or non-SDK external references SHALL fail explicitly. Result type arguments SHALL retain Morphir error/success meaning.

#### Scenario: Common SDK operations

- **WHEN** a model uses the initial mapped types and operations, including `Maybe` constructors, `Result` constructors and curried `List.map`
- **THEN** generated F# compiles with documented runtime dependencies and produces the expected values

#### Scenario: Result argument order

- **WHEN** a Morphir Result has a string error type and an integer success type
- **THEN** the generated F# has `Result<int, string>` semantics and both constructors type-check

#### Scenario: Mapping is unavailable

- **WHEN** a model references an unmapped SDK function or a non-SDK dependency containing specifications without implementations
- **THEN** generation fails with the fully qualified reference and a mapping-or-dependency diagnostic

### Requirement: Deterministic source

The transpiler SHALL produce byte-identical formatted F# for the same model and options under the same tool versions. Output SHALL be independent of JSON object-property order, unordered declaration-map order, absolute input paths, wall-clock time and machine culture. Semantically ordered lists SHALL retain their order.

#### Scenario: Regeneration on another path

- **WHEN** the same model is generated twice from different directories and cultures with reordered unordered maps
- **THEN** the F# bytes are identical and pattern branches and argument lists retain their original semantic order

### Requirement: Actionable translation failures

On malformed JSON, invalid model structure, unresolved references or unsupported semantics, the transpiler SHALL fail without success-shaped placeholders. Each diagnostic SHALL contain a stable category, input path, available JSON location or qualified symbol, and an explanation. Unsupported declarations MUST NOT become `obj`, `Unchecked.defaultof`, empty output or runtime failure stubs.

#### Scenario: Malformed or invalid model

- **WHEN** a JSON file is truncated or a library contains a malformed value node
- **THEN** generation fails with a parse or decode category and the available source location

#### Scenario: Unsupported node among valid definitions

- **WHEN** a model has valid declarations followed by a node whose semantics cannot be translated
- **THEN** generation fails for the model as a whole without returning a partial successful source file
