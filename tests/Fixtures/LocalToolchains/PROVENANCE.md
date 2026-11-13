# Local toolchain contract schemas

Copied unchanged from finos/morphir. `tests/Morphir.Toolchain.Tests` validates the documents that `morphirup`
reads and writes against these schemas, and checks each file's SHA-256 against the table below. Do not edit these
files by hand; `.gitattributes` keeps their bytes exact. To adopt another family, copy its files from the commit that
publishes it and update this table.

Repository: https://github.com/finos/morphir

Commit: `0fc5cde542ec980b2d67ce3ffed8b7b3619033c2`, the head of finos/morphir#1171, which publishes local-toolchains
`0.1.0-preview.5` (the schemas were first committed at `042b69f65`). Pending: when #1171 merges, name the merge
commit here.

| Local file | Upstream path | SHA-256 |
| --- | --- | --- |
| `LICENSE` | `LICENSE` | `b40930bbcf80744c86c46a12bc9da056641d722716c378f5659b9e555ef833e1` |
| `NOTICE` | `NOTICE` | `02a20174dd6ff4d866cad7d04ce8d61503306f3266f3c03261418d5011805889` |
| `0.1.0-preview.5/bundle.schema.json` | `website/static/schemas/local-toolchains/0.1.0-preview.5/bundle.schema.json` | `406835b6035f523565a6188a763d09d71a77625d1a06a8cfa9e2570cead1663a` |
| `0.1.0-preview.5/generation.schema.json` | `website/static/schemas/local-toolchains/0.1.0-preview.5/generation.schema.json` | `8ea6e4d6e49cf22413e2b9a9994f2c6aba9004b4ea3f5b59f7216b3f2a56828c` |
| `0.1.0-preview.5/result.schema.json` | `website/static/schemas/local-toolchains/0.1.0-preview.5/result.schema.json` | `6a49c582de9fc4609767356993e5d0276272a06a00584366433dfd2e1b99839f` |
| `references/toolchains/0.1.0-preview.1/receipt.schema.json` | `website/static/schemas/toolchains/0.1.0-preview.1/receipt.schema.json` | `935a0ccc903dfd6bdff097f3a37262779b19187a1be47a3d254e50eca8909e6a` |
| `references/extensions/0.1.0-preview.1/release.schema.json` | `website/static/schemas/extensions/0.1.0-preview.1/release.schema.json` | `e9be40a101ba745689bd7bf769f07ec19f1d19e48703538f0840ff5d4afcdd5a` |
| `references/extensions/0.1.0-preview.1/claims.schema.json` | `website/static/schemas/extensions/0.1.0-preview.1/claims.schema.json` | `9cc87748441e727c766a9a080f3160dbdb1b576e25f14455e199a0f47e07d6ff` |

The files below `references/` are the schemas whose `$defs` the bundle and generation schemas reference, from the
same commit. The tests register them, so no schema is fetched.
