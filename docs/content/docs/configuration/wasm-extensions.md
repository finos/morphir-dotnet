---
title: "WASM extensions"
linkTitle: "WASM extensions"
weight: 46
description: "How the host runs a WASM extension: one guest instance per session, and the memory, instruction and time bounds of each call"
---

# WASM extensions

The host runs WASM extensions of the `extism-kernel-v1` profile: an Extism guest that speaks the Morphir
Extension Protocol (MEP) `0.2.0-draft.1`. [Extension settings](extensions.md) says how to name a module and
how the host checks it before it runs.

No command loads a configured module in this release. Today only the internal worker mode,
[`morphir extension worker`](../cli/extension-worker.md), runs a module. The bounds below are the ones every
session gets, and the ones the first product commands will use.

## One instance for each session

Each session gets its own instance of the guest and its own instance of the Extism kernel, in a store of its
own. Two sessions of the same module share no memory and no state. A guest that fails ends its own session
only.

## Bounds of a session

| Bound | Default | When a guest reaches it |
| --- | --- | --- |
| Memory ceiling | 256 MiB for each linear memory | See [Memory](#memory). |
| Instruction budget | 5,000,000,000 units for each call | The call stops with `extension.budget_exhausted`. |
| Call deadline | 30 seconds for each call | The call stops, and the session ends with outcome `deadline`. |

The worker takes `--memory-pages`, `--instructions` and `--call-ms` to set other values for one run. A page is
64 KiB, so the default ceiling is 4096 pages.

## Memory

An Extism session has two linear memories: the guest's own memory, and the memory of the Extism kernel, which
holds the guest's input, its output and what it allocates through the kernel. The ceiling applies to each of
them separately. **A session's linear memory can therefore reach twice the ceiling: 512 MiB with the default
of 256 MiB.** The code the engine compiles from the module, and the host's own memory, come on top of that.
Plan for twice the ceiling for each WASM session that can run at the same time.

A memory cannot grow past its ceiling. The engine refuses the growth, and the guest decides what happens next:

- **The guest stops with a trap.** If the guest's memory or the kernel's memory is at its ceiling when the call
  ends, the session fails with `extension.memory_limit`.
- **The guest reports the failure itself**, for example with an Extism error or a non-zero result. The session
  fails with that failure, `extension.incomplete_session`, even when a memory is at its ceiling.
- **The guest handles the refusal and answers.** The call succeeds.

Every failure diagnostic states both memory sizes and the ceiling, so you can see which memory was full:

```text
extension.memory_limit: the guest failed at the memory ceiling (the guest trapped, with the guest's memory at its ceiling): <what the engine reported>; guest memory 256 MiB (268435456 bytes), kernel memory 1 MiB (1048576 bytes), ceiling 256 MiB (268435456 bytes)
```

A module whose memory starts above the ceiling does not start. It fails with `extension.memory_limit` before
any guest code runs.

In worker mode the same failure is a `fault` event on stderr with kind `memory-limit`, and the worker exits
with status 1.

If an extension needs more memory, raise the ceiling for it. Each memory can then grow to the new ceiling, so
the session can reach twice the new value.
