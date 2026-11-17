# Morphir IR schemas

`model verify` embeds the `.json` files (see `Morphir.Tooling.csproj`); each
`.yaml` file of v1 to v3 is the source of its `.json` file, and the two must state
the same schema. They come from `website/static/schemas/` of
[finos/morphir](https://github.com/finos/morphir), Apache License 2.0.
`SchemaLoader` makes the changes that evaluation needs when it loads a schema
(see `JsonSchema/TaggedUnionGuards.cs`), so do not edit the files for that.

## v1 and v2

Copies from before this record was kept. At finos/morphir `main`
`648ae374cf68d7e7b8431353604827b77c7fad68` they differ from upstream only in
descriptions.

## v3

An earlier copy, with three local changes:

- `doc` of a module definition and of a module specification is
  `[string, "null"]` (#510). Upstream has the same.
- `Attributes` is `oneOf: [object, Type]`, and `Name` allows a word that is a
  number. Both definitions are copied from upstream at `648ae374`. The first
  accepts the inferred types that morphir-elm writes as attributes, for example
  in `website/static/ir/examples/v3/greeting-example.json`.

Later upstream changes are not taken yet. finos/morphir#986 (IR 3.1.0) adds
`Specs` distributions and string format versions, and requires the
`{ "doc", "value" }` wrapper for each type and value of a module. The classic v3
writer of morphir-dotnet writes a type or value with no documentation without
that wrapper, so the whole upstream schema would reject the output of
`morphir compile`. Take it together with a change to that writer.

## v4 and the v4 document-tree files

Copied unchanged from `website/static/schemas/` of finos/morphir at `origin/main`
commit `9776c7010b5528a9fa68b4af79d9c971dce73ccd` (2026-11-16), with
`git show <commit>:<path>`. They are the same commit as the kit data of
`tests/Fixtures/MorphirTrees`. Only the `.json` files are pinned; their `.yaml`
sources stay upstream. `.gitattributes` keeps their bytes on every checkout.

| File | Upstream path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `morphir-ir-v4.json` | `website/static/schemas/morphir-ir-v4.json` | `9d29e297bd31b716213f5117a8884ffcf1424828` | `7954dfecc6f922810ae2911e6a7f5a53d7b95ef705290e51cdc876cec681aa12` |
| `morphir-ir-v4-document-tree-files.json` | `website/static/schemas/morphir-ir-v4-document-tree-files.json` | `37b5a905e8a7afec6efd9d4eedaacf575e5ac7c3` | `5e39697eb2ebcbd44ee1ca323a66c579cef40cc1b0221e7c866ea6a95a9692a0` |

`morphir-ir-v4.json` checks a single-file v4 model. The tree-file schema has no
root schema, only `definitions`; `SchemaLoader` makes the definition of a file's
kind the root with a `$ref` when it loads it: `DistributionManifestFile` for
`manifest.json`, `ModuleManifestFile` for `module.json`, `TypeDefinitionFile` for
`*.type.json` and `ValueDefinitionFile` for `*.value.json`. The parent publishes
no tree-file schema for v3, so a v3 tree is checked by the document-tree store
alone.

What the parent's v4 schemas do not check, and the store does: the tree-file
schema leaves the `def` and `spec` of a type or value file open
(`additionalProperties: true`), and `morphir-ir-v4.json` checks the types and
values of a module only in the wrapper `{ "access", "value" }`, not in the
canonical `{ "Public": {...} }` or the flattened `{ "access", "types", "values" }`
spellings. `model verify` decodes every document so that these gaps are
covered: a tree through the document-tree store, and a single v4 file through
`V4Json.readDistribution` after the schema pass. A test of
`tests/Morphir.Tooling.Tests` (`VerifyModelHandlerTests`) pins that the schema
alone accepts `FixtureCatalog.Models.V4DailyRateUnknownTypeTag`, a module in the
flattened spelling with a type node of an unknown tag.

Parent issue to raise in finos/morphir: `morphir-ir-v4.json` (`PackageDefinition.modules`,
lines 873 to 885 at `9776c701`) applies `ModuleDefinition` only to the `value` member
of the legacy wrapper, so the canonical and flattened module spellings, which its own
`AccessControlled` description names as canonical and accepted, have unchecked types
and values. When the parent fixes it, pin the new schema here.
Copy the files again only from finos/morphir, and update the commit, the blobs
and the hashes here.
