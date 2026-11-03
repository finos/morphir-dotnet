# Resolving explicit configuration sources

`Morphir.Configuration` provides additive immutable document contracts and pure
resolution. Its only NuGet dependency is FSharp.Core. The existing `MorphirConfig`,
`ConfigLayer`, `ConfigResolution`, `Merge` functions and `CiProfileMode` remain
available. Existing tooling continues its Git-first, cache-only TOML resolver and
CI overlay until a caller explicitly adopts the document API.

`Morphir.Configuration.IO` reads supplied text and explicit file paths without
constructing the CLI. It does not discover a root or search for configuration.
Use `ConfigurationReader.ReadText`, `ReadFile`, or `ConfigurationLoader` for an
explicit candidate inventory. See its [reader profile](../../../../src/Morphir.Configuration.IO/README.md)
and the [profile guide](profiles.md).

## Host responsibilities

The host supplies source identities, canonical path identities, source roles,
format groups, required/optional status, an environment snapshot and explicit
values. Use the same canonical `SourceIdentity.Path` for a selected discovered
file and its root/member inventory entry. The core compares these strings
ordinally; filesystem identity, symlinks and platform path normalization belong
to the host. `Order` orders supplied sources within the same role/format. Paths
break ties deterministically.

The host also supplies `ProjectScope` and a separate `OwningWorkspace` context.
Resolution never infers ownership, reads ambient process state, executes project
code, resolves secrets, or mutates input trees. A non-CLI host can construct typed
trees and call `Normalization.normalize`, then pass those documents directly to
`Resolver.resolve`. The same normalized documents yield the same values,
origins and diagnostics as the reader adapter.

```fsharp
open Morphir.Configuration

let result =
    Resolver.resolve
        { DocumentDefaults.resolutionInput with
            Sources = suppliedDocuments
            Environment = environmentSnapshot
            Explicit = explicitTypedValues }

let name = ConfigTree.tryFind ["project"; "name"] result.Effective
let origin = Map.tryFind ["project"; "name"] result.Origins
```

Check error diagnostics before consuming an effective document. Failed inputs
produce structured errors and source reports, rather than exceptions for normal
configuration failures. An explicitly selected missing file is always an error;
optional misses remain visible in `Sources`.

## Source and value order

The ascending source ladder is defaults, system, global, root primaries, selected
file, member primaries, active profiles, adjacent user overrides, environment,
and explicit values. Ordinary file groups merge JSON/JSON5, YAML/YML, then TOML.
Each active profile name contributes workspace formats then member formats,
before the next name. A selected discovered file contributes ordinary settings
only once, at the selected layer, while retaining its base control authority.

The core supplies the pinned baseline defaults: project source `src`, workspace
output `.morphir/out`, Scheme frontend, and IR v4 single-file JSON. Host defaults
can override these at the defaults layer. Classic checkpoint defaults are a
separate low-priority contribution, so system/global settings override them.

Objects recurse, arrays replace completely, and incompatible shapes replace.
Secret references are indivisible typed leaves. Null overlays leave lower values
and origins unchanged. Completed ordinary loads omit null object fields,
including objects inside arrays, while retaining null array elements. Apply
sources with an ordered left fold; regrouping shape changes is unsafe.

Winning leaf, empty-object and whole-array origins retain the source and any
available source position. Shape replacements remove stale child origins.
Consulted-source reports include loaded, skipped, missing and failed inputs.
Profile controls are excluded from ordinary values and origins.

## Ownership filters

System/global documents cannot supply any part of `project` or `workspace`.
For member resolution, inherited root primaries, workspace profiles, selected
root primaries, and root-adjacent user overrides exclude those entire subtrees.
Future project/workspace fields therefore cannot leak across ownership scopes.
The member keeps its project identity and defaults while inheriting applicable
tooling settings.

Use `SourceRole.WorkspaceUserOverride` for a root-adjacent override inherited by
a member, and `UserOverride` for an override adjacent to the target project.
Member `workspace.out_dir` and `workspace.output_dir` are filtered with
`config.member_output` diagnostics. They cannot relocate the supplied owning
workspace output context. Profile files also cannot redefine workspace root,
members, exclusions or default-member topology.

Profile-control authority is checked before ownership filters. A forbidden
`profiles` subtree cannot hide inside a source whose ordinary values are filtered.

## Environment and diagnostics

`MORPHIR_` uses `__` for nesting, keeps single underscores, and lowercases key
segments. Booleans, integers and JSON arrays/objects retain their types. Integers
beyond Int64 use `ConfigValue.BigInteger`. Other values remain strings. A
conflicting shorter path wins regardless of environment enumeration order.
Origins identify the winning variable. The reserved controls are `MORPHIR_HOME`,
`MORPHIR_PROFILES`, `MORPHIR_DIAGNOSTICS__SUPPRESS`, `MORPHIR_CONFIG`,
`MORPHIR_CONFIG_FILE`, `MORPHIR_PROJECT`, and `MORPHIR_WORKSPACE`.

Validation uses the pinned canonical schema, including nested task/workflow and
toolchain shapes. Unknown permitted properties remain in the typed document.
The generated pure rules can be refreshed with
`/usr/bin/python3 scripts/configuration/generate-schema.py` after reviewing and
pinning a new schema. No schema file is read at runtime.

`config.yaml_beside_toml` identifies sibling YAML/TOML primary sources.
`config.json_keys_below_toml` identifies JSON keys that survive their ordinary
format group alongside TOML. Suppression is accepted only from explicit host
options, comma-separated `MORPHIR_DIAGNOSTICS__SUPPRESS`, or system/global
`diagnostics.suppress` string arrays. Workspace/member suppression cannot hide
these warnings. Unknown suppression codes produce `config.unknown_suppression`.
