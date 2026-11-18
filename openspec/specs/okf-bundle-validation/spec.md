# okf-bundle-validation Specification

## Purpose

Let .NET callers and CI validate a knowledge bundle with the appropriate OKF conformance rules and, when requested, Morphir knowledge-base policy.

## Requirements

### Requirement: Bundle validation shares one library contract across input providers

Callers SHALL be able to load a knowledge bundle from a directory or an in-memory virtual file source, validate a single parsed document, and validate the whole bundle. A bundle SHALL retain its member paths, document parse outcomes and asset entries. Invalid or unreadable members SHALL produce findings while the validator continues checking other members when possible.

#### Scenario: Equivalent virtual and physical bundles
- **WHEN** a directory and an in-memory source hold the same document paths and contents
- **THEN** both validations return equivalent findings in the same order

#### Scenario: Independent member failures
- **WHEN** one member has unreadable contents and another contains a malformed header
- **THEN** the report identifies both members and includes a range for the syntax error

### Requirement: Conformance uses the selected OKF version

Validation SHALL support OKF v0.2 and v0.3. It SHALL use an explicit supported version when supplied, then the root index declaration if present, then v0.3. For an unrecognized declaration, it SHALL identify the version as unverified and report the available best-effort findings. It SHALL apply the selected version's concept and reserved-file rules.

#### Scenario: Plain Markdown under different versions
- **WHEN** a non-reserved README has no frontmatter
- **THEN** v0.3 treats it as ordinary Markdown and it does not fail conformance, while v0.2 reports the missing concept frontmatter as a conformance error

#### Scenario: Reserved documents and extensions
- **WHEN** a v0.3 bundle contains a valid log, a concept with an unfamiliar type and nested extension fields, and no root index
- **THEN** it passes conformance and the log is excluded from concept lookup

#### Scenario: A newer revision is declared
- **WHEN** a root index declares an unsupported version and the caller has not selected an override
- **THEN** the report identifies the declared version and distinguishes best-effort findings from verified conformance

### Requirement: Bundle lookup follows OKF path semantics

Lookup SHALL derive concept IDs from bundle-relative paths and resolve legal document-relative and bundle-root-relative references. It SHALL distinguish concept, reserved document, asset, unresolved and external targets as applicable. Validation SHALL remain inside the supplied bundle and SHALL perform no external fetches or computation execution.

#### Scenario: Internal and external targets
- **WHEN** a concept links to another concept by both legal path forms, an asset, a missing member and an outside URL
- **THEN** the first two forms resolve to the same concept, the asset is recognized, the missing link is reported without failing OKF conformance and the external URL is left unresolved locally

#### Scenario: A path crosses a physical bundle boundary
- **WHEN** a physical bundle entry is a symbolic link whose target is outside the bundle
- **THEN** validation never reads the target and reports the excluded entry if encountered

### Requirement: Morphir policy can enforce authoring conventions

Callers SHALL be able to opt into Morphir knowledge-base rules requiring a root index with a version declaration and checking local references, authored index descriptions and source-citation IDs. Findings promoted by the policy SHALL be distinguishable from versioned OKF conformance findings.

#### Scenario: A broken internal link is a policy finding
- **WHEN** a conformant v0.3 bundle has an unresolved internal link and omits its root index
- **THEN** the Morphir policy reports errors for the link and missing index while the OKF conformance result remains passing

#### Scenario: A citation points to an undeclared source
- **WHEN** a concept uses a claim footnote whose label does not match an ID in its frontmatter sources
- **THEN** the Morphir policy reports a finding at the citation

### Requirement: The CLI reports validation for CI

`morphir knowledge validate <bundle>` SHALL expose the library validation with a version override, optional Morphir policy, and text or JSON output. Reports SHALL have stable codes, severities, message text and applicable paths and source ranges, in deterministic order. The command SHALL return 0 for a completed check with no errors, 1 for findings of error severity and 2 when input or invocation prevents the check.

#### Scenario: Structured findings
- **WHEN** the CLI validates a bundle with a malformed document using JSON output
- **THEN** it emits a single parseable report on stdout, includes the document path and range, and returns exit code 1

#### Scenario: The bundle root cannot be read
- **WHEN** the CLI is given a missing bundle directory
- **THEN** it reports that the check could not run and returns exit code 2
