# Spec Delta

## MODIFIED Requirements

### Requirement: Environment mapping is deterministic
MORPHIR_ values SHALL use double underscores for nesting, retain single underscores, lowercase segments, and type booleans, integers, and JSON arrays/objects. Reserved operational variables, including the containment controls MORPHIR_DISCOVERY_BOUNDARY and MORPHIR_ISOLATED, SHALL NOT become ordinary keys. A shorter scalar path SHALL win over a conflicting nested environment path.

#### Scenario: Canonical field name
- **WHEN** MORPHIR_IR__FORMAT_VERSION is 4
- **THEN** ir.format_version is the integer 4

#### Scenario: Environment iteration order
- **WHEN** MORPHIR_IR and MORPHIR_IR__STRICT_MODE are supplied in either enumeration order
- **THEN** the shorter ir scalar wins in both resolutions

#### Scenario: Containment controls are not configuration
- **WHEN** MORPHIR_DISCOVERY_BOUNDARY and MORPHIR_ISOLATED are supplied
- **THEN** resolved configuration has no discovery_boundary or isolated key from the environment

## REMOVED Requirements

### Requirement: Legacy configuration entry points remain compatible
**Reason**: The cache-only resolver and its workspace discovery walk to the filesystem root and treat any Git metadata or `.morphir` directory as a workspace, including the global Morphir home. That contradicts the project-system boundary rules and cannot be contained. No restorable package release ever exposed these entry points: Morphir.Tooling's dependencies were not published until 0.4.0-alpha.1.
**Migration**: Use the document-aware loading and resolution entry points (`ProjectSystem.Discover` / `ProjectSystem.LoadDetailed` and the shared resolver). Replace the legacy CI mode with a named profile selected through `--profile` or `MORPHIR_PROFILES`.
