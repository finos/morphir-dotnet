# Bounded-generation receipt replay fixtures

These files are exact Git blobs from [finos/morphir at 177371fa](https://github.com/finos/morphir/tree/177371fa7a6199b7f177a8c1d886f583ee57e813). The sample is synthetic. The controls supply the independent case expectation, receipt patches, and expected replay results. Tests read the files through `FixtureCatalog.BoundedGenerationReceipts`.

| File | Parent path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `receipt.controls.json` | `spec/mck/bounded-generation/receipt.controls.json` | `e3ff17993db1709042262b9ccd42d6c07913c72d` | `b5be6a8bbcc8a24306499ed58c529180569e75df0a392da511ac014b5fb88f3e` |
| `receipt.schema.json` | `website/static/schemas/bounded-generation-kit/0.1.0-draft.1/receipt.schema.json` | `f0464ae8b6ee73448830a0674da36db1da1578bf` | `daea631f7b6ded1ff981395d7477c66827ffdb9fd4f17a59458d1e1576602b07` |
| `fixtures/receipt-qualified-wasm.json` | `spec/mck/bounded-generation/fixtures/receipt-qualified-wasm.json` | `e834f132c9420a0b1925be039ef246c68b5bbfb3` | `48097614346a2a302cdd43959fdc03d949e05279e027c0a6d9692b1bf918d735` |

The [Orders scale cases](https://github.com/finos/morphir/blob/305623ec52a96868a5a2e9cac7e80951b6030f73/spec/mck/bounded-generation/orders-scale.cases.json)
are vendored byte for byte. They record fixture identities and frozen artifact
expectations. Resource observations come from each execution receipt.

| File | Source path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `orders-scale.cases.json` | `spec/mck/bounded-generation/orders-scale.cases.json` | `97b5d6ad26d4e53730b906235bc8a04e2c6cf8a4` | `cde3dc58b49c229bac45af57126c2aa8b2f62c6e06766c84dd7f99bb8b0c4910` |
