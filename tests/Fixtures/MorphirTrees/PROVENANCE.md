# Morphir document-tree kit data

The files of `kit/` come from outside this repository, so a bug in the .NET tree reader or writer cannot change what the tests expect. Do not edit them by hand. `.gitattributes` keeps their bytes exact, and tests check their hashes.

The kit is reference data here, not a suite that this repository runs. `FixtureCatalog.Kit` in `tests/Morphir.TestSupport` reads each tree case of `document-tree.feature` (its id, its title, the files of its tree, its canonical single-file spelling and its expected diagnostic) and raises on a step form it does not know. The kit's runner, adapter protocol and CI gates are not used. When the kit is redesigned, only this folder and the reader change.

## `kit/`

Copied unchanged from `spec/ir/mck/` in [finos/morphir](https://github.com/finos/morphir) at `main` commit `4fa51c8b9a8634f8779e0e6b9900fc14b049da06` (2026-11-17), with `git cat-file blob <commit>:<path>`. The three files have the same blobs as at `9776c7010b5528a9fa68b4af79d9c971dce73ccd` (2026-11-16), the commit of the first copy. `FixtureCatalogTests` in `tests/Morphir.TestSupport.Tests` checks the embedded bytes against the catalog, and `FixtureOwnershipTests` in `tests/Morphir.Build.Tests` checks the files on disk against this table.

| File | Upstream path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `kit/document-tree.feature` | `spec/ir/mck/document-tree.feature` | `f0cd1247fe43666667c11e0b8df0b11f394ad687` | `a1aa74103357f788fcd0e6dac589b9f7e9c2e519b3f98eec66033c4d47148524` |
| `kit/distributions.feature` | `spec/ir/mck/distributions.feature` | `e4bc266c921e9a30a631a713c0059ce6e7cf6855` | `05ae2cfe848f98bced7a6dda407a6c08ffac15ed0e334173f7025de963fa3936` |
| `kit/versions.feature` | `spec/ir/mck/versions.feature` | `c065283a4fbe0b7650877cb2cc2dc170d379391d` | `b300695cc442fff827b093289eaa3e4a05bbac2221f37b9ea8d4e262ca2ece09` |

`distributions.feature` has the same bytes as `tests/Fixtures/MorphirIon/kit/distributions.feature`, which is pinned at an older commit. `distributions.feature` and `versions.feature` hold no tree file sets. They are pinned beside `document-tree.feature` because its cases refer to them (distributions-0006, -0010 and -0011 for the distribution kinds; versions-0003 to -0005 for the YAML profile, which `YamlToJson` follows).

`document-tree.feature` names no file of `spec/ir/mck/documents/`, so none is pinned.

## The tree cases

`FixtureCatalog.Kit.DocumentTree` lists the 14 cases with a tree or a diagnostic. `FixtureCatalog.Kit.FileSpellings` lists the other 3, `document-tree-0001`, `-0002` and `-0010`, which pin the spelling of a single manifest file: each with the file's name in a tree (`manifest` or `module`, from its `@node` tag), its canonical JSON and the other JSON spellings a reader accepts (0002's `module` for `path`). Together they cover all 17 cases of the feature.

| Cases | What the catalog gives |
| --- | --- |
| `document-tree-0003`, `-0004`, `-0006` to `-0009`, `-0011` to `-0015` | The tree's files and the canonical single file it reads back as |
| `document-tree-0005`, `-0017` | The same, with `ReadOnly`: the files carry `$meta`, which a writer never writes back |
| `document-tree-0016` | No tree: `RejectedDocument` and its `Diagnostic`, `version_mismatch` |

A YAML file or canonical spelling is translated to JSON. A JSON spelling is kept as the kit writes it. When a case gives both a YAML and a JSON canonical spelling, the reader checks that they are the same JSON value and keeps the JSON one.

## `acme-demo/`

A v4 document tree written by hand for this repository, not kit data. Its own `acme-demo/PROVENANCE.md` describes it and has its hashes.

## The written trees: `written-v4-library-with-dependency/`, `written-v3-library-with-dependency/` and `written-v4-unknown-type-tag/`

Trees that the document-tree store of `src/Morphir.Models` wrote, with its default options (path budget 4000, no compaction), from cases of the mothers of `tests/Morphir.Models.TestSupport`. `model convert` writes a tree through the same store; a check of the trees that `model convert` itself writes is task 6.3 of `add-model-convert`. They are on disk for the tests that need a tree as files, such as the black-box scenarios of `model verify` in `tests/Morphir.BlackBox.Tests/Integration/ModelVerification.feature`, which cannot build a tree with the store. `FixtureCatalog.Trees` in `tests/Morphir.TestSupport` serves them as `FixtureTree`s.

| Directory | Mother case |
| --- | --- |
| `written-v4-library-with-dependency/` | `DocumentTreeMother.v4LibraryWithDependency`, the split of `DistributionMother.v4LibraryWithDependency` |
| `written-v3-library-with-dependency/` | `DocumentTreeMother.v3LibraryWithDependency`, the classic split of `DistributionMother.v3LibraryWithDependency` with numbered attribute slots |
| `written-v4-unknown-type-tag/` | The one file of `DocumentTreeMother.Invalid.typeFileWithAnUnknownTypeTag` that differs from the written v4 tree: the type expression of `invoice` has the tag `Struct` |

`WrittenTreeTests` in `tests/Morphir.Models.Tests` fails when the store or a mother case writes other bytes. When that change is meant, write the files again from the mother cases, with `DocumentTreeStoreDriver.write` and `DocumentTreeStoreDriver.texts`, as UTF-8 with no byte order mark, and update the hashes here and in `FixtureCatalog`. The tree with no file for one value, `DocumentTreeMother.Invalid.moduleListingAMissingValue`, is the written v4 tree without `pkg/my-org/finance/pricing/models/default-currency.value.json`, so it has no files of its own.

| File | SHA-256 |
| --- | --- |
| `written-v4-library-with-dependency/deps/morphir/_sdk/@/api/module.json` | `e88833b3b5849cee61ee739eca26e44ee34dcffb179913dc0946045efa04e115` |
| `written-v4-library-with-dependency/deps/morphir/_sdk/@/api/money.type.json` | `60fdada51a9f8e69a326095b91d5c2c74fe3445e50e97c69171daf36055f98ab` |
| `written-v4-library-with-dependency/deps/morphir/_sdk/@/api/zero.value.json` | `8d31d6c801c2648a1a12d7e27019c14b429761343a57d67f8eac9472b2f4b0c2` |
| `written-v4-library-with-dependency/manifest.json` | `364827525a4e877ef167d91093626d76cd0bf87b9851802ecf8db67d3c510bf0` |
| `written-v4-library-with-dependency/pkg/my-org/finance/pricing/models/default-currency.value.json` | `765b553c9bf3198b4cebb8114b0968114fc3e18105e379563bf5bbf04dc86bf1` |
| `written-v4-library-with-dependency/pkg/my-org/finance/pricing/models/invoice.type.json` | `39db582b34df8e2180b22d5c74bb4f9fba0a85070e788e56b0feaf959b86033d` |
| `written-v4-library-with-dependency/pkg/my-org/finance/pricing/models/module.json` | `c40d1fa7ec51bb00a35006b9bf94cb0ae77c67db169d5d4c0deb163634f6028f` |
| `written-v4-library-with-dependency/pkg/my-org/finance/pricing/models/same-invoice.value.json` | `7b7b27ecd5a16f8c6d54922cf22392de81137362e13ff2f9bc8b99862850e253` |
| `written-v4-library-with-dependency/pkg/my-org/finance/pricing/models/status.type.json` | `fdc8acf6e3b600474bb815b8f2b53d8dea4c84d11554aed2ddbd62a74d18121f` |
| `written-v3-library-with-dependency/deps/morphir/_sdk/@/api/module.json` | `b6c2ba068b2caa8fab16e02cd0dcadfaa6c3e11e2fa0818e50868edc43a2c5ac` |
| `written-v3-library-with-dependency/deps/morphir/_sdk/@/api/money.type.json` | `113ffed5a1167e3ac72e6d9d53d3181798a173b210cb4d0806f53e1f1d415ece` |
| `written-v3-library-with-dependency/deps/morphir/_sdk/@/api/zero.value.json` | `ae1a65a6bc4a22dabcf2f2bc6afa2bf1592922ad8edf2d411f0d84278236a804` |
| `written-v3-library-with-dependency/manifest.json` | `78a3a5b2f928c3622e391b15c6ba97439270e4db2578ac1748d05e105ee100b6` |
| `written-v3-library-with-dependency/pkg/my-org/finance/pricing/models/default-currency.value.json` | `1ee1fbe087017465b141c0b2d5455f03cadafa3b05106098a98c2e7d587800de` |
| `written-v3-library-with-dependency/pkg/my-org/finance/pricing/models/invoice.type.json` | `a2eb31f5ccb5e1d63b6e594c6df153521980d5bba4db0cf304854acf50aa8904` |
| `written-v3-library-with-dependency/pkg/my-org/finance/pricing/models/module.json` | `46b86cbbf579eb4b1262f76dc0e63db8b2781267e7a4d842db682215bc9c61a0` |
| `written-v3-library-with-dependency/pkg/my-org/finance/pricing/models/same-invoice.value.json` | `b269aaa997d9b9ac1e314a69025e4f8450b0ba09173aa735c46f2bc1789cba7b` |
| `written-v3-library-with-dependency/pkg/my-org/finance/pricing/models/status.type.json` | `ff987a5f45e2bd1973cea494d0e091ff7c2fed788f24ded79b5b6179cf1b767f` |
| `written-v4-unknown-type-tag/pkg/my-org/finance/pricing/models/invoice.type.json` | `ca2b3a89a1a982fe7a59c6c491920ee365fde3ef5fdc555e451290e9fde43039` |
