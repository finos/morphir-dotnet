# Spike: the .NET tool as platform-specific packages with a fallback

Task 5.4 of the OpenSpec change `add-wasm-extension-host` asks first whether the pinned SDK (10.0.403) can
publish a framework-dependent .NET tool as one package per platform that has a WASM engine, plus a fallback
package with no engine, under one package id. This folder answers that with a throwaway tool. The result is
in `openspec/changes/add-wasm-extension-host/evidence/5.4-tool-packages.md`.

The spike is not in `Morphir.slnx`, and its `Directory.Build.props` and `Directory.Build.targets` keep the
repository's build settings and central package management out.

| File | What it does |
| --- | --- |
| `PlatformProbe/` | The tool, command `spike-platform-probe`. It references `Wasmtime` 50.0.1. `--report` prints one JSON line: the runtime identifier, the engine libraries in the tool's directory, and whether a Wasmtime engine can be created. |
| `pack.ps1` | Packs the tool into a local folder feed: two complete versions, a partial third, two other arrangements, and an inventory (`packages.json`). |
| `test.ps1` | Installs, runs, updates and uninstalls the tool from that feed, globally with `--tool-path` and locally with a tool manifest, runs it once with `dotnet dnx`, and writes `result.json` and `result.md`. It touches nothing outside its work directory. |

The workflow `.github/workflows/tool-platform-packages.yml` packs on Linux x64 and runs `test.ps1` on Linux
x64 (glibc) and in an Alpine container (musl).

Locally:

```powershell
./spikes/extension-model/tool-platform-packages/pack.ps1 -OutDirectory <scratch>/pack
./spikes/extension-model/tool-platform-packages/test.ps1 -Feed <scratch>/pack/feed -WorkDirectory <scratch>/work -Machine local
```
