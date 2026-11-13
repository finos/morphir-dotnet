# Local toolchain contract schemas

Copied unchanged from finos/morphir. `tests/Morphir.Toolchain.Tests` validates the result documents that `morphirup`
writes against these schemas, and checks each file's SHA-256 against the table below. Do not edit these files by
hand; `.gitattributes` keeps their bytes exact. To adopt another family, copy its files from the commit that
publishes it and update this table.

Repository: https://github.com/finos/morphir

Commit: `648ae374cf68d7e7b8431353604827b77c7fad68` (finos/morphir#1164, which publishes local-toolchains
`0.1.0-preview.4`)

| Local file | Upstream path | SHA-256 |
| --- | --- | --- |
| `LICENSE` | `LICENSE` | `b40930bbcf80744c86c46a12bc9da056641d722716c378f5659b9e555ef833e1` |
| `NOTICE` | `NOTICE` | `02a20174dd6ff4d866cad7d04ce8d61503306f3266f3c03261418d5011805889` |
| `0.1.0-preview.4/result.schema.json` | `website/static/schemas/local-toolchains/0.1.0-preview.4/result.schema.json` | `9ace148b4273f50e1fc7ce6cf0b5d2f5013be2afdf7e17ca02981df6e71bbc02` |
