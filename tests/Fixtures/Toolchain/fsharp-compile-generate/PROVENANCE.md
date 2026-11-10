# Toolchain case fsharp-compile-generate: provenance

These files are byte-identical copies of the finos/morphir toolchain case
`spec/toolchain/workflows/fsharp-compile-generate`, at commit
`1451c081f56cf5b74618302d315dcdbe573028a1`. The case and its expectations stay in finos/morphir as data; do not
edit these copies. Copy them again when the case changes.

| File | Bytes | SHA-256 | Git blob |
| --- | --- | --- | --- |
| `source/Pricing.fs` | 124 | `f924e05d37b0517ac8777b495691ad86207f2b1dc7f5b6e038e6f6e370817d6c` | `3b37b2edeb08a92de133770c0cb2f67b149bd3a2` |
| `expected/ir-4.0.0.json` | 5,768 | `b1035d065ecfddc557476d7203afe29c83a9340abc27300aaeeb911490c30cb7` | `2dd743ba9853b65194c071d63ffa966c0f3e2643` |

The SHA-256 values are the ones that the case's `case.json` names.

Tests that use them:

- `tests/Morphir.Frontend.FSharp.Tests/ToolchainCaseTests.fs`: the compile step through the compatibility kit (the
  source compiles with the F# frontend to the expected IR).
- `tests/Morphir.CodeGeneration.FSharp.Extension.Tests/HostAdapterTests.fs`: the pinned unformatted generated set
  (`--option format=false`) of the built-in backend and the external guest.
