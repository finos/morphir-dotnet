---
title: "morphir extension worker (experimental)"
linkTitle: "extension worker"
weight: 91
description: "Experimental and internal: serve one WASM extension module over stdio for a conformance runner"
---

# morphir extension worker

> **Experimental and internal.** The worker exists so that a conformance runner can qualify this
> host's WASM runtime with the crafted cases of the WASM execution profile contract (OpenSpec change
> `add-wasm-extension-host`, task 4.1). Product commands do not use it, and it is not in the help of
> `morphir extension`. Its arguments and events follow the pinned contract, which is still a draft,
> so they can change in any release. Contributors: see
> [WASM host qualification]({{< relref "/docs/contributing/qa/wasm-host-qualification" >}}).

## Synopsis

```console
morphir extension worker --abi <profile> --module <path> --build-identity <hex>
    [--request-bytes <n>] [--response-bytes <n>] [--call-ms <n>] [--instructions <n>]
    [--memory-pages <n>] [--json]
morphir extension worker --help [--json]
```

Both executables have it: `morphir` and `dotnet-morphir`, and so does the managed `morphir` built from
`src/Morphir.Managed`, which is not published yet. Each starts the worker before it builds the rest of the
command line, so the worker starts fast and nothing but MEP frames reaches stdout.

## Description

The worker loads one module and relays MEP messages to it. Each `Content-Length` framed message on
stdin is one `handle` call of the module. The reply to a request is written as one frame on stdout. A
notification gets no frame, and reaches the guest only if the guest asked for notifications in its
`morphir.initialize` result. The worker does not negotiate the protocol, and does not check the
JSON-RPC version or ids: the runner at the other end is the MEP host.

The worker keeps reading until the module's instance ends, at `morphir.exit` or at a fault, and then
exits by itself. A runner keeps stdin open until then.

Before it serves, the worker checks the build identity (before the module file is read), then loads
the module: it reads the file, computes its SHA-256, validates and compiles it, classifies it against
the pinned contract and admits it with the declared ABI. Then it opens one instance under the bounds
and writes `started`.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `--abi <profile>` | required | The declared profile, `extism-kernel-v1` or `morphir-bytes-v1`. This host runs `extism-kernel-v1` only and refuses a `morphir-bytes-v1` module. A declaration only confirms the profile the imports give. |
| `--module <path>` | required | The module file. A relative path is resolved against the current directory. |
| `--build-identity <hex>` | required | This executable's build identity, 64 hexadecimal digits, as `--help --json` reports it. Without it the worker serves nothing and exits with 2. Another identity is refused with `extension.worker_runtime_mismatch`. |
| `--request-bytes <n>` | `6291456` (6 MiB) | The largest request frame, its `Content-Length` header included. A larger frame is fault `failure`, and the guest is not called. |
| `--response-bytes <n>` | `16777216` (16 MiB) | The largest response frame, header included. A larger response is fault `failure`, and nothing of it reaches stdout. |
| `--call-ms <n>` | `30000` (`10000` for `morphir-bytes-v1`) | The deadline of each `handle` call, in milliseconds. |
| `--instructions <n>` | `5000000000` | The instruction budget of each call, in Wasmtime fuel units, from 1 to 9007199254740991, the range the contract states. |
| `--memory-pages <n>` | `4096` (256 MiB) | The ceiling of the guest's memory, in 64 KiB pages, from 1 to 65536. This host has one limit for every memory of a session, so it applies to the Extism kernel's memory too, as the contract allows. Below the default the two hosts differ: the MoonBit host leaves the kernel at 256 MiB. A value outside the range is a usage error, exit 2, before the module is read. |
| `--json` | off | Write the report as `morphir/command-event` lines. |
| `--help`, `-h` | | Write the help on stdout and serve nothing. With `--json`, write one `morphir/command-result` whose `result.buildIdentity` is the identity to pass. |

The defaults and the range of `--instructions` are the pinned contract's
(`tests/Fixtures/MepWasm/wasm-execution-profiles.json`, `worker.arguments`). The contract states no
other range; the others are this host's own.

## The build identity

The identity is the SHA-256 of a short text that names the pinned profile contract (id, version and
SHA-256), the host (`morphir` or `dotnet-morphir`), the release version with its build metadata (the
git commit of a CI or release build), the embedded Extism kernel (`moonrockz/extism` version and
SHA-256) and the engine (`wasmtime 50.0.1`). Two builds of one version and commit share an identity,
also when one of them has uncommitted changes.

```console
$ morphir extension worker --help --json
{"profile":"morphir/command-result","contractVersion":"0.1.0","command":"extension worker","state":"succeeded","exitCode":0,"result":{"help":"...","buildIdentity":"e4d9...531d"},"diagnostics":[]}
```

## Output

Stdout carries only `Content-Length` framed replies, in every case.

Stderr carries the report. With `--json` each event is one line:

```json
{"profile":"morphir/command-event","contractVersion":"0.1.0","command":"extension worker","sequence":2,"event":"fault","fault":{"kind":"trap","message":"extension.guest_trap: the guest trapped: ..."}}
```

| Event | Members after the envelope | When |
| --- | --- | --- |
| `started` | `abi`, `moduleSha256`, `buildIdentity`, `bounds` | The module has loaded. `bounds` has exactly `memoryCeiling`, `instructionBudget` and `callDeadline`, each `{"enforced": true}`: whether the bound is enforced, not its value. |
| `output` | `output`; or `output` and `droppedBytes` | Guest output of the call that just ended: WASI stdout and stderr, Extism and Morphir log messages, refused HTTP requests. The newest 64 KiB of a call are reported, each message cut to 4 KiB, then one event with `droppedBytes` for the rest. |
| `fault` | `fault.kind`, `fault.message` | The session failed. The kind is `trap`, `memory-limit`, `budget-exhausted`, `call-deadline` or `failure`. The message states the guest's memory size, the kernel's memory size and the ceiling. A fault has no code. |
| `refused` | `error.code`, `error.message` | A refusal before any request: `extension.worker_runtime_mismatch`, `extension.runtime_unavailable`, `extension.abi_unsupported`, `extension.abi_mismatch` or `extension.wasm_feature_unsupported`. The message of a module refusal names the file and its SHA-256. |
| `exited` | `exitCode` | Last, in every run that got past the arguments. |

The envelope is `profile`, `contractVersion`, `command` and `sequence` (from 1), then `event`. A
message in an event is cut to 4 KiB of JSON text and then ends with `…`.

No event carries the engine identity, with or without `--json`. The engine is part of the build identity, and a
runner is given it as an input; session diagnostics and the runtime report state it.
The contract fixes the
event names and their members. The rest of the `morphir/command-event` and `morphir/command-result`
documents, the help text and the usage-failure document follow the MoonBit CLI until a shared
contract states them.

Without `--json` each event is one text line that starts with `morphir extension worker: `, for
example `morphir extension worker: serving extism-kernel-v1 module 013d...e11e`.

A usage error serves nothing. With `--json` it is one `morphir/command-result` failure on stderr
(`error.code` `usage`); without it, one text line on stderr.

## A host without its WASM runtime

When this executable cannot run WASM modules, the worker checks that after the arguments and the build identity and before it reads the module. It writes one `refused` event with `extension.runtime_unavailable`, whose message names the host, the platform and the reason, writes nothing on stdout and exits with 3. `--help` and the identity check never load the engine. The reasons are those of [morphir extension runtimes](../extension-runtimes/#reasons). The pinned profile contract lists no such refusal code; the code comes from this host's capability.

## Faults before started

A module file that cannot be read, or that is not a well-formed WASM module, is fault `failure`
before `started`. So is a module whose initial memory is above the ceiling, with kind
`memory-limit`. Each exits with 1. None of them is a refusal.

## Message checks

The worker checks that each request and each response is UTF-8 text holding one JSON object.
Anything else is fault `failure`; a request that fails the check never reaches the guest, and a
response that fails it never reaches stdout.

## Interruption

Ctrl+C (SIGINT) stops the guest within one second, even inside a call that spins. The worker writes
no `fault` event, because the guest did not fail, and exits with 130. On Windows Ctrl+Break does the
same, because a parent can send only Ctrl+Break to a child that runs in its own process group.

## Private workspace

A guest that calls `morphir_get_workspace_info` gets an empty private directory. The worker creates
its own directory in the system's temporary directory (`morphir-extension-worker-<hex>`), the session
creates the guest's directory below it, and the worker removes both when it exits, also after a
fault or an interruption.

## Exit status

| Status | Meaning |
| --- | --- |
| 0 | The guest served until its instance ended. |
| 1 | A fault, an invalid frame, or stdin ended first. |
| 2 | Invalid arguments. |
| 3 | A refusal before any request. |
| 130 | Interrupted. |

## Example

```console
$ id=$(morphir extension worker --help --json | jq -r .result.buildIdentity)
$ morphir extension worker --abi extism-kernel-v1 --module ./extism-echo.wasm --build-identity "$id" --json
```
