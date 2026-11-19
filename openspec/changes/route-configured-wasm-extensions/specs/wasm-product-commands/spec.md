## ADDED Requirements

### Requirement: Configured modules participate in product selection
The product SHALL select `[extensions.<name>].path` when the selected frontend
language or generation target names that extension. Explicit extension selection
SHALL take precedence, followed by the existing frontend/codegen extension setting.
An extension selection without a matching WASM path SHALL retain process behavior.

#### Scenario: Python target selects its configured module
- **GIVEN** `[extensions.python]` contains `path` naming the Python 0.6.0 module
- **WHEN** `morphir generate --target python` is run with valid model input
- **THEN** the configured WASM backend is selected without a development runner
- **AND** normal artifact validation and owned publication apply

#### Scenario: Python language selects its configured module
- **GIVEN** the Python module is configured
- **WHEN** `morphir compile --language python` is run on Python source files
- **THEN** the configured WASM frontend is selected
- **AND** the normal IR writer and compile task publication apply

#### Scenario: An explicit process selection is preserved
- **GIVEN** a Python WASM module is configured
- **WHEN** the caller explicitly selects a different process extension
- **THEN** the process extension is used

### Requirement: Module settings retain their configuration origin
Relative module paths SHALL resolve against their configuration source directory,
or the project root without a file origin. Optional ABI and digest pins SHALL pass
to the existing loader without reinterpreting its refusals.

#### Scenario: Relative configured path
- **WHEN** a configuration file names a relative extension path
- **THEN** the path is resolved against that configuration file's directory
- **AND** its declared ABI and digest remain attached to the selected module

### Requirement: Product adapters share the MEP model rules
WASM calls SHALL reuse the existing frontend/backend adapters' negotiation, tuple,
model, diagnostic and artifact checks. One command SHALL own one session and end
the channel on success, refusal or cancellation. Notifications SHALL retain the
runtime's existing advertisement rule. Guest output SHALL remain bounded on stderr.

#### Scenario: Real published bundle
- **WHEN** end-to-end CLI tests use the Python 0.6.0 bundle with its fixed SHA-256
- **THEN** compile publishes IR and generate publishes Python files
- **AND** the reports identify the extension and WASM transport

### Requirement: Runtime refusal remains explicit
A selected module on Windows x64 SHALL fail by name with
`extension.runtime_unavailable` and `win_x64_trap_unsupported` before module I/O.
Commands that select no WASM module SHALL retain their behavior and lazy probing.

#### Scenario: Windows x64 command
- **WHEN** compile or generate selects the configured Python module on Windows x64
- **THEN** the command fails with the named unavailable reason and publishes nothing

### Requirement: Experimental usage is documented
The user guide SHALL mark this capability experimental and give exact commands,
the pinned bundle identity, configuration path semantics and platform limitations.

#### Scenario: Configuration guide
- **WHEN** the user reads the extension path setting
- **THEN** it names the product commands that now consume it
- **AND** it no longer says no command reaches the setting
