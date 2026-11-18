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

The 36 contextual documents below preserve the bytes of [the same shared corpus](https://github.com/finos/morphir/tree/177371fa7a6199b7f177a8c1d886f583ee57e813/spec/mck/bounded-generation/fixtures/acme-orders).

| File | SHA-256 |
| --- | --- |
| `v3.1/contextual-json/deps/morphir/_sdk/@/basics/add.value.json` | `c2cb8924aa3820614f4fde953265512be6fe87aabb9687548f55710109c5bb3b` |
| `v3.1/contextual-json/deps/morphir/_sdk/@/basics/int.type.json` | `7ec7004dcece668d34211e4a90b25365477ee96c4e7973676e2cc72d084bf755` |
| `v3.1/contextual-json/deps/morphir/_sdk/@/basics/module.json` | `9ffd7f011f6cccc2491fa94263ccb5591935a97ec00b92f11da1c3438b85ab38` |
| `v3.1/contextual-json/manifest.json` | `650507f6f808c59f8114453bf9cb2517bd2affe97819b6fded58a538adc3c930` |
| `v3.1/contextual-json/pkg/acme/orders/audit/module.json` | `4c6d31dc53bdd878e2bb57afc43678b91d7428f026aedbf17e9c99cffc963791` |
| `v3.1/contextual-json/pkg/acme/orders/domain/module.json` | `f43986af381b890a652f26c69519afc8293e4ac7daed4b96464628439b909217` |
| `v3.1/contextual-json/pkg/acme/orders/domain/order.type.json` | `94f0fd96a052690745b8d7a118737357189beca015bc2654869d12d6ad58301e` |
| `v3.1/contextual-json/pkg/acme/orders/domain/order.value.json` | `db0d0ee2e2200a252849efa4708e1599f675c6b2c8d05b2088b43c6d4f472d17` |
| `v3.1/contextual-json/pkg/acme/orders/domain/total-in-_usd.value.json` | `1885e3b748bb4c4564ab1d420999fe2847d1292d1c056908c46f287a991d9844` |
| `v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/add.value.yaml` | `7fd60a4ae08775acd1d402c88172438451c98a165aae43a7ccad18fc2000c06d` |
| `v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/int.type.yaml` | `6832cac69b727a1c4c4f7da35cd0b3231f7c57ba23644b653b58c53282843dab` |
| `v3.1/contextual-yaml/deps/morphir/_sdk/@/basics/module.yaml` | `5e470f56c26dc3c09e9ed9ffcc825fc3d8e2a527427973b7f0e556f07d4cb452` |
| `v3.1/contextual-yaml/manifest.yaml` | `87de6576a564b4948779c687eaf47f9b60b4bdf5e47a058cc5bced4bfe33885a` |
| `v3.1/contextual-yaml/pkg/acme/orders/audit/module.yaml` | `b63eb8e4d645d5ff662e5bfcc5468a462b2b24532c8f8d1a6ab66dadfe659e8a` |
| `v3.1/contextual-yaml/pkg/acme/orders/domain/module.yaml` | `3dd4e97f73113c73b603b2b3cca1dc5372a8701474afe7a4437f8a771cb74880` |
| `v3.1/contextual-yaml/pkg/acme/orders/domain/order.type.yaml` | `4ad4cce7243213474ce72750c6ff561495a30e3f51db03cfffea1412a3fb0522` |
| `v3.1/contextual-yaml/pkg/acme/orders/domain/order.value.yaml` | `35dd385bbffb207ffc253152f2b18e2d1b25841b0ac53e144de30df9467e8306` |
| `v3.1/contextual-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml` | `b4b537a6c29557f4c768bd34eaa2f9aff5e03823b71db9d3f34898f9338dcec1` |
| `v4/contextual-json/deps/morphir/_sdk/@/basics/add.value.json` | `250b426de0516e1a1b35167565657272add6ed4f773ab65e0fd9163aac04581e` |
| `v4/contextual-json/deps/morphir/_sdk/@/basics/int.type.json` | `b1a8e7b33039224bb85ba5bbbbf0cbfa9a17213c46b90e47b5c6b098b23aff43` |
| `v4/contextual-json/deps/morphir/_sdk/@/basics/module.json` | `0a4cd495c97cd3ed46dc4e5f0169dc76620f82978ea4a6366402ebf45a63ae53` |
| `v4/contextual-json/manifest.json` | `dc765f931bd12dad3dbb9a7367ff907c8122e1c485758b6dcd4f43037224fd79` |
| `v4/contextual-json/pkg/acme/orders/audit/module.json` | `436d562855210a0a6946a16b5ac1ef405036c7604e32b72a5e5044207cf1d112` |
| `v4/contextual-json/pkg/acme/orders/domain/module.json` | `11ebadc15ca3b7d932c07eefbca3eef6f5ea2d116ff1d091c0102c2a9c49f3aa` |
| `v4/contextual-json/pkg/acme/orders/domain/order.type.json` | `81f7e0d6fcbc524504c305caca4970596b2a8d7f4c82d1e03636600f59d85213` |
| `v4/contextual-json/pkg/acme/orders/domain/order.value.json` | `dbc695403c12e5d2af6a8ab7d5ae5d8e16b8ba333ff4b60c5499620825bc1229` |
| `v4/contextual-json/pkg/acme/orders/domain/total-in-_usd.value.json` | `6d668cc8651bcb2b90906286d09377dce9e5dbc1f564c1a3200b0f3f8a44435a` |
| `v4/contextual-yaml/deps/morphir/_sdk/@/basics/add.value.yaml` | `d38845a4cc251ef3d0116c456987d8f2da9084ec3c0529bf412078f8541c5531` |
| `v4/contextual-yaml/deps/morphir/_sdk/@/basics/int.type.yaml` | `5ac936d834f5d1c860e6e77b756344571c3d1615d505bf147ad39ab66f07197d` |
| `v4/contextual-yaml/deps/morphir/_sdk/@/basics/module.yaml` | `ea51ad7e89eddd43cef86039bca32aabc18289096d1c5e3fc9e7006e6597c8f2` |
| `v4/contextual-yaml/manifest.yaml` | `07e3c9905604d32d5ee81d1454b40265bd3e747018d97c68c07e66db52e0c5a2` |
| `v4/contextual-yaml/pkg/acme/orders/audit/module.yaml` | `c3e4b5cff4f47396e27e4b9bb9f171c0aa3304cfa814d56db84b6b4c5dafc1fd` |
| `v4/contextual-yaml/pkg/acme/orders/domain/module.yaml` | `facf19da57f2b184ee4df0744e129f49c556521117e6e90ffb65318f9b385edb` |
| `v4/contextual-yaml/pkg/acme/orders/domain/order.type.yaml` | `ae93f6990dfcdf4f0de6bda7ec73c02abbe470ed469f837546f7b9fa34d1768a` |
| `v4/contextual-yaml/pkg/acme/orders/domain/order.value.yaml` | `d937a64df6112da43a39344f802ed19cf354ae06c9677bc66b69a701fc53ff13` |
| `v4/contextual-yaml/pkg/acme/orders/domain/total-in-_usd.value.yaml` | `bb8b6ae644f5484caed867c55cf81f666c550b5589168396471b3dbb1877ed2d` |
