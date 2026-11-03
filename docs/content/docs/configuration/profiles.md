# Profile planning for library hosts

The additive document API plans profiles from supplied documents and controls.
`Profiles.plan` never discovers files, reads the process environment, executes
project code, or resolves secrets. Pass the environment map explicitly.

Call the planner before reading profile candidates. Read only candidates whose
`SourceIdentity.Profile` appears in the successful plan's `Active` list. Keep
inactive candidates unread, including invalid files. `ProfileSelection.Disabled`
returns an empty disabled plan without interpreting profile controls.

## Selecting profiles

The selector has this order of authority:

1. `ProfileSelection.Names` supplied by the host.
2. A present `MORPHIR_PROFILES` environment entry, including an empty value.
3. The discovered root primary documents' active selector.
4. The discovered root primary documents' default selector.

There is no implicit CI or default profile. A comma-separated environment
selector trims names. A present whitespace-only environment selector disables
profiles and suppresses includes and file loading. An explicit `Names []` selector
still permits base includes.
CSV trimming uses ASCII space, tab, newline, carriage return, vertical tab, and
form feed. Empty CSV elements fail validation. Values such as `dev,,metrics` or
`dev,` do not silently discard an empty name.
The legacy `CiProfileMode` API retains its separate behavior.

## Control ownership and ordering

Only discovered root primary documents own activation controls. A root primary
that is also explicitly selected keeps its original control authority, while its
ordinary values contribute once at the selected-file layer. Machine, member,
arbitrary selected, user, and profile documents cannot activate profiles. The
planner reports invalid controls before the resolver filters ownership subtrees.

`profiles.include` expands before selected roots. Groups emit their own name
before expanding members depth first, and duplicate names keep their last
occurrence. A higher group definition replaces the whole
lower definition. Enabled processing checks the entire declared group graph,
including inactive groups, for cycles and depth/expansion bounds. Names match
`[a-z][a-z0-9_-]{0,63}`. A graph contains at most 64 groups, each with at most 64
members and at most 32 group levels. The active plan contains at most 128 distinct
names. Host options may tighten depth and expansion bounds.

For example, `profiles.groups.production = ["postgres", "metrics"]` and
`profiles.active = ["production"]` activate `production`, `postgres`, then
`metrics`. A higher `profiles.active = null` fails control typing even if a lower
source supplied a valid list. Ordinary null-as-unset merging does not apply to
profile controls.

Resolution applies each active name's workspace documents before its member
documents, using JSON/JSON5, then YAML/YML, then TOML. All files for the next active
name follow all files for the previous name. Adjacent user overrides, environment
values, and explicit host values follow profiles.

## Diagnostics and integration

Inspect `ProfilePlan.Diagnostics` before reading any planned profile files.
Errors identify the declaring control's source and key path. The resolver
retains winning origins separately from the control plan. An explicit host owns
the source inventory and workspace/member identity. The library does not infer
either from current directories or ambient environment state.

The qualification baseline is Moon-Lander
[`d6216c7469c045548cb15b3f9321857ac41d2cd0`](https://github.com/moonrockz/moon-lander/tree/d6216c7469c045548cb15b3f9321857ac41d2cd0),
specifically `pkgs/config/profile_types.mbt`, `pkgs/config/profiles/profiles.mbt`,
and `pkgs/config/profiles/control_layers.mbt`. Its control merge keeps nulls until
validation, unlike ordinary configuration merging.
