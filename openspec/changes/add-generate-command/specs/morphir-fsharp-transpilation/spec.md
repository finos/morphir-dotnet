# Spec Delta

## MODIFIED Requirements

### Requirement: Unsupported input formats

The transpiler's existing JSON-file entry point SHALL reject missing, malformed or unsupported versions, non-library distributions and document-tree inputs. It SHALL identify the input and explain that this entry point supports classic library JSON versions 1, 2 and 3. It MUST NOT infer version 3 for unknown input. The additive typed-model entry point SHALL apply its declared semantic compatibility rules independently of file serialization.

#### Scenario: Future IR version

- **WHEN** the legacy JSON-file entry point receives a model declaring version 4, a semantic version string beginning with 4, or a later major version
- **THEN** transpilation fails with an unsupported-version diagnostic and produces no successful output

#### Scenario: Missing or invalid version

- **WHEN** the JSON-file entry point receives a model whose `formatVersion` is absent, a string such as `3`, zero, negative or fractional
- **THEN** transpilation fails with a version diagnostic rather than using a default

#### Scenario: Document tree or other distribution

- **WHEN** the JSON-file entry point receives a document tree or a distribution other than Library
- **THEN** transpilation fails with a diagnostic identifying the unsupported input shape

## ADDED Requirements

### Requirement: Unified generation conformance across versions

Generation conformance SHALL use one behavior corpus, compiler/execution harness and assertion approach across supported IR versions and formats. Version-specific adapters SHALL prepare input without replacing the shared behavioral oracle. Case applicability and expected refusals SHALL be explicit data; missing required cases SHALL fail coverage checks rather than silently skip. Genuine version-specific semantics SHALL use the same harness.

#### Scenario: One behavior across representations

- **WHEN** pricing, recursion or record-update behavior is supported in multiple IR versions and formats
- **THEN** the same case identity, independently specified expected result and shared assertions run for each supported combination
- **AND** no version-specific runner or duplicate behavioral expectation is introduced

#### Scenario: A construct belongs to one version

- **WHEN** a case exercises v4-only incomplete-body or inferred-type semantics
- **THEN** it uses the common harness and reporting with explicit version applicability and expected diagnostics
- **AND** it does not establish a separate v4 testing strategy

#### Scenario: Unsupported encoding is accounted for

- **WHEN** the case matrix includes an IR version with no supported encoding in a selected format
- **THEN** the matrix records the incompatibility and its reason instead of fabricating an equivalent input or silently omitting the combination

#### Scenario: Expected results remain independent

- **WHEN** classic and v4 outputs are compared
- **THEN** both are checked against the shared independently specified behavior
- **AND** passing output from one version is not the sole oracle for the other

### Requirement: Typed versioned library generation

The transpiler SHALL accept already decoded classic and v4 Libraries with explicit version context through an additive entry point. It SHALL support existing classic v1/v2/v3 semantics, supported 3.0.0/3.1.0 Ion equivalents, and v4 Library expressions without reparsing JSON or downgrading v4 to classic. Specs, Application and unsupported constructs SHALL fail without partial successful source.

#### Scenario: JSON and typed generation agree

- **WHEN** a supported JSON model is generated through the existing file API and through the typed entry point with equal options
- **THEN** generated source, ordered manifests and applicable semantic diagnostics are equivalent

#### Scenario: Ion library reaches the same generator

- **WHEN** a supported classic Ion library is decoded and passed to the typed entry point
- **THEN** the same scope/type checks and emitter run without an Ion-to-JSON write/read step

#### Scenario: Classic Specs are not executable libraries

- **WHEN** an already decoded Specs distribution is offered to F# generation
- **THEN** it is rejected as an unsupported distribution rather than becoming an empty successful library

#### Scenario: Nonempty v4 library

- **WHEN** equivalent supported classic and v4 libraries define aliases, unions, generic records, curried functions, recursion and pattern matching
- **THEN** both generated libraries compile and produce equivalent public behavior for the same calls

#### Scenario: V4 physical formats agree

- **WHEN** the same v4 library arrives as JSON, Ion text, Ion binary and an Ion document tree
- **THEN** generation produces equivalent F# source and ordered manifests

### Requirement: Typed generation retains semantic validation

Typed classic and v4 input SHALL pass scope, type, naming, dependency and SDK compatibility checks appropriate to their version. Skipping decoding SHALL not bypass validation. V4 inferred types SHALL not excuse contradictions with declared types. Diagnostics SHALL retain available source locations, and any generation error SHALL fail the whole model.

#### Scenario: Unsupported SDK reference in typed input

- **WHEN** a loaded classic library contains an unmapped SDK reference
- **THEN** typed generation fails with the fully qualified reference and no successful source files

#### Scenario: Invalid typed model cannot bypass validation

- **WHEN** an application constructs typed input containing an unbound variable or an invalid type application
- **THEN** generation returns the same scope or type category used by the existing validation pipeline

#### Scenario: Contradictory v4 inferred type

- **WHEN** v4 attributes declare an inferred type inconsistent with a definition's declared type or expression
- **THEN** generation reports the contradiction and emits no successful output

### Requirement: V4 structural and expression semantics

V4 generation SHALL preserve supported aliases, unions, generics, visibility, records, tuple/list/unit values, references, field access, currying, lambdas, let/letrec, destructuring, conditionals, matches and record updates. It SHALL preserve ordered operands, fields and parameters and retain available source context. Open extensible records SHALL fail explicitly as in classic generation.

#### Scenario: V4 function behavior

- **WHEN** a v4 library combines ordered parameters, recursive lets, record updates and ordered match cases
- **THEN** compiled F# preserves lexical scope, currying, parameter order and branch behavior

### Requirement: V4 literal fidelity

V4 scalar literals SHALL be emitted only when the selected F# representation preserves their value, Unicode scalar semantics and relevant decimal precision and scale. The generator SHALL reject narrowing, rounding and truncation rather than silently changing values. Schema-less document literals SHALL receive an explicit unsupported-document-literal diagnostic in this delivery.

#### Scenario: Representable and unrepresentable literals

- **WHEN** v4 input includes representable scalars alongside an integer, decimal or character outside the supported target representation
- **THEN** generation reports the offending literal without publishing rounded, narrowed or truncated source

#### Scenario: Document literal

- **WHEN** a v4 expression contains a DocumentLiteral
- **THEN** generation reports unsupported-document-literal rather than emitting a JSON string, null or placeholder

### Requirement: V4 incomplete and platform-specific bodies

V4 holes, incomplete definitions and native/external bodies SHALL fail with specific diagnostics. Native hints and external names SHALL not become executable implementations. Nonempty type constraints and unknown extension semantics required for generation SHALL be refused while the original model remains retained. Optional opaque metadata SHALL not be represented as executed semantics.

#### Scenario: Hole or draft definition

- **WHEN** a v4 Library contains a Hole, IncompleteBody or IncompleteTypeDefinition
- **THEN** generation identifies the incomplete node and produces no successful source

#### Scenario: Unimplemented native or external body

- **WHEN** a v4 definition has NativeBody or ExternalBody
- **THEN** generation reports unsupported-native-body or unsupported-external-body and does not invent a binding

### Requirement: Optimization choice preserves behavior

Both entry points SHALL share an explicit optimization setting that defaults to the existing constant-folding behavior. Disabling optimization SHALL retain required validation and emission rules. Existing callers that do not supply options SHALL retain their current generated behavior.

#### Scenario: Generated programs agree

- **WHEN** a representative supported library is generated with optimization enabled and disabled
- **THEN** both outputs compile and return equivalent results for the same calls

#### Scenario: Existing consumers remain compatible

- **WHEN** the existing Myriad integration and transpiler tests run without supplying new options
- **THEN** their supported inputs, deterministic module order and expected generated behavior remain unchanged
