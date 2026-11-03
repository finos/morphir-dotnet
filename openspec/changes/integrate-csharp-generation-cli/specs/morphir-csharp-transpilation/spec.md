# Spec delta

## MODIFIED Requirements

### Requirement: Deterministic names and source artifacts

Generation SHALL return relative C# source paths and stable bytes independent of host paths, culture, time and unordered map enumeration. Names SHALL preserve qualification, escape keywords and avoid capturing locals. Unresolvable symbol or portable path collisions SHALL fail explicitly.

Generation SHALL additionally accept an immutable validated namespace override
through a source-compatible overload. Existing options constructors and the
three-argument generation entry point SHALL remain available with byte-identical
default output. An override SHALL change the namespace of only the input Library
package and every qualified reference to that package, preserving module, type,
value and constructor identities, package-derived source paths and the original
typed model. SDK and System qualifications SHALL remain unchanged. Naming state
SHALL be local to each request.

Namespace validation SHALL accept dotted literal C# identifiers with optional
`@` prefixes, reserved words only when escaped, and admitted Unicode identifier
characters in Normalization Form C. The literal-name boundary SHALL reject source
Unicode escape syntax and malformed UTF-16 rather than interpreting source text.
Null, empty, malformed and injection-like names SHALL produce a typed
`invalid-namespace` diagnostic at `$options/namespace`. An explicit null namespace
value at the generation overload SHALL fail without sources; caller cancellation
SHALL retain precedence.

#### Scenario: Reordered maps and cultures

- **WHEN** the same model is generated from different host paths and cultures with reordered unordered declarations
- **THEN** file paths and source bytes are identical while branch and argument order remain unchanged

#### Scenario: Keyword and collision

- **WHEN** a declaration is a C# keyword or two public names map to one target identity
- **THEN** the keyword is escaped and an unresolvable collision produces a diagnostic naming both owners

#### Scenario: Namespace and qualified references

- **WHEN** equivalent classic and v4 libraries with cross-module records, generic aliases, constructors and value calls use a validated namespace override
- **THEN** emitted sources compile and execute under both numeric profiles using that namespace, retaining the SDK equality protocol and original source paths

#### Scenario: Request isolation and default compatibility

- **WHEN** callers generate the same model with two distinct namespace overrides and the default entry point, including concurrent requests
- **THEN** each result uses only its own namespace and default sources remain byte-identical to generation before the override calls

#### Scenario: Invalid namespace boundary

- **WHEN** namespace parsing receives null, malformed identifiers or source syntax, or an explicit null override reaches generation
- **THEN** it returns the located typed namespace refusal with no successful sources, except that an already cancelled caller receives cancellation
