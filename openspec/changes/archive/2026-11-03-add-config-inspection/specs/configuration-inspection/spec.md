# Spec Delta

## Purpose

Lets users and other hosts query effective Morphir settings, inspect particular sources, and understand why a value wins without modifying configuration.

## ADDED Requirements

### Requirement: Queries distinguish effective and scoped values
Queries SHALL use effective configuration by default. An explicit workspace, project, adjacent-user, global, system, or file scope SHALL inspect only that source scope and SHALL NOT fill missing entries from other scopes.

#### Scenario: Scoped missing value
- **WHEN** a setting exists only in global configuration and a workspace-scoped query requests it
- **THEN** the query reports not found while the effective query returns the global value

### Requirement: Read commands expose the shared query behavior
The CLI SHALL expose config get, list, show, path, and explain. Get SHALL return one typed value, list key/value entries, show the nested document, path consulted sources/status/order, and explain contributing layers plus the winner. Equivalent shared operations SHALL be available to non-CLI hosts.

#### Scenario: Winning member value
- **WHEN** a member overrides a workspace setting and config explain requests that key
- **THEN** the result identifies both contributions and the member winner

### Requirement: Key paths are unambiguous
Queries SHALL address canonical case-sensitive paths. Dots SHALL separate segments; double-quoted segments with JSON string escaping SHALL allow literal dots or special characters. Arrays SHALL be queried as whole values; malformed paths SHALL be usage errors.

#### Scenario: Named table containing a dot
- **WHEN** the key path is codegen."custom.generator".enabled
- **THEN** it selects the literal custom.generator segment beneath codegen rather than two nested segments

### Requirement: Origins and load statuses are inspectable
Queried results SHALL optionally include origin and scope metadata. Source reports SHALL distinguish loaded, missing optional, and failed sources. Missing keys SHALL remain distinguishable from present empty strings or arrays.

#### Scenario: Empty array is present
- **WHEN** config get requests a setting whose winning value is an empty array
- **THEN** it succeeds with that array and its origin rather than reporting a missing key

### Requirement: CLI results are scriptable
Configuration read commands SHALL support structured JSON with typed values and diagnostics, stdout reserved for results, and stderr for diagnostic text. Exit codes SHALL be 0 for success, 1 for missing queried keys, and 2 for usage/loading failures. Result ordering SHALL be deterministic.

#### Scenario: Warning in JSON mode
- **WHEN** config show --json succeeds with a mixed-format warning
- **THEN** stdout is valid JSON containing structured diagnostics and human diagnostic text is confined to stderr

#### Scenario: Nonfinite values retain types in JSON
- **WHEN** inspection presents NaN, positive infinity, negative infinity and an ordinary object containing a `$number` key
- **THEN** the nonfinite values have explicit tags and external JSON Pointer type metadata, the ordinary object remains distinguishable, finite values keep normal JSON types and shared redaction precedes encoding

#### Scenario: Published inspection serialization
- **WHEN** the inspection serializer runs under trimming or Native AOT with reflection serialization disabled
- **THEN** all nonfinite cases, tag-shaped user objects, nested values and contribution metadata retain the managed JSON representation

### Requirement: Inspection controls do not trigger hidden reads
The CLI SHALL expose selected-file, boundary, profile, isolation, and supported warning-suppression controls using the shared resolver. Help SHALL perform no configuration I/O. Invalid profile arguments SHALL fail before discovery.

#### Scenario: Help in broken workspace
- **WHEN** config get --help is requested in a directory with malformed configuration
- **THEN** help succeeds without reading that configuration

### Requirement: Sensitive inspection is redacted
Human and JSON inspection SHALL redact sensitive values while retaining safe key/scope/origin metadata. Inspection SHALL NOT resolve secret references, execute scripts, write files, or construct the CLI when called through shared host APIs.

#### Scenario: Embedded explanation
- **WHEN** an embedded host explains a sensitive key
- **THEN** it receives redacted data and safe source metadata without executing the referenced secret command
