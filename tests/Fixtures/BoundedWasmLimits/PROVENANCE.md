# Bounded generation WASM limit controls

These test guests use the plain JSON IR 4 document-tree tuple and the limits in the first `generateModel.start` entry of [the Orders transcript](../Mep/session-model-access.md). Their initialize replies select different guest-state and frame allowances.

The state control selects one byte against a module with one initial memory page. The two frame controls select 1 MiB of guest state. After initialization they require the engine to deny growth to 17 pages, then send a 4,096-byte response with a 24-byte frame header. The selected frame ceilings are 4,119 and 4,120 bytes.

Each `.wasm` was assembled from the adjacent `.wat` with `wat2wasm` from WABT 1.0.39. The fixture catalog pins the binary hashes. The black-box bounded-generation scenarios copy these bytes into their own workspaces.
