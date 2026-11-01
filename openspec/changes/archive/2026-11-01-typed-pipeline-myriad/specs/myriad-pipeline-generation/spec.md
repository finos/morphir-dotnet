# Spec Delta

## Purpose

Runs classic Morphir model to F# generation through a real pipeline with validation and optimization, while consumers of the Myriad package keep the build behaviour they rely on.

## ADDED Requirements

### Requirement: Generation runs through the pipeline

Generating F# from a classic model SHALL run as a pipeline taking the model file as input and returning a file tree with one F# file per module. Parsing, scope validation, type validation, optimization and F# compilation SHALL each be a separate stage.

#### Scenario: Model to file tree

- **WHEN** the supported car rental model is processed
- **THEN** the tree contains seven F# files at the same relative locations as before this change

#### Scenario: Model parsed from a stream

- **WHEN** the model file's content is a stream source
- **THEN** generation produces the same output as for the same model given as text

### Requirement: Dependency order is declared

The generated tree SHALL declare the dependency-ordered list of modules and their locations, independently of directory structure. The declared order SHALL be deterministic, and every generated file SHALL appear in it exactly once.

#### Scenario: Order crosses directories

- **WHEN** a model's dependency order alternates between two directories
- **THEN** the declared order preserves dependency order and the consumer compiles in that order

### Requirement: Failures become diagnostics

Decoding, validation and generation failures SHALL appear as error diagnostics on the output tree, each naming the input path, a rule, a logical location and a message. Generation SHALL NOT produce output files for a failed model.

#### Scenario: Unsupported format version

- **WHEN** a model declares format version 4
- **THEN** the tree has no files and an error diagnostic with the existing rejection rule

#### Scenario: Unbound variable

- **WHEN** a value uses a variable that is not in scope
- **THEN** the tree has no files and an `unbound-variable` error naming the value

### Requirement: Optimization preserves generated behaviour

Optimization SHALL only simplify an expression when the result matches what the generated F# would compute at run time. It SHALL recognize SDK functions by fully qualified name, and it SHALL leave an expression unchanged when folding would overflow the mapped numeric type.

#### Scenario: User function with an SDK name

- **WHEN** a model defines its own function named `add` and applies it to two literals
- **THEN** the call is not folded and the user's function runs

#### Scenario: Result out of range

- **WHEN** two whole-number literals are added with the SDK function and the sum does not fit the F# type mapped from Morphir `Int`
- **THEN** the expression is not folded

#### Scenario: Equivalent behaviour

- **WHEN** a model containing foldable expressions is generated, compiled and run with optimization on and with it off
- **THEN** both builds return the same results

#### Scenario: Optimization is observable

- **WHEN** a model contains the SDK addition of two small literals
- **THEN** the generated source contains the folded literal and the tree records an informational diagnostic

### Requirement: Optimization does not change which models are accepted

Optimization in the generation pipeline SHALL NOT make a rejected model generate, or make a generating model fail. It SHALL be limited to folding operations the backend already maps.

#### Scenario: Unmapped SDK function in a short-circuit

- **WHEN** a model uses the unmapped SDK boolean `and` with a literal `true` as its first argument
- **THEN** generation still fails with the existing `unsupported-sdk` rule

#### Scenario: Unsupported reference in a dead branch

- **WHEN** an `if` on a literal condition has an unsupported reference in the branch that cannot run
- **THEN** generation still fails with the existing rule

### Requirement: Type validation reports only definite errors

Type validation SHALL fail a model only for a definite type mismatch. An expression it cannot infer SHALL be treated as unknown and SHALL produce no warning or error. Every supported fixture SHALL pass without diagnostics of warning severity or above.

#### Scenario: Function input in scope

- **WHEN** a value body uses one of its declared inputs
- **THEN** validation reports no error

#### Scenario: Definite mismatch

- **WHEN** a value declared to return `Int` has a body that is a string literal
- **THEN** validation reports an error naming the value and generation stops

#### Scenario: Declared type is an alias

- **WHEN** a value declared to return a model alias of `Int` has a whole-number literal body
- **THEN** validation reports no error

#### Scenario: Supported fixtures stay quiet

- **WHEN** the car rental v1, v2 and v3 fixtures are processed
- **THEN** no warning or error diagnostics are produced

### Requirement: Consumer build behaviour is preserved

The `MorphirModel` item, one Myriad host invocation per module, generated file layout, manifest location, compile order, design-time behaviour and failure blocking SHALL keep working as they did before this change. Build diagnostics SHALL keep naming the model path, rule, location and message.

#### Scenario: Cold package consumer

- **WHEN** a clean consumer restores the packed plugin with an empty package cache and builds the car rental example
- **THEN** the pipeline and plugin assemblies load from the package and the consumer's lifecycle output matches the documented results

#### Scenario: Invalid model after a success

- **WHEN** a successful build is followed by an invalid model and a rebuild
- **THEN** the build fails before compilation and the earlier generated files are not compiled

#### Scenario: Module added and removed

- **WHEN** a model adds one module and removes another between builds
- **THEN** the new module compiles in dependency order and the old generated file is deleted

### Requirement: Safety checks run before the host

The build SHALL validate the planned locations and generation result before starting the Myriad host, and SHALL publish host output through the shared publication behaviour.

#### Scenario: Colliding module paths

- **WHEN** a model's module paths differ only by case
- **THEN** the build fails with a collision error and the host is never started

#### Scenario: Host fails for one module

- **WHEN** the host fails for one of several modules
- **THEN** the build fails and existing generated output is unchanged
