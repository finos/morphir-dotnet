# Spec Delta

## Purpose

Adopts a repository of classic Morphir projects into the newer workspace system with an explicit bounded inventory, coordinated project plans, and recoverable application.

## ADDED Requirements

### Requirement: Workspace migration works before a workspace exists
The CLI SHALL expose workspace migrate [directory], backed by shared inventory, planning, and application services. The supplied directory or current directory SHALL bound the operation even when it has no declared Morphir workspace. Migration SHALL NOT scan ancestors for additional projects to adopt.

#### Scenario: Repository of classic projects
- **WHEN** the selected directory contains two classic member projects and no workspace primary
- **THEN** migration can propose a workspace and individual project conversions within that directory

### Requirement: Inventory respects explicit boundaries and exclusions
Candidate inventory SHALL remain within the selected root, skip discovered links, stop at nested declared workspaces, and exclude version-control/dependency/build output directories. Repeatable root-relative include/exclude patterns SHALL refine candidates. Existing declared exclusions SHALL remain effective.

#### Scenario: Nested independent workspace
- **WHEN** a child directory declares its own workspace
- **THEN** inventory reports the boundary and does not adopt its members into the outer workspace

#### Scenario: Excluded classic candidate
- **WHEN** an excluded directory contains morphir.json
- **THEN** it is reported as excluded and no project edit is planned for it

### Requirement: Membership proposals preserve existing ownership
Plans SHALL use explicit root-relative member paths, preserve existing membership/settings, and include already migrated members without rewriting them. A root with a project SHALL remain a root project; otherwise the root SHALL be virtual. Duplicate names SHALL be diagnosed without conflating project paths.

#### Scenario: Root classic project
- **WHEN** the selected root itself contains a classic project plus child projects
- **THEN** the plan retains its root-project identity and adds workspace membership without making it a glob member

#### Scenario: Existing default member
- **WHEN** a valid workspace.default_member exists before migration
- **THEN** the plan preserves it rather than choosing another default

### Requirement: Project migration behavior is reused
Each selected classic project SHALL be planned and independently verified using the single-project migration contract. Workspace migration SHALL NOT apply a looser mapping or omit unsupported fields to make a batch pass.

#### Scenario: One blocked member
- **WHEN** one selected project has an unmapped nonempty dependency field
- **THEN** the aggregate plan is blocked and no member or workspace destination is replaced

### Requirement: Aggregate verification checks the resulting workspace
Before application, migration SHALL verify discovery, membership, selection, and each conversion in the same proposed workspace context, excluding converted classic sources and ambient overlays. Source-path meaning and non-adoption settings SHALL be preserved. Existing workspace settings SHALL remain effective except for explicit membership additions.

#### Scenario: Workspace change alters a member
- **WHEN** classic and converted versions of a project would discover different source paths under the same proposed workspace
- **THEN** aggregate verification reports the mismatch and blocks application

### Requirement: Workspace adoption effects are explicit
The plan SHALL report intentional changes to ownership, membership, default selection, and shared output roots separately from configuration conversion. It SHALL NOT move existing generated artifacts. These adoption effects SHALL NOT excuse unrelated value or source-path changes.

#### Scenario: Standalone output roots become workspace output
- **WHEN** standalone projects with member-local default output roots are adopted into a new workspace
- **THEN** preview reports their new shared workspace output context, conversion is compared within that context, and existing generated files are untouched

### Requirement: Workspace previews enumerate the full operation
Workspace migrate SHALL support --dry-run, --json, and include/exclude controls. Reports SHALL list candidates, exclusions, boundaries, already migrated projects, member edits, workspace edits, blockers, and verification. Preview SHALL write nothing.

#### Scenario: Repeatable inventory
- **WHEN** identical bounded trees are enumerated in different directory orders
- **THEN** the reported plan has the same project/path ordering and membership

### Requirement: Apply is coordinated and recoverable
Application SHALL validate and stage the entire plan before any destination replacement and recheck all relevant source/target preconditions. It SHALL coordinate overlapping writers, journal replacements, and attempt rollback on commit failure. It SHALL NOT claim cross-file crash atomicity or delete classic sources.

#### Scenario: Member changes before commit
- **WHEN** a selected source changes after planning
- **THEN** the batch reports a conflict and no destination is replaced

#### Scenario: Commit failure
- **WHEN** replacement fails after an earlier destination changed
- **THEN** rollback is attempted and the result identifies restored and unresolved paths

### Requirement: Recovery state prevents false completion
Success and complete rollback SHALL clean temporary transaction artifacts. Incomplete rollback or crash recovery SHALL retain an identifiable journal and report an explicit recovery-required outcome. A pending recovery SHALL block overlapping new writes until resolved.

#### Scenario: Interrupted batch
- **WHEN** an interrupted migration leaves a journal with unresolved replacements
- **THEN** the next overlapping write reports recovery required instead of treating the batch as complete

### Requirement: Repetition preserves completed work
An equivalent repeat SHALL report unchanged files without rewriting them. Already migrated projects SHALL remain in membership. A valid existing workspace with no further conversions SHALL be a successful no-op; an undeclared root with no eligible projects SHALL report that no adoption can be planned.

#### Scenario: Second successful run
- **WHEN** the same workspace migration is applied twice
- **THEN** the second run leaves file bytes and timestamps unchanged

### Requirement: Results remain host-independent and scriptable
Non-CLI hosts SHALL access the same aggregate plan and outcomes. CLI success/applicable preview/no-op SHALL exit 0; blocked, invalid, failed, or recovery-required operations SHALL exit 2 with structured JSON available and diagnostic text confined to stderr.

#### Scenario: Mounted host preview
- **WHEN** an embedded host inventories the same tree as the CLI
- **THEN** it receives the same project plans, membership proposal, and blockers
