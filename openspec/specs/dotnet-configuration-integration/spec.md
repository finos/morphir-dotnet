# dotnet-configuration-integration Specification

## Purpose

Allows .NET hosts to consume resolved Morphir settings through standard configuration APIs while preserving the canonical document and host-independent resolution semantics.

## Requirements

### Requirement: Standard host integration is opt-in
A host SHALL be able to register resolved Morphir configuration through an additive adapter without referencing or constructing the CLI. Registration SHALL NOT implicitly replace unrelated host configuration sources or reconfigure logging.

#### Scenario: Embedded .NET host
- **WHEN** a non-CLI application registers Morphir configuration with supplied loading options
- **THEN** it can query standard .NET configuration while retaining its existing host configuration setup

### Requirement: Projection follows canonical resolution
The adapter SHALL project one already resolved Morphir result rather than re-merging raw file providers. Replacement arrays SHALL NOT retain lower-layer elements. Empty arrays and missing settings SHALL remain distinguishable to supported binding APIs.

#### Scenario: Shorter array reaches options
- **WHEN** three lower-layer codegen targets are replaced by one higher-layer target
- **THEN** standard configuration and supported options binding expose only the one target

### Requirement: Canonical metadata remains accessible
The typed Morphir document and its origins/diagnostics SHALL remain available alongside the string-based projection. Consumers SHALL NOT need to reconstruct the canonical document from flattened strings.

#### Scenario: Typed values survive alongside projection
- **WHEN** a document distinguishes the integer 4 from the string "4"
- **THEN** the canonical API retains that distinction even where the standard string view is identical

### Requirement: Unrepresentable key collisions fail visibly
Projection SHALL reject canonical paths that collide under .NET key comparison or hierarchical separators, naming the affected paths. It SHALL NOT silently overwrite case-distinct keys or collapse literal separator characters.

#### Scenario: Case-distinct extension settings
- **WHEN** a canonical document contains distinct custom.Foo and custom.foo keys
- **THEN** projection returns a collision diagnostic while the canonical document remains intact

### Requirement: Trimming does not change configuration behavior
The adapter and documented binding path SHALL work under Native AOT and trimming without reflection-dependent discovery of configuration types.

#### Scenario: Published host reads settings
- **WHEN** a minimal host using the documented adapter is published with AOT and trimming
- **THEN** it reads the same representative scalar, nested, and array settings as the ordinary host
