# Spec Delta

## Purpose

Reads the MEP `morphir_model` document that a `morphir-model`/`ion-binary` payload decodes to, keeping its exact bytes and unknown data, so later MEP work can admit a model without losing anything.

## ADDED Requirements

### Requirement: Payload reading requires binary Ion

Reading a decoded `ion-binary` payload SHALL require the Ion 1.0 binary marker `E0 01 00 EA` and exactly one top-level value. A separate entry for local files SHALL also accept the text spelling.

#### Scenario: Reference payload

- **WHEN** the 499-byte `rich-model.ionb` fixture is read as a payload
- **THEN** the read succeeds

#### Scenario: Text given as a payload

- **WHEN** `rich-model.ion` is read as a payload
- **THEN** the reader returns `invalid_model_payload`

#### Scenario: Text read as a local file

- **WHEN** `rich-model.ion` is read through the local file entry
- **THEN** the read succeeds, and its values equal those of the binary fixture

#### Scenario: Two top-level values

- **WHEN** a payload holds two top-level values
- **THEN** the reader returns `invalid_model_payload`

### Requirement: The document shape is checked

The top-level value SHALL be a struct annotated `morphir_model` holding exactly one each of `ir`, `enrichments` and `provenance`. Each enrichment SHALL be a struct with a nonempty namespaced `owner` and a `data` member.

#### Scenario: Missing annotation

- **WHEN** the top-level struct is not annotated `morphir_model`
- **THEN** the reader returns `invalid_model_document`

#### Scenario: Repeated reserved field

- **WHEN** the top-level struct holds `ir` twice
- **THEN** the reader returns `invalid_model_document`

#### Scenario: Repeated names in unknown data

- **WHEN** an enrichment's `data` holds a repeated field name
- **THEN** the read succeeds and keeps both fields

#### Scenario: Enrichment without an owner

- **WHEN** an enrichment has no `owner`, or an empty one
- **THEN** the reader returns `invalid_model_document`

### Requirement: Reading retains the model exactly

A successful read SHALL keep the original bytes unchanged, and SHALL expose enrichments and provenance as Ion values with their annotations, numeric exactness and unknown members intact.

#### Scenario: Original bytes

- **WHEN** `rich-model.ionb` is read
- **THEN** the retained bytes have SHA-256 `49f87687c033a8ec39a7f42b93bb1c54b4856ceab0b05246d81ea0a8b27e10b1`

#### Scenario: Unknown enrichment

- **WHEN** `rich-model.ionb` is read
- **THEN** its enrichment keeps `source_name::target_name` in order, `exact` is `9007199254740993`, `amount` is `123.4500`, `kind` is the symbol `int64`, and `futureFact` keeps its `future_vocabulary` annotation

#### Scenario: Provenance

- **WHEN** `rich-model.ionb` is read
- **THEN** provenance holds one value annotated `fixture_origin`

### Requirement: Limits apply to the document

Reading SHALL apply caller-supplied byte, depth and node limits, and SHALL report them with the reference corpus codes.

#### Scenario: Corpus limit cases

- **WHEN** the fixture is read under limits lower than its byte count, depth or node count
- **THEN** the reader returns `decoded_limit_exceeded`, `depth_limit_exceeded` or `node_limit_exceeded` respectively

### Requirement: The IR has a typed view

Asking for the IR SHALL take the expected IR version and SHALL decode `ir` through the JSON reader for that version. A failure in the IR view SHALL NOT invalidate the retained model.

#### Scenario: Reference IR

- **WHEN** the IR of `rich-model.ionb` is requested with version `4.0.0`
- **THEN** the result equals the IR read from `plain-ir.json`

#### Scenario: Version mismatch

- **WHEN** the IR of `rich-model.ionb` is requested with version `3.0.0`
- **THEN** the reader returns `ir_version_mismatch`

#### Scenario: Ion-only data in the IR member

- **WHEN** `ir` holds an Ion timestamp where JSON has no equivalent
- **THEN** the IR view returns `ir_not_json_compatible` and the retained model stays readable

#### Scenario: Repeated JSON member in the IR

- **WHEN** a struct inside `ir` holds a repeated field name
- **THEN** the IR view returns `ir_not_json_compatible`
