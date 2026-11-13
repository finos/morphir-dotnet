# Upstream IR fixtures

## greeting-example-v3.json

A copy of `website/static/ir/examples/v3/greeting-example.json` from
[finos/morphir](https://github.com/finos/morphir), Apache License 2.0, at `main`
commit `648ae374cf68d7e7b8431353604827b77c7fad68` (git blob
`3233304dcec5303efbc3d1168514e7dffe787723`). The bytes are unchanged.

The release qualification of finos/morphir runs `morphir ir verify` on this file
for each offering and platform. Its values carry their inferred types as
attributes, and its expressions nest deeply, so it covers the cost of schema
evaluation and the `object | Type` form of attributes in the v3 schema.
Copy it again only from finos/morphir, and update the commit and blob here.
