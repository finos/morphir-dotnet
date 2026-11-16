# Spec Delta

## MODIFIED Requirements

### Requirement: Validated C# backend options

The C# provider SHALL accept the language-version value 15, defaulting to 15, an optional valid C# namespace, a profile (`imperative` or `functional`), and the handling options of the C# handling options capability. Existing last-occurrence option precedence SHALL apply. Unknown option names, unsupported versions, malformed namespaces and wrong JSON types SHALL fail before emission.

#### Scenario: Explicit RC language target

- **WHEN** the user supplies --target csharp --option language-version=15
- **THEN** the provider generates with the C# 15 profile and reports target and provider identity

#### Scenario: Repeated option and default

- **WHEN** language-version is omitted
- **THEN** the effective language version is 15, and a repeated option uses its last occurrence

#### Scenario: Older language version

- **WHEN** the invocation supplies language-version=14
- **THEN** the invocation returns code 2 with a diagnostic that names 15 as the supported version, and no publication

#### Scenario: Profile option

- **WHEN** the user supplies --option profile=functional
- **THEN** the provider writes the functional form of each traversal

#### Scenario: Invalid options

- **WHEN** the invocation supplies language-version=preview, a string instead of a numeric language version, or a namespace containing source syntax
- **THEN** the invocation returns code 2 with a specific option diagnostic and no publication

## ADDED Requirements

### Requirement: Generation runs the upgrade and the passes

Before the C# backend, `morphir generate --target csharp` SHALL upgrade a classic model and SHALL run the target-side passes for `csharp`. A pass error SHALL stop the run before publication.

#### Scenario: Pass error

- **WHEN** a target-side pass reports an error for the model
- **THEN** the command returns a failure exit code and publishes nothing

#### Scenario: Checks of the backend

- **WHEN** a model has a name collision that the backend checked before this change
- **THEN** the same diagnostic is reported, now by a target-side pass
