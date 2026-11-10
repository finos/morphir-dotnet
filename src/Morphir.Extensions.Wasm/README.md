# Morphir.Extensions.Wasm

The WASM extension runtime of morphir-dotnet: Wasmtime 50.0.1 with the embedded Extism kernel module. It is
built by the OpenSpec change `add-wasm-extension-host` (finos/morphir-dotnet#563). This host runs
`extism-kernel-v1` modules only.

## From bytes to a session

```csharp
var engine = WasmEngine.Shared;                        // one engine for the process
var load = engine.Load(bytes);                         // validated and compiled, reused by SHA-256
var admission = load.Match(
    compiled => WasmHost.Admit(compiled, declared),    // classified against the pinned contract
    refused => new ModuleAdmission.Refused(refused),   // extension.wasm_feature_unsupported
    invalid => throw new InvalidDataException(invalid));
using var session = admission.Match(
    module => SessionStore.Open(module, SessionLimits.Default),   // a new store per session
    refusal => throw new InvalidOperationException($"{refusal.Code}: {refusal.Message}"));
var result = session.CallHandle(request);
```

| Layer | Type | What it does |
| --- | --- | --- |
| Engine | `WasmEngine` | One Wasmtime engine per process, created on first use. Fuel consumption and epoch interruption are on, and a timer advances the epoch every 10 ms. Threads, 64-bit memory and multiple memories are off, so a module with a shared, 64-bit or second memory fails validation. |
| Module | `CompiledModule`, `ModuleLoad` | `Load` validates and compiles the bytes, or returns the module compiled earlier in the process from bytes with the same SHA-256. Nothing is cached on disk. A module that fails validation is a feature refusal when an engine with every feature enabled accepts it, and otherwise `Invalid`. |
| Classification | `ModuleClassifier`, `WasmHost` | `Classify` applies the contract's rules in the contract's order: the first profile import decides the profile, every import must be in that profile's table with its exact type, `handle` must be `() -> i32`, and a declared ABI must equal the classified profile. `WasmHost.Admit` then refuses a profile this host does not run. Refusals are `WasmRefusal` values with the contract's codes. |
| Session | `SessionStore`, `SessionLimits` | A new store per session, holding its own kernel instance, guest instance, memory ceiling, fuel budget and call deadline. Each call starts with the full budget and a new deadline. |

## The contract is read, not copied

The import table, the bounds and the refusal codes come from `wasm-execution-profiles.json`. That file is
embedded from `tests/Fixtures/MepWasm/`, where it is pinned unchanged (see that directory's `PROVENANCE.md`),
and parsed with `JsonDocument`. A test checks that the embedded copy has the pinned file's SHA-256. WASI
preview 1 imports are checked against the standard types that Wasmtime's own WASI definitions give, because the
contract admits every WASI preview 1 function and does not list them.

## Left for later tasks

- Task 2.5 maps the engine's signals to outcomes. `CallHandle` throws Wasmtime's `TrapException`, with
  `TrapCode.OutOfFuel` for an exhausted budget and `TrapCode.Interrupt` for a passed deadline. `Open` throws
  `WasmtimeException` for an initial memory above the ceiling. Task 2.5 also adds cancellation and the
  at-ceiling memory rule, which can read `GuestMemory` and `KernelMemory`.
- Task 3.1 builds the Extism adapter and MEP channel on `SessionStore.CallHandle`.
- Tasks 3.3 and 3.4 replace the trapping callbacks for the Extism and Morphir host functions, and the empty
  WASI configuration.
- Task 2.6 turns `ModuleLoad.Invalid` into the diagnostic for a path that is not a WASM module.

The Native AOT probe in `tests/Morphir.Extensions.Wasm.AotProbe` runs this library on the Python `0.4.2` guest
(task 2.2 evidence: `openspec/changes/add-wasm-extension-host/evidence/2.2-kernel-proof.md`).
