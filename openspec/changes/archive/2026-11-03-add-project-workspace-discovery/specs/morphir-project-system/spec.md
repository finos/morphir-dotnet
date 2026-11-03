# Spec Delta

## Purpose

Identifies Morphir project and workspace locations, members, and applicable configuration sources consistently across native and mounted host filesystems.

## ADDED Requirements

### Requirement: Standard primary layouts are discoverable
Discovery SHALL inspect morphir.<ext>, .morphir/morphir.<ext>, and .config/morphir/config.<ext> for json, json5, yaml, yml, and toml. The enclosing project directory SHALL be the location. Supplemental morphir-workspace.json SHALL load below an ordinary JSON primary.

#### Scenario: Dot-config primary
- **WHEN** /repo/.config/morphir/config.yaml is the only primary and invocation is /repo/src
- **THEN** /repo is a primary location rather than /repo/.config/morphir

#### Scenario: Supplemental workspace and project manifest
- **WHEN** morphir-workspace.json and morphir.json both set workspace.out_dir
- **THEN** both contribute and morphir.json wins their JSON-layer overlap

### Requirement: Same-format candidates are ambiguous
Multiple ordinary primary candidates in one format group at a location SHALL fail with every conflicting path. Different format groups SHALL coexist in JSON, YAML, TOML order. Supplemental morphir-workspace.json SHALL NOT conflict with the ordinary JSON primary.

#### Scenario: Layout conflict
- **WHEN** morphir.toml and .morphir/morphir.toml exist at one location
- **THEN** discovery fails and names both paths

#### Scenario: Extension conflict
- **WHEN** morphir.yaml and morphir.yml exist at one location
- **THEN** discovery fails instead of choosing one

### Requirement: Nearest declared workspace wins
Discovery SHALL parse merged primaries while walking upward from the invocation directory or input file's parent. It SHALL remember the nearest project and continue until the nearest workspace section or boundary. With no workspace, it SHALL use the nearest primary location. It SHALL NOT merge every ancestor.

#### Scenario: Standalone member does not hide workspace
- **WHEN** invocation is /repo/app/src, /repo/app has a project primary, and /repo declares a workspace
- **THEN** /repo is the workspace root and /repo/app remains the member primary location

#### Scenario: Nested workspace stops the walk
- **WHEN** /repo and /repo/nested declare workspaces and invocation is /repo/nested/src
- **THEN** /repo/nested wins and the outer primary is not read

#### Scenario: Standalone fallback
- **WHEN** no workspace exists within the boundary but /repo/app has the nearest project primary
- **THEN** /repo/app is selected

### Requirement: Boundaries constrain discovery
Discovery SHALL stay within the supplied boundary or mounted tree and SHALL NOT follow discovered links outside it. Git metadata and empty .morphir directories SHALL NOT declare workspaces. Missing primaries SHALL leave the host at its start location with applicable defaults.

#### Scenario: Bounded invocation
- **WHEN** the boundary is /repo/app and /repo declares a workspace
- **THEN** discovery does not read /repo configuration

#### Scenario: Git root without a Morphir primary
- **WHEN** only a .git directory exists above the invocation
- **THEN** it does not override a nearer Morphir project or create a declared workspace

### Requirement: Members follow declared paths and exclusions
Workspace members SHALL be selected by workspace.members globs relative to workspace.root, with workspace.exclude applied. A virtual workspace SHALL NOT become its own glob member. A root declaring project SHALL be eligible as a root project.

#### Scenario: Virtual workspace glob
- **WHEN** a root declares members ["**"] with no project section and one child project
- **THEN** only the child is a project and hidden config directories do not become members

#### Scenario: Excluded member
- **WHEN** packages/* includes a project also matched by workspace.exclude
- **THEN** that project is absent from the selected membership

### Requirement: Project selection is deterministic
An explicit path SHALL take priority over name matching. Unique names, deepest containing members, default_member, and finally the workspace projects SHALL determine selection. Unknown or ambiguous explicit selectors and invalid defaults SHALL return errors. Duplicate names SHALL be diagnosed.

#### Scenario: Invocation inside nested member
- **WHEN** two members contain the invocation path
- **THEN** the deepest containing member is selected

#### Scenario: Duplicate display name
- **WHEN** two members share a name and that name is explicitly selected
- **THEN** selection fails and identifies the ambiguity while path selection remains usable

### Requirement: Invalid and missing members remain visible
Missing literal members SHALL produce warnings. Existing literal member directories without configuration and invalid member documents SHALL remain identifiable with structured diagnostics. Same-format member ambiguity SHALL fail discovery rather than be silently skipped.

#### Scenario: Broken literal member
- **WHEN** a declared literal member directory exists without a primary
- **THEN** its error is reported instead of presenting a complete healthy workspace

### Requirement: Machine sources use platform conventions
Discovery SHALL inspect system and global Morphir locations using platform config directories and the alternate Morphir home. Nonempty MORPHIR_HOME SHALL replace only the home alternative. Relative XDG_CONFIG_HOME SHALL be ignored. Same-format alternatives SHALL be ambiguous; cross-format sources SHALL layer.

#### Scenario: Invalid XDG path
- **WHEN** Linux XDG_CONFIG_HOME is relative
- **THEN** global discovery uses the platform home fallback

#### Scenario: Split global ambiguity
- **WHEN** a platform config directory and Morphir home both contain ordinary TOML global files
- **THEN** loading fails with both paths instead of choosing a directory

### Requirement: Overlays follow eligible primaries
User overrides SHALL use the adjacent primary layout's stem. Profiles SHALL use the same eligible stems and format groups. Only active profiles SHALL be requested; profiles SHALL NOT identify roots. Workspace overrides SHALL precede member overrides.

#### Scenario: Dot-config personal file
- **WHEN** the primary is .config/morphir/config.toml
- **THEN** the eligible personal stem is .config/morphir/config.user

### Requirement: Isolation excludes personal and machine inputs
Isolated loading SHALL omit system/global configuration and personal override files while retaining declared primaries, explicitly selected configuration, active profiles, and supplied environment/CLI values.

#### Scenario: Isolated profile
- **WHEN** isolation is enabled with an active ci profile
- **THEN** the eligible profile loads while machine and personal files are not read

### Requirement: Host adapters produce equivalent evidence
Given the same bounded file contents and platform/environment inputs, filesystem and in-memory hosts SHALL return equal roots, members, selection, source order, and diagnostics. Discovery SHALL be usable without CLI dependencies.

#### Scenario: Mounted workspace
- **WHEN** an embedded host supplies the same file tree as a native host
- **THEN** both obtain equivalent project-system results
