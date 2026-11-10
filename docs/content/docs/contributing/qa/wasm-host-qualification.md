---
title: "WASM host qualification (internal)"
linkTitle: "WASM host qualification"
weight: 60
description: "The internal, experimental mck wasm-host commands that run the crafted WASM host cases against a host's worker mode"
---

# WASM host qualification

`morphir mck wasm-host run` and `morphir mck wasm-host report-check` are **internal and
experimental**. They are in both executables, `morphir` and `dotnet-morphir`, so that a contributor
can run the crafted cases of the `wasm-host` suite against any host that has a worker mode: the
MoonBit native CLI, and the .NET host through `morphir extension worker` (task 4.1 of
`add-wasm-extension-host`). They are not a user feature: their options and report can change in any
release, and the compatibility-kit change will replace them with its general `mck` command. The
reference is [morphir mck wasm-host]({{< relref "/docs/cli/mck-wasm-host" >}}).

## What runs where

| Part | Location |
| --- | --- |
| The cases (generated Gherkin) | `conformance/suites/wasm-host/`, from `tests/Fixtures/MepWasm/wasm-host.cases.json` |
| The worker adapter, comparer, judge, report and check | `src/Morphir.Conformance/WasmHost/` (`Worker.fs`, `Comparer.fs`, `Judge.fs`, `HostRunner.fs`, `HostReport.fs`, `Commands.fs`) |
| The commands | `src/Morphir.Tooling/Features/Mck/` |
| The tests, with a scripted fake worker | `tests/Morphir.Conformance.Tests/`, `tests/Morphir.Conformance.FakeWorker/` |
| The generic guest kit's model tier through a worker (no command) | `src/Morphir.Conformance/GuestKit/WorkerSessions.fs`, `tests/Morphir.E2E.Tests/Features/ExecutableGuestKitModelTierTests.cs` |

## The .NET worker

`morphir extension worker` is the worker mode of this repository's host, in both `morphir` and
`dotnet-morphir`. It is **internal and experimental**, and it exists for one purpose: so that a runner
can drive the host's WASM runtime (`src/Morphir.Extensions.Wasm`) with crafted modules over stdio, the
way it drives the MoonBit worker. Product commands do not use it: a command that runs a WASM extension
opens a `HostSession` on the same `WasmChannel` in process. The reference is
[morphir extension worker]({{< relref "/docs/cli/extension-worker" >}}).

| Part | Location |
| --- | --- |
| Arguments, help and defaults | `src/Morphir.Tooling/Features/Extensions/WorkerInvocation.cs` |
| The relay, the start-up order and the exit statuses | `src/Morphir.Tooling/Features/Extensions/ExtensionWorker.cs`, `WorkerMessage.cs` |
| The stderr events | `src/Morphir.Tooling/Features/Extensions/WorkerReport.cs` |
| The build identity, and the engine identity it covers | `WorkerBuildIdentity.cs`, `WasmRuntimeIdentity.cs` |
| Stdio, Ctrl+C and the private directory | `ExtensionWorkerProcess.cs`, called from `MorphirApplication.Run`, where every entry point starts, before the CLI host is built |
| Service tests | `tests/Morphir.Tooling.Tests/Features/Extensions/ExtensionWorker*Tests.cs` |
| Process tests, both executables | `tests/Morphir.E2E.Tests/Features/ExecutableExtensionWorker.*` |

The worker relays messages; it is not a second MEP host (design WRK-2). It does not negotiate, and it
looks inside a message only for its `id` and `method`. Its events and exit statuses follow the pinned
contract's `worker` section. When the contract and the MoonBit worker's README differ, the contract
wins.

To run the end-to-end tests against each executable, build `src/Morphir`, `src/Morphir.Managed` and
`src/Morphir.Tool` in Release and run the E2E assembly once for each: with no variable for the Native
AOT `morphir` in `artifacts/executables/<rid>/`, with `MORPHIR_EXECUTABLE_PATH` set to
`src/Morphir/bin/Release/net10.0/morphir` or `src/Morphir.Managed/bin/Release/net10.0/morphir`
(`.exe` on Windows), and with `MORPHIR_EXECUTABLE_TYPE=tool`. `MORPHIR_WORKER_CORPUS=all` drives every case of the pinned corpus
through the worker in `ExecutableExtensionWorkerTests`, not only the sample. On Windows the Ctrl+C
and Ctrl+Break tests send the console control event through a hidden Windows PowerShell helper that
attaches to the worker's console; no window opens.

## Running it

The commands are internal and experimental. Build the runner once; any build of `morphir` or
`dotnet-morphir` can be the runner, and it does not have to be the host it drives.

```console
dotnet build src/Morphir/Morphir.csproj -c Release
```

Against a .NET executable, here the Native AOT `morphir` published for `win-arm64`:

```console
src/Morphir/bin/Release/net10.0/morphir mck wasm-host run --kit conformance/suites/wasm-host \
  --host artifacts/executables/win-arm64/morphir.exe \
  --offering native-aot --platform win-arm64 --engine "wasmtime 50.0.1" \
  --report .dev/wasm-host-native-aot.json
src/Morphir/bin/Release/net10.0/morphir mck wasm-host report-check --kit conformance/suites/wasm-host \
  --report .dev/wasm-host-native-aot.json
```

Against the MoonBit native CLI, built from finos/morphir-moonbit as its worker README says:

```console
src/Morphir/bin/Release/net10.0/morphir mck wasm-host run --kit conformance/suites/wasm-host \
  --host /path/to/moonbit/_build/native/release/build/finos/morphir/morphir.exe \
  --host-profile morphir-bytes-v1 --offering native-cli --platform win-x64 \
  --engine "moonrockz/extism 0.3.0 (WAMR)" --report .dev/wasm-host-moonbit.json
src/Morphir/bin/Release/net10.0/morphir mck wasm-host report-check --kit conformance/suites/wasm-host \
  --report .dev/wasm-host-moonbit.json
```

The two command lines differ in the host path, the three identity inputs (`--offering`,
`--platform`, `--engine`) and `--host-profile`. Nothing else changes, and the runner sends both hosts
the same worker arguments. Give `--host-profile morphir-bytes-v1` only for a host that runs the byte
profile. The .NET host runs `extism-kernel-v1` only, so its ten byte-profile cases are
`not_applicable`. Take the engine from `extension runtimes --json` of the .NET executable you drive,
and the offering from the four names of `add-deployment-and-acquisition` (`native-aot`,
`self-contained`, `framework-dependent`, `dotnet-tool`). A development build is recorded under the
offering it would ship as. The platform is the host executable's, so the x64 MoonBit build under
emulation on Windows ARM64 is `win-x64`.

On a Windows ARM64 machine a run against a .NET executable takes 6 to 12 seconds. The Wasmtime fuel
budget ends the spinning Extism guest after about 3 seconds. A run against the MoonBit CLI takes about
40 seconds, mostly waiting for the 10-second deadline of the byte profile and for its instruction
budget to run out after about 25 seconds.

## The generic guest kit's model tier through the worker

The generic guest kit (`tests/Fixtures/MepGuestKit/`) has its own runner. Its model tier runs as a
library, `ModelTierRunner.RunAsync` in `src/Morphir.Conformance/GuestKit/ModelTier.fs`, which asks its
caller for a `HostSession` for each session. `WorkerSessions`
(`src/Morphir.Conformance/GuestKit/WorkerSessions.fs`) starts a worker for each of those sessions with
the corpus's worker arguments,
`<host> extension worker --abi extism-kernel-v1 --module <wasm> --build-identity <id> --json`. It
reads the build identity from the host's `--help --json`, gives the runner a `HostSession` over the
worker's stdio and keeps the worker's stderr events. `EndAsync` waits for each worker to exit, stops
the process tree of one that does not, and reports each worker's exit status, events and problems.

This is internal and experimental, and there is no command for it. The end-to-end test
`ExecutableGuestKitModelTierTests` runs it for the published Python `0.6.0` guest against the
executable the E2E run selects. It covers the model tier only. The host does not speak MEP `0.1`, so
the kit's legacy tier is not run, and the result is not a complete run of the generic guest kit. The
maintainer has not decided who runs the kit's model tier against this host, so this test is not cited
as that run.

A worker that has ended cannot be written to. A later request, or the `morphir.exit` of its session,
then fails with an `IOException` that `ModelTierRunner` does not catch. `EndAsync` still reports the
worker.

## Reading a result

- A failure names the matcher of the corpus's conventions that failed (`exitCode`, `stdoutBytes`,
  `stdoutFrames`, `events`, `noEvents`, `guestOutput`, `guestOutputIncludes`, `noGuestOutput`,
  `withinSeconds`) or a convention check (`started.moduleSha256`, `started.bounds`,
  `fault.members`).
- A run with alternatives passes on one alternative that applies to the host; the report names it in
  `satisfied`, and a failure names its alternative.
- Record each difference as a runner defect, a contract question or a host defect before you compare
  another host. The runs against the MoonBit native CLI are in
  `openspec/changes/add-wasm-extension-host/evidence/1.5-moonbit-run.md`, and the runs against the
  four .NET executables in `evidence/4.3-worker-runs.md`.
- The report holds no qualification verdict until parent task 1.4 defines one. Do not read the exit
  status of `run` as a qualification.
