---
title: "Config commands"
linkTitle: "Config"
weight: 30
description: "Inspect effective and scoped Morphir configuration"
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

Every command emits one object with these fields. The [JSON Schema](https://github.com/finos/morphir-dotnet/blob/main/docs/spec/schemas/config-inspection-result.schema.json) is checked against subprocess results in the CLI behavior suite. No F# union or reflection-based serialization is exposed.

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
| `0` | Successful inspection, including present empty values and optional missing sources |
| `1` | Requested get/explain key not found in the selected scope |
| `2` | Invalid usage, ambiguous configuration/selection, malformed required configuration or loading failure |

IR verification retains its existing interpretation of code `1`.

Malformed or inaccessible host `appsettings` files return `config.load` with exit code 2. With `--json`, the failure remains one JSON result. Invalid command arguments return `config.usage` before this failure; help does not load host configuration.

## CI example

```bash
morphir config show --isolated --discovery-boundary . --profiles ci --json > effective-config.json
morphir config get ir.format_version --isolated --discovery-boundary . --profiles ci --json
```

Isolation preserves supplied environment values and selected profiles. Control the job's environment as well when checking reproducibility. An explicitly selected eligible personal file retains its discovered ownership restrictions even under isolation.

## Planned editing and migration

`config set` and `config unset` are separate active plans and are not registered by this inspection scenario. Project and workspace migration are also planned separately. Their designs include explicit targets, TOML preservation, validation and concurrent-write checks. Do not use those planned commands until their implementation is delivered.

## Related documentation

- [Configuration guide]({{< relref "/guides/configuration" >}})
- [Configuration walkthrough]({{< relref "/docs/getting-started/configuration" >}})
- [Shared inspection API]({{< relref "/docs/configuration/inspection" >}})
- [Configuration troubleshooting]({{< relref "/docs/cli/troubleshooting#configuration" >}})
- [Layered configuration PRD]({{< relref "/docs/contributing/design/prds/layered-configuration" >}})
