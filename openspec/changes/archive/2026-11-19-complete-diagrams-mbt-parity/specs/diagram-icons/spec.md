# Spec Delta

## Purpose

Identifies and draws participant and architecture symbols without runtime lookup, from a frozen catalogue that records each symbol's source, digest, licence and attribution. Provider symbols are copied unchanged under their owners' terms; symbols whose terms the project cannot meet are drawn originally for the project.

## ADDED Requirements

### Requirement: Icon references
An icon reference SHALL be an immutable namespace and key, independent of participant kind. Aliases SHALL resolve through the catalogue while the syntax keeps the declared spelling.

#### Scenario: Alias
- **WHEN** a ZenUML participant is annotated `@postgres`
- **THEN** it resolves to the PostgreSQL symbol and the syntax still reads `@postgres`

### Requirement: Complete catalogue coverage
Every key and alias of the ZenUML core 3.47.9 participant registry, and the architecture defaults `cloud`, `database`, `disk`, `internet` and `server`, SHALL resolve to a symbol without a fallback warning.

#### Scenario: Representative keys
- **WHEN** a diagram uses Actor, Boundary, Control, Entity, Database, EC2, Lambda, AzureFunction, BigQuery, PostgreSQL and Kubernetes
- **THEN** each renders its own symbol with its label

### Requirement: Provenance and notices
Each catalogue record SHALL state its source, original and normalised digests, licence, attribution and provider terms. The package SHALL ship third-party and trademark notices. A symbol under share-alike terms SHALL NOT be embedded; the project SHALL draw its own symbol for that key.

#### Scenario: Catalogue record
- **WHEN** the record for `Lambda` is read
- **THEN** it names Amazon Web Services, the AWS icon terms and the asset digest

### Requirement: Unknown symbols stay visible
An unresolved key SHALL produce a warning naming the key and element, keep the label, and draw a visible generic symbol. Nothing SHALL be fetched.

#### Scenario: Unknown key
- **WHEN** a participant is annotated `@custom:missing`
- **THEN** the SVG shows the generic symbol and a warning names `custom:missing`

### Requirement: Inline, isolated output
Only symbols a diagram uses SHALL be emitted, as inline vector definitions scoped to the diagram's ID, shared by repeated uses. Symbols SHALL be visible in dark and light themes, keep provider colours, and carry accessible text naming the symbol.

#### Scenario: Two diagrams on a page
- **WHEN** two diagrams using the same symbol are inlined in one page
- **THEN** their definitions have different IDs and neither affects the other
