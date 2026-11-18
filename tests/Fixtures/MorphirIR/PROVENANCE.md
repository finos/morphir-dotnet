# Upstream IR fixtures

## greeting-example-v3.json

A copy of `website/static/ir/examples/v3/greeting-example.json` from
[finos/morphir](https://github.com/finos/morphir), Apache License 2.0, at `main`
commit `648ae374cf68d7e7b8431353604827b77c7fad68` (git blob
`3233304dcec5303efbc3d1168514e7dffe787723`). The bytes are unchanged.

The release qualification of finos/morphir runs the CLI's verify command on this
file for each offering and platform (`morphir ir verify` until its pin moves to a
release with `morphir model verify`). Its values carry their inferred types as
attributes, and its expressions nest deeply, so it covers the cost of schema
evaluation and the `object | Type` form of attributes in the v3 schema.
Copy it again only from finos/morphir, and update the commit and blob here.

## The files and their hashes

`FixtureCatalog.Models` in `tests/Morphir.TestSupport` serves these files by intent, and
`FixtureOwnershipTests` in `tests/Morphir.Build.Tests` checks each one against its row. `.gitattributes`
keeps their bytes the same on every checkout. Each file other than `greeting-example-v3.json` was written by
hand in this repository. The four files of `model verify` (`empty-library-v1.json`, `empty-library-v2.json`,
`no-format-version.json`, `string-format-version.json`) have the bytes of the models that `ir verify` tests read
before the catalog; the unit tests of `tests/Morphir.Tooling.Tests/TestData` keep their own copies.
`daily-rate-unknown-type-tag-v4.json` is `daily-rate-v4.json` with the output type of `daily-rate` spelled
`{"Struct":{}}`, a type node with a tag that no type node has, in the same flattened module spelling. The parent's
`morphir-ir-v4.json` (pinned at finos/morphir `4fa51c8b`, see `src/Morphir.Tooling/Infrastructure/Schemas/PROVENANCE.md`)
refuses the file, but locates the fault only at the module; `model verify` names the node by decoding the model
(`FixtureCatalog.Models.V4WithAnUnknownTypeTag` gives its JSON Pointer).

| File | Intent | SHA-256 |
| --- | --- | --- |
| `greeting-example-v3.json` | a v3 Library | `539664c494509047ff485e37aff86a900978fa717bde8da30c24dc72599a0423` |
| `daily-rate-v3.json` | a v3 Library with one value | `d93da69798fd94ac1362738587b0d3586533173daef67ad11e7695a167a37f37` |
| `daily-rate-v4.json` | a v4 Library with one value | `1209abae3b32764518e01138fe42a57212cd4c3791963fe00e1ecf46ae109f7f` |
| `daily-rate-mismatch-v3.json` | a v3 Library with a type mismatch | `b8d68dd08cff4b218d66a2f1b73fa866cf483e0e0691f3e472da152d997519d3` |
| `constant-folding-v3.json` | a v3 Library with a constant expression | `84d6311b9c71bf915e9dbdb4b26afc4ff2c6b8529980cf1d9dc1528a3b3071ee` |
| `constant-folding-v4.json` | a v4 Library with a constant expression | `edbab7c2b612d93cf075c7a71b78882f3a796b9e403cc7586937d6549179703e` |
| `malformed-model.txt` | a model file that is not JSON | `e2579a145407c34c0a22f779fcd001d07fe56f3860645e0915f90e4543665bd1` |
| `empty-library-v1.json` | a v1 Library with no modules | `adb360f70001e3f19a84fba55f323c08e6d446266710d8d6128afe04389fdc95` |
| `empty-library-v2.json` | a v2 Library with no modules | `c13b52cff9d1fc3939e21d672ffc458d3a62991db343f7158bfffb0c103cc6a3` |
| `no-format-version.json` | a model with no format version | `fd1150056eed9943ac2944d91c00ae4a83a0faa3a486d88a582b4754904a268e` |
| `string-format-version.json` | a model whose format version is a string | `a772afcac09350b2792fc894010e4cab06e0ee19bcc7225d717f3a0e037e0562` |
| `daily-rate-unknown-type-tag-v4.json` | a v4 Library with an unknown tag in one type node | `7d25e4f44621846ab7264439d0ca457a1c4e9e1fcb66faf1f515000e95ee992b` |
