---
title: "WASM runtime spike, 2026-11-07"
linkTitle: "WASM runtime spike"
description: >
  Native AOT and dotnet tool probes of Wasmtime for .NET, Extism for .NET and the WACS interpreter as hosts for morphir-bytes-v1 guests, and the decision for step 2a.
---

# WASM runtime spike, 2026-11-07

Status: Decided. Step 2a uses Wasmtime for .NET (NuGet `Wasmtime` 50.0.1). The `morphir` AOT binary links `libwasmtime.a` statically, so it stays one file. `dotnet-morphir` becomes a RID-specific dotnet tool that contains the native library from the NuGet package.

This spike is part of the "Spikes before step 2" item of the [Morphir extension model PRD](../../prds/morphir-extension-model.md). The probe sources and the guest are in `spikes/extension-model/wasm-runtime/`.

The host must run `morphir-bytes-v1` guests (`finos/morphir-moonbit`, `pkgs/morphir-host/worker/wasm/README.mbt.md`):

- The module imports only `morphir_bytes_v1.input_len: () -> i32`, `input_byte: (i32) -> i32` and `output_byte: (i32) -> ()`.
- The module exports `handle: () -> i32`. A positive result is the response length, 0 is exit, -1 is a failure and -2 is a notification.
- The host applies a 256 MiB memory ceiling, a 30 s deadline for each call and a fuel limit.

## Reproduction

The probes build outside the repository. Thus the repository `Directory.Build.props` and central package management do not apply.

Environment: macOS 26.6.2, arm64, .NET SDK 10.0.403, ILCompiler 10.0.14, RID `osx-arm64`, `wasm-tools` 1.229.0.

```bash
export DOTNET_ROOT="$HOME/.local/share/mise/installs/dotnet/10.0.403"
export PATH="$DOTNET_ROOT:$PATH"
WORK="$(mktemp -d)"
spikes/extension-model/wasm-runtime/publish.sh "$WORK"   # publishes all variants, packs and installs the tools
spikes/extension-model/wasm-runtime/run.sh "$WORK" 20     # sizes, checks and cold start (median of 20 runs)
```

`publish.sh` copies the probes to the work folder. It downloads the static libraries from the GitHub releases: `wasmtime-v50.0.1-aarch64-macos-c-api.tar.xz` (`libwasmtime.a`) and `libextism-aarch64-apple-darwin-v1.13.0.tar.gz` (`libextism.a`). It writes one build log for each variant to `$WORK/out/<variant>.log`.

To make the guest again: `wasm-tools parse guest/guest.wat -o guest/guest.wasm`.

The probes:

- `guest/guest.wat` is a `morphir-bytes-v1` guest of 362 bytes. It has one memory of 1 page and no declared maximum. The first input byte selects the behavior: echo (any other byte), exit (no input), an endless loop (`!`), a growth of 1,024 pages (`+`) and a growth of 4,096 pages (`^`, above the ceiling).
- `shared/Probe.cs` is the shared harness. Each runtime gives one `IGuestRuntime`. The harness loads the guest with the PRD limits and makes these checks: echo of a JSON-RPC request, a warm call, echo of 1 MiB (about 2 million host calls), memory below and above the ceiling, exit, fuel (endless loop, 100,000,000 fuel, no deadline) and deadline (endless loop, 2 s deadline, no fuel). A watchdog stops a check after 20 s.
- `BaselineProbe` has the harness and no runtime. Its size is the floor.
- `WasmtimeProbe` uses `Wasmtime` 50.0.1: `Store.SetLimits`, `Store.Fuel` and epoch interruption with a 10 ms ticker. It has three bindings. The default uses untyped callbacks and an untyped call. `WASMTIME_TYPED_IMPORTS` uses typed callbacks. `WASMTIME_TYPED` also uses a typed call.
- `ExtismProbe` uses `Extism.Sdk` 1.10.0 with `Extism.runtime.osx-arm64` 1.13.0: the manifest `memory.max_pages` and `timeout_ms`, and `FuelLimit`. `EXTISM_SDK_CALL=1` uses `Plugin.Call`. The default calls `libextism` directly (see the results).
- `WacsProbe` uses `WACS` 0.16.14: `InvokerOptions.GasLimit`, a module memory maximum and a `CancellationToken` in `ExecContext.Ct`.
- `ToolPortable` and `ToolRid` pack the Wasmtime probe as a portable dotnet tool and as a RID-specific dotnet tool.

The managed interpreter is WACS. It is the best maintained pure C# WASM runtime on NuGet: 14,600 downloads, release 0.16.14 on 2026-05-28, Apache-2.0, WASM 3.0, and it says that it is AOT-safe. It is the only one with instruction metering. The other candidates are not usable:

- `WebAssembly` 2.1.0 (dotnet-webassembly) compiles WASM to IL at run time with `Reflection.Emit`. Native AOT does not support this.
- `DotWasm` 0.1.0 has one release (2026-05-27) and about 160 downloads.
- `Wasm` 0.1.7 (cs-wasm) has had no release since 2019.

## Results

| Item | Wasmtime 50.0.1 | Extism.Sdk 1.10.0 + libextism 1.13.0 | WACS 0.16.14 |
| --- | --- | --- | --- |
| Trim and AOT warnings | 0 with untyped callbacks. 1 with typed callbacks (IL3050). 5 with typed callbacks and a typed call (IL2055, IL2070, IL2072, IL3050 × 2). All are in `Wasmtime.Dotnet`. | 0 | 2: IL3050 in FluentValidation (`EnumValidator`), IL2075 in `WasmRuntime.BindHostFunction<TDelegate>` |
| AOT single file | Yes, with static link of `libwasmtime.a` (`DirectPInvoke` + `NativeLibrary`). Without it, `libwasmtime.dylib` must be beside the binary; alone, the binary fails with `DllNotFoundException`. | Yes, with static link of `libextism.a`. The linker warns that the objects are for macOS 15.5 and the binary is for macOS 12.0. | Yes. No native code. |
| JIT single file and tool layout | Yes. `PublishSingleFile` with `IncludeNativeLibrariesForSelfExtract` extracts the library at the first start. The portable and the RID-specific tool run. | Yes, the same | Yes. No native code. |
| AOT size (bytes); baseline 1,302,224 | Static: 11,949,456 (+10,647,232). Not static: 1,767,392 + 23,518,480 (`libwasmtime.dylib`). | Static: 17,587,432 (+16,285,208). Not static: 2,597,112 + 20,197,376 (`libextism.dylib`). | 7,220,808 (+5,918,584) |
| Memory ceiling, 256 MiB | Yes. `Store.SetLimits(memorySize)`. `memory.grow` above the ceiling returns -1. | Partly. `max_pages` 4,096 let the memory grow to 4,097 pages. A refused growth is a trap (`oom`), not -1. | No host limit. The host sets the module memory maximum before instantiation. Then `memory.grow` above it returns -1. |
| Fuel | Yes. `Store.Fuel` for each call. 100,000,000 fuel ran out after 24 to 47 ms (`TrapCode.OutOfFuel`). | Yes. `FuelLimit`. Ran out after 26 to 46 ms. | Yes. `GasLimit`. Ran out after 368 to 383 ms, which is about 13 times slower. |
| Wall-clock deadline | Yes. Epoch interruption stops the loop after 2,000 ms (`TrapCode.Interrupt`). | Yes. `timeout_ms` stops the loop after 2,004 ms. | No. The endless loop did not stop in 20 s. The source checks `ExecContext.Ct` only at function calls. |
| Cold start, AOT: first run after publish; median of 20 runs; process start to first response | Not static: 24.5 ms; 2.06 ms; 6 ms. Static: 18.7 ms; 1.23 ms; 5 ms. | Not static: 16.9 ms; 1.45 ms; 5 ms. Static: 10.5 ms; 0.59 ms; 5 ms. | 5.5 ms; 1.43 ms; 8 ms |
| Cold start, tool layout (JIT): median load and call; process start to first response | 11.8 ms; 30 ms | 4.7 ms; 23 ms | 76 ms; 96 ms |
| Echo of 1 MiB, AOT | Untyped callbacks: 149 ms. Typed callbacks: 18 ms. | 115 to 120 ms | 118 ms |

"Cold start" is the time for a new engine, compile, instantiate and one `handle` call. "Process start to first response" uses `Process.StartTime`, which has a resolution of 1 ms.

Notes on Wasmtime:

- The warnings come from `ReturnTypeFactory<T>.Create`, `TypeExtensions.TryGetResultInterface` and `ValueRaw.Converter<T>`. They use `MakeGenericType` for tuple types. `int` does not use that branch, so the typed probes pass all checks under AOT. But the warnings cannot be removed at the call site.
- The untyped callbacks box each value. For this ABI, which moves one byte for each host call, they are 8 times slower than typed callbacks.
- `libwasmtime.dylib` in the NuGet package has the same size (23,518,480 bytes) as the one in the C API release v50.0.1. The NuGet version and the C API version are the same.
- Native library sizes in the NuGet package: `linux-x64` 29,667,272, `linux-arm64` 25,962,952, `osx-x64` 26,816,496, `osx-arm64` 23,518,480, `win-x64` 21,257,728, `win-arm64` 16,144,896 bytes.

Notes on Extism:

- `Plugin.Call` does not give the result of `handle`. A positive result and 0 return normally, with no error. Thus the host cannot tell a response from exit, failure or notification. The probe default does not use `Plugin.Call`. It calls `extism_plugin_new` and `extism_plugin_call` with its own `DllImport`, and it gets the internal `HostFunction.NativeHandle` with `UnsafeAccessor`.
- `max_pages` 1,024 let the memory grow to 1,025 pages; `max_pages` 1,023 refused it. Thus the ceiling is one page (64 KiB) too high.
- Extism uses the Wasmtime compile cache on disk (`~/Library/Caches/BytecodeAlliance.wasmtime`) by default. This explains part of its lower cold start.
- `Extism.Sdk` 1.10.0 is from 2025-12-01. `libextism` contains Wasmtime.

Notes on WACS:

- `CreateInvokerFunc<Value>` works under JIT. Under AOT it fails at run time with "Return type mismatch". The untyped `CreateInvoker` works in both.
- The invoker wraps guest faults in `TargetInvocationException`.
- WACS has one maintainer and is not at 1.0.

dotnet tool packages (Wasmtime probe, `Wasmtime` 50.0.1):

| Package | Size (bytes) | Tool store after install |
| --- | --- | --- |
| Portable tool (all six native libraries) | 51,246,906 | 240,824 KB |
| RID-specific tool, pointer package | 2,010 | n/a |
| RID-specific tool, `osx-arm64` package | 8,503,905 | 40,452 KB |
| RID-specific tool, other RIDs | 7,295,761 (`win-arm64`) to 9,955,276 (`linux-x64`) | not installed |

Both tools install from a local feed and pass all checks.

The `morphir` AOT binary today: 18,160,968 bytes (`osx-arm64`, commit `b72baa9`, `PublishAot`, `PublishSingleFile`, `PublishTrimmed`).

## Reference guest: morphir-elm

`finos/morphir-elm` publishes a real `morphir-bytes-v1` guest: release `extension/elm/v0.5.0-preview.2` has `morphir-elm-0.5.0-preview.2.wasm` (2,006,951 bytes) and a `release.json` (descriptor `2.0.0-draft.2`, ABI `morphir-bytes-v1`, MEP `0.2.0-draft.1`). The module imports only `morphir_bytes_v1.input_len`, `input_byte` and `output_byte`, and it exports `memory`, `handle` and `_start`. It claims an Elm frontend (`morphir.frontend.compileModel`) and an Elm backend (`morphir.backend.generateModel`) through model transport (`morphir-json`, IR 4.0.0).

A check on 2026-11-07: the `.sha256` file matches. A `morphir.extension.describe` request sent with `wasmtime` and the test host module from the guest-toolchain spike (`spikes/extension-model/dotnet-guest-toolchains/host/`) returns the claim set of the `release.json`, without a call to `_start` first.

```console
gh release download extension/elm/v0.5.0-preview.2 --repo finos/morphir-elm
make-host.sh '{"jsonrpc":"2.0","id":1,"method":"morphir.extension.describe","params":{"protocolVersions":["0.2.0-draft.1"]}}' describe.wasm
wasmtime run --preload morphir_bytes_v1=describe.wasm --invoke handle morphir-elm-0.5.0-preview.2.wasm
```

So the step 2a host can be tested against a guest that it did not write. See the PRD for how the steps use it.

## Decision

Step 2a uses Wasmtime for .NET (NuGet `Wasmtime` 50.0.1).

Reasons:

- Wasmtime is the only candidate that applies all three PRD limits correctly: the exact memory ceiling, fuel for each call and a wall-clock deadline.
- It publishes with zero trim and AOT warnings when it uses untyped callbacks.
- The static link keeps `morphir` one file. The same NuGet package gives the native library for the dotnet tool.
- The Bytecode Alliance maintains it. The NuGet release follows the Wasmtime release (50.0.1 on 2026-11-06).

Extism is not the choice:

- `Plugin.Call` loses the result of `handle`. The host must call `libextism` itself and use an internal member of `HostFunction`.
- The memory ceiling is one page too high, and a refused growth is a trap.
- The static binary is 5.6 MB larger than the Wasmtime static binary. The linker warns about the macOS version of the objects.
- Extism adds no limit that Wasmtime does not have, because `libextism` runs on Wasmtime.

WACS is not the choice:

- It cannot stop a loop that does not call a function. The 30 s deadline is a PRD limit.
- It has 2 AOT warnings, and a typed invoker that works under JIT fails under AOT.
- Compute is about 13 times slower than Wasmtime.

WACS has no native code and is the smallest (+5.9 MB). If a later change cannot use native code, WACS can run guests only in a separate process that the host can stop.

## Consequences for step 2a and packaging

Host design:

- Use one `Engine` for each process, with fuel and epoch interruption on. One timer increments the epoch. The probe uses 10 ms ticks.
- Use one `Store` for each guest. Call `Store.SetLimits(memorySize: 256 MiB)` before instantiation.
- Before each `handle` call, set `Store.Fuel` and `Store.SetEpochDeadline`.
- Map `TrapCode.OutOfFuel` to `extension.budget_exhausted` and `TrapCode.Interrupt` to the call deadline. Map other traps to `extension.guest_trap`.
- With `SetLimits`, a refused growth returns -1 to the guest. Upstream fails the session with `extension.memory_limit`. Step 2a must decide how the host finds this case. For example: a trap when the memory is at the ceiling.
- Start with the untyped callbacks. They give zero warnings. With them, an echo of 1 MiB (about 2 million host calls) takes about 150 ms. For large IR, this cost is important. Then do one of these (builder's call):
  - Ask for a fix in `bytecodealliance/wasmtime-dotnet` for the IL3050 in `ValueRaw.Converter<T>`, and use typed callbacks after the fix. They are 8 times faster.
  - Write a small `LibraryImport` binding for the three callbacks on the Wasmtime C API.
- Keep compiled modules with `Module.Serialize` and `Module.Deserialize`. Use the module SHA-256 and the engine settings as the key. Large guests then do not compile again on each run.
- The fuel value is still an open point in the PRD (100M or 1B). Wasmtime counts about one fuel unit for each WASM operator.

`morphir` (Native AOT):

- Reference `Wasmtime` with `ExcludeAssets="native"`. Add `DirectPInvoke Include="wasmtime"` and `NativeLibrary` for the `libwasmtime.a` of the RID.
- The build downloads the Wasmtime C API archive of the same version as the NuGet package for each release RID, and checks its SHA-256. The C API version and the NuGet version must always be the same.
- This spike verified only `osx-arm64`. The release RIDs are `linux-x64`, `linux-arm64`, `win-x64` and `osx-arm64`. The static link on Linux and Windows can need more system libraries. Verify each RID in the release matrix.
- If the static link fails for one RID, put the native library beside the binary in the release archive for that RID. Then the binary is not one file on that RID.
- Size: the static link adds about 10.6 MB. With today's 18.2 MB, the estimate is about 28.8 MB. This is above the 20 MB guideline in `.agents/aot-optimization.md`. The PRD accepts this. Record the measured size in step 2a. A custom `libwasmtime` build with fewer Cargo features can make it smaller. This spike did not measure that.
- The managed single-file target (`PublishSingleFile` without AOT) must set `IncludeNativeLibrariesForSelfExtract=true`. The first start extracts 23.5 MB to the bundle folder (`DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

`dotnet-morphir` (dotnet tool):

- Make it a RID-specific tool: set `RuntimeIdentifiers` in `src/Morphir.Tool/Morphir.Tool.csproj`. `dotnet pack` then makes one pointer package and one package for each RID.
- Use the six RIDs that the `Wasmtime` package has: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64` and `win-arm64`. A user on another RID (for example `linux-musl-x64`) cannot install the tool. The documentation must say this.
- A RID-specific package is about 8.5 MB. A portable tool is 51 MB and installs all six native libraries (235 MB in the tool store).
- RID-specific tools need the .NET 10 SDK to install. The tool already targets `net10.0`.
- The release workflow must push the pointer package and all RID packages.
- The change that does this must update the install documentation and the release notes. This is a PRD rule.

Later steps:

- Wasmtime does not run Extism plugins by itself. Step 5 (`extism-kernel-v1`) must add the Extism kernel and the host functions on Wasmtime, or add `libextism`. Decide this in step 5.
- Add a link to this page in the "Evidence" section of the PRD.
