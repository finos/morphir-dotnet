---
title: "Config commands"
linkTitle: "Config"
weight: 30
description: "Planned commands for inspecting and editing Morphir configuration"
---

# Config commands

Status: planned. These commands are the proposed interface for the shared configuration system and are not available in the current .NET CLI. Exact option grammar and JSON result schemas will be finalized with implementation.

Use `morphir config` to inspect settings, discover their sources, and edit a specific configuration file. The commands call shared library services that other hosts can also use. See the [configuration guide]({{< relref "/guides/configuration" >}}) for discovery, formats, and precedence.

## Synopsis

```text
morphir config get <key> [options]
morphir config list [options]
morphir config show [options]
morphir config path [options]
morphir config explain <key> [options]
morphir config set <key> <value> [options]
morphir config unset <key> [options]
```

The explicit verbs and origin/scope options follow [Git's config command](https://git-scm.com/docs/git-config). Morphir uses its own typed document and workspace rules rather than Git's multi-valued INI model.

## Inspection commands

| Command | Result |
| --- | --- |
| `get <key>` | One value, using effective configuration unless a source scope is requested |
| `list` | Settings as key/value entries, with optional origin and scope metadata |
| `show` | The complete nested configuration document |
| `path` | Candidate and loaded sources, with paths, scopes, load status, and precedence |
| `explain <key>` | The contributing layers and winning source for a setting |

Keys use canonical configuration names such as `ir.format_version`, `project.source_directory`, and `workspace.out_dir`. Dots separate key segments. Use double-quoted segments with JSON string escaping for literal dots or special characters, for example `codegen."custom.generator".enabled`. Arrays are queried as whole values.

```bash
morphir config get ir.format_version
morphir config get ir.format_version --show-origin --show-scope
morphir config list --show-origin
morphir config show --json
morphir config path --json
morphir config explain workspace.out_dir
```

`get` reports a missing key separately from an empty string, empty array, or other present value. `explain` shows why an environment, profile, or member value overrides another source. Arrays have one winning source because they replace earlier arrays as a whole.

## Scope selectors

Without a scope selector, inspection reads effective configuration. With a selector, inspect only the selected file source or scope. A scoped query does not fill absent keys from defaults, environment values, or other scopes.

| Selector | Inspection source or edit target |
| --- | --- |
| `--workspace` | Workspace primary configuration |
| `--project <name-or-path>` | Primary configuration for one selected project |
| `--user` | Adjacent personal override for the current unambiguous project/workspace |
| `--global` | Global user configuration |
| `--system` | System configuration |
| `--file <path>` | A specific configuration file |

Scope selectors are mutually exclusive. For inspection, a scope can contain merged format layers; edits select its single TOML target. Ambiguous candidates remain errors. `--file` reads any supported format, but edits require a TOML file and do not redirect silently.

Without an edit selector, target the current project's primary, or the workspace primary when invoked at the workspace root. Refuse ambiguous multi-project targets and default writes outside a discovered project/workspace. Global and system writes require their corresponding selectors.

## Resolution and output options

| Option | Purpose |
| --- | --- |
| `--show-origin` | Include the winning source path or non-file source identity with queried values |
| `--show-scope` | Include the source scope with queried values |
| `--json` | Emit machine-readable results; it does not select the input value type |
| `--config <path>` | Overlay an explicitly selected config during effective resolution |
| `--discovery-boundary <directory>` | Limit the upward search |
| `--profiles <names>` | Select configuration profiles; comma-separated and repeatable in order |
| `--no-profiles` | Disable profile processing; conflicts with `--profiles` |
| `--isolated` | Exclude machine configuration and personal overrides; active profile files remain eligible |
| `--suppress-warning <code>` | Suppress a supported configuration warning; repeatable |
| `--help` | Show command help without loading configuration |

`--config` participates in effective resolution. `--file` directly selects a source for inspection/editing. Neither means "write to whichever file supplied the winning value." Source selection and edit targeting must remain explicit in the final option validation.

## Setting values

`set` writes one key to one TOML target. `set` and `unset` support `--dry-run` to preview their shared edit plan without writing. Use `--type string|bool|int|number|json` to specify the input value type; the default is a string. Known settings must pass validation before a write. JSON input represents arrays and objects as well as JSON scalar values supported by the model.

```bash
morphir config set ir.format_version 4 --type int --workspace
morphir config set ui.color false --type bool --global
morphir config set ui.theme dark --user
morphir config set project.source_directory src --project packages/orders
morphir config set codegen.targets '["fsharp","typescript"]' --type json --workspace
```

Setting an array or object writes its complete replacement value to the target. Other document keys stay unchanged. Cross-source resolution still follows the recursive map merge rules in the guide.

If the chosen scope has only YAML/JSON primaries, create the corresponding TOML primary while leaving those files unchanged. Preserve comments and formatting in an existing TOML target. For an explicit `--file settings.yaml`, refuse the edit instead of converting or modifying that YAML file.

Return the target path, change outcome, and resulting effective value. When a higher layer still wins, explain which source overrides the newly written value. A successful file update does not imply that the written value is effective.

## Removing values

`unset` removes the named entry only from the selected TOML target. It does not remove the same key from other files, modify environment variables, or insert a deletion marker.

```bash
morphir config unset ui.theme --user
morphir config unset ui.color --global
```

After removal, a lower-format file, inherited scope, or built-in default may supply the effective value. Report that result and its origin. A missing target entry is a not-found result, not an instruction to delete an inherited value.

## Project-system migration

Use [morphir project migrate]({{< relref "/docs/cli/project" >}}) to adopt one classic project, or [morphir workspace migrate]({{< relref "/docs/cli/workspace" >}}) to coordinate adoption of a repository and its members. Migration belongs to those command families; there is no planned `config migrate` alias.

## Write guarantees

- Parse and validate edits before modifying the target.
- Preserve unrelated settings, comments, and formatting.
- Atomically install the updated file and detect concurrent changes.
- Leave the file unchanged after a validation error or write conflict.
- Redact sensitive values in human-readable and JSON inspection output. Inspection does not resolve secret references or execute code.

Configuration warnings and operational diagnostics go to stderr. Stdout contains command results only. In JSON mode, return a structured result and include structured diagnostics, so callers can inspect warnings without parsing human-readable stderr. Final JSON field names remain an implementation contract to document and test.

## Exit codes

Proposed exit codes for this command family:

| Code | Meaning |
| --- | --- |
| `0` | Successful inspection, edit, or applicable edit preview; includes unchanged edits and successful writes still overridden by a higher layer |
| `1` | Requested key or target entry not found |
| `2` | Invalid usage/value, ambiguous configuration/target, parse error, inaccessible file, or write conflict |

These codes apply to configuration commands; IR verification retains its own meaning for code `1`.

## CI example

After implementation, an isolated job can inspect effective settings with explicitly selected profiles:

```bash
morphir config show --isolated --discovery-boundary . --profiles ci --json > effective-config.json
morphir config get ir.format_version --isolated --discovery-boundary . --profiles ci --json
```

Isolation does not itself clear supplied environment values or remove the selected profiles. Control the job's environment as well when checking reproducibility.

## Related documentation

- [Configuration guide]({{< relref "/guides/configuration" >}})
- [Configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}})
- [Configuration troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration-planned" >}})
- [Layered configuration PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}})
