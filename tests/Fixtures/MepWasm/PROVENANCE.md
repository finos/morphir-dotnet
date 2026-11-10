# Pinned upstream inputs

Repository: https://github.com/finos/morphir

Commit: `48d10706f1feea2131f76ef5deca9017efc0df97`

Branch: `t3/design-file-based-layout`, the branch of the draft pull request
[finos/morphir#1148](https://github.com/finos/morphir/pull/1148). The commit is on
`origin/t3/design-file-based-layout`; it is not yet on `main`.

Pinned: 9 November 2026 at `7b049c223`, for task 0.2 of the OpenSpec change
`add-wasm-extension-host` (design PIN-1). Moved: 10 November 2026, to
`48d10706f`, which applies the .NET host's findings to the profile contract
(`c0c1096b3`) and makes the memory-growth modules reach the ceiling in about a
dozen growths.

The upstream files below are copied without edits from that one commit: the WASM
execution profile contract, the crafted WASM host qualification corpus, every
module the corpus names (the `.wasm` binary and the `.wat` source it is assembled
from) and the licence and notice of finos/morphir. These are reviewed contract
inputs. No host implementation, test or generator in this repository writes them,
and no expectation is recorded from a host run.

`Morphir.Extensions.Wasm.Tests` (`FixtureProvenanceTests`) recomputes every
SHA-256 in the table, fails on a file in this directory that has no row, and checks
that each module's SHA-256 equals the `sha256` the corpus records for it.
`.gitattributes` marks `tests/Fixtures/MepWasm/**` as `-text`, so a checkout never
changes the line endings of a pinned file.

## Layout

The corpus names each module by a path relative to the corpus file
(`fixtures/wasm/<name>.wasm` and `.wat`). Those paths keep their upstream form
here, so they resolve from `wasm-host.cases.json` without a mapping. The profile
contract and the corpus move up from `spec/mep/` and `spec/mep/mck/` to this
directory. The corpus's `contract.path` member (`spec/mep/wasm-execution-profiles.json`)
is relative to the root of finos/morphir; here that file is
`wasm-execution-profiles.json`, beside the corpus.

At this commit the corpus holds 58 cases and 50 modules. Every module in
`spec/mep/mck/fixtures/wasm/` is named by the corpus and every module the corpus
names is present. The corpus references no other file by path: its frames and
matcher conventions are inline. The kit's checker,
`tools/check-wasm-host-kit.ts`, and the generic guest kit are not pinned here.

## Reference, not copied

The worker-mode tasks (4.1 and 4.3) follow the MoonBit worker's README:

- Repository: https://github.com/finos/morphir-moonbit
- Commit: `89f892468649bc6fef6acc40e5bfb2e9800c16da` (`origin/main` on 9 November 2026)
- Path: `pkgs/morphir-host/worker/wasm/README.mbt.md`

## Moving the pin

All files move together, to one newer finos/morphir commit, in one change: copy
every file below again from that commit, recompute every SHA-256, update the
commit, branch, date and this table, and update the case and module counts in
`FixtureProvenanceTests`. Never edit a pinned file, and never take files from two
commits. Where the parent specs and the pinned contract JSON differ, the parent
specs win (design PIN-1): report the difference upstream and move the pin when the
parent is corrected, rather than changing a file here.

## Files

| Local file | Upstream path | SHA-256 |
| --- | --- | --- |
| `LICENSE` | `LICENSE` | `b40930bbcf80744c86c46a12bc9da056641d722716c378f5659b9e555ef833e1` |
| `NOTICE` | `NOTICE` | `02a20174dd6ff4d866cad7d04ce8d61503306f3266f3c03261418d5011805889` |
| `wasm-execution-profiles.md` | `spec/mep/wasm-execution-profiles.md` | `b2f84f668aafaea0503ea42fd409409329c139e6d02e7f60945c05a3f7170567` |
| `wasm-execution-profiles.json` | `spec/mep/wasm-execution-profiles.json` | `f5451395c6f6e729ff3ed93bc0636a452bc43bfef19b22f676f072fe24b7f9b5` |
| `wasm-host.cases.json` | `spec/mep/mck/wasm-host.cases.json` | `6f1196b37dcf484780f2adaf276fa8f833f8388d8a9dd861c892e2ccf493c120` |
| `fixtures/wasm/bytes-echo.wasm` | `spec/mep/mck/fixtures/wasm/bytes-echo.wasm` | `49a87314af3cb02860dc6c4bc3e84dc3b4ec1330609f0d4fad88de7491afaa76` |
| `fixtures/wasm/bytes-echo.wat` | `spec/mep/mck/fixtures/wasm/bytes-echo.wat` | `84995b32cca3bc83bccbf9b795d82db61142894d72be7bec12bf1909d4d1d8e0` |
| `fixtures/wasm/bytes-exit.wasm` | `spec/mep/mck/fixtures/wasm/bytes-exit.wasm` | `49c946c1f69ebfe6b4440acf7b9390c8d949199e7d0a43fd35f41b4c89e93f47` |
| `fixtures/wasm/bytes-exit.wat` | `spec/mep/mck/fixtures/wasm/bytes-exit.wat` | `f32d66cd117fe07512ca81acbe0531056848cf2de274907bd410e4a86b94f422` |
| `fixtures/wasm/bytes-grow-then-respond.wasm` | `spec/mep/mck/fixtures/wasm/bytes-grow-then-respond.wasm` | `04d4a0c459466316f54231d392b9397cd067410310a40516b16dd41295d50621` |
| `fixtures/wasm/bytes-grow-then-respond.wat` | `spec/mep/mck/fixtures/wasm/bytes-grow-then-respond.wat` | `063c648547e17631539005f3b3c11fc1f773a44ec320998462d584482e16b39a` |
| `fixtures/wasm/bytes-imports-wasi.wasm` | `spec/mep/mck/fixtures/wasm/bytes-imports-wasi.wasm` | `7f4a2dd3b06dfaa926fa0cb9dbfcc9c125b9268dc7410d237a9703b8102a5c29` |
| `fixtures/wasm/bytes-imports-wasi.wat` | `spec/mep/mck/fixtures/wasm/bytes-imports-wasi.wat` | `1d7cf75701f0e0235c644930fc0c98d587c4ba883bd9204ee5e46cf960dd2a09` |
| `fixtures/wasm/bytes-input-byte-i64.wasm` | `spec/mep/mck/fixtures/wasm/bytes-input-byte-i64.wasm` | `6ea50a7f4c76d4dbf3eab91880b4dbae1c763bfea29136d10138a5802f77026b` |
| `fixtures/wasm/bytes-input-byte-i64.wat` | `spec/mep/mck/fixtures/wasm/bytes-input-byte-i64.wat` | `a7023327c970a3255df08ed48ffefa0abfc0c199fb38f3a765af08b6bb56bc51` |
| `fixtures/wasm/bytes-minus-one.wasm` | `spec/mep/mck/fixtures/wasm/bytes-minus-one.wasm` | `4a241f77c90fbd7c0925bed84592eb7a53756a18d73cdf0a1fe0661d7d779921` |
| `fixtures/wasm/bytes-minus-one.wat` | `spec/mep/mck/fixtures/wasm/bytes-minus-one.wat` | `e6d04bfba60e1d3ef182c73e62efb37f22b974f63c51309811f4e28b39c2efa3` |
| `fixtures/wasm/bytes-minus-two.wasm` | `spec/mep/mck/fixtures/wasm/bytes-minus-two.wasm` | `709f65979c52897dba2420fdb37b8d9b9f5cf4705bbb900cdcabd24c606cd453` |
| `fixtures/wasm/bytes-minus-two.wat` | `spec/mep/mck/fixtures/wasm/bytes-minus-two.wat` | `28fc539ec3a2cc5f59f7ee292cd2e413072141f458d340b1cf7ae5acc98f5ae3` |
| `fixtures/wasm/bytes-not-utf8.wasm` | `spec/mep/mck/fixtures/wasm/bytes-not-utf8.wasm` | `1de385e7d1a9fb08c9a455f1617d9815b39c642de18323c332e763dc9e8f38e9` |
| `fixtures/wasm/bytes-not-utf8.wat` | `spec/mep/mck/fixtures/wasm/bytes-not-utf8.wat` | `a12fcedf9a43ce5233cf9c4e886569aa0c0c614886f950a1f703aad83fb1c61f` |
| `fixtures/wasm/bytes-spin.wasm` | `spec/mep/mck/fixtures/wasm/bytes-spin.wasm` | `69768991322b8dc01561b9d8c7952cf9c8bcfe3e30ca7239513919088e91e5cc` |
| `fixtures/wasm/bytes-spin.wat` | `spec/mep/mck/fixtures/wasm/bytes-spin.wat` | `3073b3fe45ca7336955a4d04895d4d5d357381fdf973f65d591bca5b2eb58579` |
| `fixtures/wasm/env-open-socket.wasm` | `spec/mep/mck/fixtures/wasm/env-open-socket.wasm` | `bebbfeda31c2703c2022bf4ba27206fc0fcf22a540dda195476569c973e7addd` |
| `fixtures/wasm/env-open-socket.wat` | `spec/mep/mck/fixtures/wasm/env-open-socket.wat` | `602bf93397c385527b64bbd22321434c6bd1986cf09a2f5b2ed537cab68edc67` |
| `fixtures/wasm/extism-clock-random.wasm` | `spec/mep/mck/fixtures/wasm/extism-clock-random.wasm` | `ea62e7ee25cd80b7de535aaa7a7bf78e925ee549801d769dec552318cd05acaa` |
| `fixtures/wasm/extism-clock-random.wat` | `spec/mep/mck/fixtures/wasm/extism-clock-random.wat` | `5cb5d6ca73f4dd192e0a4daebf4808518c0ec84183647c2dacfb6375ba4556bc` |
| `fixtures/wasm/extism-config.wasm` | `spec/mep/mck/fixtures/wasm/extism-config.wasm` | `a6617957fc1ad006e8bb7d02568d53cef86780755e8acdc4d05ba9dc2d821252` |
| `fixtures/wasm/extism-config.wat` | `spec/mep/mck/fixtures/wasm/extism-config.wat` | `342fed49cde7b088e1660780bae4739e6bb81c6ffb07c8bbdbdb579420154682` |
| `fixtures/wasm/extism-count-calls.wasm` | `spec/mep/mck/fixtures/wasm/extism-count-calls.wasm` | `bc61a4014f59112351563703a06ef5f370df4b5f2a259d4d679e0f8f45aba539` |
| `fixtures/wasm/extism-count-calls.wat` | `spec/mep/mck/fixtures/wasm/extism-count-calls.wat` | `a73738d114883ea55a423b36ad1d7e3f20a4636e85c9348277032e25d121d2b7` |
| `fixtures/wasm/extism-echo.wasm` | `spec/mep/mck/fixtures/wasm/extism-echo.wasm` | `c4cc77b47d1d3251713a12bcece9937afab58c73b29967c01f17d4102b877acc` |
| `fixtures/wasm/extism-echo.wat` | `spec/mep/mck/fixtures/wasm/extism-echo.wat` | `33a754798fc622d63cefd72bc60905ffed5626fbaf641c55a3d9067569c56b4f` |
| `fixtures/wasm/extism-error.wasm` | `spec/mep/mck/fixtures/wasm/extism-error.wasm` | `b384508b1553cb3357109f5251c333a1abecc866b0cfff07d85dbcf242b47b48` |
| `fixtures/wasm/extism-error.wat` | `spec/mep/mck/fixtures/wasm/extism-error.wat` | `a9d138cd19533eff4f6dc2472b0c3df863d9f5b4459b7287551849e084f578ef` |
| `fixtures/wasm/extism-fd-write.wasm` | `spec/mep/mck/fixtures/wasm/extism-fd-write.wasm` | `cef05b0a5472c30c44712fc2a1227649f14cd8b9e42e728e568173e144c09064` |
| `fixtures/wasm/extism-fd-write.wat` | `spec/mep/mck/fixtures/wasm/extism-fd-write.wat` | `88d9aca91216ccb57e0290e051ff1facb033eb4cd322604deda434cdf4b0ae26` |
| `fixtures/wasm/extism-full-surface.wasm` | `spec/mep/mck/fixtures/wasm/extism-full-surface.wasm` | `58555a17ca0c3e501ecb32c9c45152d5165d2e470d3514893f40e9e983c604ac` |
| `fixtures/wasm/extism-full-surface.wat` | `spec/mep/mck/fixtures/wasm/extism-full-surface.wat` | `3d7d27b7b91e4dba62cfe152a6b0d07da523d73bf8362b36997c6b362bf3fd50` |
| `fixtures/wasm/extism-grow-below-ceiling.wasm` | `spec/mep/mck/fixtures/wasm/extism-grow-below-ceiling.wasm` | `db6338d3fa18fc323540beab4080bb08f78b0f16b7c2c2952a84ff73946c318c` |
| `fixtures/wasm/extism-grow-below-ceiling.wat` | `spec/mep/mck/fixtures/wasm/extism-grow-below-ceiling.wat` | `790468fd7f283958494fb5739cdcfde40ab5d6ae143c9b1f8f00e8f168276ac9` |
| `fixtures/wasm/extism-grow-then-respond.wasm` | `spec/mep/mck/fixtures/wasm/extism-grow-then-respond.wasm` | `e3826d6d858eca51a0ccf78ed9e4ae14786b2d1530ee51427cb7db4614951e0c` |
| `fixtures/wasm/extism-grow-then-respond.wat` | `spec/mep/mck/fixtures/wasm/extism-grow-then-respond.wat` | `1acb1a62f499a83fc4da78d1fb5413ed50c00880106a3d24a2fcd0fa19a5e898` |
| `fixtures/wasm/extism-grow-then-trap.wasm` | `spec/mep/mck/fixtures/wasm/extism-grow-then-trap.wasm` | `17243e24c0ac33d48c901b4a7e399cc073e2c1250c46830d9cf410d20f0d4083` |
| `fixtures/wasm/extism-grow-then-trap.wat` | `spec/mep/mck/fixtures/wasm/extism-grow-then-trap.wat` | `08192453f5463ec35afcb7ee39265399dd2afade81c9b1d1d50ccc57c79d5e22` |
| `fixtures/wasm/extism-handle-i64.wasm` | `spec/mep/mck/fixtures/wasm/extism-handle-i64.wasm` | `9f39cf8ea1e70ac53bef96ea9be509989847e02bc99c7dbefaa4920c51ca581c` |
| `fixtures/wasm/extism-handle-i64.wat` | `spec/mep/mck/fixtures/wasm/extism-handle-i64.wat` | `d270144baef44643dd982d76fcdb5b0029c4a29314e46ee319c5833033297f7b` |
| `fixtures/wasm/extism-http-status.wasm` | `spec/mep/mck/fixtures/wasm/extism-http-status.wasm` | `f37686d570a6992f3d59f9724407139ed3992aca1ff2a44b3e537089d9aef9ce` |
| `fixtures/wasm/extism-http-status.wat` | `spec/mep/mck/fixtures/wasm/extism-http-status.wat` | `92a4a35a48ac2300709e19b399b8f29f8718cc96876838d2b6b83cbcf5e6e9f4` |
| `fixtures/wasm/extism-http.wasm` | `spec/mep/mck/fixtures/wasm/extism-http.wasm` | `a00b6b70ca7c9c2cd35d7198c081c3342b758905ef3418b7b880e95238a27507` |
| `fixtures/wasm/extism-http.wat` | `spec/mep/mck/fixtures/wasm/extism-http.wat` | `572a73dc26d935e8151b3348f2c3d07bc42a205219e83474e9fb6a5880683e4c` |
| `fixtures/wasm/extism-ir-cache-over-bytes.wasm` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-over-bytes.wasm` | `378d73679535f114fb99c280e100c2f45a0f1456294dc8a16d837181b76cdf44` |
| `fixtures/wasm/extism-ir-cache-over-bytes.wat` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-over-bytes.wat` | `90d18ba20aab5026ea3174084edd8de5d0b3276efd92c6c6b186dbed35ac921b` |
| `fixtures/wasm/extism-ir-cache-over-entries.wasm` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-over-entries.wasm` | `d471becd94d0a4d42a85f635161ba727a8736c82fc94a7f6931ad255419905ea` |
| `fixtures/wasm/extism-ir-cache-over-entries.wat` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-over-entries.wat` | `eea52b83d659f99048e1673770990ab0e9f7cf5459a475a7980a3e2e49836ff0` |
| `fixtures/wasm/extism-ir-cache-round-trip.wasm` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-round-trip.wasm` | `b49f74bd5ad94ff7fc54c937755b966a453dfd25e3740f6df73648de92be425c` |
| `fixtures/wasm/extism-ir-cache-round-trip.wat` | `spec/mep/mck/fixtures/wasm/extism-ir-cache-round-trip.wat` | `06693f495abec1360609937ace9f6f9f216b653d9ba9aff0fadfae273d68a9aa` |
| `fixtures/wasm/extism-log.wasm` | `spec/mep/mck/fixtures/wasm/extism-log.wasm` | `741df2ecb99b09bc9c14a1531243a9b1db217ba4897adc8c205319c4b5091247` |
| `fixtures/wasm/extism-log.wat` | `spec/mep/mck/fixtures/wasm/extism-log.wat` | `5571b696b3a4d16a12b9b9992655502d6d9c20992c857478c6fbaf0641e588cc` |
| `fixtures/wasm/extism-no-handle.wasm` | `spec/mep/mck/fixtures/wasm/extism-no-handle.wasm` | `bd519f47cce30eee592290302651510f37a2952ecd724d12785f1d3d6c9eb8f0` |
| `fixtures/wasm/extism-no-handle.wat` | `spec/mep/mck/fixtures/wasm/extism-no-handle.wat` | `10e19c2e6e78ba45b14d27fda48b5d60e462f96f3ff175dc8665601e9b42f938` |
| `fixtures/wasm/extism-no-output.wasm` | `spec/mep/mck/fixtures/wasm/extism-no-output.wasm` | `18554781406e6ae0832565f77ef4834823500eb3c80a7b4f0f3b2f3aa60e4e32` |
| `fixtures/wasm/extism-no-output.wat` | `spec/mep/mck/fixtures/wasm/extism-no-output.wat` | `94e54325e65c66b98550226d07794f9bcf622cb4b7d563d0de0af10b42833156` |
| `fixtures/wasm/extism-nonzero.wasm` | `spec/mep/mck/fixtures/wasm/extism-nonzero.wasm` | `cecf8a5216e495e865f4e8203d9883e73d0f71547138a67a149f1da638aec861` |
| `fixtures/wasm/extism-nonzero.wat` | `spec/mep/mck/fixtures/wasm/extism-nonzero.wat` | `c1925e7880b62f483512e6a645274827f3375feb2d34cc5e175a3e1ba0d34e94` |
| `fixtures/wasm/extism-not-object.wasm` | `spec/mep/mck/fixtures/wasm/extism-not-object.wasm` | `ca4fccff6e89decc0ad4d465a09b949cffdf3bb47f592ae137939d5838c02880` |
| `fixtures/wasm/extism-not-object.wat` | `spec/mep/mck/fixtures/wasm/extism-not-object.wat` | `4f028459c09f20cec713183abf0aca4f6d31c7bac85d477618a57e2285a19529` |
| `fixtures/wasm/extism-notify-accept.wasm` | `spec/mep/mck/fixtures/wasm/extism-notify-accept.wasm` | `2fbe22665b6eab79245310e984287891305b372252995928a6a354c59b547826` |
| `fixtures/wasm/extism-notify-accept.wat` | `spec/mep/mck/fixtures/wasm/extism-notify-accept.wat` | `9ff1d7b0e8fc857fddeea72b21e20476fd20a15c0e61e0a578d90b0987761522` |
| `fixtures/wasm/extism-notify-answer.wasm` | `spec/mep/mck/fixtures/wasm/extism-notify-answer.wasm` | `6c343f491df44d43516b2711da486f77f164cb987209220ef1d7fbc7309f14da` |
| `fixtures/wasm/extism-notify-answer.wat` | `spec/mep/mck/fixtures/wasm/extism-notify-answer.wat` | `0f34c4534ff3a1f6a582af6bec1cb1203d99de2a023a43d6693084579015ba6c` |
| `fixtures/wasm/extism-notify-unlisted.wasm` | `spec/mep/mck/fixtures/wasm/extism-notify-unlisted.wasm` | `7dfce08dcf370b9550dc36e03d03ce30f4fe666f76a82261e9abcf95fd9f5414` |
| `fixtures/wasm/extism-notify-unlisted.wat` | `spec/mep/mck/fixtures/wasm/extism-notify-unlisted.wat` | `ad97e0e61b83f6f0ad51bf17a84ec98939614a9f3053470152b50cccb46563a8` |
| `fixtures/wasm/extism-path-open.wasm` | `spec/mep/mck/fixtures/wasm/extism-path-open.wasm` | `c46b195beba466bbe4eea6b0d179dcaaf4ae979a811a50f587bad568cbe6fe59` |
| `fixtures/wasm/extism-path-open.wat` | `spec/mep/mck/fixtures/wasm/extism-path-open.wat` | `88b5802a2d8462dffce2f233c60c1b1c282e00a9b50b8b09ddd30bf701c9e507` |
| `fixtures/wasm/extism-proc-exit.wasm` | `spec/mep/mck/fixtures/wasm/extism-proc-exit.wasm` | `235f6ee9511fcb6d1ed294ed1d3606f40f09fb004da11887816f3081fdf9e578` |
| `fixtures/wasm/extism-proc-exit.wat` | `spec/mep/mck/fixtures/wasm/extism-proc-exit.wat` | `390e2531e613cdcae52476bb7b97dbbd98efeac79ddefd24e2aad6c05d7109ed` |
| `fixtures/wasm/extism-spin.wasm` | `spec/mep/mck/fixtures/wasm/extism-spin.wasm` | `013df84b7cdecce0386f1b20b6395d663df1533c01fcac16141019ce87d3e11e` |
| `fixtures/wasm/extism-spin.wat` | `spec/mep/mck/fixtures/wasm/extism-spin.wat` | `7c6088c55d78939ab9e5e13083664f34745070875e9f8780037f22ff1bc8d412` |
| `fixtures/wasm/extism-trap.wasm` | `spec/mep/mck/fixtures/wasm/extism-trap.wasm` | `bfc9709481aa4d8a5ae6109c5a7ecb0c61eac230384b9971c3524976155ecf6b` |
| `fixtures/wasm/extism-trap.wat` | `spec/mep/mck/fixtures/wasm/extism-trap.wat` | `7651e00d7cf198170ce7bd0ea51c1a19e5efe71aee2018683bbe6f0385e73ef1` |
| `fixtures/wasm/extism-var-over-limit.wasm` | `spec/mep/mck/fixtures/wasm/extism-var-over-limit.wasm` | `c2c068fe57cd814fba0bdf7285df61fcb69647d2c3ec8d896b0909e1cf52d8c3` |
| `fixtures/wasm/extism-var-over-limit.wat` | `spec/mep/mck/fixtures/wasm/extism-var-over-limit.wat` | `128fec93cfe5d81f638b27c538efbe78dd2f77ccc126b727c1524dff5e00da40` |
| `fixtures/wasm/extism-var-round-trip.wasm` | `spec/mep/mck/fixtures/wasm/extism-var-round-trip.wasm` | `cd0620e38887459fc4fb83da6b48f6103ab18d2e7d5c75f67d942161ef6be270` |
| `fixtures/wasm/extism-var-round-trip.wat` | `spec/mep/mck/fixtures/wasm/extism-var-round-trip.wat` | `d06b5b22f97c372a62d852a8cd93e35c07e02943240702d720d4f7b91f2cdc1a` |
| `fixtures/wasm/extism-wasi-empty.wasm` | `spec/mep/mck/fixtures/wasm/extism-wasi-empty.wasm` | `58c597ffe4e359ccba4dfbcf2c75dd1f5d4cf48a1965a339915d1714054deeae` |
| `fixtures/wasm/extism-wasi-empty.wat` | `spec/mep/mck/fixtures/wasm/extism-wasi-empty.wat` | `6415c7dda9fbd06b25bcbe02f3011024b9db2d8f06b5cdb06dd5e018ab3da10b` |
| `fixtures/wasm/extism-wasi-unsupported.wasm` | `spec/mep/mck/fixtures/wasm/extism-wasi-unsupported.wasm` | `365828e58cfb67c9720d8f64208326c3f5a3948f531e12053f01047ef9597b7c` |
| `fixtures/wasm/extism-wasi-unsupported.wat` | `spec/mep/mck/fixtures/wasm/extism-wasi-unsupported.wat` | `fa4e79c3113c85214d315c7f6d63663453544282eb7764db3af998d5d2e4ae82` |
| `fixtures/wasm/extism-workspace-info-i64.wasm` | `spec/mep/mck/fixtures/wasm/extism-workspace-info-i64.wasm` | `8819b2f55761a0dcc22f9eb93c6a7deeba50eabd1dfc0932b62f1c01faab6434` |
| `fixtures/wasm/extism-workspace-info-i64.wat` | `spec/mep/mck/fixtures/wasm/extism-workspace-info-i64.wat` | `9e452c3c74df3d7afca3ec0bd3737f4b410a9133a447e0fd77f54042e8a81dd6` |
| `fixtures/wasm/extism-workspace-info.wasm` | `spec/mep/mck/fixtures/wasm/extism-workspace-info.wasm` | `9ca458d3b1416f5cdf6c119c4430d162d7b97208913c55c1c1928f62813a09fc` |
| `fixtures/wasm/extism-workspace-info.wat` | `spec/mep/mck/fixtures/wasm/extism-workspace-info.wat` | `92bf625dc9d19f1f4de2a5b87bf581156be4f9632d71e0bcea2b0cbf7b5ac88b` |
| `fixtures/wasm/initial-memory-4097.wasm` | `spec/mep/mck/fixtures/wasm/initial-memory-4097.wasm` | `71f72636dfbdab186b3e7c90e306f11e1de24c5b64a7113133ada2358161a2c9` |
| `fixtures/wasm/initial-memory-4097.wat` | `spec/mep/mck/fixtures/wasm/initial-memory-4097.wat` | `87ac0f3a2ed8a51e90e2789af94cebd54d7ad8877d38085146c0b364264d92bc` |
| `fixtures/wasm/memory64.wasm` | `spec/mep/mck/fixtures/wasm/memory64.wasm` | `b0fa65516410b3c0ae4971af97127148e8b40fc8ba1dc0849f0b4ff275fd0e30` |
| `fixtures/wasm/memory64.wat` | `spec/mep/mck/fixtures/wasm/memory64.wat` | `96b83eee074614715650590a8cc27d8c0df5d1e6146678e2ee934b3656119ca1` |
| `fixtures/wasm/mixed-extism-first.wasm` | `spec/mep/mck/fixtures/wasm/mixed-extism-first.wasm` | `2fcacc0461c84341ce1d6e05f04564cfa9310329314cdeabe4107dced2a8f39e` |
| `fixtures/wasm/mixed-extism-first.wat` | `spec/mep/mck/fixtures/wasm/mixed-extism-first.wat` | `d20ab6edcd2cc0925e926516cc093204d6a3a79b5a4d075685185c9d71e1ea21` |
| `fixtures/wasm/no-imports.wasm` | `spec/mep/mck/fixtures/wasm/no-imports.wasm` | `00eccd449472ee25314e7f8fc6c2da7ddb69f7a1523e4361be1ac70d323ebc05` |
| `fixtures/wasm/no-imports.wat` | `spec/mep/mck/fixtures/wasm/no-imports.wat` | `1be9a0d1436ac32d9e207d9aac05bf3e824e6fba29c608f65167df3cf5021365` |
| `fixtures/wasm/shared-memory.wasm` | `spec/mep/mck/fixtures/wasm/shared-memory.wasm` | `38159c11009cdf29b049e7d5152d4e9db64d31cf0e37fed6df441a532d2b8914` |
| `fixtures/wasm/shared-memory.wat` | `spec/mep/mck/fixtures/wasm/shared-memory.wat` | `48a0398f64f7ee7f11da8ff811686e22fbd92826a608f5aef832145b8ffe4003` |
| `fixtures/wasm/two-memories.wasm` | `spec/mep/mck/fixtures/wasm/two-memories.wasm` | `1675608ae18fd79913d2bfc61c5f04cd3652607b7d8d3e9655c6be5388a39126` |
| `fixtures/wasm/two-memories.wat` | `spec/mep/mck/fixtures/wasm/two-memories.wat` | `422406cb0c58c8d2b50318ae6aa4947ca90fc70054825da8a5f408546662baf4` |
