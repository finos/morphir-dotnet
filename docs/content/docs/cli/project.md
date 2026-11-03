---
title: "Project migration"
linkTitle: "Project"
weight: 40
description: "Planned migration of one classic morphir-elm project to the newer Morphir project system"
---

# Project migration

Status: planned. `morphir project migrate` is not yet implemented in .NET. The command calls shared migration services that other hosts can use without invoking the CLI.

Use it to adopt one classic `morphir.json` project into the newer project system. It does not convert source code, migrate `morphir-ir.json`, or choose a new IR version. For a repository-wide plan, use [workspace migration]({{< relref "/docs/cli/workspace" >}}).

## Synopsis

```text
morphir project migrate [<path>] [--output <toml-path>] [--dry-run] [--json]
```

`<path>` identifies one project directory or its classic `morphir.json`. When omitted, select the current unambiguous project. A member invocation does not implicitly migrate its siblings or change workspace membership.

```bash
morphir project migrate ./packages/orders --dry-run
morphir project migrate ./packages/orders
morphir project migrate packages/orders/morphir.json --dry-run --json
morphir project migrate morphir.json --output .config/morphir/config.toml
```

## Options

| Option | Purpose |
| --- | --- |
| `--output <path>` | TOML target in a supported primary layout at the same project location; defaults to adjacent `morphir.toml` |
| `--dry-run` | Return mappings, edits, conflicts, and verification without writing |
| `--json` | Emit a structured migration plan/result and diagnostics |

Output selection must preserve the project location and relative-path meaning. An output that introduces same-format primary ambiguity is refused.

## Migration behavior

Map `name`, `sourceDirectory`, and `exposedModules` to their canonical `project` fields. Preserve supported additional settings and materialize legacy-derived defaults, including Elm frontend and checkpoint behavior, so the new TOML works without the classic source.

If a TOML target exists, add only nonconflicting missing values while preserving comments, formatting, and unrelated settings. Identical existing values are unchanged. Different values or same-file legacy/canonical conflicts block the plan. Every source setting must be mapped, preserved with equivalent meaning, or reported as blocking. Unsupported dependency or decoration shapes cannot be silently dropped.

Before application, compare project-scoped resolution before migration with the candidate TOML while excluding the classic source from the candidate. Exclude ambient machine, personal, environment, CLI, and activated-profile overlays from both sides so they cannot hide a mismatch. Retain the declared base workspace context and compare resolved path meaning as well as values. Report the expected JSON-to-TOML provenance changes separately.

Only a verified plan can be applied. Recheck source, target, and relevant workspace preconditions before atomic target replacement. Keep `morphir.json` byte-identical for older consumers; removal is a separate deliberate action after those consumers have moved. Repeating a completed migration is a no-op.

## Results and exit codes

The result identifies source/target paths, mappings, planned or applied edits, blockers, equivalence status, and expected origin changes. Sensitive values are redacted. Stdout contains results only; diagnostics go to stderr. JSON mode includes structured diagnostics in the result.

| Code | Meaning |
| --- | --- |
| `0` | Verified success, applicable preview, or unchanged repeat |
| `2` | Invalid input, unsupported field, conflict, failed equivalence check, I/O failure, or recovery required |

Validation and stale-plan failures leave source and target unchanged. If a write failure cannot be fully recovered, report the affected paths and retained recovery data rather than claiming success or discarding evidence.

## Related documentation

- [Migration walkthrough]({{< relref "/docs/getting-started/configuration#migrate-the-classic-project" >}})
- [Configuration formats and discovery]({{< relref "/guides/configuration" >}})
- [Configuration inspection and editing]({{< relref "/docs/cli/config" >}})
- [Workspace migration]({{< relref "/docs/cli/workspace" >}})
