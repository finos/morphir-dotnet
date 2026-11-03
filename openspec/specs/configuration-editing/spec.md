# configuration-editing Specification

## Purpose

Lets CLI and other hosts plan and apply scoped TOML configuration edits without losing unrelated document content or silently changing the wrong source.

## Requirements

### Requirement: Each edit has one explicit target
Edits SHALL select exactly one workspace, project, adjacent-user, global, system, or explicit TOML target. The default SHALL be the current unambiguous project or the workspace primary at its root. Ambiguous multi-project targets, conflicting selectors, or missing default contexts SHALL fail before writing.

#### Scenario: Virtual workspace root
- **WHEN** a set operation is invoked at a declared virtual workspace root without a target selector
- **THEN** the workspace primary is targeted rather than all member projects

#### Scenario: Explicit YAML target
- **WHEN** an edit directly selects settings.yaml
- **THEN** it fails without modifying or silently converting that file

### Requirement: Set accepts typed complete values
Set SHALL support explicit string, boolean, integer, number, array, and object inputs using the shared key-path grammar. String SHALL be the default input type. Known settings SHALL validate before application; JSON output selection SHALL NOT change input typing.

#### Scenario: Boolean input
- **WHEN** config set ui.color false --type bool targets global configuration
- **THEN** a boolean false is written rather than the string "false"

#### Scenario: Array replacement
- **WHEN** a set operation replaces three codegen targets with one
- **THEN** the target entry contains exactly the replacement array

### Requirement: Edits preserve TOML presentation
Edits SHALL preserve unrelated entries, comments, and formatting. Lower-format-only scopes SHALL receive the corresponding TOML primary without modifying JSON/YAML. A target that would introduce same-format primary ambiguity SHALL be refused.

#### Scenario: YAML-only project
- **WHEN** a scoped set targets a project whose only primary is morphir.yaml
- **THEN** morphir.toml is created with the changed value and the YAML bytes remain unchanged

#### Scenario: Unrelated comments
- **WHEN** one TOML key is changed
- **THEN** unrelated comments and entries retain their original text

### Requirement: Unset removes only the targeted entry
Unset SHALL remove the entry from its TOML target without deleting values from other sources or adding tombstones. A missing target entry SHALL be reported as not found.

#### Scenario: Lower JSON value reappears
- **WHEN** a TOML source-directory override is removed above a classic JSON manifest
- **THEN** the resulting effective value comes from JSON and is reported with that origin

### Requirement: Plans are reviewable without writes
Shared edit services SHALL expose read-only planning and explicit application. CLI set/unset SHALL support --dry-run and --json. Plans SHALL report target, changes, validation, and resulting effective values, redacting sensitive values.

#### Scenario: Embedded preview
- **WHEN** a non-CLI host plans an edit without applying it
- **THEN** it receives a structured plan and no file is modified

### Requirement: Application checks observed inputs
Before replacing a target, application SHALL verify source/target preconditions used by the plan and install the file atomically through the host adapter. A stale plan, validation failure, or conflict SHALL leave target bytes unchanged. Repeat identical writes SHALL not rewrite the file.

#### Scenario: Concurrent target edit
- **WHEN** another writer changes the target after planning
- **THEN** application reports a conflict and preserves the other writer's contents

#### Scenario: New primary appears
- **WHEN** another writer creates an alternate TOML primary after planning
- **THEN** application refuses the stale target selection without writing

### Requirement: Results explain effective outcomes
Successful edits SHALL report the target, written/unchanged outcome, and resulting effective value/origin. A higher overriding source SHALL be identified without turning a successful write into a failure.

#### Scenario: Environment still wins
- **WHEN** TOML is successfully changed but an environment setting still overrides it
- **THEN** the result identifies the environment winner and the successful file edit

### Requirement: Command results have stable failure categories
The CLI SHALL expose config set/unset using shared services, with exit 0 for success, 1 for a missing unset target entry, and 2 for usage, validation, I/O, or conflict failures. JSON stdout and stderr diagnostics SHALL follow the inspection contract.

#### Scenario: Invalid typed value
- **WHEN** --type int receives a non-integer
- **THEN** the command exits 2 and the target is unchanged

### Requirement: Transaction artifacts have explicit ownership
Successful or fully rolled-back edits SHALL remove their temporary artifacts. An incomplete recovery SHALL return a typed failure identifying retained recovery data and SHALL NOT claim unchanged or successful completion.

#### Scenario: Replacement failure with incomplete recovery
- **WHEN** an adapter cannot restore an earlier state after a write failure
- **THEN** the result reports the uncertain outcome and retained recovery location rather than deleting recoverable data
