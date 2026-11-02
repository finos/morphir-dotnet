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

Copied unchanged from [finos/morphir](https://github.com/finos/morphir) at commit `679199153dcab3615bad4e2afa064b435bb19ec9`. `Json/V4/V4KitTests.fs` in `Morphir.Models.Tests` checks every file against this table.

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

## Ion spec issues

Each issue found in the Morphir Ion spec is discussed with the maintainer, then corrected both here and in finos/morphir.

| Issue | Correction | Upstream |
| --- | --- | --- |
| The draft's `match` row wrote `[pattern body]`, which is not legal Ion | The reader takes `[pattern, body]` | [finos/morphir#1133](https://github.com/finos/morphir/pull/1133) |
| The draft did not name a collapsed record's members, and the knowledge base said the v4 reader refuses a record | Records hold `dependencies` and `modules` lists, for v3 and v4 | [finos/morphir#1133](https://github.com/finos/morphir/pull/1133) |
