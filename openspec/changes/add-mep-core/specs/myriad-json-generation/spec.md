# Spec Delta

## MODIFIED Requirements

### Requirement: Records follow the configured member rules
A record SHALL be a JSON object with members named by JsonName or the context naming policy. Non-option fields SHALL be required unless they declare a missing-member default or extension data. Explicit null SHALL retain configured null rules. Options SHALL be written as null or omitted as configured.

#### Scenario: Missing required member
- **WHEN** JSON for Person has no unannotated name member
- **THEN** deserialization fails with a JsonException naming name and Person

#### Scenario: Failure inside a nested value
- **WHEN** a member of a nested record has the wrong kind
- **THEN** the message names each member on the way to the failure

#### Scenario: Skippable option
- **WHEN** skippable options are enabled and Nickname is None
- **THEN** nickname is omitted and an absent nickname reads as None

### Requirement: Unknown and duplicate members follow the configured rule
Unknown members SHALL be ignored or refused as configured, or captured by an explicitly declared extension-data field. Duplicate members, including aliases of the same field, SHALL be refused with a JsonException naming the member and carrying the path reported by System.Text.Json.

#### Scenario: Refuse unknown members
- **WHEN** a type sets unknown members to refuse and JSON has an extra member
- **THEN** deserialization fails and names that member

#### Scenario: Duplicate member
- **WHEN** a record's name member appears twice
- **THEN** deserialization fails and names name

## ADDED Requirements

### Requirement: Record fields can declare wire defaults
A record field MAY opt into a JSON-literal default for an absent member. The default SHALL be read through the generated codec. An explicit null SHALL retain the field's null rules. Malformed JSON defaults SHALL fail generation. Unannotated fields SHALL remain required.

#### Scenario: Absent capability flag
- **WHEN** a boolean field declares the default `false` and its JSON member is absent
- **THEN** it reads as false, while a null member is refused

### Requirement: Empty collections can be omitted
A collection field MAY opt into omission when empty. Nonempty collections SHALL retain their configured encoding. Unsupported shapes SHALL fail generation.

#### Scenario: Optional diagnostic related information
- **WHEN** a related-information list declares an empty default and empty omission
- **THEN** an absent list reads as empty and an empty list is not written

### Requirement: Extension data is preserved without a wrapper
A record MAY have one extension-data field of a string-keyed JsonElement map type. Unknown members SHALL be captured and written beside named members. A repeated member or a write collision with a named member or its alias SHALL fail. An absent extension-data set SHALL be empty.

#### Scenario: Future capability
- **WHEN** a capability object includes an unknown nested member
- **THEN** its JSON value survives a read/write cycle without an extra wrapper member

### Requirement: Annotated version aliases migrate canonically
A record MAY declare a legacy alias and exact legacy/current versions. Readers SHALL require a matching name/version pair, refuse both aliases together, and normalize legacy values and paths. Writers SHALL use the current member and version.

#### Scenario: MEP claims migration
- **WHEN** a claim set uses statementVersion draft.1 and lists statementVersion as critical
- **THEN** it reads as claimsVersion draft.2 and writes the canonical current version and path

#### Scenario: Version-alias defaults are canonical
- **WHEN** a version-alias field also declares a missing-member default
- **THEN** generation accepts only a JSON string containing the exact current version, so an absent member cannot bypass the canonical-version invariant

#### Scenario: Unknown critical child
- **WHEN** a critical list includes a child path not present in its exact allowlist
- **THEN** the reader refuses it even if its parent is allowed

### Requirement: Critical paths are checked exactly
A critical-path field MAY declare an exact allowlist. Readers and writers SHALL refuse an unknown path. A legacy version alias SHALL refuse a critical path naming the current version member.

#### Scenario: Current path on legacy input
- **WHEN** a draft.1 statement lists claimsVersion as critical
- **THEN** the reader refuses it instead of treating it as a migrated legacy path
