---
title: ".NET guest toolchain spike, 2026-11-07"
linkTitle: ".NET guest toolchain spike"
description: >
  The state of C# and F# compiled to WASM in November 2026, a C# and an F# morphir-bytes-v1 guest probe, and the decision for the runtime of the .NET guest SDK.
---

# .NET guest toolchain spike, 2026-11-07

Status: Decided. In step 2b, the .NET guest SDK targets only the `process` runtime (MEP over stdio). WASM guests for C# and F# come later. Do not start them before the conditions in [Decision](#decision) are true.

This spike is the ".NET guest toolchains" spike in [Spikes before step 2](../../prds/morphir-extension-model.md#spikes-before-step-2) of the Morphir extension model PRD. The probe sources are in `spikes/extension-model/dotnet-guest-toolchains/`.

**Note, 9 November 2026.** This spike tested guests against `morphir-bytes-v1`, which was the host's planned profile on 7 November. The maintainer has since decided that the morphir-dotnet host runs `extism-kernel-v1` only and refuses a `morphir-bytes-v1` module with `extension.abi_unsupported` (see [Runtimes](../../prds/morphir-extension-model.md#runtimes) in the PRD). The decision of this spike is unchanged: the .NET guest SDK targets the `process` runtime. A later .NET WASM guest would have to be an Extism module to run in this host. The findings below are unchanged; they record what the spike measured.

## The guest contract

The host loads a `morphir-bytes-v1` guest (upstream `finos/morphir-moonbit`, [`pkgs/morphir-host/worker/wasm/README.mbt.md`](https://github.com/finos/morphir-moonbit/blob/main/pkgs/morphir-host/worker/wasm/README.mbt.md) and [`profile.mbt`](https://github.com/finos/morphir-moonbit/blob/main/pkgs/morphir-host/worker/wasm/profile.mbt)). The guest must obey these rules:

- It is a core WASM module. It is not a WASI component.
- It imports only `morphir_bytes_v1.input_len: () -> i32`, `input_byte: (i32) -> i32` and `output_byte: (i32) -> ()`. The host refuses any other import, and WASI imports too, with `extension.abi_unsupported`.
- It exports `handle: () -> i32`. The result is the response length, 0 for exit, -1 for failure or -2 for a notification.
- It has one 32-bit memory that is not shared. The host sets the maximum to 4,096 pages (256 MiB). An initial memory above 4,096 pages fails with `extension.memory_limit`.
- The host calls only `handle`. It does not call `_initialize` or `_start`.

The `extism-kernel-v1` ABI permits WASI preview 1 imports, but morphir-dotnet hosts that ABI only in step 5.

## Reproduction

Environment: macOS 26.6 on arm64, .NET SDK 10.0.403, Wasmtime 49.0.3, wasm-tools 1.229.0, Binaryen 126, WASI SDK 29.0 (`arm64-macos`) and the Docker 29.9.3 client.

1. Build the IL of the two guests. This step works on any host:

   ```sh
   cd spikes/extension-model/dotnet-guest-toolchains/csharp-naot-llvm
   dotnet build -c Release -p:ProbeIlOnly=true -o ../il-csharp
   cd ../fsharp-naot-llvm
   dotnet build -c Release -p:ProbeIlOnly=true -o ../il-fsharp
   cd ..
   dotnet fsi inspect-il.fsx il-csharp/MorphirGuestProbe.dll
   dotnet fsi inspect-il.fsx il-fsharp/MorphirGuestProbeFs.dll
   ```

2. Publish a guest to WASM with NativeAOT-LLVM on macOS arm64:

   ```sh
   cd csharp-naot-llvm && dotnet publish -c Release
   ```

   The result on macOS arm64 is `error NU1101: Unable to find package runtime.osx-arm64.Microsoft.DotNet.ILCompiler.LLVM`.

3. On a Linux or Docker host, use `build-in-linux.sh`. It builds in a `linux/arm64` container, closes the module (step 4), prints the imports and exports, and runs one request and one `exit` call:

   ```sh
   ./build-in-linux.sh csharp-naot-llvm
   ./build-in-linux.sh fsharp-naot-llvm
   # If the runtime libraries do not link for wasip1:
   ./build-in-linux.sh csharp-naot-llvm -p:ProbeWasip2=true
   ```

   Run the script from a copy outside the repository, because it writes `.wasi-sdk-29` and `.nuget-linux` next to itself.

4. Check the post-link step with a stand-in guest. The stand-in is a C reactor that uses the same libc services as the NativeAOT runtime libraries:

   ```sh
   SDK=<path to wasi-sdk-29.0-arm64-macos>
   $SDK/bin/clang --target=wasm32-wasip1 --sysroot=$SDK/share/wasi-sysroot \
     -Oz -mexec-model=reactor postlink/standin.c -o standin.wasm
   WASM_TOOLS=wasm-tools BINARYEN_BIN=<binaryen>/bin \
     postlink/close-guest.sh standin.wasm standin.closed.wasm
   host/make-host.sh '{"jsonrpc":"2.0","id":7,"method":"initialize","params":{}}' req.wasm
   wasmtime run --preload morphir_bytes_v1=req.wasm --invoke handle standin.closed.wasm
   ```

   Remove the mise shims from `PATH` before you run `clang`. With `-Oz`, `clang` runs the first `wasm-opt` that it finds.

The test host `host/host.wat.in` is a core module that Wasmtime preloads as `morphir_bytes_v1`. It holds one request and writes each output byte to stdout. `host/echo-guest.wat` is a hand-written guest that checks the test host.

### What this machine could not do

- **The NativeAOT-LLVM build.** The `dotnet-experimental` feed has compiler host packages only for `linux-x64`, `linux-arm64` and `win-x64` (newest: `10.0.0-rc.1.26357.1`, built 2026-07-07). The `osx-x64` package stops at `9.0.0-alpha.1.24173.1`, and there was never an `osx-arm64` package. [dotnet/runtimelab#3279](https://github.com/dotnet/runtimelab/issues/3279) asks for one and is open. The `tools/ilc` file in the host package is a native Linux executable, so it does not run on macOS.
- **The Linux path.** Docker Desktop was already running, but its VM network was down. The log showed `dial 192.168.65.7:2376: no route to host` and `linuxkit/network tx dropped ... destination address required`, and each API call returned HTTP 500. The spike did not restart or reset Docker.
- **The Mono `wasi-experimental` workload.** The spike did not install it. The install changes the shared SDK. The command is `dotnet workload install wasi-experimental`. It also needs a WASI SDK in `WASI_SDK_PATH`. The output of this toolchain is a component (see [Results](#results)), so the build cannot give a module that the host loads.

Thus the module size and the run results of the .NET guests are "not measured on this machine".

### Linux run (added 2026-11-07, after Docker Desktop was restarted)

The main session ran `build-in-linux.sh` in a `linux/arm64` container (`mcr.microsoft.com/dotnet/sdk:10.0`, `Microsoft.DotNet.ILCompiler.LLVM` `10.0.0-rc.1.26357.1`, wasi-sdk 29), from a copy outside the repository.

| Build | Result |
| --- | --- |
| C#, wasip1 (default) | Native code is generated, then the link fails: the wasi-sdk linker still wraps the module as a component and stops with `failed to resolve import morphir_bytes_v1::input_byte`. |
| F#, wasip1 (default) | The same link failure. FSharp.Core adds IL2104 and IL3053. |
| C#, `-p:ProbeWasip2=true` | The link succeeds. Raw module: 2,072,765 bytes. It imports the three `morphir_bytes_v1` functions and about 30 WASI Preview 2 functions (`wasi:io`, `wasi:cli`, `wasi:clocks`, `wasi:filesystem`, `wasi:random`), and it exports `memory`, `_initialize`, `handle` and `cabi_realloc`. |
| F#, `-p:ProbeWasip2=true` | The link succeeds. Raw module: 4,128,207 bytes, with the same kind of imports. |
| Closing step, both | Fails. binaryen `wasm-merge` refuses the module because it uses non-trapping float-to-int instructions (`i64.trunc_sat_f64_u`) that the step does not enable. The WASI Preview 2 imports also need other stubs than the Preview 1 stubs in `postlink/wasi-stubs.wat`. |

No `handle` call ran, so the call checks in [Decision](#decision) are still open. The run confirms the decision: the raw modules depend on WASI Preview 2 and the component-model ABI, which the `morphir-bytes-v1` host does not provide.

## Results

### Toolchain summary

| Toolchain | Maturity, November 2026 | Core-module output | Module size | F# support | Runs a `handle` call |
| --- | --- | --- | --- | --- | --- |
| NativeAOT-LLVM (`Microsoft.DotNet.ILCompiler.LLVM`) | Experimental, in `dotnet/runtimelab`, not in the SDK. Last branch commit 2026-09-22. No compiler host for macOS. | Yes, after work: set `wasm32-unknown-wasip1` and `NativeLib=Shared`, then close the WASI imports and run `_initialize` from the start section | Not measured on this machine | The IL is correct. The F# compiler warns FS0202 for `UnmanagedCallersOnly`, but emits the attribute. The F# runtime behavior is not measured. | Not measured on this machine |
| Mono `wasi-experimental` workload | Experimental. The .NET 10 workload builds (10.0.12); the .NET 11 RC3 workload does not ([dotnet/runtime#136691](https://github.com/dotnet/runtime/issues/136691)) | No. The link adds `--component-type WasiHttpWorld`, so the output is a WASI P2 component | Not built | The Extism .NET PDK shows F# on this route, but it stays on .NET 8 ([extism/dotnet-pdk#133](https://github.com/extism/dotnet-pdk/issues/133)) | Not built |
| componentize-dotnet `0.8.0-preview00011` (2026-06-12) | Preview. It wraps NativeAOT-LLVM, so it has the same host limits (Windows and Linux) | No. It makes WASI P2 components | Not built | Not documented | Not built |
| `browser-wasm` (Mono, and CoreCLR in .NET 11 work) | Supported for browsers | No. The module needs the `dotnet.js` loader and its JavaScript imports | Not built | Yes, in a browser | Not applicable |
| CoreCLR on WASI (`wasihost`, R2R) in `dotnet/runtime` | In development on `main`, not released (for example [dotnet/runtime#134954](https://github.com/dotnet/runtime/issues/134954)) | No. It is a runtime host that loads assemblies, not one closed module | Not built | Not documented | Not built |
| NetWasm `0.11.2-preview.2` (2026-11-07) | Preview, from one author, first commit 2026-08. It has its own CoreLib (`netwasm0.1`) | Not yet in the necessary form. Its raw core output needs its own JavaScript adapter contract. A plain core module with imports that you select is "Planned" in its [roadmap](https://github.com/zion-sati/NetWasm/blob/main/docs/roadmap.md) | Not built | Not documented | Not built |
| `process` runtime (control) | Supported. Framework-dependent dotnet tool, MEP over stdio | Not applicable | Not applicable | Yes | Yes, see the [JSON-RPC library spike](jsonrpc.md) |

### NativeAOT-LLVM details

- **Packages.** `Microsoft.DotNet.ILCompiler.LLVM` and `runtime.<host>.Microsoft.DotNet.ILCompiler.LLVM` come from the `dotnet-experimental` feed. The [compile guide](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/compiling.md) says: "Compilation using NativeAOT-LLVM is currently only supported on Windows x64 and Linux x64". The feed also has `linux-arm64`. The [prerequisites](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/prerequisites.md) pin WASI SDK 29.
- **Component or core module.** `Microsoft.NETCore.Native.targets` sets `IlcLlvmTarget` to `wasm32-unknown-wasip2` for `wasi-wasm`, which links a component. The probe sets `wasm32-unknown-wasip1`. The other choice is to keep wasip2 and pass `-Wl,--skip-wit-component`. The Linux run must show which choice links.
- **WASI imports.** The runtime libraries in `runtime.wasi-wasm.Microsoft.DotNet.ILCompiler.LLVM` use these libc functions: `clock_gettime`, `nanosleep`, `getenv`, `environ`, `write`, `fwrite`, `arc4random_buf`, `open`, `read`, `mmap`, `abort` and `exit` (`llvm-nm -u` on `libPortableRuntime.a`, `libSystem.Native.a` and `libaotminipal.a`). Through wasi-libc, these become WASI preview 1 imports. The `morphir-bytes-v1` host refuses all of them. Thus a .NET guest needs a post-link step.
- **Initialization.** With `NativeLib=Shared`, the link uses `-mexec-model=reactor`, so the module exports `_initialize`. The host does not call it. The post-link step must run it from the start section, or the runtime must initialize itself on the first call to `handle`.
- **Known defects.** [dotnet/runtimelab#3247](https://github.com/dotnet/runtimelab/issues/3247) (open) reports thread-static corruption after a small number of export calls into a WASI component. A `morphir-bytes-v1` guest calls `handle` many times in one session, so the Linux run must call `handle` more than once.
- **Exceptions.** For `wasi`, the default exception model is `emulated`. Thus the module does not need the WASM exception-handling feature, which the upstream engine can refuse.

### Post-link step (stand-in guest, measured)

The post-link step (`postlink/close-guest.sh`) does three things:

1. It merges `postlink/wasi-stubs.wat` into the guest with `wasm-merge`. The stubs give no environment, a clock at 0, zero bytes for `random_get`, `EBADF` for `fd_write` and a trap for `proc_exit`.
2. It removes all exports except `handle` and `memory`.
3. It moves `_initialize` into the start section.

| Measurement | Result |
| --- | --- |
| Raw stand-in module (`-Oz`, wasm32-wasip1 reactor) | 81,485 bytes. It imports the three `morphir_bytes_v1` functions and 6 WASI functions, and exports `memory`, `_initialize` and `handle` |
| Closed stand-in module | 69,223 bytes. It imports only `morphir_bytes_v1.input_len`, `input_byte` and `output_byte`, exports `handle` and `memory`, and has a start section. `wasm-tools validate` passes |
| Request call (`{"jsonrpc":"2.0","id":7,...}`, 58 bytes) | The guest wrote the 58 bytes, and `handle` returned 58. The constructor ran before `handle` |
| `exit` call | `handle` returned 33 (the echo guest does not interpret `exit`) |

This shows that the post-link step is possible for a wasi-libc reactor. It does not show that the NativeAOT runtime works with these stubs. For example, the runtime can need a clock that moves.

### F# details

- The F# guest (`fsharp-naot-llvm/Guest.fs`) uses `extern` with `DllImport` and `WasmImportLinkage`, and a module function with `UnmanagedCallersOnly(EntryPoint = "handle")`. Its message type is a closed union.
- `dotnet build` gives FS0202: "This attribute is currently unsupported by the F# compiler." The IL has the attribute and the three P/Invoke methods with `WasmImportLinkage`. This matches [dotnet/fsharp#18445](https://github.com/dotnet/fsharp/issues/18445) and [dotnet/fsharp#14883](https://github.com/dotnet/fsharp/issues/14883). There, users report that the export works with Native AOT in spite of the warning.
- FSharp.Core under NativeAOT-LLVM is not measured. Do not use `printf`-family functions or `%A` in a guest. They use reflection. NativeAOT also prints a union differently ([dotnet/runtimelab#1365](https://github.com/dotnet/runtimelab/issues/1365)).

## Decision

**Decision: in step 2b, the .NET guest SDK targets only `process`. WASM guests for C# and F# come later.**

The evidence supports this decision without the module measurement:

1. No supported .NET toolchain makes a core module. The supported toolchains (Mono `wasi-experimental`, componentize-dotnet and `browser-wasm`) make components or modules that need JavaScript.
2. The only route to a core module is NativeAOT-LLVM. It is experimental, it is outside the .NET SDK, and its compiler does not run on macOS. Most guest authors on macOS would need Docker or a Linux CI job to build a guest.
3. A NativeAOT-LLVM module imports WASI. Each .NET guest needs a post-link step (WASI stubs and a start section) that the SDK would own and maintain.
4. F# compiles. But the export attribute gives a compiler warning, and FSharp.Core under NativeAOT-LLVM is not measured.

**The Linux run must show these items** to confirm that WASM is possible later. Do the run with `build-in-linux.sh` for C# and for F#:

- `dotnet publish` succeeds with wasip1, or with `ProbeWasip2=true`.
- After the post-link step, the module imports only the three `morphir_bytes_v1` functions, exports `handle: () -> i32` and `memory`, has one 32-bit memory that is not shared, and has an initial memory of 4,096 pages or fewer.
- The request call writes the JSON-RPC response and returns its length. The `exit` call returns 0.
- `handle` returns correct results for 100 calls or more in one instance (see dotnet/runtimelab#3247).
- Record the size of the raw module and the closed module.

**These results change the decision:**

- If the Linux run fails one of the checks above, WASM guests for .NET stay out of the roadmap until a supported toolchain makes core modules. Then step 2b and later steps use only `process`.
- If the Linux run passes, the decision stays "later". It becomes "now" only when these are also true:
  - Authors can build on macOS: a `runtime.osx-arm64` compiler package exists (dotnet/runtimelab#3279), or NativeAOT-LLVM moves into `dotnet/runtime`.
  - The F# guest passes the same checks.
  - The closed module is smaller than the 20 MB guideline in `.agents/aot-optimization.md`.
- Other changes that make the decision necessary again: NetWasm ships a plain core module with imports that you select, a supported .NET toolchain makes core modules, or morphir-dotnet hosts `extism-kernel-v1`. That ABI permits WASI preview 1 imports, so the stubs are not necessary.

## Consequences for step 2b

- The .NET guest SDK serves MEP over stdio, with `Content-Length` framing. Stdout carries frames only, and stderr carries logs. The C# and F# samples ship as framework-dependent dotnet tools, as the PRD says.
- Keep the SDK core independent of the transport. The core is one function from one request (bytes) to zero or one response (bytes). A framing adapter for stdio calls it. A later WASM adapter can call the same function from `handle`.
- Keep the core usable under NativeAOT-LLVM later. Use System.Text.Json with source generation, no reflection, no threads, and no `async` in the dispatch core. Put `async` only in the stdio adapter.
- Put the WASM export and imports in a C# part of the SDK, not in author code. Then F# authors do not write `UnmanagedCallersOnly`, and FS0202 does not occur in their code. NativeAOT-LLVM can export from a referenced assembly with `UnmanagedEntryPointsAssembly`.
- The step 2a sample analyzer stays in Rust or MoonBit, as the PRD says.
- Add an open point to the PRD: do the Linux run of this spike on a Linux host or in CI. Watch dotnet/runtimelab#3279 and #3247, dotnet/runtime#136691, the NetWasm roadmap and extism/dotnet-pdk#133.

## Evidence

- Upstream ABI: [finos/morphir-moonbit `pkgs/morphir-host/worker/wasm/README.mbt.md`](https://github.com/finos/morphir-moonbit/blob/main/pkgs/morphir-host/worker/wasm/README.mbt.md), [`profile.mbt`](https://github.com/finos/morphir-moonbit/blob/main/pkgs/morphir-host/worker/wasm/profile.mbt) and [the `core-wasm-modules` spec](https://github.com/finos/morphir-moonbit/blob/main/openspec/changes/run-core-wasm-guests-from-any-language/specs/extensions/core-wasm-modules/spec.md).
- NativeAOT-LLVM: [compiling.md](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/compiling.md), [prerequisites.md](https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/prerequisites.md), the [`dotnet-experimental` feed](https://dev.azure.com/dnceng/public/_artifacts/feed/dotnet-experimental/NuGet/Microsoft.DotNet.ILCompiler.LLVM), [#3279](https://github.com/dotnet/runtimelab/issues/3279), [#2119](https://github.com/dotnet/runtimelab/issues/2119), [#3247](https://github.com/dotnet/runtimelab/issues/3247) and [#1365](https://github.com/dotnet/runtimelab/issues/1365).
- Mono WASI: [dotnet/runtime#136691](https://github.com/dotnet/runtime/issues/136691), [".NET 10: No WASI" (July 2025)](https://henrikrxn.github.io/blog/Wasi-dotnet-10), [extism/dotnet-pdk](https://github.com/extism/dotnet-pdk) and [#133](https://github.com/extism/dotnet-pdk/issues/133).
- componentize-dotnet: [repository and README](https://github.com/bytecodealliance/componentize-dotnet).
- CoreCLR on WASI: [dotnet/runtime#134954](https://github.com/dotnet/runtime/issues/134954).
- NetWasm: [README](https://github.com/zion-sati/NetWasm), [targets and outputs](https://github.com/zion-sati/NetWasm/blob/main/docs/targets-and-outputs.md), [roadmap](https://github.com/zion-sati/NetWasm/blob/main/docs/roadmap.md) and [licensing](https://github.com/zion-sati/NetWasm/blob/main/docs/licensing.md). The compiler uses the NetWasm Community License 1.0, which is not an OSI license. Larger organizations must pay for it.
- F#: [dotnet/fsharp#18445](https://github.com/dotnet/fsharp/issues/18445) and [dotnet/fsharp#14883](https://github.com/dotnet/fsharp/issues/14883).
