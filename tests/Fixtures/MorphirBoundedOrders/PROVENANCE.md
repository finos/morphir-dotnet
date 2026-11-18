# Bounded orders trees

The 36 documents are the plain JSON and YAML v3.1 and v4 trees in the frozen `acme/orders` example. The originals and independent expected facts are in [the Morphir corpus](https://github.com/finos/morphir/tree/3f3157318af5fafe474a4e1b83e7e34812517f2f/spec/mck/bounded-generation/fixtures/acme-orders). The files preserve the parent bytes.

The shared fixture catalog owns these files. Bounded reader tests open the embedded content by logical tree location.

| File | SHA-256 |
| --- | --- |
| `v3.1/plain-json/deps/morphir/_sdk/@/basics/add.value.json` | `34286f5a937e100e8d16e65d900abba4c2604336a8958357b57693fb5188092d` |
| `v3.1/plain-json/deps/morphir/_sdk/@/basics/int.type.json` | `200803d530066ef7d7b1412c6479382ce7aec12e237669faa212948dfe13ed21` |
| `v3.1/plain-json/deps/morphir/_sdk/@/basics/module.json` | `a05f890f089a810f0da35ef220ee3e7e09d3dcbd1dd34439f217dff46a380fe6` |
| `v3.1/plain-json/manifest.json` | `25138745c6c761304fe7d83e8f3c45cb1cb997f9c4f0da07f89a872cf712fe8a` |
| `v3.1/plain-json/pkg/acme/orders/audit/module.json` | `b2f8491eb86ffa936596bea3ce70ef1ae4cbda315076bd08196c5a6f247b963b` |
| `v3.1/plain-json/pkg/acme/orders/domain/module.json` | `46e88978c783a5743b4be092e10193942a1d251d27e8ca048f1058adedecd626` |
| `v3.1/plain-json/pkg/acme/orders/domain/order.type.json` | `b09c5e72c5b1f353cc334c6059ebd031eedb85790cc536fff86533c749882c6a` |
| `v3.1/plain-json/pkg/acme/orders/domain/order.value.json` | `1736ca10d492fbdf53e4f88974fe54efbe91607b50a1668769eade755ed5416f` |
| `v3.1/plain-json/pkg/acme/orders/domain/total-in-_usd.value.json` | `a80f1398ad347b12b03a4f2a96d17c365d9a980014a8fc876a743709dd205bae` |
| `v3.1/plain-yaml/deps/morphir/_sdk/@/basics/add.value.yaml` | `d01bd4797f3b2e77e1e1e884ea5a58429b301d573438cbfdf57ec937006e5b02` |
| `v3.1/plain-yaml/deps/morphir/_sdk/@/basics/int.type.yaml` | `13bb51879327ea6dd9504b120858034184358b542951585a7df97585946c4f36` |
| `v3.1/plain-yaml/deps/morphir/_sdk/@/basics/module.yaml` | `d4ae72b6abe51f4e0696200cc2a3889b2dbcc14398b66d93d465f94534f6266d` |
| `v3.1/plain-yaml/manifest.yaml` | `a4368e1e8fdd8e2291e4cfa3d697a0929419b85ffd2a9ef39f62a924e7c13e9f` |
| `v3.1/plain-yaml/pkg/acme/orders/audit/module.yaml` | `8c3a729de403bc3e0eb226e9bee8acef0cfa8259534ca0bc06765c22e58a4934` |
| `v3.1/plain-yaml/pkg/acme/orders/domain/module.yaml` | `0b7e6e7425788c7733766ebfa689800984124b6012cdb2901e95b251d50a136c` |
| `v3.1/plain-yaml/pkg/acme/orders/domain/order.type.yaml` | `5fbd79e69a49489bf6ad1c67f9275e64c51d54db98df0299615e742c9c5fec97` |
| `v3.1/plain-yaml/pkg/acme/orders/domain/order.value.yaml` | `e771ff98beb16ce07198a8b2b27aa2108c3da593e79ff798af9ed401aa875f46` |
| `v3.1/plain-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml` | `452bf177ca7bf1ad2d40c24221ed7d69d6d05e523d01f4a0d2dc3ae44d3e2189` |
| `v4/plain-json/deps/morphir/_sdk/@/basics/add.value.json` | `d0a05067e8f35760070a362cc7ac9576af334237d7b63358e6649ad8146a4581` |
| `v4/plain-json/deps/morphir/_sdk/@/basics/int.type.json` | `f705c58f4063de276c2d5b2b90519c7b90f77df8ea1102644f9109854924c823` |
| `v4/plain-json/deps/morphir/_sdk/@/basics/module.json` | `f560f925c078f6c2dfe8cfd9d63492a7a6de56a27ce193932a7c8ec3ae0a777f` |
| `v4/plain-json/manifest.json` | `16733854b16b962c16cbfe28e0b62134092ec268e6e2c21fda6452e3ba71becf` |
| `v4/plain-json/pkg/acme/orders/audit/module.json` | `d9b482373b92d4d6f26467e4f98efcbf03f90abc0a0e3e367d5a281bdad59be6` |
| `v4/plain-json/pkg/acme/orders/domain/module.json` | `f20321dd16d4e5f0f3c0b1a3c438028e7c52c8ad50070a1d1ad127e0cb5171a9` |
| `v4/plain-json/pkg/acme/orders/domain/order.type.json` | `69b508ca018feb78f371e13bab6124e70e3c00337c2b7058745b44aad7cd56f4` |
| `v4/plain-json/pkg/acme/orders/domain/order.value.json` | `23b1c85f2ee470a468e335098619d394fd23d7a70ee592d39a28612dc5e98fa0` |
| `v4/plain-json/pkg/acme/orders/domain/total-in-_usd.value.json` | `f4e5dcae87eb9b022bf446711b4173cd04839da7cf4e2f122ca1600143acf9d4` |
| `v4/plain-yaml/deps/morphir/_sdk/@/basics/add.value.yaml` | `8e3ff55a36b35261aa3cbd8dbba172f54bd4dc4104a713f4108b863fe2285e0c` |
| `v4/plain-yaml/deps/morphir/_sdk/@/basics/int.type.yaml` | `db89fb411de58f801304f8c20678534d48d970550aa0296f3691aa908210c59e` |
| `v4/plain-yaml/deps/morphir/_sdk/@/basics/module.yaml` | `a104becbf555457691c83508bc9eb1a1661b0a7b4d61786507fef0f3128dd937` |
| `v4/plain-yaml/manifest.yaml` | `f566e3642bb3ea4d6eb62ab2f245063b9bb9e6d2782824ad7f34b98604021914` |
| `v4/plain-yaml/pkg/acme/orders/audit/module.yaml` | `cf826379493b5397249f7683fe0d287af013f87f727264ea16fdcd28127eaeb1` |
| `v4/plain-yaml/pkg/acme/orders/domain/module.yaml` | `415e5db36f30bda0186205f1851e48d5c2cde9db738e9c26c3c6e3591c2a3db7` |
| `v4/plain-yaml/pkg/acme/orders/domain/order.type.yaml` | `557e0df61d43341e0163cbc1588f31737ae9e3a71d6a46b56468ba8da0a48b45` |
| `v4/plain-yaml/pkg/acme/orders/domain/order.value.yaml` | `b9f627924c280a8d4bd064139db38b782891f7564f928b87ab3398e9923c5c78` |
| `v4/plain-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml` | `e5fe89e9adb3211f987583804132d55eaf30b33a8e474fc128db5face8cb720c` |
