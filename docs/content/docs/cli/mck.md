---
title: "mck"
linkTitle: "mck"
weight: 85
description: "Run the separately installed Morphir Compatibility Kit, morphir-mck"
---

# mck

`morphir mck <args>` runs `morphir-mck <args>`, the Morphir Compatibility Kit (MCK), the way `dotnet morphir` runs
`dotnet-morphir`. The kit is a development tool from [finos/morphir](https://github.com/finos/morphir), installed
separately. This CLI does not include it and installs nothing: without `morphir-mck`, `morphir mck` only says where it
looked.

The commands and options of the kit are those of `morphir-mck`. See its own help with `morphir mck --help` or
`morphir-mck --help`.

## Synopsis

```text
morphir mck [<argument>...]
dotnet morphir mck [<argument>...]
```

Every entry point routes `mck` the same way: the Native AOT `morphir`, the managed `morphir` and `dotnet-morphir`.
This pass-through uses `MorphirApplication.Run`. A custom host that parses through `CreateApp` first gets
System.CommandLine's help and response-file expansion before the routing action.

## What reaches the tool

- **Arguments.** Every argument after `mck`, in order and unchanged. The CLI does not read them, so `--help`,
  `--version` and `@file` go to the tool.
- **Working directory and environment.** The CLI's own.
- **Standard input, output and error.** The CLI's own handles, so pipes, redirection and terminal colours behave as for
  `morphir-mck` run directly. The CLI writes nothing to standard output when it routes.
- **Exit code.** `morphir mck` exits with the exit code of `morphir-mck`.

## How morphir-mck is found

1. If `MORPHIR_MCK` is set and not empty, it must be the absolute path of the tool. A relative path is refused, so the
   result never depends on the current directory. On Windows it must be an `.exe` file. On Linux and macOS it must
   have an execute permission.
2. Otherwise the folders of `PATH` are searched in order. Only absolute folders count: empty and relative entries,
   `.` included, are skipped. The first folder that holds `morphir-mck.exe` (Windows) or an executable `morphir-mck`
   (Linux and macOS) wins.

The current directory and the folder of `morphir` itself are never searched. On Windows, scripts such as
`morphir-mck.cmd` are not candidates, because they would run through a shell.

## When the tool is missing

```text
$ morphir mck run
morphir mck runs the Morphir Compatibility Kit, morphir-mck, which is installed separately: it is a development
tool from finos/morphir, and this CLI does not include it. MORPHIR_MCK is not set, and no absolute folder on PATH
holds morphir-mck. ...
$ echo $?
127
```

The message goes to standard error, and standard output stays empty.

## Interrupts and termination

- **Ctrl+C** reaches `morphir-mck` directly. The tool decides how to stop, and `morphir mck` waits for it and exits
  with its code.
- **Windows.** Before it starts the tool, `morphir` joins a job object that ends every process in it when `morphir`
  ends. If `morphir` is killed, for example from Task Manager or by a CI runner, the tool and every process it started
  stop too. This also stops a process that the tool meant to leave running after it exits.
  If the job cannot be joined, the CLI warns on standard error and continues without that kill-on-close protection.
- **Linux and macOS.** SIGTERM or SIGHUP to `morphir` stops the tool's process tree, and `morphir` exits with 143.
  SIGKILL to `morphir` cannot be passed on, so the tool keeps running in that case.

## Exit codes

| Code | Meaning |
| --- | --- |
| The tool's code | `morphir-mck` ran. |
| 126 | The resolved file could not be started. |
| 127 | No tool to run: none found, or `MORPHIR_MCK` was refused. |
| 143 | Linux and macOS: `morphir` received SIGTERM or SIGHUP and stopped the tool. |

## Only mck is routed

`mck` is the only command that runs another executable. Any other unknown command, such as `morphir lint`, is a
command-line error, even when an executable `morphir-lint` is on `PATH`.
