# Morphir Ion fixtures

These files come from outside this repository, so a bug in the .NET reader cannot change what the tests expect. Do not edit them by hand. `.gitattributes` keeps their bytes exact, and tests check their hashes.

## `mep/`

Copied unchanged from `spec/mep/mck/` in [finos/morphir](https://github.com/finos/morphir) at commit `679199153dcab3615bad4e2afa064b435bb19ec9`. `model-transport.cases.json` records each fixture's length, SHA-256 and base64 content, and `IonReaderTests.fs` checks every fixture against that record.

| File | SHA-256 |
| --- | --- |
| `mep/model-transport.cases.json` | `bdb5af481230...` (first 12 digits; the case file is not listed in its own manifest) |
| `mep/fixtures/rich-model.ionb` | `49f87687c033a8ec39a7f42b93bb1c54b4856ceab0b05246d81ea0a8b27e10b1` |
| `mep/fixtures/rich-model.ion` | `3c314a4e29bb3e1477171aff632fecec35257d0835c2e18549c732c609dfa664` |
| `mep/fixtures/plain-ir.json` | `fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f` |
| `mep/fixtures/legacy-ir.json` | `fd49ad8b0cab49216323beb969c08f91a888925dbb995838c57a05b48a80bb2f` |
| `mep/fixtures/guarantee-evidence.json` | `5f09ec4a785145b8442a6789437559aeea1e9b7432ee243a0796b0f0294ff6c8` |

### Corpus cases and the decoded reader

`ModelDocumentTests.fs` runs `model-0012`, `model-0013` and `model-0014` through `ModelDocument.readPayload` with each case's guest limits, and checks the expected code. It reads the fixtures for `model-0024` (retention) and `model-0025` (the explicit JSON projection) through the scenarios in `mep-model-reading`, not through the case file. The reader starts from decoded bytes, so every other case belongs to later MEP work:

| Cases | Kind | Why the decoded reader leaves them out |
| --- | --- | --- |
| `model-0001` to `model-0010`, `model-0031` | negotiation | They match offered and supported tuples, protocol versions and IR versions. No document is read. |
| `model-0011` | limits | The encoded-byte bound applies to base64 text before decoding. The reader takes decoded bytes. |
| `model-0015`, `model-0016`, `model-0017` | integrity | Declared length, SHA-256 and base64 syntax are checked on the envelope before the payload is decoded. |
| `model-0018` to `model-0020`, `model-0028`, `model-0029` | outcome | They check whether a result carries a model, which is a property of the JSON-RPC result. |
| `model-0021`, `model-0022`, `model-0023`, `model-0030`, `model-0032`, `model-0034` | evidence | They match guarantee evidence against a model, revision, context and scope. |
| `model-0026`, `model-0027` | compatibility | They concern the `morphir-ir` profile and legacy MEP 0.1, which carry plain JSON. |
| `model-0033` | compatibility | It checks critical paths in the envelope. |
| `model-0035` | limits | It bounds the sum of the model and evidence payloads. The reader sees one payload. The case's code is `decoded_limit`, while `model-0012` uses `decoded_limit_exceeded` for the same kind of bound. |

## `v4/`, `kit/` and `ion-draft.md`

Copied unchanged from [finos/morphir](https://github.com/finos/morphir) at commit `679199153dcab3615bad4e2afa064b435bb19ec9`. `Json/V4/V4KitTests.fs` in `Morphir.Models.Tests` checks every file against this table. `FixtureCatalog.UpstreamIr.KitFeatures` in `tests/Morphir.TestSupport` serves the `kit/` features with these hashes, and `FixtureCatalog.Kit.CanonicalSpellings` reads their canonical YAML and JSON spellings, which `IR/Trees/CanonicalYamlTests.fs` checks the YAML writer against. `FixtureCatalog.UpstreamIr.IonDraft` serves `ion-draft.md`, whose canonical empty v3 library `IonIrWriterTests.fs` writes. `v4/morphir-ir-v4.schema.yaml` and `ion-draft.md` have changed upstream since `67919915` (`website/static/schemas/morphir-ir-v4.yaml` by `a5718c0b`, through finos/morphir#1185 and #1186; `docs/design/draft/ir/ion.md` by finos/morphir#1133). They are kept as an Ion snapshot. The hash check of `V4KitTests.fs` and `FixtureOwnershipTests` in `tests/Morphir.Build.Tests` read them to verify their hashes, and the Ion writer's reference tests in `IonIrWriterTests.fs` read `ion-draft.md` through `FixtureCatalog.UpstreamIr.IonDraft` to write its canonical empty v3 library; the v4 schema that `model verify` uses is pinned at `4fa51c8b` in `src/Morphir.Tooling/Infrastructure/Schemas`.

| File | Upstream path | SHA-256 |
| --- | --- | --- |
| `v4/complete-example.json` | `website/static/ir/examples/v4/complete-example.json` | `30fbea9879b377dde11d0a4916b926dba152069e7b24bd43e4cf33cb27fcd979` |
| `v4/books-and-records-example.json` | `website/static/ir/examples/v4/books-and-records-example.json` | `bdc25e9fb332a49f7f12bfa80c907eca482ae5b63bad07575ef06194ab99fda3` |
| `v4/morphir-ir-v4.schema.yaml` | `website/static/schemas/morphir-ir-v4.yaml` | `a4733ec14ae94fe6bd17a22584f8c74e1ab38920c14ec408f016d3160c1f339a` |
| `kit/definitions.feature` | `spec/ir/mck/definitions.feature` | `cacff2dfe5a0edd7f759a3db4de50289cc1c37f3aeb080c5525f1054c984d3e5` |
| `kit/distributions.feature` | `spec/ir/mck/distributions.feature` | `05ae2cfe848f98bced7a6dda407a6c08ffac15ed0e334173f7025de963fa3936` |
| `kit/names.feature` | `spec/ir/mck/names.feature` | `9e7de8831da4270ccdcc701f9a57f929fd39d376b9743821a3d253973cf19e43` |
| `kit/patterns-and-literals.feature` | `spec/ir/mck/patterns-and-literals.feature` | `45b71901c8a2cb6ffe636c165b105c1fcdfa66bdf1d28b3fecb21108ed44652d` |
| `kit/types.feature` | `spec/ir/mck/types.feature` | `4dc0c0e95e11d4203e96c8d561676a9c23134e3635ae68cc3e840eee8f7eeed8` |
| `kit/values.feature` | `spec/ir/mck/values.feature` | `9bbb3d092e29148d5eac4e8cd63bf2f9a9d5418b38755fba9453dfc108248f8c` |
| `ion-draft.md` | `docs/design/draft/ir/ion.md` | `ebb72938b80d65e91683d484a98444e79835d3dd7aca317397d88dd8e2915a5d` |

## Reference pairs and records

These files come from the `morphir` CLI release `v0.4.0-beta.12`, asset `morphir-0.4.0-beta.12-aarch64-pc-windows-msvc.zip`. The zip's SHA-256 is `ebdaf284e22d725d0cb5fb532e6a193f2dda3398e282db21a3166ab9b3bbef23`, which is the release's published value. The `morphir.exe` inside it has SHA-256 `d901aadce10376354eb7e164b306fda14527b8486dc43bb1d6b17e9e4249883c`. `IonIrPairTests.fs` checks every file against the table below, and fails on a file with no row.

Each case in `pairs/` was generated from a scratch directory with these two commands, where `<input>` is the source named below and `<version>` is `v3` or `v4`:

```text
morphir ir migrate <input> --target-version <version> --output-format json -o pairs/<name>.json
morphir ir migrate <input> --target-version <version> --output-format ion -o pairs/<name>.ion
```

The hand-written inputs also pass `--input-format json`.

| Case | Source |
| --- | --- |
| `greeting-example`, `rule-set-example`, `direct-rules-example` | `website/static/ir/examples/v3/` in finos/morphir at `95a57e1` (v3) |
| `complete-example`, `books-and-records-example` | `v4/` in this folder (v4) |
| `v3-daily-rate` | `pairs/inputs/v3-daily-rate.input.json`, hand-adapted from `tests/Fixtures/MorphirIR/daily-rate-v3.json` |
| `v3-library-nodes`, `v3-specs`, `v4-library-nodes`, `v4-specs`, `v4-application`, `v4-empty-library` | `pairs/inputs/<name>.input.json`, hand-written |

The CLI refuses `daily-rate-v3.json` as it stands: it has no `doc` wrappers (`missing field doc`), and its value attributes are `{}`, which kit case distributions-0013 refuses. The adapted input adds the wrappers and types the attributes, and is otherwise the same document.

`UpdateRecord` was removed from `v3-library-nodes`. The CLI writes the v3 record update as `Update`, which upstream kit case versions-0028 already records as a writer bug. The classic reader takes only `UpdateRecord`, so the node is tested against hand-written canonical JSON instead of a CLI pair. The hashes below are of the files as they stand.

`lcr-morphir-ir.json` is left out because its Ion form is about 5 MB, and because the reader refuses the unmodified file: the pinned CLI writes raw U+0002 characters in its docs, which Eleven19.IonDotnet refuses as `malformed_ion`. The hand-written inputs reach what the examples miss: native, external and incomplete bodies, holes, attributes on every node, annotations, document literals, the `specs` and `application` kinds, and v3 dependencies.

### Hand-written record vectors

The CLI's writer does not emit records, so `records/<name>.record.ion` and `records/<name>.datagram.ion` are written by hand. `records/<name>.json` is generated. Each file was read with these commands, and the two outputs were byte-identical for every vector:

```text
morphir ir migrate records/<name>.record.ion --input-format ion --target-version <version> --output-format json -o <name>.record.json
morphir ir migrate records/<name>.datagram.ion --input-format ion --target-version <version> --output-format json -o records/<name>.json
```

Do not read `document-numbers` as a claim about the draft. It records the JSON text the CLI gives for each Ion decimal, which is what the reader's lexeme rule follows.

| File | SHA-256 |
| --- | --- |
| `pairs/books-and-records-example.ion` | `d7cfa91adc4759d621a43a058c767d252f5a70b29c00ff828de2611de636c21c` |
| `pairs/books-and-records-example.json` | `1b16d3ee46aa6dec65841000e3bf8d28a990837843aef63e0003c2ca4829a15e` |
| `pairs/complete-example.ion` | `5f56fee733a00e51ae9920ed456c2860194a2a1fa425304e7e923e4fca4caa48` |
| `pairs/complete-example.json` | `fbaa25336c90b7f369c17f5f2104496d3de481dff211532319724c465ef1b095` |
| `pairs/direct-rules-example.ion` | `c0097541b81a4dec2967f8b4c3fbe0a02c6bab17aa914337cfa6c29bca12febe` |
| `pairs/direct-rules-example.json` | `dd78f3baab0b7639567134c8d23dffc5a337f82a9521f0a73e33e635860118da` |
| `pairs/greeting-example.ion` | `a30ed9bf164a3260bb7df6db7952894eddc7c7399af99072685c0f4ce0f237b9` |
| `pairs/greeting-example.json` | `2cf6a695a9f3859da30cea76a4b4d9acb81d3d55023affa5a9bc8a9c005e7004` |
| `pairs/inputs/v3-daily-rate.input.json` | `c937eb1490b0bac66d9ebaf3f11923b9b2b23a3dcae9d36f91b3a41abb322a4a` |
| `pairs/inputs/v3-library-nodes.input.json` | `f4ef068c72597027c668008c0d4625efbe7ba72bbaae8821af898fcbaa9e55f9` |
| `pairs/inputs/v3-specs.input.json` | `66ec4f5f1cce95bf8742186a282d5a11adeed51df8a21979f3c6baa85af602f2` |
| `pairs/inputs/v4-application.input.json` | `00cbf4f8a684a83f1feda085b429eb8240139cb7fd723c4cec81383dc4a42e12` |
| `pairs/inputs/v4-empty-library.input.json` | `36365cef275591a949125a40886142d22571da8aac89055dd8f7f526e3270bce` |
| `pairs/inputs/v4-library-nodes.input.json` | `1de0f7f4768bc555d4fc098cf4e85bf3ab2c7b285866c9a17c34922178710373` |
| `pairs/inputs/v4-specs.input.json` | `79a3c0aab91e9ba2c985e8a3d38090422e5b8ff1dcc354b895ecd612805b3c05` |
| `pairs/rule-set-example.ion` | `3ce9c2873a84192beeccfb7583f986ba17f78137b31d5f96ebf9f4405536997c` |
| `pairs/rule-set-example.json` | `a5bc8d6e2acfd4ff8d9a6c54cf1e9058de597c710ba458b31a8be1bd0c8414d1` |
| `pairs/v3-daily-rate.ion` | `057ff48219c0280173e42ae9649907cf8c3e6a30586a007f5fd6c71e57d982e0` |
| `pairs/v3-daily-rate.json` | `5afebe65d650a3467d116aa3d83b26f01631657ab03ef2dfb4e146b1573f001d` |
| `pairs/v3-library-nodes.ion` | `3941df9f799d1dc2830d2e82ea991bb30f85b27683311a7b118cf245624df2b1` |
| `pairs/v3-library-nodes.json` | `f2ca693fc65ca3244bb96227ea45c7af2fb98be9faf7100e50cf3c94eac621ed` |
| `pairs/v3-specs.ion` | `9e80b67c34744c36dee004e20d1a4ba9e4f733ca1c414db2ebe9779224e43991` |
| `pairs/v3-specs.json` | `079390570095546e6ff721edcea96e492d8f66640befdba25c5725d63aaadf2f` |
| `pairs/v4-application.ion` | `dc72d6b350cc826e433f330e036c0a5d07359daa635d482a7f3c3961f99bcec7` |
| `pairs/v4-application.json` | `256b18910a28aede380d4ed4aefba5e6e60b790d0d828e4fe28276bb54771756` |
| `pairs/v4-empty-library.ion` | `fc5826b33ae0efc7830a2b6cc1fad2e014cf30a10a4ea357f1e08073ae005694` |
| `pairs/v4-empty-library.json` | `2ba7fb00c8c0c1cc9b956043e1bbe5919be303a2946ed7558c796ad15e23fe69` |
| `pairs/v4-library-nodes.ion` | `02a8815ed8c1d665c7b71a951f6f883779c7f87964765ba38c3ed5e66d7e414a` |
| `pairs/v4-library-nodes.json` | `f4251a5bae9d4d7abb8566e1e48d11eb8d31b3cfa22b269e8c96014a70e1affc` |
| `pairs/v4-specs.ion` | `e983cc0abfaf6687a6bf66bec97091589de24fcaa016e9665bcab66c4e78346f` |
| `pairs/v4-specs.json` | `5bc82d132605dd88db4d661246b8df0b3af91694dc3533414b82f2d30956c4ef` |
| `records/application.datagram.ion` | `ff1522b8dafa0507614590c884fc4b1f308eb5c4a069094a02cafc234f66ebb1` |
| `records/application.json` | `8adc711586ca42d1ca0c8e63809f2b6eb56ca53903bcd087fdc323a6bd6805ed` |
| `records/application.record.ion` | `eb2a2e52dec63a0f9b471cd8508201cce3e5456906c47193a593b300cd67d26e` |
| `records/document-numbers.datagram.ion` | `a9f1f0425a1f695cefd85262a03b7c78fe487521328329bfd28f306a1df2309c` |
| `records/document-numbers.json` | `ed37fcd6d6f3e903fe95582d46fb11506e561f5094f5f8ae2f07b1be31d3d0cb` |
| `records/document-numbers.record.ion` | `78376853b299eefa1a90003daff9312b6a83f8608fc5c88bb756946665365467` |
| `records/empty-library.datagram.ion` | `fc5826b33ae0efc7830a2b6cc1fad2e014cf30a10a4ea357f1e08073ae005694` |
| `records/empty-library.json` | `7a05a8318f10ad481d2e33f9b7b6122c41f548777200ab8f15a98ad2e082c363` |
| `records/empty-library.record.ion` | `83664ded6666de3f0bcfce3040839a429917ee6b125fc826e8d7f69a5d71028c` |
| `records/library.datagram.ion` | `ac64a4a3f770c65d40bf237deb3240bcfd0e7ad91eb2a3561c39905042c8ff4c` |
| `records/library.json` | `6ba238abdac2e6cfcc58e09324f39ba36d255c755dd636f163813b314edc123b` |
| `records/library.record.ion` | `06d709a0e2644037529deb12de85fd1fbd0117da7f2b63f7c466306ddc8e1a8f` |
| `records/shorthand.datagram.ion` | `de8a040f3c37a808b412cd2fab7804cff0c5803dd814ddd0ac66f35ce92e087c` |
| `records/shorthand.json` | `3869de77813dc556ec54e42b746fb313fb0b8841df34e06b67f91d37c84d7504` |
| `records/shorthand.record.ion` | `664a45e12a85055d6e0c921f5f383d2d63aff7bf29c2fdf5f3f0f4dbeee292a5` |
| `records/specs.datagram.ion` | `94fc02574f62e24221e2d99109f6268cece4544fda20ba4b7b9a6c4a47cabb0c` |
| `records/specs.json` | `83e2b01e62de866aac3742b45e05644d3ad83c46f571d1c092ff8bd996b73fe0` |
| `records/specs.record.ion` | `fd4685aec378689e9f9b5ffc9a7a52035f88b318695aca756a7e3e2916a04110` |
| `records/v3-library.datagram.ion` | `69a25647fb0ed016a9ac2e0e2701ad5fcf92c71f3670cb02c865512452e59815` |
| `records/v3-library.json` | `210c803484dbda09b9f647a4190f3f8aa1dbb7fedc5243bfb00814b1170ded4c` |
| `records/v3-library.record.ion` | `5c2d1f61c9c205871df726a4ac1667311dd0f9f65be23f152940319dc7872fdc` |
| `records/v3-shorthand.datagram.ion` | `428ce29c73834ed212ccec13d9b1f2fc92b7030cc0ec675c0d7846fd682568e8` |
| `records/v3-shorthand.json` | `312dd7b9f690fbf55732ae94349c7366ff644dd54250f0509d6dd41c11d99eb3` |
| `records/v3-shorthand.record.ion` | `254dc2c8b28766862e30a8343054627f71dc460a8a8765ab8055a5ef809380d3` |

## Reference trees

These Ion document trees come from the same `morphir` CLI release, `v0.4.0-beta.12`, the zip with SHA-256 `ebdaf284e22d725d0cb5fb532e6a193f2dda3398e282db21a3166ab9b3bbef23` named above. Each is the tree form of a distribution that `pairs/` already holds as a single file, so `IonIrTreeReferenceTests.fs` reads the tree and the single file and expects equal results. Each was generated from a scratch directory with this command, where `<version>` is `v3` for the cases whose name starts with `v3-` and for `greeting-example`, and `v4` for the others:

```text
morphir ir migrate pairs/<name>.json --target-version <version> --output-layout vfs --output-format ion -o trees/<name>
```

`v4-escaped-names` has no pair. Its input, `trees/inputs/v4-escaped-names.input.json`, is hand-written to reach the names that need escaping: Windows device names (`con`, `aux`, `nul`, `com1`, `lpt9`) as types, values and a module directory, initialisms (`value-in-USD`, `user-ID`, a module `ID`, packages `morphir/SDK` and `acme/XML`), a module `manifest` holding a type `manifest` and a type `module`, and a module `manifest/module`. Its tree and its single file came from:

```text
morphir ir migrate trees/inputs/v4-escaped-names.input.json --target-version v4 --output-layout vfs --output-format ion -o trees/v4-escaped-names
morphir ir migrate trees/inputs/v4-escaped-names.input.json --target-version v4 --output-format ion -o trees/v4-escaped-names.ion
```

The trees hold no empty directories, because git would not keep them. A dependency with no modules, such as `morphir/SDK` in `v4-library-nodes`, has an entry in the manifest and no directory.

The CLI cannot write a cut stem here. It takes no path budget option, its default is 4000, and a path that long is over the limit of many file systems. The cut-stem cases in `IonIrTreeTests.fs` and `TreeStemTests.fs` use digests computed with PowerShell's `SHA256` from the rule in finos/morphir decision 0012, not with this library.

A tree is recorded as one digest: the SHA-256 of the lines `<SHA-256 of the file>  <location in the tree>`, one per file, sorted by ordinal comparison of the location, each ending in a line feed. That is the output of `sha256sum` over the tree's files in `LC_ALL=C` order. `IonIrTreeReferenceTests.fs` recomputes it, and fails on a tree with no row.

| Path | SHA-256 | Files |
| --- | --- | --- |
| `trees/books-and-records-example/` | `cc7b009bf557dc40e25ded51757a0a3c4afbd785336d171c768913c5016a2da5` | 19 |
| `trees/complete-example/` | `aea6c6ef293b328fc3d9351ce85beff1f9b2ff977e43c1e875fb6b78f1937e52` | 13 |
| `trees/greeting-example/` | `80b984d840413280e3c553d3e5e79001ceabcfe6936ed9a23aa9eac64009aeef` | 18 |
| `trees/v3-daily-rate/` | `b5f293aa7ac8cefa1f705c80644a5a744054b2be55ade760369fe3ad4074fedd` | 3 |
| `trees/v3-library-nodes/` | `0c892d057ccff6a4dc4dfc83cb9929762ebd2781b7a9b52437d8f8abb64cda1a` | 26 |
| `trees/v3-specs/` | `d2a86d5bbe70644e2f6b6783ae268e073c738bb6ef6839e9d800aa6fb3a5d622` | 12 |
| `trees/v4-application/` | `04c70164bebef63154ff41c9d4e5992056d2bb6e92ae59b363a964c07bc76801` | 6 |
| `trees/v4-empty-library/` | `fc86155d4bb45b3785bceed5537ec05fa2718cf6e00bee3ab7c2c2150a69d33e` | 1 |
| `trees/v4-escaped-names/` | `09320d43b73b0d4355ea596cc13a115ea745227fde7a3adfb8e905de5306b843` | 26 |
| `trees/v4-library-nodes/` | `871713ac6fffa5e47983b15e71c7b4e3812dcdc361800a4c169019bd1f290de5` | 35 |
| `trees/v4-specs/` | `ee8c4217257c2c80fb9e2489f3b3ca92400c0719fb9b8bebd2422146c96df192` | 18 |
| `trees/v4-escaped-names.ion` | `c9b9054e58cf2c80a47dc7ef17d8a2c00c9467dce6d2c86f93e3359928198e1d` | 1 |
| `trees/inputs/v4-escaped-names.input.json` | `69a4f83198c8ac9b9c9953a3d9899e25df1d354938ad94257ea42d269554dca9` | 1 |

### Reference tree files

`FixtureCatalog.UpstreamIr.IonReferences` in `tests/Morphir.TestSupport` serves each reference single file and each file of the reference trees, so `IonIrWriterTests.fs` reads the canonical writer's byte reference through the catalog. Each tree file's SHA-256 is below; together they give the tree digests above.

| File | SHA-256 |
| --- | --- |
| `trees/books-and-records-example/deps/morphir/_sdk/@/basics/bool.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/basics/float.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/basics/int.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/basics/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/dict/dict.type.ion` | `3591857c8682c385e322292be8a221c5c1108d92acc4d36bef034b9b28f558ac` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/dict/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/string/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/books-and-records-example/deps/morphir/_sdk/@/string/string.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/books-and-records-example/manifest.ion` | `7fc68fc3fa1b394c8e13e585725b30c480436d1217adc70a3c7b42f96bb0c80b` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/book.type.ion` | `2f45f9c13b30e5eb1ba72b12246f2436edbde938782fec393829d183255a3917` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/close-deal.value.ion` | `7794ef168039953be6b589133b1081f795747b575a285f716f7898017192ae31` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/deal-cmd.type.ion` | `22c144e8e1291ad29b22bc95b50e6b754d4169c89f5f08b83d4e0a6d09c34085` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/deal-event.type.ion` | `85e1e2314119af055a8f7439e400ff95d2468dec094398517ced5715b1456690` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/deal.type.ion` | `243ad5f8b47c3ef0cc379a321ed7ad1b847b4bd59173ad66ff3d6a53cb4b3c41` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/module.ion` | `ed6dd45a59afccb9adfc7eb841466d7ca2601cd27d480f221f10345bd84582e9` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/open-deal.value.ion` | `2068731d7837fb1e1e1f6695f57b9888522ad2fd158596fa669f2155eac0ced6` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/price.type.ion` | `757633b5cdefc3b83678d260ae9a21521fc9dba5db85df00bd9e174df433bc16` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/product-_id.type.ion` | `8e7084b8b47322e57613b60c3a46e939afac80bb73c5993c79fab7d4b256f0a8` |
| `trees/books-and-records-example/pkg/morphir/reference/model/books-and-records/quantity.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/complete-example/deps/morphir/_sdk/@/basics/add.value.ion` | `f557fe8bad0641a87d39756a5812d84143520f8e0e3723b9b58a461f618afb49` |
| `trees/complete-example/deps/morphir/_sdk/@/basics/bool.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/complete-example/deps/morphir/_sdk/@/basics/float.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/complete-example/deps/morphir/_sdk/@/basics/int.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/complete-example/deps/morphir/_sdk/@/basics/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/complete-example/deps/morphir/_sdk/@/list/list.type.ion` | `b0c63dd3a1b3536f3e675e951bcbde72fe909fca8f8d6b52b21a3b40217a678c` |
| `trees/complete-example/deps/morphir/_sdk/@/list/map.value.ion` | `7d4d40aec35da74aa8be1f039c50e000b28a3c7feebf17b2f0662a77b97da148` |
| `trees/complete-example/deps/morphir/_sdk/@/list/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/complete-example/manifest.ion` | `d96a861b1418f0f6493772700e6f25af335dc78f94fe45ec2059e76e0c259447` |
| `trees/complete-example/pkg/regulation/u-s/f-r-2052-a/data-tables/calculate-total.value.ion` | `2bd1612aa97524d265ea5f163e787b2f64c6e4ce349eb499355415cf7d5fb960` |
| `trees/complete-example/pkg/regulation/u-s/f-r-2052-a/data-tables/data-tables.type.ion` | `f6bb4f94e2f4eb3a8462cae0ff028815ba98f9c672aadf88e72821ebb6758b9c` |
| `trees/complete-example/pkg/regulation/u-s/f-r-2052-a/data-tables/inflows.type.ion` | `5bd66492eed9b133e1463ddee48b1bc14e8715b7a259c9b0965f815d6d514778` |
| `trees/complete-example/pkg/regulation/u-s/f-r-2052-a/data-tables/module.ion` | `4da9a719f7698c8d76fe1394e0243373cff72c4dea71d86e1683911ce29024ca` |
| `trees/greeting-example/manifest.ion` | `21950920a70f33d6bbd756de2285fd6336e407b8f2d23d642c5db330cac101c1` |
| `trees/greeting-example/pkg/elm-compat/api/api-error.type.ion` | `5b2420d02a8ca16785006ab55bcbaad67bc48db2359ac32a11a805b935a84372` |
| `trees/greeting-example/pkg/elm-compat/api/create-order.value.ion` | `c93b7b107598883caf9d430181c6c3a7585cf4debd466de931b0388690e3ba10` |
| `trees/greeting-example/pkg/elm-compat/api/get-order-status.value.ion` | `4dc7a93830cc27d61c9c1661694a82d6af89151b4325bec4dd4663587f4f3545` |
| `trees/greeting-example/pkg/elm-compat/api/module.ion` | `50df6bb4990e278394e395d74f69097cbd53514a5bbf4a61d1d38ce4d2dfee31` |
| `trees/greeting-example/pkg/elm-compat/api/process-request.value.ion` | `25f2a1faf2c4f2c2184dc716319346a2dcfbc0145796b98544f2b8b561517693` |
| `trees/greeting-example/pkg/elm-compat/api/request.type.ion` | `0f9b2c5d79d7afe1f5036b0d53e76c81c316d44351392441b7c69ae4b7dc0d54` |
| `trees/greeting-example/pkg/elm-compat/api/response.type.ion` | `2a6a784244ee119326e7b513fefd52ca874e1d8411274c2b643f7a83c1536e0a` |
| `trees/greeting-example/pkg/elm-compat/main/apply-discount.value.ion` | `e582130011dd9e600a19d15ef3b5085ece0718353d45a5f35ec95933e036de3d` |
| `trees/greeting-example/pkg/elm-compat/main/calculate-total.value.ion` | `786c9c2659d13bd5bd18656d669d161b66220a373520473bad723fbdf2837c7b` |
| `trees/greeting-example/pkg/elm-compat/main/customer-order.type.ion` | `a90419b8c8194b841750998f660f35f0cd03cca99a8390352d6d4fd707b9da86` |
| `trees/greeting-example/pkg/elm-compat/main/is-valid-order.value.ion` | `01ecb4c369a42c3aedff56a4a27b24a4994e0e8159576643242529c77d28b7b7` |
| `trees/greeting-example/pkg/elm-compat/main/module.ion` | `1444e6e7c6b1ee85c3c82f9f1be93d6493d8ebcb728ddbd4428e8a326525f070` |
| `trees/greeting-example/pkg/elm-compat/main/order-status-to-string.value.ion` | `4310df2fe146a6717e458708993b03811be02dbc61984fcc4df8ad3660cc1096` |
| `trees/greeting-example/pkg/elm-compat/main/order-status.type.ion` | `9c795b38f8d6c07ee0166bdbdb93d208a0083765b264e9b9bbc4cb23c269dd0c` |
| `trees/greeting-example/pkg/elm-compat/main/product-id.type.ion` | `9497e57023b8eb1c692512ae7f802c62c1061f9b7c68455e47d88bdcb16898f3` |
| `trees/greeting-example/pkg/elm-compat/main/product.type.ion` | `cc02a5e45241f825713996ccea6295656ddbbd5ce87298e5e52539b24281dc74` |
| `trees/greeting-example/pkg/elm-compat/main/quantity.type.ion` | `78e0874f75916f73431c3ebdfcc9829fe38ef5741f7b96b1c8ebdf8865c21139` |
| `trees/v3-daily-rate/manifest.ion` | `655a8604be8cd2db6488ce45cd3152aabbe62b870f93fdcd4af94f1c90fa50a1` |
| `trees/v3-daily-rate/pkg/car/rental/inventory/daily-rate.value.ion` | `754ce7627f3db7199a6f7ad514a064e5368c50471eabfd0348af1a755d2aa196` |
| `trees/v3-daily-rate/pkg/car/rental/inventory/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v3-library-nodes/deps/my/dep/@/util/choice.type.ion` | `9560f87171f96c0382be4a07f6be53bd17e45d93a89409e4140e2dbb74d9b97d` |
| `trees/v3-library-nodes/deps/my/dep/@/util/handle.type.ion` | `216bbd8c9d1494215721a2b27cd4b2e336f190a76372f031f050fac623fbab82` |
| `trees/v3-library-nodes/deps/my/dep/@/util/id.type.ion` | `584123cb814cddac15067225adcf0adf5beeeae9ebdc9ed701f970a9b32f8d3e` |
| `trees/v3-library-nodes/deps/my/dep/@/util/module.ion` | `152b799b5d6cf78cdeebf720ac9b8c7f848500b60927cafe6748788d0108e0ca` |
| `trees/v3-library-nodes/deps/my/dep/@/util/money.type.ion` | `44cb0b555b6f1ca74712cdd718b4f29f564e46f67ea6c8bddb71ebe9f904b6ec` |
| `trees/v3-library-nodes/deps/my/dep/@/util/now.value.ion` | `d6316f0f9112bfb0edf0c211cdb1ec0883156a2c7e5b869ce35b1c5ea3dc4427` |
| `trees/v3-library-nodes/deps/my/dep/@/util/parse.value.ion` | `0ce799c7ef699062d4383deb28d76ee905806efcb085a8a8c0d3c964ca379baa` |
| `trees/v3-library-nodes/manifest.ion` | `595f2a35ef7dff6c47207cd281b96044cec212f6b83fe00b166d66e64a03ab7d` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/accessors.value.ion` | `110390c2a6d00a54f9e3b4a067952562c1a3b960f4f737fe3f8eb9c425f58048` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/box.type.ion` | `218e5005cd221e82f16db525f9e4d988911aba7fd5de455916b19ee48d717412` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/control.value.ion` | `2d8e37bc7b231cb104f5df4c5c2924d5253062bbfdb500a784d5ba9af5589b14` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/destructured.value.ion` | `a9fd57cf4a8bc2850537aadf63278e22acbce0aba55f6387d0cfcc35dfc7b43a` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/handler.type.ion` | `ea06c2d7cca6150730b03eb26119c86538b570ae9d689f17a5b37f85d0814255` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/lambda-value.value.ion` | `994ac756e07ec4956f563ac635ff8b541882c8c038cbe45c8963a0150abcb274` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/list-value.value.ion` | `41baf33605f16dde2277c46bdde11d431ae1e36f19deacbfd5bf2ae6163aec29` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/literals.value.ion` | `3da92655c25cb5553fd3887658232feef50429d7a02be75ce3610e7215ce8d8e` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/maker.value.ion` | `c685a7bdfbd6f23491e41530189f8a29540251862d1f7c571b4655ef23887ed6` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/module.ion` | `7a5d996a81d1165d4bb192b78f4f68344eee67d1f61b67087f9d10a0e1c8ed75` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/open-record.type.ion` | `9b4d645eb811729be6feae7cd0de59dc90c78ebd7aa2d2f749da4a97566cce5a` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/pair.type.ion` | `6619e6a2fdf4bdc0d6fccd7114c9477bff638d9c313875a17b239a276f6a9345` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/patterns.value.ion` | `0d4880eec6cac8e81098031bea38e71e9077746c2860e1d131e976ec824e69c1` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/records.value.ion` | `70ce609c85a88ac41d9749d3020f78833fe483e7925ba072a8f0eb152f0f43d1` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/references.value.ion` | `53d8a2655b7725fb37d4eb4dece6e558973db585474c644b7c7955fbe15b4c7a` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/shape.type.ion` | `e0db97388829f5583c1b9ec890f3e89fdb423122a56ddbb0e4dccbc8a74671ba` |
| `trees/v3-library-nodes/pkg/my/pkg/domain/unit-value.value.ion` | `c56446e95ef0d4a1134e33665fdce0353a34c3b2bbc4f9ce08fd4abcb199341c` |
| `trees/v3-library-nodes/pkg/my/pkg/hidden/module.ion` | `44d6ea18c5aa02e03491bb7c7165d51781e18025e1164a4a66f601faa3e0e59d` |
| `trees/v3-specs/deps/my/dep/@/util/choice.type.ion` | `9560f87171f96c0382be4a07f6be53bd17e45d93a89409e4140e2dbb74d9b97d` |
| `trees/v3-specs/deps/my/dep/@/util/handle.type.ion` | `216bbd8c9d1494215721a2b27cd4b2e336f190a76372f031f050fac623fbab82` |
| `trees/v3-specs/deps/my/dep/@/util/id.type.ion` | `584123cb814cddac15067225adcf0adf5beeeae9ebdc9ed701f970a9b32f8d3e` |
| `trees/v3-specs/deps/my/dep/@/util/module.ion` | `152b799b5d6cf78cdeebf720ac9b8c7f848500b60927cafe6748788d0108e0ca` |
| `trees/v3-specs/deps/my/dep/@/util/money.type.ion` | `44cb0b555b6f1ca74712cdd718b4f29f564e46f67ea6c8bddb71ebe9f904b6ec` |
| `trees/v3-specs/deps/my/dep/@/util/now.value.ion` | `d6316f0f9112bfb0edf0c211cdb1ec0883156a2c7e5b869ce35b1c5ea3dc4427` |
| `trees/v3-specs/deps/my/dep/@/util/parse.value.ion` | `0ce799c7ef699062d4383deb28d76ee905806efcb085a8a8c0d3c964ca379baa` |
| `trees/v3-specs/manifest.ion` | `10be395f4d44ea32e9aaee95c228623bad7fbf74a6c24bbab3f59f9b9f368f3a` |
| `trees/v3-specs/pkg/my/pkg/basics/identity.value.ion` | `1682c0539c7c668a2cdf2bce28e71cb77ca7098e45765540f5bf6595e92dee03` |
| `trees/v3-specs/pkg/my/pkg/basics/int.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/v3-specs/pkg/my/pkg/basics/module.ion` | `2a9d10f49a38a921f3d399a4e6f9561fc8ad6609ca501f5dc46de1c278c81af3` |
| `trees/v3-specs/pkg/my/pkg/empty/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-application/deps/my-org/shared/@/secret/module.ion` | `e00672ed57d5ef450d871c035bc1756399a4112ef2fb94239d541d776ddcbeaf` |
| `trees/v4-application/deps/my-org/shared/@/util/identity.value.ion` | `3c2915c58f786904680470b130daf6b86f455838abfc2c906784d05c34489a6a` |
| `trees/v4-application/deps/my-org/shared/@/util/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v4-application/manifest.ion` | `d93e25c64dfdd2542f0f983e39998742429e0e05782061a684d30255f1424a2d` |
| `trees/v4-application/pkg/example/main/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v4-application/pkg/example/main/run.value.ion` | `441bad2f9497f95defe647fb2a17d263a6c9d789d57db61c9757040d56ab6738` |
| `trees/v4-empty-library/manifest.ion` | `275bb27a4036e71abf0bd8f58be7d8ab7a53a621f1558b93b2d52356f87674cc` |
| `trees/v4-escaped-names/deps/acme/_xml/@/manifest/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-escaped-names/deps/acme/_xml/@/manifest/module.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/v4-escaped-names/deps/morphir/_sdk/@/basics/int.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/v4-escaped-names/deps/morphir/_sdk/@/basics/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-escaped-names/deps/morphir/_sdk/@/con_/aux_.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/v4-escaped-names/deps/morphir/_sdk/@/con_/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-escaped-names/deps/morphir/_sdk/@/con_/value-in-_usd.type.ion` | `dfd8dc4e56c928cc6897ed8af8f132dd60f55266231d24a004da7152a8c5557f` |
| `trees/v4-escaped-names/manifest.ion` | `663b5a2d96da433719c3011af1a76872e31edfecca5a0e07b70359412b38bd9b` |
| `trees/v4-escaped-names/pkg/example/_id/key.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/_id/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v4-escaped-names/pkg/example/con_/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v4-escaped-names/pkg/example/con_/thing.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/aux_.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/com1_.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/con_.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/lpt9_.value.ion` | `41e35430e2a32460c08a6c877b2e348dcfc497de14961179dcfb401541bed9d8` |
| `trees/v4-escaped-names/pkg/example/manifest/manifest.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/manifest.value.ion` | `5e89852468dd8c9b6729e15bdd558331b63aebb0de1c37ce8d254274b28f8070` |
| `trees/v4-escaped-names/pkg/example/manifest/module.ion` | `ea7f80a2223cbdf6efcb538647284797a376a2eb0eaf6c56ec570a29fd870edf` |
| `trees/v4-escaped-names/pkg/example/manifest/module.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/module/item.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/module/module.ion` | `80f3802f6bd8134981575b708e23a8174ae375f0905f4d0b0f68545a0396b6af` |
| `trees/v4-escaped-names/pkg/example/manifest/nul_.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/rate-in-_usd.value.ion` | `0a7e77a6a27e38352b1757e860c1cff12b0f4bc26e62742f47de9388701f7c18` |
| `trees/v4-escaped-names/pkg/example/manifest/user-_id.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-escaped-names/pkg/example/manifest/value-in-_usd.type.ion` | `d1cd62d4de91812e79f877a4f48a95196a1711b8026d808dced2397b0d6fc4a4` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/add.value.ion` | `ec80f92f67861ba1cc35179ee8d1b7dba87b5681983b16729e1b8b1459141875` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/alias.type.ion` | `b0c626025b94f2f79910a8184dcd9d1220c7fa22ff3360c196ab0a5cb9b19d5b` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/custom.type.ion` | `e472a63d780289b424a5e8bcff8f4305446e9a72719c7bd4e2a483f7f849d69a` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/derived.type.ion` | `328b234bfd518d04e191c4cdc52af30214fb2fe89d72c8870f4e940de20e3b32` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/module.ion` | `bee081e31c772be94b6b9cc0309c54ea73e316d2630fc34356ea13498d341efb` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/opaque.type.ion` | `141f72a8045c2380865eb6c12f3f415cf98c92cda6b9ed9c86b9526b42ce5f46` |
| `trees/v4-library-nodes/deps/my-org/shared/@/annotated/zero.value.ion` | `d907d786d37be012384f95b6100f2f008f62c938dcec8cdaa8f3e08d41e602f3` |
| `trees/v4-library-nodes/deps/my-org/shared/@/plain/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-library-nodes/manifest.ion` | `f533d6c552805be9900acc29b3a13f68d65c11eb89407df9ae0acffe54e9b8c6` |
| `trees/v4-library-nodes/pkg/my-org/project/hidden/module.ion` | `44d6ea18c5aa02e03491bb7c7165d51781e18025e1164a4a66f601faa3e0e59d` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/attributes-on-types.type.ion` | `a84c7d5cb55d36b7081c25471e9de6890b6c643bf50ec209e9a16ce3e1f2c7f2` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/attributes-on-values.value.ion` | `48745aaff9d8d6fedb021cfedee979bafdafb6a95389c21cb4cc0e55083c15d3` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/custom-public.type.ion` | `a586ca1e3ec6b72d53421bb78869b3daa9c4b23df442e3611577942ea7263c14` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/custom.type.ion` | `20203f9e8740622d9dcccdc7d4150173bfce77fc6b5d079d58ab019adb6a7f6b` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/document.value.ion` | `2cc0b65acda0adcd76439900678b607867fdd27bee15eb75cd3e4c9ac22a262f` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/external-plain.value.ion` | `56807905ce25bc6c103b5b95b4db1f60598c8c6e36f61f15863c15b5cda78cba` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/external.value.ion` | `eb78bb7ecfc31942f9a6fa29144a53bdae4124a4825aaab8d5cd93b3f641333e` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/holes.value.ion` | `306555d18f8e9ca17e72e9fc901ed7b9a9922932ac309a637dd21b7046b42889` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-deleted.value.ion` | `9005149f65847f8972a3a8a75dfcfbe0f579183d19c72840a4c70f46051bf17c` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-draft-type.type.ion` | `97cbd441a9fb5f9123546c604b034b6e5ac819b3e26fa71507315f83f9b117bf` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-draft.value.ion` | `25c39db1620124c13100b7cef4c8ceb2d0c81ba1b0c68d1015fda139d4e66568` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-hole-type.type.ion` | `0f5c9ddb8bc695bbcb103a04e7fe2fc0676f305ac57d77196c12178b7170adc0` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-mismatch.value.ion` | `980dc058fdeee93c1bdf9aed6c7cc6d5b8e7fa3e71309bd9b773beebe074a6fa` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-partial.value.ion` | `c92dd4575b7ccf4e58fc529fdc9121695f05eb295f2dcb2292506f3d7e825e16` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/incomplete-unresolved.value.ion` | `4eb77ef808fe0796aa18ddf0593e1e9c0f62fc1188f41005939db04e6a644487` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/module.ion` | `55043f86d95389a19d493c0ed5c2fa8d2b82cc9e90c7977304e76d6e63524c7e` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/native-collection.value.ion` | `478371caee0a772f8e4049aa2c4f1553ce9d7cb5ac440fe49242e465347a7935` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/native-compare.value.ion` | `341696c6b8f2fe17b603389c37aab99a741d2955db2c690ea1d4d5ffeec8b16e` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/native-platform.value.ion` | `4315a987944e88ec7c1f3167317623c406fb79ecb982b37ab1101df47e5321b4` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/native-string.value.ion` | `a4e9e2b4cfa2879090df17ffd34d34382d6ce6facd40b45a09bffdc638456a7a` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/native.value.ion` | `7c0a31358637b5dc9c20f5c1e731ea1a737b413173ea8d0e1fb8ffde7467236b` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/numbers.value.ion` | `85682a5cfbc9f7f5234cd1bdca8683f4f1b2600aed3382da63d0942f4790be7f` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/patterns.value.ion` | `8bf007efd551d06a7ee8a8528b07ae90af84786ea2c86423632e9a73ddac1081` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/plain-expressions.value.ion` | `c0bae63af4f334db27e87fa36ad41ab7fd7dce14a88d9213ec62c970fb2a1820` |
| `trees/v4-library-nodes/pkg/my-org/project/nodes/plain-types.type.ion` | `f1cc7bdf00cc55dc340704ba757e4ba5842a81e0a3c04fb579b3efa04cc4c5fe` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/add.value.ion` | `ec80f92f67861ba1cc35179ee8d1b7dba87b5681983b16729e1b8b1459141875` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/alias.type.ion` | `b0c626025b94f2f79910a8184dcd9d1220c7fa22ff3360c196ab0a5cb9b19d5b` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/custom.type.ion` | `e472a63d780289b424a5e8bcff8f4305446e9a72719c7bd4e2a483f7f849d69a` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/derived.type.ion` | `328b234bfd518d04e191c4cdc52af30214fb2fe89d72c8870f4e940de20e3b32` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/module.ion` | `bee081e31c772be94b6b9cc0309c54ea73e316d2630fc34356ea13498d341efb` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/opaque.type.ion` | `141f72a8045c2380865eb6c12f3f415cf98c92cda6b9ed9c86b9526b42ce5f46` |
| `trees/v4-specs/deps/my-org/shared/@/annotated/zero.value.ion` | `d907d786d37be012384f95b6100f2f008f62c938dcec8cdaa8f3e08d41e602f3` |
| `trees/v4-specs/deps/my-org/shared/@/plain/module.ion` | `5f9a8cb0c573e9559b69582d2d00bdf93bdee121809fb5401c9d6450e3f7865a` |
| `trees/v4-specs/manifest.ion` | `11b0c673fc141728b9e4b5eea0fd6f5932d96077eaf3f4c9dc3c22d485ab6050` |
| `trees/v4-specs/pkg/example/main/add.value.ion` | `ec80f92f67861ba1cc35179ee8d1b7dba87b5681983b16729e1b8b1459141875` |
| `trees/v4-specs/pkg/example/main/alias.type.ion` | `b0c626025b94f2f79910a8184dcd9d1220c7fa22ff3360c196ab0a5cb9b19d5b` |
| `trees/v4-specs/pkg/example/main/custom.type.ion` | `e472a63d780289b424a5e8bcff8f4305446e9a72719c7bd4e2a483f7f849d69a` |
| `trees/v4-specs/pkg/example/main/derived.type.ion` | `328b234bfd518d04e191c4cdc52af30214fb2fe89d72c8870f4e940de20e3b32` |
| `trees/v4-specs/pkg/example/main/module.ion` | `bee081e31c772be94b6b9cc0309c54ea73e316d2630fc34356ea13498d341efb` |
| `trees/v4-specs/pkg/example/main/opaque.type.ion` | `141f72a8045c2380865eb6c12f3f415cf98c92cda6b9ed9c86b9526b42ce5f46` |
| `trees/v4-specs/pkg/example/main/zero.value.ion` | `d907d786d37be012384f95b6100f2f008f62c938dcec8cdaa8f3e08d41e602f3` |
| `trees/v4-specs/pkg/example/other/greet.value.ion` | `276f13b0e0b24ab9ecf5dc1ae8a42f9b5cc605c7aecaac813da9edb030b3920c` |
| `trees/v4-specs/pkg/example/other/module.ion` | `1a514cf7a805ab7b149ce62a80097f7a8a8346899280e08efe835a8928e9b671` |

## Ion spec issues

Each issue found in the Morphir Ion spec is discussed with the maintainer, then corrected both here and in finos/morphir.

| Issue | Correction | Upstream |
| --- | --- | --- |
| The draft's `match` row wrote `[pattern body]`, which is not legal Ion | The reader takes `[pattern, body]` | [finos/morphir#1133](https://github.com/finos/morphir/pull/1133) |
| The draft did not name a collapsed record's members, and the knowledge base said the v4 reader refuses a record | Records hold `dependencies` and `modules` lists, for v3 and v4 | [finos/morphir#1133](https://github.com/finos/morphir/pull/1133) |
