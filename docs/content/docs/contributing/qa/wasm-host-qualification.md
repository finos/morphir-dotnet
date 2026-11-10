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
| Stdio, Ctrl+C and the private directory | `ExtensionWorkerProcess.cs`, called from `Morphir.Program.Run` before the CLI host is built |
| Service tests | `tests/Morphir.Tooling.Tests/Features/Extensions/ExtensionWorker*Tests.cs` |
| Process tests, both executables | `tests/Morphir.E2E.Tests/Features/ExecutableExtensionWorker.*` |

The worker relays messages; it is not a second MEP host (design WRK-2). It does not negotiate, and it
looks inside a message only for its `id` and `method`. Its events and exit statuses follow the pinned
contract's `worker` section. When the contract and the MoonBit worker's README differ, the contract
wins.

To run the end-to-end tests against both executables, build `src/Morphir` and `src/Morphir.Tool` in
Release and run the E2E assembly twice: once with `MORPHIR_EXECUTABLE_PATH` set to
`src/Morphir/bin/Release/net10.0/morphir` (`.exe` on Windows), once with
`MORPHIR_EXECUTABLE_TYPE=tool`. `MORPHIR_WORKER_CORPUS=all` drives every case of the pinned corpus
through the worker in `ExecutableExtensionWorkerTests`, not only the sample. On Windows the Ctrl+C
and Ctrl+Break tests send the console control event through a hidden Windows PowerShell helper that
attaches to the worker's console; no window opens.

## Running it

```console
dotnet build src/Morphir/Morphir.csproj -c Release
src/Morphir/bin/Release/net10.0/morphir mck wasm-host run \
  --kit conformance/suites/wasm-host --host /path/to/host \
  --host-profile morphir-bytes-v1 --offering native-cli --platform <rid> --engine "<engine>" \
  --report .dev/wasm-host.json
src/Morphir/bin/Release/net10.0/morphir mck wasm-host report-check \
  --kit conformance/suites/wasm-host --report .dev/wasm-host.json
```

Give `--host-profile morphir-bytes-v1` only for a host that runs the byte profile; the .NET host runs
`extism-kernel-v1` only. A complete run takes a few minutes, mostly in the runaway cases, which wait
for the 10- and 30-second call deadlines.

## Reading a result

- A failure names the matcher of the corpus's conventions that failed (`exitCode`, `stdoutBytes`,
  `stdoutFrames`, `events`, `noEvents`, `guestOutput`, `guestOutputIncludes`, `noGuestOutput`,
  `withinSeconds`) or a convention check (`started.moduleSha256`, `started.bounds`,
  `fault.members`).
- A run with alternatives passes on one alternative that applies to the host; the report names it in
  `satisfied`, and a failure names its alternative.
- Record each difference as a runner defect, a contract question or a host defect before you compare
  another host. The first run, against the MoonBit native CLI, is in
  `openspec/changes/add-wasm-extension-host/evidence/1.5-moonbit-run.md`.
- The report holds no qualification verdict until parent task 1.4 defines one. Do not read the exit
  status of `run` as a qualification.
