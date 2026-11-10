# Spike: a WASM trap kills the process on win-x64

Issue #563, branch `ci/wasm-win-x64-trap`. This folder is not in the solution. The findings are in
`openspec/changes/add-wasm-extension-host/evidence/5.2-win-x64-trap.md`.

`TrapRepro` runs one trap per process and prints how it ended. Its cases:

| Case | How Wasmtime raises the trap |
| --- | --- |
| `no-trap` | none (the control) |
| `unreachable`, `oob-load`, `div-zero`, `int-overflow`, `call-indirect-null`, `stack-overflow` | a hardware fault, which Wasmtime's vectored exception handler catches |
| `host-throws`, `fuel`, `epoch-interrupt`, `epoch-callback-throws` | from Wasmtime's own runtime code, after a host call or a libcall |
| `product-trap`, `product-grow-then-trap` | the corpus modules of `bounds/trap` and `bounds/grow-to-ceiling-then-trap`, through the product's `SessionStore` |

`--signals-based-traps false` builds the engine with `Config.WithSignalsBasedTraps(false)`. For the product cases it sets
`MORPHIR_WASM_EXPERIMENT_SIGNALS_BASED_TRAPS=false`, the off-by-default experiment switch in `WasmEngine`.

The other commands are tools for the diagnosis: `info` (the process's architecture, executable and user shadow stack
policy), `pe` (the CET-compatible bit, guard flags and EH continuation count of PE files), `pe-cet` (sets or clears
the CET-compatible bit) and `dump` (the exception record of a minidump, with the fast-fail code).

`run.ps1` builds every launch kind, runs the matrix and the experiments, and writes `results.json` and `summary.md`.
The workflow `.github/workflows/wasm-win-x64-trap.yml` runs it on `windows-2025` and `windows-11-arm`. To run it on a
developer's machine, without changing the registry:

```powershell
pwsh spikes/wasm-win-x64-trap/run.ps1 -Rid win-arm64 -WorkDirectory ../scratch/trap -SkipProduct -NoSystemChanges
```

Build each variant from a clean `obj`. An incremental publish keeps the apphost it made before, so passing
`-p:CETCompat=false` to a second publish leaves the mark set.
