---
title: "Config commands"
linkTitle: "Config"
weight: 30
description: "Inspect and edit Morphir configuration through shared services"
---

# Config commands

`morphir config get|list|show|path|explain` reads configuration through the same shared APIs available to embedded hosts. It never edits project files, executes scripts, or resolves secret references. Existing IR and artifact-management commands keep their own configuration behavior; these inspection commands do not make them consume every setting.

## Synopsis

```text
morphir config get <key> [options]
morphir config list [options]
morphir config show [options]
morphir config path [options]
morphir config explain <key> [options]
morphir config set <key> <value> [--type <type>] [--dry-run] [options]
morphir config unset <key> [--dry-run] [options]
```

| Command | Result |
| --- | --- |
| `get <key>` | One typed value; a missing key is distinct from an empty string or array |
| `list` | Ordered canonical key/value entries; arrays are complete values |
| `show` | The nested document |
| `path` | Consulted sources with status, role, format and ordering metadata |
| `explain <key>` | Contributing layers, the effective value and winning origins |

Keys are case-sensitive. Dots separate segments; JSON-escaped double-quoted segments select literal dots or special characters. Protect the complete path with shell single quotes:

```bash
morphir config get ir.format_version
morphir config get 'codegen."custom.generator".enabled' --json
morphir config list --show-origin --show-scope
morphir config show --json
morphir config path --json
morphir config explain custom.theme
```

Arrays are queried as a whole, without indexing syntax. `custom.items.0` does not select an array element. Empty unquoted segments, trailing dots and unterminated quoted segments are usage failures.

## Scope selectors

Effective configuration is the default. A scope selects its own sources before merging and never fills a missing key from defaults, environment values or another scope.

| Selector | Sources |
| --- | --- |
| `--workspace` | Workspace primary files |
| `--project <name-or-path>` | Primary files of one selected project; a relative path starts at the workspace root |
| `--user` | Personal overrides adjacent to the selected project or root |
| `--global` | Global user files |
| `--system` | System files |
| `--file <path>` | One explicitly selected supported-format file |

Selectors are mutually exclusive. `--file` and `--config` cannot be combined. `--file` reads a required file, even when it was not a discovered primary. `--config` overlays a required selected file during effective resolution. Relative file paths start at the invocation directory. Neither option writes anything.

An invalid `workspace.default_member` produces a selection error for effective, project and adjacent-user queries. Workspace, global, system and file scopes inspect their own sources independently of automatic project selection. File scope also excludes synthetic classic compatibility defaults, so an unauthored `pipeline.backend` remains missing there.

A workspace-root invocation reads the root unless shared selection chooses one default project. Invocation inside a member reads that member with applicable inherited settings. Use `--workspace` or an explicit project selector when inspecting a particular primary scope. Ambiguous or unknown project selectors fail visibly.

## Resolution and output options

| Option | Purpose |
| --- | --- |
| `--show-origin` | Include winning paths, formats and available positions with entries |
| `--show-scope` | Include winning source roles with entries |
| `--json` | Emit one JSON report with typed JSON values and structured diagnostics |
| `--config <path>` | Overlay an explicitly selected configuration file |
| `--discovery-boundary <directory>` | Include this directory in discovery and exclude its parents |
| `--profiles <names>` | Comma-separated profile names, repeatable in order |
| `--no-profiles` | Disable profiles; conflicts with `--profiles` |
| `--isolated` | Exclude machine and personal files; retain active profiles and supplied environment values |
| `--suppress-warning <code>` | Suppress a supported warning, repeatable |
| `--help` | Show help without configuration discovery or loading |

Profile names match `[a-z][a-z0-9_-]{0,63}`. Invalid explicit or environment profile names fail before discovery. `--profiles ci --profiles metrics` activates `ci` then `metrics`; use `--no-profiles` to disable activation. The supported mixed-format warning codes are `config.yaml_beside_toml` and `config.json_keys_below_toml`. Unknown suppression codes produce a structured warning.

Without an explicit boundary, the native CLI searches within the current filesystem volume. It captures native machine locations and environment values at execution time. Machine paths use their own filesystem roots, including other Windows drives and redirected UNC shares, independently of the project discovery boundary. Embedded hosts can instead supply bounded or mounted trees without ambient machine access. See the [configuration guide]({{< relref "/guides/configuration" >}}).

## JSON contract

Every read command emits one object with these fields. The [JSON Schema](https://github.com/finos/morphir-dotnet/blob/main/docs/spec/schemas/config-inspection-result.schema.json) is checked against subprocess results in the CLI behavior suite. No F# union or reflection-based serialization is exposed.

| Field | Type and meaning |
| --- | --- |
| `operation` | `get`, `list`, `show`, `path` or `explain` |
| `scope` | `effective`, `workspace`, `project`, `user`, `global`, `system` or `file:<canonical-path>` |
| `key` | Canonical quoted key path, or `null` |
| `found` | Boolean; distinguishes missing keys from present empty values |
| `value` | JSON scalar, array or object; `null` for a missing key or an operation without a value |
| `entries` | Array of `{key, value, origins}` |
| `sources` | Array of `{source, status, contributed, detail}` |
| `contributions` | Array of `{source, value, winner, origins}` |
| `diagnostics` | Array of `{code, severity, message, path, key, line, column}` |
| `valueTypes` | Array of `{pointer, kind, encoding}` for canonical types requiring JSON metadata |
| `exitCode` | Integer `0`, `1` or `2` |

A `source` is `{path, format, role, order, profile}`. `format` is `json`, `yaml`, `toml` or `memory`; `role` uses the canonical source-role name such as `RootPrimary`, `MemberPrimary`, `Global` or `Environment`. `profile` is a name or `null`. Source status is `loaded`, `missing`, `skipped` or `failed`; optional missing candidates remain in the source inventory.

An origin is `{key, source, line, column}`. Origins are empty unless requested for get/list/show entries; explanations always include them. With only `--show-scope`, path/format are empty and positions are `null`; with only `--show-origin`, the role is empty. `order` and profile metadata remain available. Composite values can have multiple winning origins.

For example, querying a present empty array returns a successful report containing:

```json
{
  "operation": "get",
  "scope": "effective",
  "key": "custom.targets",
  "found": true,
  "value": [],
  "entries": [{ "key": "custom.targets", "value": [], "origins": [] }],
  "sources": [],
  "contributions": [],
  "diagnostics": [],
  "valueTypes": [],
  "exitCode": 0
}
```

Reports preserve the readers' supported 128 levels of configuration nesting.
Consumers using System.Text.Json should set `MaxDepth = 256` when parsing deeply
nested reports, allowing room for the report envelope and typed value tags.

The `sources` array above is abbreviated to emphasize the value contract. `path` and other result source inventories retain their actual consulted-source reports. Finite numbers and integers stay JSON numbers, including integers outside Int64. Nonfinite numbers use a tagged value such as `{"$number":"NaN"}`, `{"$number":"Infinity"}` or `{"$number":"-Infinity"}`. The envelope's separate `valueTypes` entry identifies the canonical node with `kind: "number"`, `encoding: "nonfinite"` and a JSON Pointer such as `/value/custom/nan`. An ordinary user object containing `$number` has no numeric metadata at that pointer and remains an object. This avoids reserving user keys.

For example these two value fragments have different canonical types:

```json
{
  "value": {"$number": "NaN"},
  "valueTypes": [{"pointer": "/value", "kind": "number", "encoding": "nonfinite"}]
}
```

```json
{
  "value": {"$number": "NaN"},
  "valueTypes": []
}
```

The first represents a nonfinite number. The second is an ordinary object. The
complete report also carries the other required envelope fields listed above.
Positive and negative infinity use the tags `Infinity` and `-Infinity` with the
same external discriminator. Consumers use the metadata pointer, never user
keys alone, to recognize special values.

Temporal values retain their string text and have `kind: "temporal"` metadata with the original temporal kind in `encoding`, for example `LocalDate`. Metadata also identifies special values under `/entries/0/value` and `/contributions/0/value`. Pointers use standard JSON Pointer escaping, including `~0` for `~` and `~1` for `/`. Finite values need no metadata. Redaction replaces sensitive values and secret references with `"[REDACTED]"` before presentation and before special-value encoding; safe key/source metadata remains inspectable. Diagnostics retain structured codes and metadata while avoiding raw secret-bearing input text.

Stdout contains only results. Human diagnostic text goes to stderr in both human and JSON modes. Successful JSON with mixed-format warnings therefore remains parseable, and `diagnostics` carries the warnings for scripts. Usage errors also return one structured JSON report when `--json` is present.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Successful inspection, preview or edit, including unchanged and overridden writes |
| `1` | Requested get/explain key or targeted unset entry not found |
| `2` | Usage, validation, I/O, ambiguous selection, stale-plan conflict or recovery failure |

IR verification retains its existing interpretation of code `1`.

Malformed or inaccessible host `appsettings` files return `config.load` with exit code 2. With `--json`, the failure remains one JSON result. Invalid command arguments return `config.usage` before this failure; help does not load host configuration.

## CI example

```bash
morphir config show --isolated --discovery-boundary . --profiles ci --json > effective-config.json
morphir config get ir.format_version --isolated --discovery-boundary . --profiles ci --json
```

Isolation preserves supplied environment values and selected profiles. Control the job's environment as well when checking reproducibility. An explicitly selected eligible personal file retains its discovered ownership restrictions even under isolation.

## Editing one TOML target

`set` and `unset` select one file, then use the same reusable planner and writer available to embedded hosts. Without a selector, an invocation inside a project targets its primary. At a declared workspace root it targets the workspace primary, including a virtual workspace. A missing or ambiguous default context fails before writing.

Use one of `--workspace`, `--project <name-or-path>`, `--user`, `--global`, `--system` or `--file <path>`. `--user` uses the selected project's adjacent personal file, including a selected default member; a virtual root with no selected project uses its root-adjacent personal file. Multiple selectors fail. Explicit `--file` targets must end in `.toml`. `--config` remains a selected read overlay and cannot be combined with `--file`.

The editor uses an existing TOML primary when present. A JSON/YAML-only scope receives the corresponding TOML primary at the same layout. It leaves lower-format files unchanged and refuses a new same-format primary ambiguity. Comments, unrelated entries, key spelling, ordering, line endings, BOM and accepted BOM encodings are preserved. Repeating an identical set does not rewrite the file or update its timestamp.

Strings are the default, even with `--json`. Explicit types use JSON literal syntax:

| `--type` | Input |
| --- | --- |
| `string` | Literal command argument, including an empty string |
| `bool` or `boolean` | `true` or `false` |
| `int` or `integer` | Signed 64-bit JSON integer |
| `number` | Finite JSON number, retained as a TOML float |
| `array` | Complete JSON array |
| `object` | Complete JSON object |

Duplicate object keys, null values and nested nulls fail. Integer input and integer tokens inside composite values must fit Int64; explicit `number` input chooses finite floating-point precision, including for large literals. Arrays and objects replace the complete target entry. Paths address whole arrays and opaque references; traversal through an atomic ancestor fails. Known settings are validated in the authored target as well as the effective candidate, so a higher environment value cannot hide an invalid edit.

```bash
morphir config set ui.color false --type bool --global --dry-run --json
morphir config set custom.caption false --json  # writes the string "false"
morphir config set custom.targets '["csharp"]' --type array
morphir config set custom.generator '{"enabled":true,"targets":["csharp"]}' --type object
morphir config set 'codegen."custom.generator".enabled' false --type bool
morphir config unset custom.caption --user --dry-run
```

`unset` removes only the target's authored entry. It adds no tombstone and never deletes an inherited value from another file. Removing an override can reveal JSON, YAML, another scope or a default. A missing target entry exits 1. A write that remains overridden still exits 0; the report identifies its effective value and winning origins.

Profile, isolation, warning-suppression and discovery-boundary controls retain their read meanings. The explicit edit target is read for editing even when isolation excludes it from the resulting effective view. Independent workspace/global/system/file selectors do not turn an invalid automatic member selection into a target selection. Effective explanations use a valid selected member when available, so a successful workspace write can remain overridden by that member.

## Editing JSON reports

Every editing invocation with `--json` emits one object matching the [editing JSON Schema](https://github.com/finos/morphir-dotnet/blob/main/docs/spec/schemas/config-edit-result.schema.json). Stdout contains only this report; safe diagnostics go to stderr.

| Field | Meaning |
| --- | --- |
| `operation` | `set` or `unset` |
| `outcome` | `preview`, `written`, `unchanged`, `conflict`, `rolled-back`, `recovery-required`, `io-failure` or `failed` |
| `target`, `key` | Selected source metadata and canonical quoted path, or null before target selection |
| `before`, `after` | Redacted target-entry values; null represents an absent entry |
| `changed`, `dryRun` | Whether the proposed target differs, and whether this is a preview |
| `effective` | The nested inspection explanation with contributions, origins and its own type metadata |
| `diagnostics` | Safe codes and source/key/position metadata |
| `recoveryArtifacts` | Retained `{path, kind, transactionId}` records |
| `exitCode` | Stable outer command category, 0/1/2 |
| `valueTypes` | Type metadata for outer before/after values |

A nested effective explanation can report a missing value while a successful unset still returns outer exit 0. `changed` describes the proposed edit; after a conflict or rollback it does not claim that the candidate remains installed. Sensitive names and opaque reference shapes are redacted before rendering, including raw before/after input. No command executes a reference or resolves a secret.

## Preconditions and recovery

`--dry-run` performs no writer acquisition, staging or filesystem change. Application rechecks the plan's observed source/target identities, discovery inventories and absent alternate candidates under writer coordination. A stale source, destination or alternate-primary observation produces a conflict and leaves the preexisting destination unchanged. Reload, inspect the competing change and create a fresh plan.

Native writes stage on the destination filesystem and atomically install each file. The reusable transaction service supports multiple destinations for future callers, but does not promise group or crash atomicity across files. Generated-output publication and pruning manifests are separate and unchanged.

Native configuration writers use machine-local OS coordination. An embedded host writing a shared store from several machines must supply distributed coordination itself. External editors that do not participate can race with the final filesystem operation; this is an optimistic precondition protocol, not a lock on arbitrary applications.

After a complete rollback, the command exits 2 and cleans its temporary artifacts. An incomplete rollback or cleanup returns typed recovery information with retained paths and ownership. Preserve those files, inspect the target and journal, and restore or reconcile from the matching backup before retrying. Do not remove artifacts from another transaction. See [troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration" >}}).

Native filesystem access keeps the existing no-follow policy. On macOS, the conventional `/etc` path is a symlink, so the default system target can return an I/O-category failure before writing. Use an explicitly authorized physical TOML path when needed. A permission failure likewise returns 2; previews do not request elevated access or create directories.

## Planned migration

Project and workspace migration remain separate active changes. Editing does not remove classic manifests, perform migration equivalence checks or adopt workspace membership automatically.

## Related documentation

- [Configuration guide]({{< relref "/guides/configuration" >}})
- [Configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}})
- [Shared editing API]({{< relref "/docs/configuration/editing" >}})
- [Shared inspection API]({{< relref "/docs/configuration/inspection" >}})
- [Configuration troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration" >}})
- [Layered configuration PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}})
