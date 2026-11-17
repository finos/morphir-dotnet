# A v4 document tree of the design's worked example

These files are a v4 document tree in the JSON profile, written by hand for this repository. They are the worked example of the `add-model-convert` design, package `acme/demo` with module `billing` and dependency `morphir/sdk`, laid out as finos/morphir's `docs/spec/ir/schemas/v4/document-tree-files.md` and `website/static/schemas/morphir-ir-v4-document-tree-files.json` define it, with the kit's spellings of a definition (`document-tree-0003`) and of a dependency below `deps/<package>/@/` (`document-tree-0008`). No .NET writer produced them, so a bug in the store cannot change them. The manifest has `version`, `created` and `layout`, which a reader drops, and a type's doc has a non-ASCII character.

The tests of "The same tree from two sources" in `tests/Morphir.Models.Tests` read them from a directory on disk and from resources embedded in that test assembly. `FixtureCatalog.Trees` in `tests/Morphir.TestSupport` owns them.

| File | SHA-256 |
| --- | --- |
| `deps/morphir/sdk/@/basics/module.json` | `0e47c0eb26d876b6f2db262a4bd12a9a1c5a8f0dfee16f65df971d9826db8ca6` |
| `deps/morphir/sdk/@/basics/money.type.json` | `f5d0b3a8151c9bc5f603ac5fe64d8185ade1d5a7ccbfaf558eb07a3e1b256c5e` |
| `deps/morphir/sdk/@/basics/zero.value.json` | `76413d075cfda4a3e31e8bddc9ff1146585c0f4c3710f9adaad4fd62f679203b` |
| `manifest.json` | `61eea341dfbc0f3a13c21b4bd818791af1fff7b473a5aba6359d404ae5fbaa33` |
| `pkg/acme/demo/billing/invoice-line.type.json` | `88345efdb446a5b6d8124dfd3f21334ce0da1270673ad1e23d66e6ccf9833d10` |
| `pkg/acme/demo/billing/invoice.type.json` | `e230307487bf89efdfa19f351685dfb8139e10df072a39fa50497e18c1a57767` |
| `pkg/acme/demo/billing/module.json` | `87858643a1037664b6e5b15a9b59f0b39800081aa0ab8bd7c50aa00ffa08fa4c` |
| `pkg/acme/demo/billing/total.value.json` | `4d912bf97b7298f07ae90213193eb13c9cae225cf3301536144130581c072f3a` |
