# Proposal

## Why

The .NET `morphir` host cannot run the extensions the rest of the ecosystem publishes. The Python, Avro, OpenAPI and Rust bundles are Extism WASM modules. The Rust CLI and the MoonBit CLI run them; this repository has no WASM runtime at all.

The runtime was designed in finos/morphir as the change `add-dotnet-wasm-extension-host` (draft PR [finos/morphir#1148](https://github.com/finos/morphir/pull/1148)), because the same change defines a contract that every host shares. This repository's [extension-model PRD](../../../docs/content/contributing/design/prds/morphir-extension-model.md) approves no step for implementation until its own OpenSpec change is approved. This is that change. It adopts the parent requirements, maps them onto this codebase, and records four decisions the maintainer made on 9 November 2026 that override older text here.

## What Changes

Follow one module through the host. A developer has the published Python extension, one 4.7 MB Extism module, and starts it by hand:

```text
morphir extension worker --abi extism-kernel-v1 --module morphir-python-binding.wasm --json

module bytes -> classify imports -> one store under limits -> one handle call per MEP message
                     |                      |                            |
              refuse unknown         256 MiB per memory,          guest asks for a file or
              imports by name        30 s, instruction budget     the network: it gets nothing
```

Each arrow is work this change adds:

- **A WASM extension runtime for `extism-kernel-v1` guests**, on Wasmtime, in a new project that both CLI hosts use. It classifies a module from its imports before any guest code runs, gives each session its own store, and applies the memory ceiling, the call deadline and the instruction budget of the shared profile contract.
- **The Extism kernel as an embedded WASM module.** The host links the kernel module that MoonBit ships (`moonrockz/extism`, Apache-2.0) under `extism:host/env` on Wasmtime. It does not ship `libextism` or the Extism .NET SDK. A proving slice comes first: `morphir.initialize` on the published Python module under a Native AOT build, with zero IL2026 and IL3050 warnings and no suppressions. If the proof fails, work stops and the decision returns for review.
- **A sandbox with a fixed policy.** No file, network, environment or configuration access. `http_request` fails the session without a connection. The variable store and IR cache are bounded and die with the session.
- **Negotiated notifications.** An Extism guest receives `morphir.initialized` and `morphir.exit` only after it advertises, in its `morphir.initialize` result, that it accepts notifications. Published bundles advertise nothing and receive none.
- **A message channel under the MEP host session.** Today the host session reads and writes two streams. It gains a channel that carries one whole message per call, and stream framing becomes one implementation of it. Process guests keep their behavior.
- **`morphir extension worker`**, an internal and experimental stdio mode with the MoonBit worker's argument shape, in both executables. It is how a conformance runner drives a .NET-hosted guest, and the only way to reach the runtime in this change.
- **A runtime availability report** and the failure `extension.runtime_unavailable`. An offering or platform without the engine says so when asked; it does not fail with a library loading error in the middle of a command.
- **Packaging in all four offerings** (`native-aot`, `self-contained`, `framework-dependent`, `dotnet-tool`) on six platform pairs. `Morphir.Tool` becomes a set of platform-specific packages with a fallback package that has no engine.
- **The crafted-case runner for host qualification**, in `src/Morphir.Conformance`. The cases and their fixed expectations stay in finos/morphir as data; this repository pins them and runs them through a host's worker mode.

The four decisions of 9 November 2026:

1. **Extism only.** The host runs `extism-kernel-v1` and does not host `morphir-bytes-v1`. It still classifies a byte-profile module, so that it can refuse it with `extension.abi_unsupported` and name the profile. This reverses the PRD's order (byte profile in step 2a, Extism in step 5): Extism comes first and the byte profile never.
2. **Kernel on Wasmtime**, as described above.
3. **Negotiated notifications**, as described above.
4. **The conformance runner's home is the .NET host, in this repository.** New runner code goes into `src/Morphir.Conformance`, not into the Rust `morphir mck`.

This change does not add a product command that selects a WASM analyzer, frontend or generator, user-granted host capabilities, extension installation, folder discovery or an on-disk compiled-module cache. Those consume this runtime and are separate changes.

## Capabilities

### New Capabilities

- `dotnet-wasm-host`: what the .NET host does with a WASM extension. It adopts the parent capability `extensions/dotnet-wasm-host` and binds the host to the pinned profile contract: availability per offering and platform, loading a module from configuration, module identity, isolation per session, cancellation, the memory report, guest output, the worker mode and packaging evidence.
- `wasm-host-qualification`: the runner side of the parent capability `mck/wasm-host-qualification`. It reads the pinned crafted corpus, drives a host through its stdio worker and writes a report whose verdict can be recomputed.

### Modified Capabilities

- `extension-registry`: the delivery of an entry is no longer fixed to `built-in`. `wasm` joins the value set, and a `wasm` entry is unavailable where the WASM runtime is.
- `mep-protocol`: a host session runs over a message channel. The same lifecycle and negotiation rules apply whether the guest answers on a framed stream or one message per call, and the single-message rules for replies, notifications and cancellation are stated for the host side.

The shared rules themselves (import tables, policy, bounds, failure codes) are not copied into this repository's specs. They stay in the parent capability `mep/wasm-execution-profiles`, and this repository pins the contract by commit, as it already pins the MEP contract.

## Tracking

- Parent change: `openspec/changes/add-dotnet-wasm-extension-host/` in finos/morphir, draft PR [finos/morphir#1148](https://github.com/finos/morphir/pull/1148), read at `bb9d15c3b`. The parent stays the source of truth. This change covers its tasks 2.1 to 6.2 and the runner parts of 1.5 and 4.3, with their numbers kept.
- GitHub issue: [finos/morphir-dotnet#563](https://github.com/finos/morphir-dotnet/issues/563). Related: [#326](https://github.com/finos/morphir-dotnet/issues/326) (Extism integration design, from an earlier plan) and [#558](https://github.com/finos/morphir-dotnet/issues/558) (deployment and acquisition). This change does not claim to close either.
- Roadmap: the extension-model PRD, step 2a (runtime part) and step 5. Task 0.1 updates the PRD for decision 1.
- Sibling changes in this repository: `add-deployment-and-acquisition` ([#559](https://github.com/finos/morphir-dotnet/pull/559), not merged) owns the four offerings and their publish profiles, and modifies the same `extension-registry` requirement. `establish-morphir-compatibility-kit` owns the general `morphir mck` command and has not started.
- This repository has no Beads database. Tracking is GitHub issues and OpenSpec.

## Impact

- **New code:** `src/Morphir.Extensions.Wasm` (runtime) and its tests and Native AOT probe; a channel in `src/Morphir.Mep`; `extension worker` and `extension runtimes` in `src/Morphir.Tooling`, registered in both entry points; a worker adapter and crafted-kit reader in `src/Morphir.Conformance`; a `wasm` delivery in `src/Morphir.Extensions`.
- **First native dependency.** NuGet `Wasmtime` 50.0.1 and the Wasmtime C API archive of the same version, pinned by SHA-256. The spike measured about 10.6 MB added to the Native AOT executable, on macOS arm64 only. Static linking on the other five platforms is unproven and is tried before other packaging work.
- **Embedded third-party bytes.** The Extism kernel module from `moonrockz/extism` at the version finos/morphir-moonbit pins (`0.3.0` today), with its licence and notice.
- **User-visible packaging change.** `Morphir.Tool` changes from one platform-neutral package to platform-specific packages plus a fallback. It needs release notes and updated installation guidance in the same release.
- **Pinned inputs.** The profile contract, the crafted corpus and its modules are copied from finos/morphir at a recorded commit. The parent is a draft and is about to be revised, so the pin moves at least once before qualification.
- **Platforms.** Windows x64 and ARM64, macOS x64 and ARM64, Linux glibc x64 and ARM64. Linux musl reports the WASM runtime as unsupported. Browser hosting, the Component Model and WIT are out of scope.
- **No behavior change for existing commands.** Process guests keep their stream channel, no configuration key changes meaning, and no existing command reaches the runtime.
