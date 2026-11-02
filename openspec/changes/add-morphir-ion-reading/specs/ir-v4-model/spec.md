# Spec Delta

## Purpose

Gives .NET callers an immutable, serialization-neutral model of Morphir IR version 4 and a JSON reader for it, so any physical format can produce one comparable IR value.

## ADDED Requirements

### Requirement: The model covers the v4 semantic IR

The model SHALL represent `Library`, `Specs` and `Application` distributions, every v4 type expression, type definition and specification, every value expression, pattern and literal, and expression, native, external and incomplete value bodies.

#### Scenario: Reference examples

- **WHEN** the vendored `complete-example.json` and `books-and-records-example.json` are read
- **THEN** each read succeeds and every node in them has a model case

#### Scenario: Incomplete nodes

- **WHEN** a document holds an incomplete type, an incomplete value with no output type, and a hole expression
- **THEN** each decodes with its reason, and only the incomplete body may lack an output type

### Requirement: The model excludes invalid states

Model types SHALL be immutable, SHALL use no nulls, and SHALL NOT expose a physical format's types. Attributes SHALL be typed: `source`, `constraints` and `extensions` on types, and `source`, `inferredType` and `extensions` on values and patterns.

#### Scenario: Attribute payloads

- **WHEN** a value has `source`, `inferredType` and `extensions`
- **THEN** the source is a typed location, the inferred type is a v4 type, and the extensions are a JSON-compatible document value

### Requirement: Exact values stay exact

Integer literals SHALL hold any size. Decimal literals SHALL hold their lexeme. Float literals SHALL hold their finite value and any retained lexeme. Document payload numbers SHALL hold their lexeme.

#### Scenario: Decimal identity

- **WHEN** two decimal literals have lexemes `10.50` and `10.5`
- **THEN** they are not equal

#### Scenario: Invalid decimal

- **WHEN** a JSON decimal literal's payload is `NaN`
- **THEN** the reader returns `invalid_literal`

#### Scenario: Float meaning

- **WHEN** two float literals have lexemes `4.0` and `4e+0`
- **THEN** semantic comparison treats them as equal, and their retained lexemes stay available

### Requirement: Collections keep the order the IR gives them

Parameter maps, record fields and entry points SHALL keep their order, and that order SHALL count in equality. Dependencies, modules, types and values SHALL keep document order, and that order SHALL NOT count in equality.

#### Scenario: Parameter order

- **WHEN** two values differ only in the order of their input parameters
- **THEN** they are not equal

#### Scenario: Module order

- **WHEN** two distributions differ only in the order of their modules
- **THEN** they are equal, and each still lists modules in its own document order

### Requirement: The JSON reader accepts every valid v4 spelling

The JSON reader SHALL accept compact and expanded v4 spellings and legacy name arrays, and SHALL normalize them to one value. It SHALL accept `formatVersion` as `4` or `"4.0.0"` in either member position. It SHALL use no runtime reflection.

#### Scenario: Reference shorthand

- **WHEN** a value is read from `"morphir/SDK:basics#add"` and from `{ "Reference": "morphir/SDK:basics#add" }`
- **THEN** the results are equal

#### Scenario: Expanded node

- **WHEN** a value is read from `{ "Reference": { "attributes": {}, "fqname": "morphir/SDK:basics#add" } }`
- **THEN** the result equals its shorthand

#### Scenario: Format version spellings

- **WHEN** one distribution is read with `formatVersion` `4`, with `"4.0.0"`, and after `distribution`
- **THEN** the three results are equal

#### Scenario: Kit spellings

- **WHEN** each JSON input the vendored kit marks as accepted for a value is read
- **THEN** the result equals the value that case states

### Requirement: The JSON reader refuses invalid input

The JSON reader SHALL return a typed error with a JSON location for invalid input. It SHALL refuse duplicate object members and any format version outside the v4 family.

#### Scenario: Duplicate member

- **WHEN** a JSON object repeats a member name
- **THEN** the reader returns `duplicate_member` with its location

#### Scenario: Unknown node member

- **WHEN** a value is read from `{ "Reference": "morphir/SDK:basics#add", "x": 1 }`
- **THEN** the reader returns `unknown_member`

#### Scenario: Malformed name

- **WHEN** a canonical name or FQName string does not parse
- **THEN** the reader returns `invalid_name` with its location

#### Scenario: Wrong major version

- **WHEN** a document says `formatVersion: 3`
- **THEN** the v4 reader returns `unsupported_format_version_major`

### Requirement: Canonical names parse strictly

Canonical name, path and FQName strings SHALL parse to the existing name types and reject a spelling with more than one interpretation. Initialisms SHALL follow the Morphir name canonicalization decision.

#### Scenario: Initialism

- **WHEN** `value-in-USD` and `["value", "in", "u", "s", "d"]` are read as names
- **THEN** the results are equal

#### Scenario: FQName parts

- **WHEN** `morphir/SDK:list#map` is read as an FQName
- **THEN** its package is `morphir/SDK`, its module is `list` and its local name is `map`
