# Orders scale WASM guest

This is the Extism streaming guest built from [scale-guest.c in finos/morphir](https://github.com/finos/morphir/blob/3652032a53adeb6bfa4a5d672142487de45c57c5/spec/mck/bounded-generation/scale-guest.c). The parent records this exact digest and the [Clang and wasm-ld build commands](https://github.com/finos/morphir/blob/3652032a53adeb6bfa4a5d672142487de45c57c5/spec/mck/bounded-generation/scale-guest.md#build-and-focused-verification).

The integration tests pair the guest with a one-module model and a document one byte above the reader limit. Full Orders Scale A and B runs are manual.

| File | SHA-256 |
| --- | --- |
| `orders-scale.wasm` | `9c67d3e6130726d62abf662e1d7a656ab6e92c2059b634bea2e1d444fac7a8ef` |
