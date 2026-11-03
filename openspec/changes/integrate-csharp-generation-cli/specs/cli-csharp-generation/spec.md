# Spec Delta

## Purpose

Expose the reusable C# backend through the existing generation command with explicit options, equivalent in-memory behavior, structured reporting and safe owned publication.

## ADDED Requirements

### Requirement: Exact C# target registration

The generation command SHALL resolve exact target csharp to the first-party C# provider and verify its declared model versions, distribution kinds and required semantics before generation. Existing target selection and F# behavior SHALL remain unchanged.

#### Scenario: Supported input representations

- **WHEN** equivalent supported classic or v4 JSON, Ion text, Ion binary and Ion-tree libraries are generated with target csharp
- **THEN** each reaches the typed C# backend and produces equivalent C# source artifacts

#### Scenario: Incompatible model or ambiguous provider

- **WHEN** a valid incompatible distribution or multiple compatible csharp providers are present
- **THEN** selection fails explicitly and publishes nothing

### Requirement: Validated C# backend options

The C# provider SHALL accept language-version values 14 or 15, defaulting to 14, and an optional valid C# namespace. Existing last-occurrence option precedence SHALL apply. Unknown option names, unsupported versions, malformed namespaces and wrong JSON types SHALL fail before emission.

#### Scenario: Explicit RC language target

- **WHEN** the user supplies --target csharp --option language-version=15
- **THEN** the provider generates with the C# 15 profile and reports target and provider identity

#### Scenario: Repeated option and default

- **WHEN** language-version is omitted or supplied as 15 and then 14
- **THEN** the effective language version is 14

#### Scenario: Invalid options

- **WHEN** the invocation supplies language-version=preview, a string instead of a numeric language version, or a namespace containing source syntax
- **THEN** the invocation returns code 2 with a specific option diagnostic and no publication

### Requirement: Equivalent library and CLI generation

The provider SHALL adapt the typed library result without reimplementing naming, lowering or SDK mappings. Equivalent model and generation options SHALL produce the same source paths and bytes through direct library use and CLI generation.

#### Scenario: Host parity

- **WHEN** a loaded model is generated directly and through the C# provider with equivalent options
- **THEN** the C# source paths and bytes agree; publication metadata remains a host concern

### Requirement: Safe C# artifact publication

C# generation SHALL use the existing owned publication contract. Failed generation, cancellation before publication, unsafe paths and input/output overlap SHALL leave previous output unchanged. Repeat generation SHALL report unchanged files and preserve unowned files.

#### Scenario: Unchanged rerun

- **WHEN** the same C# model is generated twice to the same output
- **THEN** the second result reports unchanged sources without rewriting them

#### Scenario: Failure preserves output

- **WHEN** a later generation fails semantically or its output overlaps the input
- **THEN** the previous successful sources and unrelated user files remain intact

#### Scenario: Removed owned module

- **WHEN** a successful regeneration no longer produces a previously owned module
- **THEN** the publisher removes that owned source and preserves unrelated files

### Requirement: C# reporting and exit outcomes

Parsed C# invocations with --json SHALL write one parseable generation report to stdout and diagnostics only to stderr. Exit codes SHALL remain 0 for success, 1 for model/semantic failure, 2 for invocation/operational failure and 130 for cancellation.

#### Scenario: Structured semantic failure

- **WHEN** a parsed JSON invocation reaches an unsupported C# construct
- **THEN** stdout contains one failure report with stable diagnostics and exit code 1 without log contamination

#### Scenario: Unavailable provider option

- **WHEN** the user supplies an unsupported C# language version
- **THEN** the report identifies invocation failure, exit code 2, and no published artifacts

### Requirement: Published CLI execution

The registered C# provider SHALL work in trimmed and Native AOT CLI builds through static registration. Generation SHALL NOT require a compiler installation; compiling generated sources remains a separate consumer action.

#### Scenario: Published CLI smoke

- **WHEN** a trimmed or Native AOT CLI generates the nonempty pricing fixture with JSON reporting
- **THEN** the report parses, the source artifacts are present, and a separate consumer with the matching compiler builds and executes them
