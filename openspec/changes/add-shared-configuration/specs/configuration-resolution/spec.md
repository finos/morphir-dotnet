# Spec Delta

## Purpose

Computes the same effective Morphir settings, origins, and diagnostics from the same supplied sources across CLI, native, and embedded hosts.

## ADDED Requirements

### Requirement: Sources have deterministic precedence
Resolution SHALL apply defaults, system, global, root primaries, selected file, member primaries, active profiles, adjacent user overrides, environment, and explicit CLI values in that order. Ordinary file groups SHALL merge JSON/JSON5 before YAML/YML before TOML.

#### Scenario: Lower-format-only keys survive
- **WHEN** JSON sets a project name and source directory, YAML changes the directory, and TOML changes it again
- **THEN** the JSON name survives and the TOML directory wins

#### Scenario: Runtime override wins
- **WHEN** a file, an environment value, and an explicit CLI value set the same setting
- **THEN** the CLI value wins and its origin is recorded

### Requirement: Merge preserves document semantics
Maps SHALL merge recursively, arrays SHALL replace completely, incompatible value shapes SHALL replace earlier values, and secret references SHALL act as indivisible leaves. Resolution SHALL NOT mutate inputs. Sources SHALL use an ordered left fold; shape replacement does not permit associative regrouping.

#### Scenario: Shorter replacement array
- **WHEN** a lower source supplies three members and a higher source supplies one
- **THEN** the effective array contains only the one higher-source member

#### Scenario: Empty replacement array
- **WHEN** a higher source supplies an empty array
- **THEN** no lower-source array elements remain

#### Scenario: Shape replacement
- **WHEN** a higher source replaces an object with a scalar or a different secret-reference shape
- **THEN** no lower-source children survive beneath the replacement

### Requirement: Ordinary nulls preserve lower values
Ordinary null overlays SHALL leave earlier values and their origins unchanged.
Completed loads SHALL recursively omit null-valued object fields, including
objects nested in arrays, while retaining null array elements.

#### Scenario: Ordinary null is not an unset
- **WHEN** a lower source provides a=1 and a higher source provides a=null
- **THEN** a remains 1 with the lower source origin

#### Scenario: Null cleanup retains array elements
- **WHEN** a completed load contains items=[{a:null},null]
- **THEN** items equals [{},null]

### Requirement: Raw profile controls preserve null replacement
Raw profile-control metadata SHALL use a separate merge where null replaces
earlier controls and fails typing rather than restoring the earlier value.

#### Scenario: Null profile control does not resurrect an earlier list
- **WHEN** a higher root primary supplies profiles.active=null over an earlier list
- **THEN** enabled profile processing fails control typing at that higher source

### Requirement: Winners and consulted sources are explainable
Resolution SHALL retain source identity for every winning leaf and array and reports of consulted sources, including missing optional files. An explicitly selected discovered file SHALL contribute ordinary values only once at the selected layer.

#### Scenario: Selected JSON overrides TOML
- **WHEN** JSON and TOML set a setting and the JSON primary is explicitly selected
- **THEN** JSON wins, TOML-only settings survive, and the selected file has one ordinary source contribution

#### Scenario: Missing explicit configuration
- **WHEN** an explicitly selected file cannot be read
- **THEN** resolution returns an error naming that file rather than treating it as an optional miss

### Requirement: Scope inheritance respects project ownership
System/global defaults SHALL exclude their entire project and workspace subtrees. Member documents SHALL exclude inherited root project/workspace subtrees while receiving applicable tooling settings and a separate owning-workspace context. Member output-directory settings SHALL NOT relocate workspace output and SHALL produce diagnostics.

#### Scenario: Member keeps its identity
- **WHEN** a workspace root and a member declare different project names
- **THEN** the member has its own name while inheriting applicable root tooling settings

#### Scenario: Missing member fields do not inherit root project metadata
- **WHEN** the root sets project.source_directory and project.exposed_modules but the member omits both
- **THEN** the member uses its own project defaults rather than the root's fields, while the owning workspace still supplies its output context

#### Scenario: Global workspace cannot change topology
- **WHEN** global configuration supplies workspace.root or workspace.out_dir
- **THEN** those fields do not alter discovered project ownership or the owning workspace output root

### Requirement: Environment mapping is deterministic
MORPHIR_ values SHALL use double underscores for nesting, retain single underscores, lowercase segments, and type booleans, integers, and JSON arrays/objects. Reserved operational variables SHALL NOT become ordinary keys. A shorter scalar path SHALL win over a conflicting nested environment path.

#### Scenario: Canonical field name
- **WHEN** MORPHIR_IR__FORMAT_VERSION is 4
- **THEN** ir.format_version is the integer 4

#### Scenario: Environment iteration order
- **WHEN** MORPHIR_IR and MORPHIR_IR__STRICT_MODE are supplied in either enumeration order
- **THEN** the shorter ir scalar wins in both resolutions

### Requirement: Profile selection is explicit and ordered
Profiles SHALL be selected by explicit options, present MORPHIR_PROFILES, base active controls, then base defaults. No implicit profile SHALL be active. Explicit disabling or an empty environment selector SHALL suppress includes and file loading.
Explicit empty host selection SHALL still allow includes. A present whitespace-only
MORPHIR_PROFILES SHALL suppress includes and control interpretation.

#### Scenario: Explicit list replaces environment selection
- **WHEN** the environment selects postgres and the host explicitly selects metrics
- **THEN** metrics replaces postgres before includes and group expansion

#### Scenario: Disabled profiles do not read files
- **WHEN** profiles are disabled and an inactive profile file is invalid
- **THEN** resolution does not read it or interpret base profile controls

#### Scenario: Explicit empty selection permits includes
- **WHEN** the host supplies an empty profile list and the root base includes metrics
- **THEN** metrics remains active through the include control

### Requirement: Profile controls belong to the discovered base
Enabled profile activation controls SHALL come only from root primary documents. A selected file already in that base SHALL retain control authority. Controls from member, machine, arbitrary selected, user, profile, or environment-mapped sources SHALL fail before scope filters hide them.

#### Scenario: Member attempts activation
- **WHEN** a member primary sets profiles.active
- **THEN** resolution fails with a diagnostic naming that control and its source

### Requirement: Profile expansion preserves precedence
Profiles SHALL expand includes before selected roots, emit each root before group
descendants, then retain last occurrences. Higher group definitions SHALL replace
whole values. All declared groups, including inactive groups, SHALL be checked for
cycles and bounds. For each active name, workspace files SHALL precede member files
in format order; later names SHALL win.

#### Scenario: Later workspace profile wins
- **WHEN** active names are postgres then metrics, member postgres sets a setting, and workspace metrics replaces it
- **THEN** workspace metrics wins unless a later layer replaces it

#### Scenario: Inactive cycle
- **WHEN** an inactive group graph contains a cycle
- **THEN** enabled profile processing fails with the cycle and its control origin

### Requirement: Profile graph bounds match the pinned framework
Names SHALL match `[a-z][a-z0-9_-]{0,63}`. Bounds SHALL be 64 groups, 64 members
per group, 32 group levels, and 128 distinct active names. Inactive groups SHALL
also be checked for depth. Exceeded bounds SHALL fail with control-source diagnostics.

#### Scenario: Inactive graph exceeds depth
- **WHEN** an inactive group chain exceeds 32 group levels
- **THEN** enabled profile processing fails at the declaring control source

#### Scenario: Too many active names
- **WHEN** expansion produces more than 128 distinct active names
- **THEN** profile processing fails rather than truncating the active list

### Requirement: Mixed-format warnings have controlled suppression
Resolution SHALL report config.yaml_beside_toml and surviving config.json_keys_below_toml values with relevant paths. Only explicit host controls, the suppression environment control, or system/global settings SHALL suppress them. Unknown suppression codes SHALL warn.

#### Scenario: Workspace cannot hide its warning
- **WHEN** only a workspace file requests suppression of its mixed-format warning
- **THEN** the warning remains visible

### Requirement: Inspection-safe resolution does not execute code
Resolution SHALL validate known values and report structured source/key diagnostics without resolving secrets or executing scripts. Identical explicit inputs SHALL produce identical values and origins without reading ambient process state.

#### Scenario: In-memory resolution
- **WHEN** two hosts supply identical documents, source order, environment, and profile options
- **THEN** both receive identical effective values, origins, and diagnostics

### Requirement: Legacy configuration entry points remain compatible
Existing cache-only configuration entry points SHALL remain available with their documented legacy behavior until an explicit compatibility migration is provided. New document-aware behavior SHALL be accessible through additive entry points.

#### Scenario: Existing CI caller
- **WHEN** an existing caller uses the cache-only resolver with CI mode On
- **THEN** its existing TOML CI-overlay behavior remains available without opting into named profiles
