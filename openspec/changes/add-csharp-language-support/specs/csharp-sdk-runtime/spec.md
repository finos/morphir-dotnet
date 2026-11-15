# Spec Delta

## MODIFIED Requirements

### Requirement: Structural equality and hashing

Supported SDK values SHALL compare by Morphir value semantics, including nested collections, records and variants. Plain C# records and plain C# unions SHALL be supported values and SHALL compare with their own equality. Equal hashable values SHALL have equal hashes independent of insertion history. Unsupported equality or ordering domains SHALL fail explicitly.

#### Scenario: Independent equal structures

- **WHEN** two independently constructed values contain equal nested immutable collections
- **THEN** value equality succeeds and their hashes agree

#### Scenario: Distinct keys and values

- **WHEN** two dictionaries have the same keys with a different value at one key
- **THEN** they compare unequal while preserving the selected key ordering

#### Scenario: Plain record in a collection

- **WHEN** two lists hold equal plain records that do not implement the Morphir equality interface
- **THEN** value equality succeeds and their hashes agree

#### Scenario: Plain union

- **WHEN** two plain unions hold the same case and value
- **THEN** value equality succeeds, and two unions with different cases are not equal

#### Scenario: Function-typed field

- **WHEN** a value holds a function-typed field
- **THEN** equality fails explicitly and does not compare references
