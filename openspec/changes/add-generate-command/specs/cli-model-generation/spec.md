# Spec Delta

## Purpose

Generate source artifacts from a retained Morphir model through a compatible backend, with explicit settings, actionable diagnostics and safe output publication.

## ADDED Requirements

### Requirement: Explicit generation invocation

The CLI SHALL expose `morphir generate` with `--input`, `--target` and `--output`. This delivery SHALL require all three after settings resolution. `--input-format` SHALL accept `auto`, `json` or `ion` and default to `auto`. The command SHALL accept repeated `--option KEY=VALUE` and `--json`.

#### Scenario: Generate from a model file

- **WHEN** the user supplies a supported classic or v4 library, target `fsharp` and an output directory
- **THEN** generation produces F# artifacts and reports the selected target, input and output

#### Scenario: A required setting is absent

- **WHEN** input, target or output remains unspecified after settings resolution
- **THEN** the command reports the missing setting, exits with code 2 and publishes nothing

#### Scenario: Future flags are not silently accepted

- **WHEN** this delivery receives `--config`, `--project` or another unimplemented flag
- **THEN** invocation fails with code 2 without pretending to discover project settings

### Requirement: Resolved settings are independent of their source

Generation SHALL consume fully resolved settings without reading project configuration itself. Equivalent resolved settings supplied by different callers SHALL select the same input, backend, options and output policy. This delivery SHALL resolve explicit CLI values without requiring a workspace or configuration file.

#### Scenario: Standalone model outside a project

- **WHEN** a valid invocation runs where there is no Git repository or Morphir configuration
- **THEN** generation succeeds using the explicit settings

#### Scenario: Equivalent caller settings

- **WHEN** the CLI and an application caller provide equivalent resolved settings
- **THEN** they produce equivalent generation requests and artifacts

### Requirement: Capability-based backend selection

Generation SHALL resolve the exact target through registered providers and verify the model version and distribution kind against the provider's declared capabilities. Unknown targets, incompatible models and multiple matching providers SHALL fail without arbitrary selection, fallback or output publication.

#### Scenario: Unknown target

- **WHEN** the requested target has no registered provider
- **THEN** the diagnostic identifies the target and available target IDs

#### Scenario: Supported v4 model

- **WHEN** a valid v4 library using supported expressions is loaded and target `fsharp` is selected
- **THEN** generation produces executable F# using v4 semantics without downgrading to classic IR

#### Scenario: Unsupported distribution

- **WHEN** a valid Specs or Application distribution is offered to the F# Library provider
- **THEN** generation reports distribution incompatibility rather than treating the model as malformed or an empty library

#### Scenario: Ambiguous registration

- **WHEN** two registered providers match the target and model capabilities
- **THEN** resolution reports the conflicting provider identities and invokes neither

### Requirement: Backend option resolution

Each option SHALL contain a nonempty key and an `=` separator. Values SHALL parse as JSON when possible and otherwise as strings. Later CLI occurrences SHALL replace earlier values of the same key. The selected backend SHALL validate names and types before generation. F# SHALL support Boolean `optimize`, defaulting to true, and reject other options in this delivery.

#### Scenario: Explicit optimization choice

- **WHEN** the user supplies `--option optimize=false`
- **THEN** F# generation runs without constant folding while preserving generated program behavior

#### Scenario: Repeated override

- **WHEN** the user supplies `--option optimize=true --option optimize=false`
- **THEN** the effective option is the Boolean false

#### Scenario: Wrong type or unknown name

- **WHEN** F# receives `optimize="sometimes"` or an unsupported option name
- **THEN** generation reports the invalid option before publishing any output

### Requirement: Backends return artifacts without publishing them

A backend SHALL return artifacts and diagnostics without writing the requested output directory or resolving host configuration. Generated paths SHALL be relative to the publication root. Generation results SHALL preserve deterministic F# module ordering and binary or text content without imposing JSON serialization on the model.

#### Scenario: In-memory generation

- **WHEN** an application invokes the registered F# backend with an already loaded supported model
- **THEN** it receives the generated file tree and ordered manifest without filesystem publication

### Requirement: Safe owned publication

The command SHALL publish only successful generation output using the existing safe-tree-publication contract and an ownership manifest. It SHALL reject output that overlaps input, and report written, unchanged and removed files. The ordered manifest SHALL remain available to consumers that compile F# modules in dependency order.

#### Scenario: Repeat generation

- **WHEN** identical output is generated twice to the same directory
- **THEN** the second result reports unchanged files without rewriting them

#### Scenario: Removed module and unrelated file

- **WHEN** regeneration removes an owned module while the output directory also contains an unowned file
- **THEN** publication removes the previously owned module and preserves the unowned file

#### Scenario: Failed generation or invalid artifact path

- **WHEN** generation fails or any returned artifact has an escaping or colliding path
- **THEN** publication is refused and previous output remains unchanged

#### Scenario: Input and output overlap

- **WHEN** the output contains an input file or overlaps an input tree
- **THEN** the command refuses publication before it can replace or enumerate its own input as generated output

### Requirement: Structured generation reporting

For a parsed invocation with `--json`, the command SHALL emit one source-generated JSON result on stdout and diagnostics only on stderr. Results SHALL include success, target, input, output, provider identity when resolved, artifacts, publication status and structured diagnostics. Diagnostics SHALL carry stable codes and available paths and model locations without arbitrary payload dumps.

#### Scenario: Machine-readable failure

- **WHEN** a parsed JSON-mode invocation fails model loading or generation
- **THEN** stdout contains one parseable failure result, diagnostics are structured, and logs do not contaminate stdout

#### Scenario: Output reflects publication

- **WHEN** generation succeeds but writing output fails
- **THEN** the result reports failure and does not label uncommitted artifacts as published

### Requirement: Exit and cancellation outcomes

The command SHALL return 0 on successful publication, 1 for malformed or incompatible models and backend semantic failures, 2 for invalid settings, provider resolution or operational failures, and 130 for cancellation. Cancellation before publication SHALL leave earlier output unchanged. Cancellation during publication SHALL follow the publisher's documented commit guarantees.

#### Scenario: Cancellation before publication

- **WHEN** cancellation is requested after generation but before publication starts
- **THEN** the command returns 130 and publishes no replacement files

### Requirement: Trimming-compatible backend execution

The first-party generation command SHALL remain usable when published with trimming and Native AOT. Provider registration and result reporting SHALL not depend on runtime assembly scanning, dynamic code generation or reflection-based JSON serialization.

#### Scenario: Published executable

- **WHEN** the published trimmed or Native AOT CLI generates from a supported model
- **THEN** it produces the expected F# files and a parseable JSON result
