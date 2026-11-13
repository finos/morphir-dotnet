# Morphir IR schemas

`ir verify` embeds the `.json` files (see `Morphir.Tooling.csproj`); each
`.yaml` file is the source of its `.json` file, and the two must state the same
schema. They come from `website/static/schemas/` of
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
