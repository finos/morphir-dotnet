---
title: "Extension model spikes"
linkTitle: "Extension model spikes"
description: >
  Spikes before step 2 of the Morphir extension model PRD, with measurements and decisions.
---

# Extension model spikes

These spikes answer the "Spikes before step 2" items of the [Morphir extension model PRD](../../prds/morphir-extension-model.md). Each report has the reproduction steps, the measurements and the decision. The probe sources are in `spikes/extension-model/` at the repository root. They are not in the solution.

| Spike | Decision |
| --- | --- |
| [JSON-RPC library](jsonrpc.md) | `Morphir.Mep` writes its own minimal JSON-RPC 2.0 layer on System.Text.Json. |
| [WASM runtime](wasm-runtime.md) | Wasmtime for .NET, statically linked into `morphir`; `dotnet-morphir` becomes RID-specific. |
| [.NET guest toolchains](dotnet-guest-toolchains.md) | The .NET guest SDK targets `process` only for now; WASM guests come later. |
| [Roslyn under AOT](roslyn-aot.md) | Feasible as a built-in of `morphir`; it ships as an optional extension for now, because of size. |
