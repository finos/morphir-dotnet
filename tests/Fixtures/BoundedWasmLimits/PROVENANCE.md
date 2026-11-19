# Bounded generation WASM limit controls

These test guests use the plain JSON IR 4 document-tree tuple and the limits in the first `generateModel.start` entry of [the Orders transcript](../Mep/session-model-access.md). Their initialize replies select different guest-state and frame allowances.

The state control selects one byte against a module with one initial memory page. The two frame controls select 1 MiB of guest state. After initialization they require the engine to deny growth to 17 pages, then send a 4,096-byte response with a 24-byte frame header. The selected frame ceilings are 4,119 and 4,120 bytes.

Each `.wasm` was assembled from the adjacent `.wat` with `wat2wasm` from WABT 1.0.39. The fixture catalog pins the sources and binaries. The black-box bounded-generation scenarios copy the binaries into their own workspaces.

| File | SHA-256 |
| --- | --- |
| `state-below-page.wat` | `927d631b62442b938799c2a507ab0efbe59a52291b66dd20baab5088604b22e3` |
| `state-below-page.wasm` | `e1a45ea2a086d4b9935ba091719818654f1db9d564586e5c7dec355e0318150b` |
| `frame-one-under.wat` | `39995def01a1f9b411902c109337c69233bf1b7d3ef44505c4983117c4dc208f` |
| `frame-one-under.wasm` | `16a79de45520e9e779b8e46f9917ef33d59f844297a681523beed2b05c24a541` |
| `frame-exact.wat` | `b1e236f111b8466638514ec427fd8bf880078864ef3154c019b7e5ee5d24397c` |
| `frame-exact.wasm` | `99a139a7d0126ffe9fb490dd082aabef9e4bd8ec50db6f557265cf812b0feede` |
