# Morphir file context data (ir-context 0.1.0-draft.1)

The files of this folder come from the parent repository [finos/morphir](https://github.com/finos/morphir), so a bug in the .NET reader or writer of compacted files cannot change what the tests expect. .NET never defines the envelope, the policy or the expected compacted bytes. Do not edit the files by hand. `.gitattributes` keeps their bytes exact, and tests check their hashes.

The envelope is identified by its `$schema` URL, `https://morphir.finos.org/schemas/ir-context/0.1.0-draft.1/document.schema.json`. Its limits are 16,384 entries per table and 1,024 UTF-8 bytes per expanded spelling (the parent's `docs/spec/ir/context/examples/README.md`).

## `examples/`

Copied unchanged from `docs/spec/ir/context/examples/` of finos/morphir at commit `e0a3449c876436e6ac141970358fd2735fafdd7e` (2026-11-17, branch `feat/model-file-context`, not yet merged), with `git show <commit>:<path>`. The pin moves to the merged commit when the parent change merges (OpenSpec task 8.5). `FixtureCatalog.Context` in `tests/Morphir.TestSupport` serves each file and reads `cases.json`; `FixtureCatalogTests` in `tests/Morphir.TestSupport.Tests` checks the embedded bytes, and `FixtureOwnershipTests` in `tests/Morphir.Build.Tests` checks the files on disk against this table. The parent's `README.md` of the folder is not pinned; `cases.json` holds what the tests read from it.

| File | Upstream path | Git blob | SHA-256 |
| --- | --- | --- | --- |
| `examples/cases.json` | `docs/spec/ir/context/examples/cases.json` | `edcf19ce5c807723bc7ea8644910c648baba782a` | `af1a59afdfbd8ae17c7724512f9ce8739df81fcafd6f78128a58e77d71dfa2dc` |
| `examples/expanded/v3-manifest.json` | `docs/spec/ir/context/examples/expanded/v3-manifest.json` | `512f6b3d7650a429c22f7a58ce9b76667e2d1675` | `c2c8a7f3bd68e78f24e2c95c430461b1448c2994e2c5ad1bf873c0bc93a80ef3` |
| `examples/expanded/v3-module.json` | `docs/spec/ir/context/examples/expanded/v3-module.json` | `1d083c03605a4f2264d97ca29c09c8c6583b8cda` | `240f7695b3e8dea2f03c9201d218ba5933129deca7583ff80918206800828c21` |
| `examples/expanded/v3-single-file.json` | `docs/spec/ir/context/examples/expanded/v3-single-file.json` | `0fcdd2eb689471c7acc2926191cc31a5aecdab80` | `6095ee94f276efb07075adbbc41242ea8494f686a05faf38c9a556e90c41c738` |
| `examples/expanded/v3-type.json` | `docs/spec/ir/context/examples/expanded/v3-type.json` | `add67b155933ca9df5547ee86e7af04f970c4fbf` | `116f2e287b96d097c602994ca14c30aa1d3238cd3c945f233ccdde5ea13f787b` |
| `examples/expanded/v3-value.json` | `docs/spec/ir/context/examples/expanded/v3-value.json` | `9d7a24fc2b9181b69d5dbb2e704e5644ff146ef5` | `a0c2faf84b7479ded2e595d1734c1035c4f865a93b930bd45302a01d3c354a99` |
| `examples/expanded/v4-manifest.json` | `docs/spec/ir/context/examples/expanded/v4-manifest.json` | `728f48a6eaa0c797481fc52925c0c3cf5fbc75d4` | `1f67490668d233d8b2e6a53cc4d312ee725eef837effdec1b899d6e767c1d0a9` |
| `examples/expanded/v4-module.json` | `docs/spec/ir/context/examples/expanded/v4-module.json` | `b59b8701d499a5c779c2242acaa6a9cc61607a9e` | `8469e9cb66313616c859419bbf1d0514ed77026ffa3dd6a4b1c4fba2214b5e5e` |
| `examples/expanded/v4-single-file.json` | `docs/spec/ir/context/examples/expanded/v4-single-file.json` | `ca8f452bbbc248e3c52fea39ec61cbec3369dcae` | `ed8dc46dfb35e75ad2f3bd6afeeea52f20cdef6e869c6226b186cfd39c8c9e5f` |
| `examples/expanded/v4-type.json` | `docs/spec/ir/context/examples/expanded/v4-type.json` | `95d7a412e07b7a7e7c8c7193464373c47c4fa11b` | `7802da9f3c6d409d3f20e4f1b7f969de7ba7b69b021e2cf4a15e48313fab8f7e` |
| `examples/expanded/v4-value.json` | `docs/spec/ir/context/examples/expanded/v4-value.json` | `21054ca8cd72eab6afb99ae871cc047989c9dc13` | `a33aa101c97101bb3ed38d1a560b1735eee9acd285e00410f6d88c6fb193de2d` |
| `examples/invalid/context-after-document.json` | `docs/spec/ir/context/examples/invalid/context-after-document.json` | `9d061554c802ffd8cfe07408a9c1de9c8afed004` | `eb748071304c955bb0cd9e3a2d158894c889d749797cb0d9a4317c1ed5697d36` |
| `examples/invalid/duplicate-entry.json` | `docs/spec/ir/context/examples/invalid/duplicate-entry.json` | `6ab3302f5ad71721ea8aff26750afea7a21e38e9` | `10e58f219400543684d371f1a427875d56289d2b22a60cfa0e42b0d6b7aba440` |
| `examples/invalid/name-too-long.json` | `docs/spec/ir/context/examples/invalid/name-too-long.json` | `1966c8b0130e5fb6a7d660e438564d7b67374725` | `42c85f4a0caae1b875c3afd671b0596294808549037a853ec596cd5aa92008fb` |
| `examples/invalid/too-many-entries.json` | `docs/spec/ir/context/examples/invalid/too-many-entries.json` | `6eb8abd97e96b831c3e10b889213ee53c7d26bc9` | `f86c270d0a61607d62dbd356039b687bec9c493b2be5e35fc0078ae3987d757c` |
| `examples/invalid/undeclared-schema.json` | `docs/spec/ir/context/examples/invalid/undeclared-schema.json` | `973d5322268794a9b40f6e61c581a3edf6c38362` | `49bbc95d6d8599edcbfbd81033f65a33d81b50a7365ae2d108132003ff329f08` |
| `examples/invalid/unknown-alias.json` | `docs/spec/ir/context/examples/invalid/unknown-alias.json` | `a03e7b6abe0bcfc98df3f4bba87a02e4e280cf6b` | `1bd3db99eec20dd4626bd5e348e0b9219ecc89ba92cd39e8e95f2b4a0df5a693` |
| `examples/invalid/unknown-critical.json` | `docs/spec/ir/context/examples/invalid/unknown-critical.json` | `c379f97c78e022835cc6f5b7144309da425c0650` | `fb950b9b9463e46ceab12c1ae6c9a9d3c048518bc0f8cf3667c2cae872521e4b` |
| `examples/valid/v3-manifest.json` | `docs/spec/ir/context/examples/valid/v3-manifest.json` | `76507971249387f5248c9c13e4b2c53995e857fb` | `2c1608b66f23df6b6fe50d91f4e520bd74b7f4a24d92dffefc8e3902656ca95b` |
| `examples/valid/v3-module.json` | `docs/spec/ir/context/examples/valid/v3-module.json` | `98a333f7d0ab984b9bf6652b6b1d5d2a6f430a94` | `1667174463458a1e047ded1adeedf2d616a95fd053d72f38848d39d031562513` |
| `examples/valid/v3-single-file.json` | `docs/spec/ir/context/examples/valid/v3-single-file.json` | `e4daa3fb7d62272b1f859a048a0c1e25c1a6519f` | `ef822e3859dcf730e3f6fef365c19fe4f65937e8edfcc7990a348dd434ceb93e` |
| `examples/valid/v3-type.json` | `docs/spec/ir/context/examples/valid/v3-type.json` | `98eb88fa937f03bfd69f56ccfcef5d0018fa7f3e` | `e536f0ee95f6f42c708cc03fa5d8fd0db2bbb595b2dc614b559ab6ed4ea8241c` |
| `examples/valid/v3-value.json` | `docs/spec/ir/context/examples/valid/v3-value.json` | `e39fdf5b140d3899beaf836310ca7c2d78d693b8` | `56b7151ea22b625f30a23c6374d48ee396fc7e305173cc7f8357bd808bb1bf52` |
| `examples/valid/v4-manifest.json` | `docs/spec/ir/context/examples/valid/v4-manifest.json` | `e46de27adb8029cfe0989bf5d781d32919a1e66c` | `a146a74881ceec07a1246bae075ce52cde1b0595308e45191d3920e7398cf52c` |
| `examples/valid/v4-module.json` | `docs/spec/ir/context/examples/valid/v4-module.json` | `4a59bba60ec9a231895ff7013324ed9c36b0e4fb` | `c3ce4d01b7f2590fdfe55af5ce48891d126adfe06bdd2bd806878b31f3eb65ae` |
| `examples/valid/v4-single-file.json` | `docs/spec/ir/context/examples/valid/v4-single-file.json` | `397f1630655ac9aa353e21c1131abe8959d202e1` | `a1583d61b731a0734d8b866747432f3ff08b642473feb91d7afc5ed017f1f589` |
| `examples/valid/v4-type.json` | `docs/spec/ir/context/examples/valid/v4-type.json` | `ec4da362752f309e60f6277349a78f0e794bb805` | `db101eda5e41ff38e2f009098226e4d7cc15392f4b1307272d10e3654f8c9143` |
| `examples/valid/v4-value.json` | `docs/spec/ir/context/examples/valid/v4-value.json` | `59fe2fc61989801ca4fd7a6c2bd32e7e3c0d90ab` | `94ec9bae07b361f6dabb7da1103e3aa122f20982a1d003738a9fd65599404a2e` |
