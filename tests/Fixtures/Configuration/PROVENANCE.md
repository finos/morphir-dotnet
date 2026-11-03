# Configuration fixture provenance

The unmodified corpus snapshots and schema are pinned to these upstream commits.
No Morphir IR fixtures are changed by this campaign.

| Source | Revision | Snapshot | SHA-256 |
| --- | --- | --- | --- |
| finos/morphir-moonbit | `10732eaf2f05b10ca6e94a78121e2753c814acd8` | `toml-corpus.json`, 3 entries | `d85b36ab549bc49b965b8aba972a799216400ba5111d8197d55a10eacc9228d6` |
| finos/morphir-moonbit | `10732eaf2f05b10ca6e94a78121e2753c814acd8` | `json-corpus.json`, 23 entries | `caff9243c0afa55d19aa7f2f8f22b1e4392f89ce431f7574a01a8661237daece` |
| finos/morphir | `cde7722eb35987042a49dc646a846d693b0bd7f6` | `morphir-config-v1.json` | `b5b2f636b8fbbdcc4dc3636ffe7bcee81776d0dbd73810d9c9391f1ee2813901` |

MoonBit pins Moon-Lander configuration 0.8.0, tag revision
`d6216c7469c045548cb15b3f9321857ac41d2cd0`.
The tests verify snapshot hashes and every corpus entry's embedded UTF-8 hash.

Primary sources:

- [MoonBit TOML corpus](https://github.com/finos/morphir-moonbit/blob/10732eaf2f05b10ca6e94a78121e2753c814acd8/tests/configuration/toml-corpus.json)
- [MoonBit JSON corpus](https://github.com/finos/morphir-moonbit/blob/10732eaf2f05b10ca6e94a78121e2753c814acd8/tests/configuration/json-corpus.json)
- [Canonical schema](https://github.com/finos/morphir/blob/cde7722eb35987042a49dc646a846d693b0bd7f6/website/static/schemas/morphir-config-v1.json)
- [Core merge rules](https://github.com/finos/morphir/blob/cde7722eb35987042a49dc646a846d693b0bd7f6/docs/spec/morphir-toml/morphir-toml-merge-rules.md)
- [Framework control merge](https://github.com/moonrockz/moon-lander/blob/d6216c7469c045548cb15b3f9321857ac41d2cd0/pkgs/config/profiles/control_layers.mbt)
- [Framework profile rules](https://github.com/moonrockz/moon-lander/blob/d6216c7469c045548cb15b3f9321857ac41d2cd0/pkgs/config/profiles/profiles.mbt)
- [Framework JSON5 tests](https://github.com/moonrockz/moon-lander/blob/d6216c7469c045548cb15b3f9321857ac41d2cd0/pkgs/config/parsers/json5/README.mbt.md)
- [MoonBit classic normalization](https://github.com/finos/morphir-moonbit/blob/10732eaf2f05b10ca6e94a78121e2753c814acd8/pkgs/morphir-engine/workspace/configuration.mbt)

## Semantics and deliberate differences

The corpus covers classic manifests and basic TOML, not JSON5 edge syntax, YAML,
null handling or profile bounds. Additional behavioral cases qualify those rules.

Ordinary null overlays cannot unset a setting. Completed loads recursively omit
null object fields, including objects inside arrays, while preserving null array
elements. Shape merging is an ordered left fold and is not associative.
Profile controls use a separate raw merge where null replaces earlier metadata
and fails control typing. Readers retain raw nulls until controls are validated.

Profile names match `[a-z][a-z0-9_-]{0,63}`. Limits are 64 groups, 64 members per
group, 32 group levels, and 128 distinct active names. Inactive cycles and depths
are validated. Includes precede selected roots; each root precedes its descendants
before last-occurrence deduplication. Explicit empty selection allows includes;
disabled selection or a present whitespace-only environment selector suppresses
control interpretation, includes and profile reads.

Duplicate keys are rejected, including equivalent decoded JSON5 key spellings.
This intentionally differs from Moon-Lander's last-duplicate-wins behavior.
Canonical dependencies and unknown settings are preserved rather than repeating
the upstream JSON wrapper's removal of canonical dependency objects. Raw legacy
fields remain available for later migration loss accounting.

YAML acceptance policy is pending the user's choice between the core strict
profile and the current MoonBit Default profile. No qualification claim is made
while that decision is pending. TOML 1.0 inputs remain supported with CsToml.
