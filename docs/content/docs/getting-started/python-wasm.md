---
title: "Python WASM extension (experimental)"
linkTitle: "Python WASM extension"
weight: 45
description: "Compile and generate Python with the pinned Python extension in the .NET CLI"
---

This experimental integration runs the Python **0.6.0** extension inside the .NET
CLI. It uses the ordinary compile records, model checks and generated-file
publication. No Python interpreter or extension worker process is required.

Use a CLI build containing this feature and check `morphir extension runtimes --json`.
The engine is supplied for Windows ARM64, macOS x64/ARM64 and glibc Linux x64/ARM64.
The CLI's existing runtime, SDK and native-library prerequisites still apply.
Windows x64 currently refuses WASM with
`extension.runtime_unavailable` and `win_x64_trap_unsupported`, tracked in
[#609](https://github.com/finos/morphir-dotnet/issues/609). Commands using built-in
frontends or backends still work there. No protection setting should be changed.

From a new project directory, download and verify the published module with
PowerShell:

```powershell
New-Item -ItemType Directory -Force .morphir/extensions | Out-Null
Invoke-WebRequest 'https://github.com/finos/morphir-python/releases/download/extension/python/v0.6.0/morphir-python-binding-0.6.0.wasm' -OutFile .morphir/extensions/python.wasm
if ((Get-FileHash .morphir/extensions/python.wasm -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c') { throw 'Python module digest mismatch' }
```

Create `morphir.toml`:

```toml
[project]
name = "acme/demo"

[extensions.python]
path = ".morphir/extensions/python.wasm"
abi = "extism-kernel-v1"
sha256 = "ce0cba07f0395ff8179b42eb021ade05c68197f69b8e9c0c8bc369d2a035727c"
```

Create `identity.py`:

```python
def identity(value: int) -> int:
    return value
```

Compile it, then generate Python from the successful compile record:

```console
morphir compile --project . --language python --input identity.py --ir-version 4 --json
morphir generate --project . --target python --output generated --json
```

To generate from an existing IR file instead:

```console
morphir generate --project . --input morphir-ir.json --target python --output generated --json
```

The language or target selects the matching `extensions.python.path` when no
extension is selected explicitly. `--extension` takes precedence, followed by
the existing `frontend.<language>.extension` or `codegen.<target>.extension`
setting. A matching module table uses WASM; other selections keep the process
transport. A relative module path is resolved from the configuration file that
set it. The CLI checks the configured digest and ABI before opening a session.

JSON results stay on stdout; guest diagnostics go to stderr. The runtime retains
its [session bounds](../configuration/wasm-extensions.md). A guest failure does
not publish a partial generated tree. This feature does not qualify a platform
or the extension against the conformance kits.

The pinned frontend supports a limited Python subset. Unsupported expressions,
including addition in this pin, produce source diagnostics and exit code 1.
They do not publish IR. The host does not substitute another Python compiler.
