---
title: "project migrate"
linkTitle: "project migrate"
weight: 45
description: "Preview and apply a loss-checked migration from classic morphir.json to TOML"
---

# project migrate

```sh
morphir project migrate [path] [--output path] [--dry-run] [--json]
```

Migrate one classic project configuration to TOML. The command maps classic
fields, checks that every original value is accounted for, and verifies the
candidate independently of the classic manifest. It keeps `morphir.json`
byte-identical for older consumers.

`path` accepts one project directory or classic `morphir.json`. With no path,
shared discovery selects the current unambiguous project. Invoking from a
workspace member selects that member. A virtual workspace with no unambiguous
project requires a member directory or manifest path.

## Options

| Option | Meaning |
| --- | --- |
| `--output path` | Choose a supported TOML primary layout at the same project location |
| `--dry-run` | Return the complete plan without creating or changing files |
| `--json` | Emit one structured JSON result on stdout |
| `--discovery-boundary directory` | Bound discovery to an explicitly selected directory tree |

The default output is `morphir.toml` adjacent to the selected classic manifest.
Supported primary layouts are `morphir.toml`, `.morphir/morphir.toml`, and
`.config/morphir/config.toml` within the same project. Hidden and dot-config
layouts retain the project's location as the anchor for relative source paths.
An arbitrary TOML filename, another project directory, a lower-format output,
or an output that creates primary ambiguity blocks migration.

## Before and after

A classic manifest:

```json
{
  "name": "Orders",
  "sourceDirectory": "elm-src",
  "exposedModules": ["Order"]
}
```

The equivalent TOML includes the implicit legacy defaults:

```toml
[project]
name = "Orders"
source_directory = "elm-src"
exposed_modules = ["Order"]

[frontend]
language = "elm"

[pipeline]
backend = "checkpoint"
checkpoint_format = "morphir-json"
```

Migration does not add an unrelated `ir.format_version` value. Supported extra
settings survive conversion. A raw field ledger records mapped, retained,
derived and blocking values. Unsupported nonempty dependency/decorations shapes,
nulls, values TOML cannot represent, and same-file classic/canonical conflicts
block application. Resolve the reported fields deliberately before retrying.
There is no drop-unsupported or overwrite switch.

Existing equal TOML entries keep their original spelling and comments. Missing
values are added with the source-preserving editor. Different values block the
entire write. A successful repeated migration returns `unchanged` and preserves
the target timestamp.

## Verification

The comparison resolves project-scoped typed settings and resolved path meaning
before and after migration. It excludes the converted classic source from the
candidate and excludes machine, personal, environment, explicit CLI and activated
profile overlays from both sides. Declared base workspace settings remain.
Expected origin changes are reported separately from value differences.

This means an ambient override cannot hide a bad conversion, and the retained
classic file cannot supply a value omitted from TOML. Profile declarations and
other supported source settings remain in the field ledger and conversion.

## Examples

```sh
# Review the current project without writes.
morphir project migrate --dry-run

# Preview one member in JSON for CI review.
morphir project migrate packages/orders/morphir.json --dry-run --json > migration.json

# Use a dot-config primary at the same project location.
morphir project migrate packages/orders \
  --output packages/orders/.config/morphir/config.toml

# Apply the default adjacent target, then verify the repeat is unchanged.
morphir project migrate packages/orders
morphir project migrate packages/orders --json
```

Relative output arguments resolve from the invocation directory. This command
adopts configuration only. It performs no source-code conversion, IR conversion,
IR-version upgrade, sibling batch operation or workspace-membership edit.

## Results and exit codes

| Exit | Meaning |
| --- | --- |
| `0` | Verified application, applicable preview, or unchanged repeat |
| `2` | Invalid input, loss, conflict, failed verification, stale evidence or write/recovery failure |

Human output lists the outcome, source/target, field mappings, proposed edits and
verification. JSON follows the
[project migration result schema](../../../spec/schemas/project-migration-result.schema.json).
The report includes redacted values, loss ledger, planned edits, independent
verification, origin changes, structured diagnostics and retained recovery paths.
Diagnostics go to stderr; JSON stdout contains one valid document, including
argument and host-initialization failures. Secret references are never executed.

## Troubleshooting and recovery

- For an IR-file rejection, pass the project directory or `morphir.json`.
- For an unmapped field or unrepresentable value, inspect its raw path in the
  ledger and make a deliberate supported equivalent before retrying.
- For an existing-target conflict, reconcile the reported values manually.
  Migration will not overwrite a differing TOML value.
- For ambiguity, retain one primary per format group across supported layouts.
- For a stale plan, preview again. Source, target and relevant workspace evidence
  are checked again at application time.
- For `rolled-back`, the transaction restored the original target. Inspect the
  diagnostic, correct the write failure and plan again.
- For `recovery-required`, preserve the reported journal, stage and backup paths.
  Inspect their transaction ownership before any manual recovery. Do not remove
  the original classic file or overwrite later user edits.

Writes reuse the configuration editor's staged per-file atomic replacement and
explicit recovery reporting. They do not claim multi-file crash atomicity.
Rollback does not automatically delete a subsequently edited TOML. Removal of a
classic manifest is a separate user action after older consumers have moved.

See [configuration editing]({{< relref "/docs/configuration/editing" >}}) for
transaction and host boundaries, and the
[configuration guide]({{< relref "/guides/configuration" >}}) for discovery and
layering.
