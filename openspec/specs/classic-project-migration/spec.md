# classic-project-migration Specification

## Purpose

Allows a classic morphir-elm project to adopt the newer Morphir project system through a loss-checked, repeatable migration shared by CLI and other hosts.

## Requirements

### Requirement: Project migration owns a single project
The CLI SHALL expose project migrate [path], backed by shared planning/application services. Path SHALL identify one project directory or classic morphir.json; omission SHALL select the current unambiguous project. It SHALL NOT migrate IR files or implicitly migrate workspace siblings.

#### Scenario: Member invocation
- **WHEN** project migrate is invoked inside one workspace member
- **THEN** only that project's migration is planned and workspace membership is unchanged

#### Scenario: IR file supplied
- **WHEN** project migrate is supplied morphir-ir.json
- **THEN** it returns a usage error identifying project configuration as the expected input

### Requirement: Output preserves project location
Migration SHALL default to adjacent morphir.toml and permit an explicit TOML primary layout at the same project location. It SHALL reject output that changes relative-path meaning or introduces primary ambiguity.

#### Scenario: Dot-config output
- **WHEN** a project at /repo/orders migrates to /repo/orders/.config/morphir/config.toml
- **THEN** its source directory remains relative to /repo/orders

### Requirement: Classic values and implicit defaults survive
Migration SHALL map name, sourceDirectory, and exposedModules to canonical project fields and explicitly materialize legacy-derived Elm/checkpoint defaults needed for equivalent behavior after the classic source is absent. It SHALL preserve supported extra settings without pinning an unrelated IR-version change.

#### Scenario: Elm manifest becomes independent TOML
- **WHEN** a classic manifest selects Elm and checkpoint behavior implicitly
- **THEN** the new TOML retains those choices when the classic manifest is excluded

### Requirement: Loss accounting blocks incomplete conversion
Every source setting SHALL be mapped, preserved with equivalent meaning, or reported as blocking. Unmapped dependency/decorations shapes or unrepresentable values SHALL NOT be silently omitted. The migration SHALL retain the raw source for loss accounting even when compatibility loading ignores a field.

#### Scenario: Unsupported nonempty dependency
- **WHEN** a classic dependency value has no supported canonical mapping
- **THEN** dry-run identifies the field and application writes nothing

### Requirement: Existing targets merge only without conflict
Existing equivalent target values SHALL remain unchanged; nonconflicting missing values SHALL be added while preserving unrelated text. Different target values or same-file legacy/canonical conflicts SHALL block application. Repeating a completed migration SHALL be a no-op.

#### Scenario: Conflicting source directory
- **WHEN** the source maps to elm-src but existing target TOML sets project.source_directory to src
- **THEN** migration reports both values and leaves both files unchanged

### Requirement: Candidate equivalence excludes the legacy source
Verification SHALL compare before/after project values and resolved path meaning with the classic source removed from the candidate. It SHALL exclude ambient machine, personal, environment, CLI, and activated-profile overlays that could mask a mismatch, retain declared base workspace context, and report expected origin changes separately.

#### Scenario: Ambient override cannot hide a mismatch
- **WHEN** an environment value makes two differing project source directories appear equal
- **THEN** verification still detects the underlying project-value mismatch and refuses application

### Requirement: Preview does not mutate files
Project migrate SHALL support --dry-run, --output, and --json. Preview SHALL report paths, mappings, edits, blocking diagnostics, and equivalence status without writing. Non-CLI hosts SHALL receive the same structured plan with redacted sensitive values.

#### Scenario: Read-only host migration preview
- **WHEN** an embedded host plans migration without a write adapter
- **THEN** it receives the complete plan and the source tree remains unchanged

### Requirement: Verified application retains classic inputs
Only a verified plan SHALL be applied. Source, target, and relevant workspace preconditions SHALL be rechecked. Application SHALL retain morphir.json unchanged and use the configuration editor's atomic-write and recovery behavior.

#### Scenario: Source changes after planning
- **WHEN** the classic manifest changes before application
- **THEN** migration reports a stale plan and does not replace the target

#### Scenario: Older consumer remains usable
- **WHEN** migration succeeds
- **THEN** the original classic manifest remains byte-identical for consumers that still require it

### Requirement: Results distinguish migration from configuration editing
The command SHALL live under project, not config. Exit 0 SHALL mean verified success, an applicable preview, or an unchanged repeat; exit 2 SHALL identify invalid input, loss/conflict, verification, or write failures. JSON results SHALL retain structured diagnostics and stdout/stderr discipline.

#### Scenario: Blocked dry-run in JSON mode
- **WHEN** project migrate --dry-run --json finds an unmapped field
- **THEN** it exits 2 with a structured blocked plan on stdout and diagnostic text only on stderr
