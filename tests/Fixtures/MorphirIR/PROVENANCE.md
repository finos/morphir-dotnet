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

## The files and their hashes

`FixtureCatalog.Models` in `tests/Morphir.TestSupport` serves these files by intent, and
`FixtureOwnershipTests` in `tests/Morphir.Build.Tests` checks each one against its row. `.gitattributes`
keeps their bytes the same on every checkout. Each file other than `greeting-example-v3.json` was written by
hand in this repository.

| File | Intent | SHA-256 |
| --- | --- | --- |
| `greeting-example-v3.json` | a v3 Library | `539664c494509047ff485e37aff86a900978fa717bde8da30c24dc72599a0423` |
| `daily-rate-v3.json` | a v3 Library with one value | `d93da69798fd94ac1362738587b0d3586533173daef67ad11e7695a167a37f37` |
| `daily-rate-v4.json` | a v4 Library with one value | `1209abae3b32764518e01138fe42a57212cd4c3791963fe00e1ecf46ae109f7f` |
| `daily-rate-mismatch-v3.json` | a v3 Library with a type mismatch | `b8d68dd08cff4b218d66a2f1b73fa866cf483e0e0691f3e472da152d997519d3` |
| `constant-folding-v3.json` | a v3 Library with a constant expression | `84d6311b9c71bf915e9dbdb4b26afc4ff2c6b8529980cf1d9dc1528a3b3071ee` |
| `constant-folding-v4.json` | a v4 Library with a constant expression | `edbab7c2b612d93cf075c7a71b78882f3a796b9e403cc7586937d6549179703e` |
| `malformed-model.txt` | a model file that is not JSON | `e2579a145407c34c0a22f779fcd001d07fe56f3860645e0915f90e4543665bd1` |
