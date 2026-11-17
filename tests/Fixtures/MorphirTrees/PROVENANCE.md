# Morphir document-tree kit data

The files of `kit/` come from outside this repository, so a bug in the .NET tree reader or writer cannot change what the tests expect. Do not edit them by hand. `.gitattributes` keeps their bytes exact, and tests check their hashes.

The kit is reference data here, not a suite that this repository runs. `FixtureCatalog.Kit` in `tests/Morphir.TestSupport` reads each tree case of `document-tree.feature` (its id, its title, the files of its tree, its canonical single-file spelling and its expected diagnostic) and raises on a step form it does not know. The kit's runner, adapter protocol and CI gates are not used. When the kit is redesigned, only this folder and the reader change.

## `kit/`

Copied unchanged from `spec/ir/mck/` in [finos/morphir](https://github.com/finos/morphir) at commit `9776c7010b5528a9fa68b4af79d9c971dce73ccd` (2026-11-16, `origin/main`), with `git cat-file blob <commit>:<path>`. `FixtureCatalogTests` in `tests/Morphir.TestSupport.Tests` checks the embedded bytes against the catalog, and `FixtureOwnershipTests` in `tests/Morphir.Build.Tests` checks the files on disk against this table.

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
