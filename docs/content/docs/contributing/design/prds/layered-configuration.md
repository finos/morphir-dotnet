---
title: "PRD: Layered Configuration"
linkTitle: "Layered Configuration"
weight: 40
description: "Shared configuration, project discovery, and configuration inspection and editing"
---

# Layered configuration and workspaces

**Status**: Approved foundation; implementation planned

**Created**: 2025-12-22

**Last updated**: 2026-11-02

## Overview

Provide one reusable Morphir configuration and project system for the CLI and other hosts. Read TOML, YAML, JSON, and JSON5; discover projects and declared workspaces; and resolve settings with provenance. Shared APIs support `morphir config` inspection/editing, `morphir project migrate` for one classic project, and `morphir workspace migrate` for coordinated workspace adoption.

The original cache-only TOML implementation exists. This revision replaces its planned Git-root-first, TOML-only design with the agreed MoonBit behavior. It does not claim the new APIs or commands have been implemented.

## Compatibility and decisions

Use the [MoonBit configuration specification](https://github.com/finos/morphir-moonbit/blob/main/openspec/changes/adopt-moon-lander/specs/application/configuration/spec.md) and [adoption decision](https://github.com/finos/morphir-moonbit/blob/main/docs/adr/0004-adopt-moon-lander.md) for discovery and layering. Use the [finos/morphir configuration model](https://github.com/finos/morphir/blob/main/docs/spec/morphir-toml/morphir-toml-specification.md) for shared fields. MoonBit's format layering supersedes the older core specification's refusal of coexisting TOML and YAML.

The [guide]({{< relref "/guides/configuration" >}}) describes target rules. The [CLI reference]({{< relref "/docs/cli/config" >}}) records the proposed command interface.

## Goals

1. Share core functionality across hosts without a dependency on the CLI.
2. Match MoonBit discovery, precedence, legacy manifest normalization, and project selection.
3. Preserve typed values and winning origins.
4. Integrate with `Microsoft.Extensions.Configuration` through an adapter.
5. Support scoped inspection and validated TOML editing.
6. Document current support separately from planned behavior.
7. Migrate classic `morphir.json` projects and adopt repositories into workspaces without silently losing settings or changing project behavior.

## Non-goals

- Implementing a task runner, compiler, cache store, or every consumer of configuration fields.
- Writing YAML/JSON configuration or silently converting existing files.
- Reproducing Git's complete command set or multi-valued INI semantics.
- Executing scripts or resolving secrets during inspection.

## User stories

- A developer invokes a tool from a member's source directory and finds its declared workspace.
- A repository retains identity in `morphir.json` while TOML overrides compiler settings.
- A developer explicitly migrates a classic manifest to TOML, previews the changes, and checks that the new configuration works independently of the old file.
- A repository maintainer adopts several classic projects through one bounded workspace plan while preserving existing membership and settings.
- A developer inspects why a setting wins and edits a specific project, workspace, personal, or machine scope.
- An IDE or embedded host loads the same configuration without invoking the CLI.
- A CI job obtains valid JSON output and selects profiles explicitly.

## Functional requirements

### Discovery and project system

- Inspect root, hidden, and dot-config primary stems with all five extensions. Include supplemental `morphir-workspace.json` below ordinary JSON primaries.
- Parse primaries during the upward walk. Remember the nearest project, but stop at the nearest declared workspace. Respect host boundaries. Git or empty `.morphir` directories do not declare workspaces.
- Merge JSON/JSON5, then YAML/YML, then TOML. Reject multiple candidates in one format group across layouts. Apply the same rules to members and eligible overrides/profiles.
- Expand `workspace.members`, apply `workspace.exclude`, respect `workspace.root` and `workspace.default_member`, and distinguish virtual workspaces from root projects.
- Support explicit path/name and invocation-based project selection. Refuse multi-project edit targets.
- Use canonical snake-case fields, including `project.source_directory` and `workspace.out_dir`. Diagnose member attempts to relocate the workspace output root.

### Resolution

- Apply defaults, system, global, root primaries, selected file, member primaries, active profiles, adjacent user overrides, environment, and explicit CLI values in that order.
- Preserve lower-format-only keys; recursively merge maps; replace complete arrays and incompatible shapes; treat secret references as leaves.
- Retain typed documents, winning leaf/array provenance, source reports, and structured diagnostics. Preserve unknown settings allowed by the shared model.
- Exclude whole `project` and `workspace` subtrees from system/global defaults and from root-to-member document inheritance. Supply owning workspace topology/output-root context separately.
- Normalize legacy morphir-elm fields and reject same-file legacy/canonical conflicts. Report unsupported fields.
- Implement OS-specific system/global paths, `MORPHIR_HOME`, same-format ambiguity across alternate global locations, and isolated loading.
- Use `__` for environment nesting. Match upstream typing and deterministic scalar/nested-key conflict behavior.
- Implement named profiles, explicit disabling, base-only controls, includes/groups, active-name order, and workspace-before-member files. Inactive files are not loaded. No implicit CI or default profile is active.
- Report upstream mixed-format warning codes. Only host controls and system/global settings may suppress them.

### Inspection and editing

- Expose `get`, `list`, `show`, `path`, `explain`, `set`, and `unset` under `morphir config`.
- Reads default to effective configuration. Scope selectors limit source inspection; origin/scope options expose the winner. `explain` reports contributing layers and the winner.
- Writes target one project, workspace, adjacent user, global, system, or explicit TOML file. Resolve an unambiguous current project/workspace by default. Do not write implicitly to an environment or profile source just because it supplies the winning value.
- Support explicit strings, booleans, integers, numbers, arrays, and objects. Separate input typing from JSON output.
- Preserve TOML formatting/comments. Create the corresponding TOML primary when only lower-format files exist. Write full replacement arrays/objects when those values are set.
- `unset` removes only the target entry; inherited values may reappear. Report the resulting effective value and whether an edited value remains overridden.
- Validate before applying; install writes atomically; detect concurrent changes; leave files unchanged on validation or conflict failures.
- Redact sensitive values. Return structured results for non-CLI hosts.

### Classic project migration

- Provide a reusable migration planner and apply operation, exposed by the planned `morphir project migrate [<path>]` command. The path accepts one project directory or classic manifest. This migrates project configuration, not `morphir-ir.json` or IR versions.
- Accept a classic morphir-elm `morphir.json`; use the current unambiguous project's classic manifest when the source is omitted. Do not implicitly migrate every workspace member.
- Default to adjacent `morphir.toml`. An explicit output may select a supported primary layout within the same project location. Refuse targets that introduce same-format ambiguity or change project-relative path meaning.
- Map `name`, `sourceDirectory`, and `exposedModules` to their canonical `project` fields. Materialize legacy-derived frontend/pipeline defaults, including Elm and checkpoint behavior, wherever needed for the new file to retain behavior without the classic manifest.
- Preserve supported additional settings and relative-path meaning. Diagnose every unmapped or unrepresentable field, including legacy dependency/decorations shapes. Block application when any value would be lost or its meaning cannot be preserved; a reader's warning-and-ignore policy is not sufficient for migration.
- Reject same-file legacy/canonical conflicts and conflicting existing TOML values. Add nonconflicting missing values to an existing target while preserving unrelated settings, comments, and formatting. Identical existing values are unchanged; a repeat migration is a no-op.
- Provide a read-only `--dry-run` that reports source, target, field mappings, planned edits, conflicts, and verification results. Redact sensitive values in previews and JSON reports.
- Before applying, compare project-scoped resolution before migration with resolution of the candidate with the legacy source excluded. Exclude ambient machine, personal, environment, CLI, and activated-profile overlays from this comparison so they cannot hide a change; keep relevant declared workspace context. Fail if values or path meaning differ. Provenance paths may change and must be reported, not treated as value differences.
- Keep the classic source unchanged. Removing it is a separate deliberate action after users have migrated any consumers that still require `morphir.json`. Apply only verified edits atomically, detecting changes to source, target, and relevant workspace context. Validation or stale-plan failures leave both files unchanged; incomplete write recovery must report affected paths and retain recovery data.

### Workspace migration

- Expose `morphir workspace migrate [<directory>]` through shared bounded inventory, planning, and application services, including when no workspace declaration exists yet.
- Inventory only the selected root. Respect include/exclude patterns and existing declared exclusions; skip discovered links, nested declared workspaces, and version-control/dependency/build-output directories.
- Propose explicit root-relative membership, preserve existing settings/defaults and already migrated members, and distinguish root projects from virtual workspaces. Report duplicate names without conflating project paths.
- Reuse project migration plans and their loss checks. Verify classic-to-TOML equivalence in the same proposed workspace context, preserving project source-path meaning and settings. Report topology, member/default-selection, and shared-output-root changes separately as intended adoption effects; no generated files are moved. Verify the aggregate workspace against those declared effects. A blocked selected project blocks the batch.
- Support read-only `--dry-run` and JSON reports listing the full inventory, exclusions, edits, blockers, and verification outcomes.
- Validate and stage the entire plan before replacing destinations. Coordinate overlapping writers, recheck preconditions, and journal replacements with rollback/recovery reporting. Do not claim crash-atomic multi-file writes. Retain all classic sources and unresolved recovery evidence; successful or fully rolled-back operations clean temporary artifacts.
- Equivalent repeats are no-ops. A valid existing workspace with nothing to convert succeeds unchanged; an undeclared root without eligible projects reports no adoption plan.

## Delivery campaigns

Each campaign is one cohesive implementation change delivered through one reviewable PR. Start dependent campaigns from the verified `main` result of their prerequisites rather than accumulating an unmerged branch stack. Tests and documentation land with the behavior they describe.

| Order | OpenSpec change | Outcome | Prerequisites |
| --- | --- | --- | --- |
| 1 | `add-shared-configuration` | Typed documents, format readers, resolution, profiles, provenance, and validation shared by hosts | None |
| 2 | `add-project-workspace-discovery` | Bounded roots, member discovery/selection, and applicable sources | 1 |
| 3 | `add-config-inspection` | Shared queries, read-only config commands, and opt-in Microsoft configuration adapter | 1, 2 |
| 4 | `add-config-editing` | Shared TOML edit planning/application and config set/unset | 3 |
| 5 | `add-classic-project-migration` | Shared loss-checked migration and project migrate | 2, 4 |
| 6 | `add-workspace-migration` | Bounded repository adoption and coordinated workspace migrate | 5 |

Planning artifacts are under `openspec/changes/<change-name>/`. Every change contains a proposal, capability specs, `design.html`, and implementation tasks. These campaigns are planned; this documentation update does not implement or land them.

### Campaign completion

A campaign is complete only when all of these outcomes are verified:

1. Its implementation, tests, and docs are in a reviewable PR; required checks and reviews pass on the delivered head.
2. Its OpenSpec changes are archived and the main specs are synchronized coherently with the delivered implementation.
3. The PR is merged to `main`, and authoritative merge evidence identifies a merged commit reachable from the updated `main` branch.
4. The campaign-owned worktree is clean and contains no sole copy of unmerged work. Its worktree and local/remote feature branches are removed after verifying the merged state.
5. The completion receipt records the PR URL, merged SHA, validation/review evidence, archive location, and worktree/branch cleanup verification.

Finishing code or obtaining green checks is not campaign completion. Cleanup is limited to artifacts owned by that campaign. Preserve unrelated branches/worktrees, unmerged changes, and recovery data; do not use forced removal or broad pruning to manufacture a clean result. Pending recovery or cleanup remains outstanding work and must be reported. Follow repository merge authorization and protection rules; this plan does not imply admin bypass or force-push permission.

## Architecture

Core behavior belongs to reusable libraries. `Morphir.Configuration` is the existing home for immutable models and pure rules. Discovery/resolution services must be callable without the CLI; file, directory, environment, and write access are supplied through adapters. A host can provide native filesystem access or an in-memory/mounted tree.

A Microsoft configuration adapter exposes the resolved model through the proposed `AddMorphirConfiguration(...)`. The typed document remains authoritative because flattened strings cannot alone preserve array, key, type, and provenance semantics. Editing uses explicit shared services.

The CLI handles arguments, output, and exit codes. Library services handle queries, explanations, edit and migration planning, validation, equivalence checks, and application. Other hosts can preview and apply the same migration without invoking the CLI. Keep Native AOT support through explicit/source-generated binding. Final package names and signatures remain to be designed.

## Documentation acceptance criteria

- A guide explains formats, layouts, boundaries, discovery, ambiguity, precedence, membership, profiles, environment values, legacy fields, and host integration.
- A CLI reference covers every subcommand, selectors, typed inputs, output, exit codes, and write semantics.
- A walkthrough demonstrates a workspace/member, inspection, personal overrides, inheritance after `unset`, and CI inspection.
- Project and workspace migration references include before/after files, dry-run/apply commands, bounded inventory, existing-target conflicts, unsupported fields, source retention, and verification independent of legacy files.
- Delivery tracking records each campaign's landed PR, synchronized/archive specs, and campaign-owned worktree/branch cleanup.
- Troubleshooting covers wrong roots, ambiguity, inactive profiles, overridden edits, revealed lower layers, and write conflicts.
- CLI, guide, getting-started, and PRD indexes link to these pages.
- Planned examples stay labeled until implemented and verified. Update examples and status with implementation.

## Verification strategy

Follow red-green-refactor for implementation. Start with BDD scenarios and focused behavioral tests using upstream compatibility fixtures where possible.

- Discovery: every layout/extension, nested workspaces, fallback, boundaries, input files, absent primaries, and same-group ambiguities.
- Resolution: each precedence boundary, types, complete/empty array replacement, shape replacement, casing, origins, environment conflicts, and legacy mappings.
- Projects/profiles: membership/exclusions, virtual/root projects, default selection, duplicates, invalid members, active ordering, and disabled profiles.
- Editing: targets, comments, unrelated keys, lower-format promotion, composite values, inheritance after removal, overridden writes, invalid edits, and concurrent writers.
- Migration: classic field mappings, explicit legacy defaults, relative paths, supported extra fields, unmapped-field failures, existing-target conflicts, idempotence, source preservation, dry-run with no writes, concurrent source/target changes, and equivalence with the classic source excluded. Cover standalone and member projects.
- Workspace adoption: bounded/excluded/nested inventory, deterministic membership, already migrated projects, root identity, aggregate verification, batch blockers, staged commits, rollback/recovery, and repeat no-ops.
- Hosts: identical results from in-memory/filesystem adapters, use without CLI dependencies, Microsoft configuration projection, and trimming/AOT.
- CLI: human/JSON results, stderr diagnostics, exit codes, and end-to-end get/set/unset roundtrips.

## Feature status tracking

| Feature | Status | Notes |
| --- | --- | --- |
| Configuration project and cache models | Implemented baseline | Cache-only F# records and merge functions exist |
| TOML resolver and legacy discovery | Implemented baseline | Git-first discovery and user/CI overlays; replacement planned |
| Multi-format documents and normalization | Planned | TOML, YAML, JSON/JSON5, legacy fields |
| Shared discovery and project selection | Planned | Document-aware search and membership |
| Shared resolution, profiles, validation, provenance | Planned | MoonBit behavior |
| Microsoft configuration integration | Planned | Adapter; proposed extension name |
| Shared query and edit services | Planned | Host-independent inspection and TOML edits |
| Classic project migration | Planned | Shared planner/apply API, project migrate command, loss checks, and independent-equivalence verification |
| Workspace migration | Planned | Bounded inventory, coordinated project plans, membership, and recovery |
| Config CLI commands | Planned | Thin adapter over shared services |
| Documentation foundation | Documented | Guide, reference, walkthrough, troubleshooting; runtime examples not yet executable |
| Compatibility and end-to-end verification | Planned | Required before advertising runtime support |

## Implementation notes

### Foundation decisions, 2026-11-02

- Adopt MoonBit's layered policy instead of the older single-serialization rule.
- Share configuration/project behavior with other hosts; keep it out of the CLI.
- Integrate with Microsoft configuration while preserving a typed canonical model.
- Expose Git-style inspection/editing with scopes, provenance, and typed values.
- Keep TOML as the written format and explain when another layer reappears or wins.
- Include explicit classic `morphir.json` migration with source retention, dry-run, and semantic equivalence checks; loading legacy files alone does not satisfy migration support.
- Own migration under project/workspace commands, with no `config migrate` alias. Workspace migration reuses the single-project planner.
- Deliver six campaigns through merged PRs to `main`, synchronized/archived specs, and verified campaign-owned worktree/branch cleanup.
- This revision changes documentation only. Existing APIs have not changed.

## Open implementation questions

- Final library/package boundaries and signatures.
- Existing cache-only APIs retain their legacy behavior through additive compatibility entry points. Document how consumers explicitly opt into the new discovery and profile model before moving them.
- Final JSON result schemas and minor public signature details. The command semantics, selector scopes, and quoted key paths are covered by the OpenSpec changes; the CLI reference remains planned until implemented.
- Pin parser edge cases, including nulls and conflicting shapes, to upstream fixtures before claiming compatibility.
