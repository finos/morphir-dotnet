# Morphir.Extensions.Wasm

The WASM extension runtime of morphir-dotnet: Wasmtime 50.0.1 with the embedded Extism kernel module. It is
built by the OpenSpec change `add-wasm-extension-host` (finos/morphir-dotnet#563). This host runs
`extism-kernel-v1` modules only, and speaks MEP `0.2.0-draft.1` only (design MEP-1). It references
`Morphir.Mep` for the host session's channel and the wire types (design LAY-1).

## From bytes to a session

```csharp
var engine = WasmEngine.Shared;                        // one engine for the process
var load = engine.Load(bytes);                         // validated and compiled, reused by SHA-256
var admission = load.Match(
    compiled => WasmHost.Admit(compiled, declared),    // classified against the pinned contract
    refused => new ModuleAdmission.Refused(refused),   // extension.wasm_feature_unsupported
    invalid => throw new InvalidDataException(invalid));
using var channel = admission.Match(
    module => WasmChannel.Open(module, SessionLimits.Default).Match(  // one store, one instance, for the channel
        opened => opened,
        failure => throw new InvalidOperationException(failure.Message)),  // extension.memory_limit: initial memory above the ceiling
    refusal => throw new InvalidOperationException($"{refusal.Code}: {refusal.Message}"));
var session = new HostSession(channel, host, offer);   // Morphir.Mep; a published guest needs the model-transport offer
var initialized = await session.InitializeAsync(cancellation);
var compiled = await session.RequestAsync(Method.CompileModel, parameters, cancellation);  // one handle call
await session.ShutdownAsync(cancellation);
await session.ExitAsync();                             // ends the instance; no call for a guest that advertised nothing
```

`SessionLimits.Default` has the contract's bounds: the 256 MiB ceiling, the instruction budget of MEP
`0.2.0-draft.1` (5,000,000,000), the 30 second call deadline, and the frame limits of 6 MiB for a request and
16 MiB for a response. There is one budget: the contract's budget for MEP `0.1` has no reader in this host. The
store underneath can be driven directly, as the bounds tests do:

```csharp
using var store = SessionStore.Open(module, SessionLimits.Default).Match(opened => opened, failure => throw ...);
var outcome = store.CallHandle(request, cancellation);     // a value, never a Wasmtime exception
outcome.Match(
    completed => ...,   // result, output and error, with FuelUsed, Elapsed and Memory
    failed => ...,      // GuestFailure: Code or Outcome, Memory, FuelUsed; the session is finished
    notRun => ...);     // an earlier call failed; the guest did not run
```

| Layer | Type | What it does |
| --- | --- | --- |
| Engine | `WasmEngine` | One Wasmtime engine per process, created on first use. Fuel consumption and epoch interruption are on, and a timer advances the epoch every 10 ms. Threads, 64-bit memory and multiple memories are off, so a module with a shared, 64-bit or second memory fails validation. |
| Module | `CompiledModule`, `ModuleLoad` | `Load` validates and compiles the bytes, or returns the module compiled earlier in the process from bytes with the same SHA-256. Nothing is cached on disk. A module that fails validation is a feature refusal when an engine with every feature enabled accepts it, and otherwise `Invalid`. |
| Classification | `ModuleClassifier`, `WasmHost` | `Classify` applies the contract's rules in the contract's order: the first profile import decides the profile, every import must be in that profile's table with its exact type, `handle` must be `() -> i32`, and a declared ABI must equal the classified profile. `WasmHost.Admit` then refuses a profile this host does not run. Refusals are `WasmRefusal` values with the contract's codes. |
| Session | `SessionStore`, `SessionLimits` | A new store per session, holding its own kernel instance, guest instance, memory ceiling, fuel budget and call deadline. Each call starts with the full budget and a new deadline. |
| Bounds | `HandleOutcome`, `GuestFailure`, `MemoryUse`, `SessionOpening` | Every call ends as a value. A failure has the contract's fault kind and host code or outcome, and the memory sizes. After a failure the session runs no more guest code. |
| Channel | `WasmChannel`, `WasmChannelOpening`, `WasmChannelFailure`, `WasmChannelException` | The Extism adapter: `Morphir.Mep`'s `ISingleMessageChannel` on one store. One `handle` call per request, the contract's `handle` results and frame limits, no notification for a guest that advertised nothing, and `morphir.exit` ending the instance. |

## Bounds and how they are reported

Parent decisions BND-1 and BND-2. The codes come from the contract's `faults` table; `cancelled` is the .NET
host's own outcome.

| Bound | Mechanism | `GuestFailure.Kind` | Code or outcome |
| --- | --- | --- | --- |
| Instruction budget | Fuel set before each call: the contract's budget for MEP `0.2.0-draft.1`, `WasmProfileContract.InstructionBudget`. | `BudgetExhausted` | `extension.budget_exhausted` |
| Call deadline | The epoch advances every 10 ms. The store's epoch callback runs at each tick on the call's thread and stops the call once `CallDeadline` has passed. | `CallDeadline` | outcome `deadline` |
| Cancellation | The same callback stops the call at the first tick after the `CancellationToken` passed to `CallHandle` is cancelled. A token cancelled before the call stops it before any guest code runs. | `Cancelled` | outcome `cancelled` |
| Memory ceiling | The store limit caps each memory, the kernel's and the guest's. The engine refuses growth past it without telling the host. | `MemoryLimit` | `extension.memory_limit` |
| Anything else | A trap, a host function that stops the call, `proc_exit` with a code that is not 0 | `GuestTrap`, `IncompleteSession` | `extension.guest_trap`, `extension.incomplete_session` |

The memory rules:

- **At the ceiling.** A failure that has a code (a trap, a failure or an exhausted budget) becomes
  `MemoryLimit` when the guest's memory is at the ceiling. `GuestFailure.Signal` keeps what was observed before
  the rule applied. The outcomes `deadline` and `cancelled` are kept as they are, because the host stopped
  those calls itself.
- **What "at the ceiling" means.** Only the guest's own memory counts; the kernel's memory is reported but does
  not decide. A memory is at the ceiling when it cannot grow by one more page. If the ceiling is not a whole
  number of pages, that is less than one page below it. A guest that defines no memory is never at the ceiling.
- **Initial memory above the ceiling.** `Open` does not instantiate such a guest and returns
  `SessionOpening.Failed` with `MemoryLimit`. This is a fault, not a refusal.
- **Sizes on every failure.** Every `GuestFailure` has `Memory`: the guest's memory, the kernel's memory and
  the ceiling. `Message` states all three, for example
  `guest memory 100 MiB (104857600 bytes), kernel memory 1 MiB (1048576 bytes), ceiling 256 MiB (268435456 bytes)`.

**Reading a memory the module does not export.** Wasmtime gives the host a memory only through an export. For a
module that defines a memory and does not export one, `WasmEngine.Load` also compiles a copy whose export
section has one more entry: memory 0 as `morphir:host/guest-memory`. Sessions run that copy
(`CompiledModule.ExportsGuestMemoryForTheHost`). The code and the memory are unchanged. Classification, the
digest and every report use the original bytes. All five published guests measured in task 2.5 export their
memory, so for them no copy is made.

After a failed call the session is finished. `SessionStore.Failure` holds the failure, and a later `CallHandle`
returns `HandleOutcome.NotRun` with that same value without running guest or kernel code. `proc_exit(0)` ends
the call successfully, as the contract's WASI policy says.

## The Extism adapter

`WasmChannel` (task 3.1) is what a `HostSession` and, in task 4.1, the worker drive. `WasmChannel.Open(module,
limits)` opens one `SessionStore` for the life of the channel and returns `WasmChannelOpening.Failed` with the
`GuestFailure` when the store cannot open.

What each call gives, from the contract's `extism-kernel-v1` call table and host-session rules:

| The guest | The channel | `WasmChannelFailure` | Code |
| --- | --- | --- | --- |
| `handle` returns 0 with output and no error | Returns the output | none | |
| Sets an Extism error, whatever `handle` returns | Throws `WasmChannelException` with the error text | `ExtismError` | `extension.incomplete_session` |
| Returns a result that is not 0, with no error | Throws; the output is not passed on | `NonZeroResult` | `extension.incomplete_session` |
| Returns 0 with no output for a request | Returns null; `HostSession` fails the session with `NoReply` | `NoOutput` | `extension.incomplete_session` |
| A failed store call: a bound, a trap, a host function that stops the call | Throws | `Guest` (the `GuestFailure`) | its code, or outcome `deadline` |
| A call cancelled by its token | Throws `OperationCanceledException` after the call has stopped | `Guest` (`Cancelled`) | outcome `cancelled` |
| A request frame over `SessionLimits.RequestFrameBytes` | Throws before `handle` runs | `RequestOverLimit` | `extension.request_limit` |
| A response frame over `SessionLimits.ResponseFrameBytes` | Throws; nothing of it is passed on | `ResponseOverLimit` | `extension.incomplete_session` |

- **Every failure ends the instance.** The store is disposed, `Failure` holds the reason, and every later call
  throws the same failure without running the guest. `HostSession` holds the exception in
  `SessionFailure.ChannelFailed`; `WasmChannelException` has `Code`, `Outcome`, `Message` and `Memory`.
- **The frame limits count the whole frame**, its `Content-Length` header included, as the contract's
  `hostSession.frameLimits.counted` says. A request frame exactly at the limit is passed. `HostSession` applies
  only its own 64 MiB message limit.
- **Notifications.** A guest receives a notification only after its `morphir.initialize` result advertises that
  it accepts them. The member is not fixed in the pinned contract (design NTFY-1), so the channel reads no
  advertisement and `NotifyAsync` returns null without a call for every notification. `DeliversNotifications` is
  false.
- **`morphir.exit` ends the instance.** For a guest that advertised nothing it ends without a call, as the contract
  says. Later calls throw `ObjectDisposedException`.
- **Where the call runs.** Each call runs on a thread the channel starts for it, which ends with the call, never on a
  thread-pool thread: a guest that spins holds its thread, and held pool threads starve the pool (see
  `WasmEngine`). The channel completes the returned task from that thread, so it never waits for the pool. Calls on
  one channel never overlap; a call made while another runs is refused with `InvalidOperationException`.
- `HandleCalls` counts the `handle` calls the channel made.

What task 3.2 changes: `WasmChannel.AdvertisesNotifications` reads the fixed member from the `morphir.initialize`
result. A guest that advertised then gets each notification as one `handle` call through the same path as a
request; output for one ends the instance as `AnsweredNotification` and is returned for `HostSession` to fail,
and `morphir.exit` is one call before the instance ends.

## The contract is read, not copied

The import table, the bounds and the refusal codes come from `wasm-execution-profiles.json`. That file is
embedded from `tests/Fixtures/MepWasm/`, where it is pinned unchanged (see that directory's `PROVENANCE.md`),
and parsed with `JsonDocument`. A test checks that the embedded copy has the pinned file's SHA-256. WASI
preview 1 imports are checked against the standard types that Wasmtime's own WASI definitions give, because the
contract admits every WASI preview 1 function and does not list them.

## Loading a configured module

`Morphir.Tooling`'s `WasmModuleLoader` (task 2.6) reads the file that `extensions.<name>.path` names, refuses
bytes whose digest differs from a declared `sha256` (read with `ModuleDigest.TryParse`) before the engine sees
them, and then calls `WasmEngine.Load` and `WasmHost.Admit` with the declared `abi`. It reports
`ModuleLoad.Invalid` as a file that is not a WASM module.

## Left for later tasks

- Task 3.2 turns on notification delivery for a guest that advertised (above).
- Task 4.1 joins Ctrl+C to the `CancellationToken` of the running call and exits with status 130, and drives
  `WasmChannel` from the worker with `--request-bytes` and `--response-bytes` as `SessionLimits.RequestFrameBytes`
  and `ResponseFrameBytes`.
- Tasks 3.3 and 3.4 replace the trapping callbacks for the Extism and Morphir host functions, and the empty
  WASI configuration.

The Native AOT probe in `tests/Morphir.Extensions.Wasm.AotProbe` runs this library on the published Python
`0.6.0` guest: the kernel proof with a MEP `0.2.0-draft.1` initialize, and one whole `HostSession` through
`WasmChannel` (evidence: `openspec/changes/add-wasm-extension-host/evidence/2.2-kernel-proof.md`, section "After
decision MEP-1"). With `--bounds <extism-spin.wasm>` it also stops the crafted spinning guest by budget, deadline
and cancellation (task 2.5 evidence: `evidence/2.5-bounds.md`).

The tests that need the Python guest get it from `artifacts/wasm-guests/` (git-ignored), or download it from its
release and check its SHA-256 (`tests/Morphir.Extensions.Wasm.Tests/PublishedGuest.cs` holds the pin). Without a
network they are skipped, unless `CI` is set, when they fail.
