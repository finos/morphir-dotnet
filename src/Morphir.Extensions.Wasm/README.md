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
await session.ExitAsync();                             // ends the instance; one call only for a guest that asked for call delivery
```

`SessionLimits.Default` has the contract's bounds: the 256 MiB ceiling, the contract's `session` instruction
budget (5,000,000,000), the 30 second call deadline, and the frame limits of 6 MiB for a request and 16 MiB for a
response. There is one budget: the contract's `probe` budget has no reader in this host. The store underneath can
be driven directly, as the bounds tests do:

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
| Channel | `WasmChannel`, `WasmChannelOpening`, `WasmChannelFailure`, `WasmChannelException`, `WasmNotificationDelivery` | The Extism adapter: `Morphir.Mep`'s `ISingleMessageChannel` on one store. One `handle` call per request, the contract's `handle` results and frame limits, one call per notification for a guest that asked for `call` delivery and none for any other, and `morphir.exit` ending the instance. |
| Sandbox | `ExtismHostFunctions`, `WasiSandbox`, `SessionWorkspace` (internal); `GuestOutputTail`, `GuestOutput`, `RefusedHttpRequest`, `WasmHostPolicy` | The contract's host-function policy and WASI preview 1 for one session, the bounded guest output, what the guest attempted and did not get, and the private workspace. |

## Bounds and how they are reported

Parent decisions BND-1 and BND-2. The codes come from the contract's `faults` table; `cancelled` is the .NET
host's own outcome.

| Bound | Mechanism | `GuestFailure.Kind` | Code or outcome |
| --- | --- | --- | --- |
| Instruction budget | Fuel set before each call: the contract's `session` budget, `WasmProfileContract.InstructionBudget`. | `BudgetExhausted` | `extension.budget_exhausted` |
| Call deadline | The epoch advances every 10 ms. The store's epoch callback runs at each tick on the call's thread and stops the call once `CallDeadline` has passed. | `CallDeadline` | outcome `deadline` |
| Cancellation | The same callback stops the call at the first tick after the `CancellationToken` passed to `CallHandle` is cancelled. A token cancelled before the call stops it before any guest code runs. | `Cancelled` | outcome `cancelled` |
| Memory ceiling | The store limit caps each memory, the kernel's and the guest's. The engine refuses growth past it without telling the host, so a trap at the ceiling is the memory limit. | `MemoryLimit` | `extension.memory_limit` |
| Anything else | A trap, a host function that stops the call, `proc_exit` with a code that is not 0 | `GuestTrap`, `IncompleteSession` | `extension.guest_trap`, `extension.incomplete_session` |

The memory rules:

- **A trap at the ceiling.** A call that ends in a trap while the guest's memory or the Extism kernel's memory
  is at its ceiling becomes `MemoryLimit` (contract rule `at-ceiling`, as the parent corrected it a second time
  on 10 November 2026). A trap carries no cause, so the host infers one from the memory. `GuestFailure.Signal`
  keeps `GuestTrap`, and `Message` says which memory was at its ceiling. The kernel's memory counts because an
  Extism guest's input, output and allocations live there.
- **Every other end keeps its result** (contract rule `guest-reported`), also at the ceiling. A failure stays
  `IncompleteSession`: a `proc_exit` with a code that is not 0, a host function that stops the call, or a
  kernel with no room for the input. A call the host stopped keeps its result: an exhausted budget stays
  `BudgetExhausted`, and the outcomes `deadline` and `cancelled` stay. The contract's `observed-refusal` rule is
  for a host that observes refused growth; Wasmtime does not tell this host, so the rule adds nothing here.
- **What "at the ceiling" means.** Each memory is at its ceiling when it cannot grow by one more page
  (`MemoryUse.GuestAtCeiling`, `KernelAtCeiling`, and `AtCeiling` for either). If the ceiling is not a whole number
  of pages, that is less than one page below it. A guest that defines no memory has only the kernel's.
- **The channel's own failures.** A `handle` result that the Extism profile makes a failure is a call the
  guest ended itself: an Extism error, a result that is not 0, no output for a request, output for a
  notification. It is `extension.incomplete_session` with `FaultKind` `failure`, also at the ceiling. So is a
  response frame over its limit, a failure the host detected. A request frame over its limit is
  `extension.request_limit`. Each diagnostic still states both memory sizes and the ceiling.
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
| Returns 0 with no output for a delivered notification | Returns null | none | |
| Gives output for a delivered notification | Returns the output; `HostSession` fails the session with `AnsweredNotification` | `AnsweredNotification` | `extension.incomplete_session` |
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
- **Notifications** (task 3.2, design NTFY-1). The guest says how it wants them in its `morphir.initialize`
  result, with the string member the contract's `notificationDelivery` names, `capabilities.notificationDelivery`:

  ```json
  {"jsonrpc": "2.0", "id": 1, "result": {"protocolVersion": "0.2.0-draft.1", "capabilities": {"notificationDelivery": "call"}}}
  ```

  The channel reads it from the reply bytes it carried (`WasmNotificationDelivery.ReadFrom`, with `JsonDocument`).
  `call` sets `DeliversNotifications`, and each later notification, `morphir.initialized` included, is one `handle`
  call that should give no output. `none`, an absent member, a value that is not a string and any value the
  contract does not list, the reserved `poll` among them, mean no delivery: `NotifyAsync` returns null without a
  call. A reply that is not a JSON object with a result advertises nothing; judging it is `HostSession`'s job. A
  notification sent before the `morphir.initialize` reply is withheld. Every published bundle advertises nothing;
  the Python `0.6.0` guest gets no notification.
- **Output for a notification fails the session.** The channel records `AnsweredNotification`, ends the instance
  and returns the output, and `HostSession` fails the session with `AnsweredNotification`. An Extism error or a
  result that is not 0 for a notification throws, as for a request.
- **`morphir.exit` ends the instance.** For a guest that asked for `call` delivery it is one call first; for any
  other guest it ends the instance without a call. Later calls throw `ObjectDisposedException`.
- **Where the call runs.** Each call runs on a thread the channel starts for it, which ends with the call, never on a
  thread-pool thread: a guest that spins holds its thread, and held pool threads starve the pool (see
  `WasmEngine`). The channel completes the returned task from that thread, so it never waits for the pool. Calls on
  one channel never overlap; a call made while another runs is refused with `InvalidOperationException`.
- `HandleCalls` counts the `handle` calls the channel made, delivered notifications included. A session on a guest
  that asked for `call` delivery makes one call for `morphir.initialize`, one for `morphir.initialized`, one per
  request and one for `morphir.exit`.

## The sandbox

Tasks 3.3 and 3.4, parent decision SBX-1: accept the imports, bound the calls. Every import the classifier admits is
linked to a callback that does what the contract's policy says this host does, and nothing more. No callback reaches
the network, the file system (beyond creating and removing the session's own workspace), the environment, the
process's arguments or streams, or any host configuration. The library references no `System.Net` assembly, which a
test checks. Every callback is an untyped Wasmtime callback (`Linker.DefineFunction` with `UntypedCallbackDelegate`),
which gives no AOT warning.

```csharp
using var channel = WasmChannel.Open(module, SessionLimits.Default, workspaceRoot).Match(...);  // root: absolute, or null for the temp directory
var reply = await channel.RequestAsync(method, message, cancellation);   // never contains guest output
var tail = channel.GuestOutput.Drain();       // the outputs of the calls so far, and the bytes dropped
var refused = channel.RefusedHttpRequests;    // method and URL of each refused http_request
var denied = channel.RefusedWasiCalls;        // WASI function -> calls answered with errno 8 or 76
```

### Host functions

A text argument or result is a handle to a kernel block, and handle 0 means no block. The callbacks read and write
blocks through the session's own kernel instance, as an Extism host does: the kernel's `length` gives a block's size
(0 for a handle that names no block), the bytes are read from kernel memory, and a result is a new block from the
kernel's `alloc`. A handle that names no block where a block is needed, or a kernel with no room for a result, stops
the call. "Stops the call" is the contract's "a host function that stops the call": fault kind `failure`,
`extension.incomplete_session`, and the session is finished.

| Function | What this host does | Limit | Tests |
| --- | --- | --- | --- |
| `config_get(key) -> i64` | Handle 0 for every key; the key is not read | none | `sandbox/config-get`, `Config_get_gives_handle_0` |
| `var_get(key) -> i64` | A new block with the session's value, or handle 0 for a missing key | | `sandbox/var-round-trip`, `The_variable_store_holds_exactly_its_limit_of_keys_and_values` |
| `var_set(key, value)` | Stores the value for the session; value handle 0 deletes the key | 1 MiB of keys and values (`var.storeBytes`); a write over it stops the call and changes nothing; replacing a value counts its new size only | `sandbox/var-over-limit`, `The_variable_store_holds_exactly_its_limit_of_keys_and_values`, `A_variable_one_byte_over_the_limit_fails_the_call` |
| `log_info`, `log_warn`, `log_error` | The text becomes guest output, `log <level>: <text>` | the output bound | `sandbox/logging`, `Logging_records_info_and_above_and_every_morphir_log` |
| `log_trace`, `log_debug` | Nothing; the text is not read (`logging.dropped`) | | `sandbox/logging` |
| `get_log_level() -> i32` | 2, `info` (`logging.getLogLevel`) | | `sandbox/logging` |
| `http_request(req, body) -> i64` | No connection. Records the request's `method` (default `GET`) and `url` (default empty) as guest output `refused http_request <method> <url>: the guest has no network access` and in `RefusedHttpRequests`, then stops the call | at most 64 KiB of the request block is read; method and URL cut to 4 KiB | `sandbox/http-request`, `Http_request_fails_the_session_and_records_the_method_and_the_URL`, `Http_request_makes_no_connection_attempt`, `The_library_has_no_network_API_to_reach` |
| `http_status_code() -> i32` | 0 | | `sandbox/http-no-request`, `Http_status_code_and_headers_give_0_and_handle_0` |
| `http_headers() -> i64` | Handle 0 | | the same |
| `morphir_get_workspace_info() -> i64` | A new block with `{"root": <private empty directory>, "output_dir": ""}`; the same directory for every call of a session | | `sandbox/workspace-info`, the workspace tests |
| `morphir_cache_ir(key, ir)` | Stores the value for the session; value handle 0 is no block and stops the call | 16 MiB of keys and values and 1,024 entries; a write over either stops the call and changes nothing; replacing an entry adds none | `sandbox/ir-cache-over-bytes`, `sandbox/ir-cache-over-entries`, `The_IR_cache_holds_exactly_its_byte_limit`, `The_IR_cache_holds_exactly_its_entry_limit` |
| `morphir_get_cached_ir(key) -> i64` | A new block with the cached value, or with the text `null` | | `sandbox/ir-cache-round-trip`, `A_missing_IR_cache_entry_is_the_text_null` |
| `morphir_log(level, message)` | The message becomes guest output whatever the level, `morphir_log <level>: <message>` | the output bound | `sandbox/logging` |

Keys are compared as bytes, and a store's size is the bytes of its keys plus the bytes of its values, as the MoonBit
host counts them. The variable store and the IR cache belong to one session: a second session of the same module
starts empty (`A_second_session_starts_empty`, and the second run of each round-trip case).

**The private workspace.** `SessionWorkspace` creates the directory the first time the guest calls
`morphir_get_workspace_info`, below the workspace root given to `Open` (the system's temporary directory when none is
given), named `morphir-wasm-session-<32 hex digits>`, and readable only by the user on Unix. It is removed when the
session fails (in `SessionStore`'s failure path, before the failure is returned), when the store is disposed, and so
when a `WasmChannel` ends its instance at `morphir.exit`, at a failure or at `Dispose`. A session that never asks has
no directory. The guest cannot open it: the sandbox has no preopened directory.

### WASI

**Decision (the design's open question, task 3.4): managed host functions supply WASI preview 1, not the engine's
WASI.** The engine's WASI was the first choice if it could capture stdout and stderr into a bounded buffer and
return an error code for every other call. It can capture the streams: Wasmtime .NET 50.0.1 has
`WasiConfiguration.WithStandardOutputCallback` and `WithStandardErrorCallback`, which hand the bytes to a callback on
a Wasmtime thread, with no file. It fails on the other half, which the crafted cases decide:

| The contract requires | The engine's WASI, with an empty configuration | Evidence |
| --- | --- | --- |
| `poll_oneoff` gives errno 76 at once; no call can block | Sleeps for the requested timeout and succeeds | `sandbox/wasi-unsupported` took 10.0 s and answered `ff` in the red run; `WasiDecisionTests` measures a 200 ms sleep |
| `path_open` on fd 0 gives errno 76 | Another errno | `sandbox/wasi-unsupported` (`ff`), `WasiDecisionTests` |
| Any other function gives errno 76 | Real answers: `fd_fdstat_get` on stdout, `sched_yield` and `args_get` succeed | `WasiDecisionTests` |

So `WasiSandbox` defines each WASI import the guest makes, by name, with the type the classifier has already checked
against the engine's own WASI definitions (the classifier still reads the types from there, so no second table of
signatures exists). The engine's WASI is never linked, and a store gets no `WasiConfiguration`. Managed callbacks are
the same untyped callbacks as the host functions, so the choice adds no AOT warning; the probe runs them under
Native AOT.

| The guest calls | It gets (policy `wasi`) |
| --- | --- |
| `args_sizes_get`, `environ_sizes_get` | Success, a count and a size of 0, whatever the host process has |
| `fd_read` on fd 0 | Success, 0 bytes read |
| A function on an fd above 2 (`fd_*`, `path_*`, `sock_*`; for `fd_renumber`, `path_link`, `path_rename` and `path_symlink` any of their fds) | Errno 8 (`badf`) |
| `clock_time_get`, `clock_res_get` | The realtime clock (id 0) in nanoseconds since 1970, or the monotonic clock (ids 1 to 3); a resolution of 100 ns or the timestamp's |
| `random_get` | Bytes from the system's cryptographic generator |
| `fd_write` on fd 1 or 2 | Success with every byte written; the text becomes guest output `stdout: <text>` or `stderr: <text>` |
| `proc_exit(0)` | The call succeeds with the output set before it; another code fails the session |
| Anything else: `args_get`, `environ_get`, `poll_oneoff`, `sched_yield`, `fd_read` on 1 or 2, `fd_write` on 0, `path_open` on 0 | Errno 76 (`notcapable`), at once |

Where the contract is silent the sandbox answers as WASI does: a pointer outside the guest's memory is errno 21
(`fault`), a clock id above 3 is errno 28 (`inval`), and a write of more than 4 GiB in one call is errno 28. The process
and thread CPU-time clocks read the monotonic clock, as the MoonBit sandbox does. Calls answered with errno 8 or 76
are counted by function in `RefusedWasiCalls`.

### Guest output

Guest output is WASI stdout and stderr, the recorded `log_*` and `morphir_log` messages, and refused HTTP requests.
It is never part of a reply: the reply is the Extism output and nothing else. The library never writes it to the
process's stdout or stderr.

`GuestOutputTail` bounds it per session, with the MoonBit worker's rule, which task 4.1's `output` events follow:

- **Each output** is cut to 4 KiB of reported text (`MessageBytes`, MoonBit's `EVENT_MESSAGE_BYTES`) at a character
  boundary and ends with `…`; `GuestOutput.Cut` says so.
- **The tail** keeps the newest outputs whose reported text is at most 64 KiB (`TailBytes`, MoonBit's
  `OUTPUT_TAIL_BYTES`) and drops the oldest.
- **Dropped bytes** are counted in `DroppedBytes`: the UTF-8 bytes of the cut ends and of every dropped output. The
  worker reports them as one `output` event with `droppedBytes`, the contract's member.
- Reported text is counted as JSON string text: a quote or a backslash takes 2 bytes and a control character 6.
- The host reads at most 4 KiB of one payload (one `fd_write`, one log block), so a guest that writes 200 MiB at once
  costs no more memory than one that writes 4 KiB; the unread bytes count as dropped.
- `Drain()` takes the kept outputs and the dropped count and empties the tail. The worker drains it after each call,
  as MoonBit does; a session that is never drained keeps the newest 64 KiB of all its calls.

A guest that writes 10 MiB in one call, through `fd_write` or `log_info` in 10,240 writes of 1 KiB, gives its reply
unchanged, keeps at most 64 KiB, counts the rest as dropped, and moves the managed heap by less than 4 MiB
(`A_guest_that_writes_10_MiB_keeps_a_bounded_tail_and_its_reply`).

## The contract is read, not copied

The import table, the bounds, the refusal codes and the host-function policy come from `wasm-execution-profiles.json`.
`WasmHostPolicy` reads the policy's numbers (the store and cache limits, the log level, the two errnos) and checks
that every other policy leaf names the behaviour this library implements, so a pin that changes a behaviour fails to
load. That file is
embedded from `tests/Fixtures/MepWasm/`, where it is pinned unchanged (see that directory's `PROVENANCE.md`),
and parsed with `JsonDocument`. A test checks that the embedded copy has the pinned file's SHA-256. WASI
preview 1 imports are checked against the standard types that Wasmtime's own WASI definitions give: the contract's
`wasiSurface` names the 46 functions of `wasi_snapshot_preview1.witx` instead of a table, and the reader refuses any
other surface. The reader also requires, and refuses to load without: the `session` instruction budget (and no
budget key other than `session` and `probe`), the classification order this library implements (profile, imports,
`handle` export, declared ABI, host profile), the memory rules this library applies (`at-ceiling`,
`guest-reported`, `observed-refusal`, `initial-above-ceiling`, in that order), a `cancelled` host-session rule
without a code, and an Extism
`notificationDelivery` of a string member of the `morphir.initialize` result with the values `none` (the default)
and `call` and the four notification rules.

## Loading a configured module

`Morphir.Tooling`'s `WasmModuleLoader` (task 2.6) reads the file that `extensions.<name>.path` names, refuses
bytes whose digest differs from a declared `sha256` (read with `ModuleDigest.TryParse`) before the engine sees
them, and then calls `WasmEngine.Load` and `WasmHost.Admit` with the declared `abi`. It reports
`ModuleLoad.Invalid` as a file that is not a WASM module.

## Left for later tasks

- Task 4.1 delivers notifications through the worker the same way, from the same `WasmChannel`, and reports the
  notification cases' exit statuses and `fault` events, which only the worker process shows.
- Task 4.1 joins Ctrl+C to the `CancellationToken` of the running call and exits with status 130, and drives
  `WasmChannel` from the worker with `--request-bytes` and `--response-bytes` as `SessionLimits.RequestFrameBytes`
  and `ResponseFrameBytes`.
- Task 4.1 reports `GuestOutput` as `output` events after each call (draining the tail, then one `droppedBytes`
  event) and may name the refused URL and WASI calls in its diagnostics; the workspace root is the worker's own
  private directory.

The Native AOT probe in `tests/Morphir.Extensions.Wasm.AotProbe` runs this library on the published Python
`0.6.0` guest: the kernel proof with a MEP `0.2.0-draft.1` initialize, and one whole `HostSession` through
`WasmChannel` (evidence: `openspec/changes/add-wasm-extension-host/evidence/2.2-kernel-proof.md`, section "After
decision MEP-1"). With `--bounds <extism-spin.wasm>` it also stops the crafted spinning guest by budget, deadline
and cancellation (task 2.5 evidence: `evidence/2.5-bounds.md`). With `--sandbox tests/Fixtures/MepWasm/fixtures/wasm`
it runs six pinned crafted modules whose calls reach the host functions and the WASI callbacks (`extism-log`,
`extism-var-round-trip`, `extism-fd-write`, `extism-wasi-empty`, `extism-clock-random`, `extism-http`), each checked
against the corpus digest, and the notification check (task 3.2): `extism-notify-accept` through `WasmChannel` gets
`morphir.initialized` and `morphir.exit` as one call each, and `extism-notify-answer` fails the session.

The tests that need the Python guest get it from `artifacts/wasm-guests/` (git-ignored), or download it from its
release and check its SHA-256 (`tests/Morphir.Extensions.Wasm.Tests/PublishedGuest.cs` holds the pin). Without a
network they are skipped, unless `CI` is set, when they fail.
