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
MoonBit native CLI today, and the .NET host once `morphir extension worker` exists (task 4.1 of
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

## Running it

```console
dotnet build src/Morphir/Morphir.csproj -c Release
src/Morphir/bin/Release/net10.0/morphir mck wasm-host run \
  --kit conformance/suites/wasm-host --host /path/to/host \
  --host-profile morphir-bytes-v1 --offering native-cli --engine "<engine>" \
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
- Record each difference as a runner defect, a contract question or a host defect before you compare
  another host. The first run, against the MoonBit native CLI, is in
  `openspec/changes/add-wasm-extension-host/evidence/1.5-moonbit-run.md`.
- The report holds no qualification verdict until parent task 1.4 defines one. Do not read the exit
  status of `run` as a qualification.
