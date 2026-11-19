---
title: "Extension settings"
linkTitle: "Extensions"
weight: 45
description: "The [extensions.<name>] table: which keys exist, which ones a command reads, and how a WASM module is checked"
---

# Extension settings

Each extension has a table named after it, at any configuration level:

```toml
[extensions.python]
path = "/opt/morphir/extensions/morphir-python-binding.wasm"
abi = "extism-kernel-v1"
sha256 = "10acafa0c79d7f0d3a1c43f2f3b3c4b2f8c25c20c1a2a5f8e3c2b1a0f9e8d7c6"
```

| Key | Type | Read today by |
| --- | --- | --- |
| `path` | string | The WASM module used by experimental Python compile/generate commands. |
| `abi` | `"extism-kernel-v1"` or `"morphir-bytes-v1"` | The WASM module loader. |
| `sha256` | 64 lowercase hexadecimal digits | The WASM module loader. |
| `url` | string | Nothing. |
| `command` | string | Nothing. |
| `args` | array of strings | Nothing. |
| `enabled` | boolean | [`morphir validate`](../cli/validate.md#configuration). `false` turns off the analyzer with that id. |
| `config` | table | Nothing. |

Configuration validation checks the type of every key in the table, and the form of `abi` and `sha256`. Other
keys in the table are kept and not checked. `abi` and `sha256` are local additions to the shared Morphir
configuration schema, proposed upstream. `tests/Fixtures/Configuration/PROVENANCE.md` records them.

## A WASM module

`path` names a WASM module. The host reads the file and decides from its bytes what it is. The file name and
its extension play no part: a module called `binding.txt` loads, and a text file called `binding.wasm` does not.

Experimental `morphir compile --language python` and `morphir generate --target python`
use the configured Python module. See the [pinned Python walkthrough](../getting-started/python-wasm.md).
`morphir extension list` does not list configured modules yet ([#565](https://github.com/finos/morphir-dotnet/issues/565)).

Give `path` as an absolute path or relative to the configuration file that supplies it.
The settings resolver resolves it before passing the module to the loader.

[WASM extensions](wasm-extensions.md) says how the host runs a module once it loads: one instance for each
session, and the memory, instruction and time bounds of each call. A session can hold twice the memory ceiling,
512 MiB by default, because the guest and the Extism kernel each have a memory of their own.

### Declare the ABI

`abi` names the profile you expect the module to have. The host still classifies the module from its imports.
The declaration only confirms the result and never changes it. If the two differ, the module is refused.

This host runs `extism-kernel-v1` modules only. `morphir-bytes-v1` is a valid value, because it is a profile
of the contract, but the host refuses a module of that profile whether you declare it or not.

### Declare the digest

`sha256` pins the exact bytes. The host computes the SHA-256 of the file before it looks at the content, and
refuses any other bytes. A module replaced on disk is then refused, even when the new file is a valid module.
Without `sha256`, the host reads, hashes and classifies the file again on every load, so a replaced module
runs as its new bytes.

Write the digest in lowercase, with no `sha256:` prefix:

```sh
sha256sum morphir-python-binding.wasm
```

```powershell
(Get-FileHash morphir-python-binding.wasm -Algorithm SHA256).Hash.ToLowerInvariant()
```

## Checks and failures

The host checks a configured module in this order. The first check that fails decides the diagnostic, so a
declared digest that differs is reported even for a file that is not a module.

| Check | Diagnostic | What to do |
| --- | --- | --- |
| A file exists at `path` | `The WASM module of extension 'python' cannot be loaded: there is no file at '<path>'.` | Correct `path`. |
| The file can be read | `The WASM module of extension 'python' at '<path>' cannot be read: <reason>` | `path` names a directory, a file you may not read, or an invalid path. Name the module file. |
| The bytes match `sha256` | `... is refused: its SHA-256 is <actual>, but the configuration declares sha256 = "<declared>".` | If you replaced the module on purpose, update `sha256` to the new digest. If not, restore the module you pinned. |
| The bytes are a WASM module | `The file '<path>' of extension 'python' is not a WASM module (SHA-256 <digest>): <engine report>` | `path` names another file, or the download is damaged. |
| The engine supports the module's features | `extension.wasm_feature_unsupported` | The module uses a shared, 64-bit or second memory, or a proposal the engine does not enable. Use a build of the extension without it. |
| The module conforms to its profile and this host runs it | `extension.abi_unsupported` | The message names the import outside the profile, the missing `handle` export, or the profile this host does not run. A `morphir-bytes-v1` module needs an `extism-kernel-v1` release of the extension. |
| A declared `abi` equals the classified profile | `extension.abi_mismatch` | The message names both profiles. Correct `abi`, or remove it. |

Each message after the read check states the module's SHA-256. Only the last three checks have a code: the
WASM execution profile contract defines those refusal codes, and it defines none for the file checks.

A bad value for `abi` or `sha256` fails configuration validation with `config.validation`, naming the key, for
example `extensions.python.sha256`. The message is `Value is not one of the permitted choices.` for `abi` and
`String does not match the required pattern.` for `sha256`.

## Guest output

The loader writes nothing to stdout or stderr. It returns every diagnostic as a value to the command that
called it. Guest log messages and guest stdout and stderr will go to the host's stderr, bounded per session,
and never to stdout. The WASI sandbox and the worker mode that capture that output are not built yet.
