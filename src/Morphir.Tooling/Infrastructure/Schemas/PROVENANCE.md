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

Copied unchanged from `website/static/schemas/` of finos/morphir at `main` commit
`4fa51c8b9a8634f8779e0e6b9900fc14b049da06` (2026-11-17, the merge of finos/morphir#1186, which contains
finos/morphir#1185), with `git cat-file blob <commit>:<path>`. They are the same commit as the name-context data of
`tests/Fixtures/MorphirContext` and the kit data of `tests/Fixtures/MorphirTrees`. Only the `.json` files are pinned;
their `.yaml` sources stay upstream. `.gitattributes` keeps their bytes on every checkout.

| File | Upstream path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `morphir-ir-v4.json` | `website/static/schemas/morphir-ir-v4.json` | `b88e304447a610d49bcef0f6ad40581735d0d844` | `f8124be2eb43ab5bcde32ad15b838e983838bd31862a3bf6bd73844e1284e1bc` |
| `morphir-ir-v4-document-tree-files.json` | `website/static/schemas/morphir-ir-v4-document-tree-files.json` | `e28da9cca4ed840e15422bd607a79e4efc525fc8` | `21bf78e500848fc2727eab2f3f9c1437842c05508f486ea1c34a2cae4ca0afd4` |

`morphir-ir-v4.json` checks a single-file v4 model. The tree-file schema has no
root schema, only `definitions`; `SchemaLoader` makes the definition of a file's
kind the root with a `$ref` when it loads it: `DistributionManifestFile` for
`manifest.json`, `ModuleManifestFile` for `module.json`, `TypeDefinitionFile` for
`*.type.json` and `ValueDefinitionFile` for `*.value.json`. The parent publishes
no tree-file schema for v3, so a v3 tree is checked by the document-tree store
alone.

Since finos/morphir#1185, `morphir-ir-v4.json` checks the types and values of a
module (`AccessControlledModuleDefinition`) in each of its spellings: the canonical
`{ "Public": {...} }`, the legacy wrapper `{ "access", "value" }` and the flattened
`{ "access", "types", "values" }` that morphir-dotnet writes. It tries them in turn
(`anyOf`), so a fault inside a module is reported at the module and at each
spelling, not at the node. Since finos/morphir#1186 both schemas also say that the
key order of a value's `inputs` and `inputTypes` is the parameter order (a
description; no check changes).

What the parent's v4 schemas do not check, and the store does: the tree-file
schema leaves the `def` and `spec` of a type or value file open
(`additionalProperties: true`). `model verify` decodes every document, so that
this gap is covered and every fault is named at its node: a tree through the
document-tree store, and a single v4 file through `V4Json.readDistribution` after
the schema pass. Two tests of `tests/Morphir.Tooling.Tests`
(`VerifyModelHandlerTests`) pin this on `FixtureCatalog.Models.V4DailyRateUnknownTypeTag`,
a module in the flattened spelling with a type node of an unknown tag:
`TheV4SchemaRefusesAnUnknownTypeTagInAFlattenedModule` (the schema alone refuses
it, at the module, and accepts the same module without the fault) and
`AV4ModelIsDecodedSoAnUnknownTypeTagIsNamedAtItsJsonPath` (`model verify` reports
the schema's errors and one `unknown_node` at the node's JSON Pointer).

Copy the files again only from finos/morphir, and update the commit, the blobs
and the hashes here.
