---
title: "morphir extension runtimes"
linkTitle: "extension runtimes"
weight: 90
description: "Report whether this host can run WASM extensions, with its engine, profiles and bounds, or the reason it cannot"
---

# morphir extension runtimes

Reports the extension runtimes of the host you run. Today there is one, `wasm`: the runtime that runs
WASM extension modules. The report says whether it is available in this executable, which engine runs
the modules, which profiles the host runs and which bounds it enforces. When the runtime is unavailable,
the report gives the reason.

## Synopsis

```console
morphir extension runtimes [--json]
dotnet-morphir extension runtimes [--json]
```

**Options:**
- `--json`: Write one JSON document on stdout.

## Description

The command loads no module. It asks the executable you run, not its file name or its install folder,
so a copy of `morphir` that lost its engine library reports that copy's state.

The host checks the engine only when something needs it: this command, or a command that runs a WASM
module. It checks once per process. Starting `morphir`, `--version`, `--help`, `extension list` with
only built-in extensions, and every other command that needs no WASM never load the engine. A missing
or broken engine therefore cannot break them.

The command exits with 0 whether the runtime is available or not: the report is the result. Logs and
diagnostics go to stderr, so `morphir extension runtimes --json | jq` works.

## Output

An available runtime:

```console
$ morphir extension runtimes
Host: morphir
Platform: win-arm64
wasm: available
  Engine: wasmtime 50.0.1
  Profiles: extism-kernel-v1
  Bounds: memory ceiling 268435456 bytes (256 MiB), call deadline enforced, instruction budget enforced
```

```json
{
  "host": "morphir",
  "platform": "win-arm64",
  "runtimes": [
    {
      "kind": "wasm",
      "available": true,
      "engine": { "name": "wasmtime", "version": "50.0.1" },
      "profiles": ["extism-kernel-v1"],
      "bounds": { "memoryBytes": 268435456, "deadline": "enforced", "budget": "enforced" }
    }
  ]
}
```

An unavailable runtime, here a `dotnet-morphir` whose engine library is not in its folder:

```console
$ dotnet-morphir extension runtimes
Host: dotnet-morphir
Platform: win-arm64
wasm: unavailable (engine_missing)
  Engine: wasmtime 50.0.1
  Reason: the engine's native library is not in this installation
```

```json
{
  "host": "dotnet-morphir",
  "platform": "win-arm64",
  "runtimes": [
    {
      "kind": "wasm",
      "available": false,
      "reason": "engine_missing",
      "engine": { "name": "wasmtime", "version": "50.0.1" }
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `host` | The command that wrote the report: `morphir` or `dotnet-morphir`. The Native AOT `morphir` and the managed `morphir` both report `morphir`. |
| `platform` | The platform the process runs on, named as the engine package names its native folders: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, or another name such as `linux-musl-x64` for a platform without an engine. |
| `runtimes[].kind` | `wasm`. |
| `runtimes[].available` | `true` when this executable can run a WASM module. |
| `runtimes[].reason` | Only when `available` is `false`: why. See [Reasons](#reasons). |
| `runtimes[].engine` | The engine this build carries, by name and version. It is the version of the Wasmtime package the build references, also when the engine cannot load. |
| `runtimes[].profiles` | Only when available: the WASM execution profiles the host runs. This host runs `extism-kernel-v1` only. |
| `runtimes[].bounds` | Only when available: `memoryBytes` is the ceiling of each linear memory of a session, and `deadline` and `budget` say that the call deadline and the instruction budget are enforced. The values are the defaults of the pinned WASM execution profile contract. |

The report has no member for the deployment offering (`native-aot`, `self-contained`,
`framework-dependent` or `dotnet-tool`). The host identity now records named release profiles, but this report has not yet added those fields. It does not guess a deployment from the file name or path. A runner that needs the
offering is given it as an input.

## Reasons

| Reason | What it means | What you can do |
| --- | --- | --- |
| `platform_unsupported` | The engine has no build for this platform, for example Linux with musl (Alpine). | Use a supported platform for WASM extensions. Every other command works here. |
| `engine_missing` | The engine's native library (`wasmtime.dll`, `libwasmtime.so` or `libwasmtime.dylib`) is not where the host looks for it. A copy, an archive or a package lost the file. | Reinstall or extract the release again, keeping every file beside the executable, including the `runtimes` folder of the dotnet tool. |
| `engine_load_failed` | The library is there but did not load: it is for another architecture, it is damaged or of another version, or one of its own dependencies is missing. | Reinstall the release for your platform and architecture. If that does not help, report it with the output of this command. |

There are no other reasons. No setting or release switch turns the WASM runtime off: if an engine
defect is found after a release, it is fixed in a patch release, and until then you avoid it by not
configuring a WASM extension. No other command loads the engine.

The host decides the reason the first time something needs the engine: this report, loading a module,
or listing a WASM extension. `--version`, `--help` and the worker's argument and build-identity checks
never load it.

## Commands that need the runtime

A command that needs the WASM runtime in a host without it fails with `extension.runtime_unavailable`
before it reads the module, and names the host, the platform and the reason. It never shows a native
library loading error. Today the only such command is the internal
[`morphir extension worker`](../extension-worker/). It checks its arguments, then the build identity,
then the engine, then the module, and reports the first that fails. Without a usable engine it writes
one `refused` event with the code `extension.runtime_unavailable` on stderr, nothing on stdout, and
exits with 3:

```json
{"profile":"morphir/command-event","contractVersion":"0.1.0","command":"extension worker","sequence":1,"event":"refused","error":{"code":"extension.runtime_unavailable","message":"The WASM module of extension 'python' at '/work/python.wasm' cannot be loaded with extension.runtime_unavailable: the WASM runtime of dotnet-morphir 0.4.0 on linux-musl-x64 is unavailable (platform_unsupported): the engine has no build for this platform."}}
```

In `extension list`, a `wasm` extension has `available` set to `false` in a host whose runtime is
unavailable, and built-in extensions stay available. See [extension list](../management/#extension-list).

## See also

- [morphir extension list](../management/#extension-list)
- [morphir extension worker](../extension-worker/) (experimental and internal)
