# Spec Delta

## MODIFIED Requirements

### Requirement: Every supported format version has a writer
The system SHALL write library distributions as JSON for classic format versions 1, 2 and 3 and for IR v4 (`4`), and specification distributions as JSON for classic format version `3.1.0`. Each writer SHALL use its version's tag spelling, access wrapper, module list shape and distribution tag. A new version, including a pre-release, SHALL be added by declaring a writer, not by changing an existing one.

#### Scenario: Version-specific spelling
- **WHEN** one model with a let-definition and a public module is written as versions 1, 2 and 3
- **THEN** version 1 uses `let_definition` and `["public", ...]`, version 2 uses `let_definition` and `{"access":"Public"}`, and version 3 uses `LetDefinition` and `{"access":"Public"}`

#### Scenario: Unsupported output version
- **WHEN** a caller requests format version 5, an Application distribution, or a Specs distribution at any version other than `3.1.0`
- **THEN** writing fails with a diagnostic that names the requested version or kind and the supported set, and writes no document

#### Scenario: A classic Specs distribution at 3.1.0
- **WHEN** a classic v3 Specs distribution is written for requested version `3.1.0`
- **THEN** the document has `"formatVersion": "3.1.0"` and the Specs distribution tag, and reading it back gives an equal distribution
