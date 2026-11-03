# configuration-documents Specification

## Purpose

Provides typed Morphir configuration documents independent of their file serialization, including compatibility with classic morphir-elm project manifests.

The accepted YAML profile intentionally differs from core's stricter profile,
which rejects nulls, anchors, aliases and custom tags. JSON/JSON5 duplicate-key
rejection is an approved stricter policy than Moon-Lander's last-duplicate-wins parser.

## Requirements

### Requirement: Equivalent serializations retain typed values
Readers SHALL accept TOML, YAML/YML, and JSON/JSON5 documents by extension and produce equivalent typed values for equivalent content. String, boolean, number, array, and object values SHALL remain distinguishable. Unknown settings allowed by the configuration model SHALL be retained.

#### Scenario: Equivalent project files
- **WHEN** equivalent project settings are supplied as TOML, YAML, and JSON
- **THEN** their canonical values are equal and their source identities identify the respective files

#### Scenario: String remains different from integer
- **WHEN** one document contains the string "4" and another contains the integer 4
- **THEN** their typed values remain different before schema validation or host projection

### Requirement: YAML follows the pinned MoonBit Default profile
YAML readers SHALL match Moon-Lander 0.8.0 Default as used by pinned MoonBit.
Nulls SHALL be retained, anchors/aliases SHALL expand without code execution,
and an empty stream SHALL produce an empty object. Duplicate keys, merge keys,
multiple documents, non-string keys, and non-mapping roots SHALL fail with source diagnostics.

#### Scenario: Anchors retain values without losing scalar types
- **WHEN** a YAML mapping anchors an object containing an integer and aliases it
- **THEN** both fields contain the same typed object and their source is retained

#### Scenario: YAML null remains available to profile validation
- **WHEN** a YAML root primary supplies profiles.active=null
- **THEN** raw null is retained and enabled profile processing fails its control type

#### Scenario: Empty YAML stream
- **WHEN** a supplied YAML stream is empty
- **THEN** the reader returns an empty object without a parse error

### Requirement: YAML scalar typing is explicit
Plain yes/no words and dates SHALL remain strings. Quoted numeric strings SHALL
remain strings. Supported numeric and boolean scalars SHALL retain their types.
Custom scalar tags SHALL produce strings, matching the pinned Default profile.

#### Scenario: Custom scalar tag
- **WHEN** a YAML scalar has a custom tag and numeric-looking text
- **THEN** its configuration value is a string

#### Scenario: Quoted numeric string
- **WHEN** YAML supplies the quoted value "42" beside the integer 42
- **THEN** the values remain a string and an integer respectively

### Requirement: JSON extensions support JSON5 syntax
Files ending in .json and .json5 SHALL accept JSON5 comments, unquoted keys, single-quoted strings, and trailing commas. Unsupported extensions SHALL return a diagnostic naming the input.

#### Scenario: Classic file with JSON5 syntax
- **WHEN** morphir.json contains comments and a project object with single-quoted strings and a trailing comma
- **THEN** parsing succeeds and retains the same values as the equivalent JSON document

#### Scenario: Unsupported explicit file
- **WHEN** an explicitly supplied configuration is named settings.ini
- **THEN** loading fails with an unsupported-format diagnostic naming settings.ini

### Requirement: Parsing failures identify their source
Invalid documents SHALL produce structured errors containing the source path and available line/column information. Duplicate keys SHALL be rejected rather than silently overwritten. Document field names SHALL preserve case.

#### Scenario: Duplicate project field
- **WHEN** a JSON document repeats project.name in one object
- **THEN** parsing fails and the diagnostic identifies the duplicate and the declaring source

#### Scenario: Malformed YAML
- **WHEN** a YAML primary has malformed structure
- **THEN** the error identifies that file and the available source position

### Requirement: Classic manifest fields normalize explicitly
Classic name, sourceDirectory, and exposedModules SHALL map to project.name, project.source_directory, and project.exposed_modules. The reader SHALL record legacy-derived Elm/checkpoint defaults and diagnose unsupported legacy dependency or decoration shapes.

#### Scenario: Classic Elm project
- **WHEN** morphir.json declares name Acme.Orders, sourceDirectory elm-src, and exposedModules containing Orders.Api
- **THEN** the canonical project fields contain those values and legacy Elm/checkpoint defaults are recorded

#### Scenario: Unsupported legacy dependency
- **WHEN** a legacy manifest contains a dependency shape not consumed by the reader
- **THEN** a diagnostic identifies that field and the original document remains available for loss-checked migration

### Requirement: Legacy and canonical conflicts are rejected
A single document SHALL NOT provide both a legacy field and its canonical equivalent, even if their values match.

#### Scenario: Two source-directory spellings
- **WHEN** one manifest supplies sourceDirectory and project.source_directory
- **THEN** loading fails with a diagnostic naming both fields

### Requirement: Document loading has no execution side effects
Parsing and normalization SHALL NOT execute project code, resolve secret references, or mutate input documents. Host-independent document reading SHALL be available without CLI construction.

#### Scenario: Secret reference inspection
- **WHEN** a document contains a command-shaped secret reference
- **THEN** parsing retains the reference without invoking the command

#### Scenario: Embedded host
- **WHEN** a non-CLI host supplies an in-memory document
- **THEN** it obtains the same canonical values and errors as a file-backed host with the same contents
