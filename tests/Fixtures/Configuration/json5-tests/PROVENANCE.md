# JSON5 grammar corpus

Unmodified fixtures from [json5/json5-tests](https://github.com/json5/json5-tests),
revision `ceb24d4080137d70833f86c25659c1331b80a387`.
The imported [MIT license](LICENSE.md) and upstream [README](README.md) retain
their attribution and acceptance rules.

`manifest.json` records SHA-256 hashes for all 124 imported files: 82 accepted
syntax cases, 32 rejected cases and 10 metadata files. Tests verify every hash
before parsing. The `todo` Unicode identifier cases are included.

The upstream extension classifies `.json`/`.json5` as accepted and `.js`/`.txt`
as rejected. Morphir deliberately rejects `objects/duplicate-keys.json`, because
its document contract forbids duplicate keys. The manifest records that policy.

Tests wrap each original text in an object property so valid scalar and array
roots exercise the JSON5 grammar without changing Morphir's configuration-root
object requirement. A newline before the wrapper's closing brace preserves
end-of-file line-comment behavior. Every case runs under both the `.json` and
`.json5` reader extension. Inputs and line endings remain byte-identical.
