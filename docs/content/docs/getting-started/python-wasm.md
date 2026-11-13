---
title: "Python WASM extension (experimental)"
linkTitle: "Python WASM extension"
weight: 45
description: "Compile and generate Python with the pinned Python extension in the .NET CLI"
---

This experimental feature runs the Python **0.6.0** extension inside the Morphir CLI. It compiles a small Python
file to Morphir IR and generates Python from it. You don't need Python installed.

Check that your CLI can run WASM extensions with `morphir extension runtimes --json`. It can on Windows ARM64, macOS
and Linux (glibc). Windows x64 can't run WASM extensions yet
([#609](https://github.com/finos/morphir-dotnet/issues/609)); the other commands still work there.
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

The `[extensions.python]` table makes `--language python` and `--target python` use the module. The CLI checks the
module's `sha256` before it runs it. JSON results go to stdout and the extension's messages to stderr. If the
extension fails, `generate` writes no files. The [WASM extension reference]({{< relref "/docs/configuration/wasm-extensions" >}})
explains how the CLI picks an extension and the limits it applies.

The extension supports a small part of Python. Code it doesn't support, including addition, gives a source
diagnostic and exit code 1, and no IR.