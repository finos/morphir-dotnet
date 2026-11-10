---
title: "Installation"
linkTitle: "Installation"
weight: 1
description: "Install Morphir .NET on your system"
---

## Requirements

What morphir-dotnet needs from the machine depends on the offering. A .NET runtime is what runs a managed program; a
.NET SDK is the larger download that also builds and installs one. Each offering states its needs in
`morphir extension list --json` (`deployment` and `prerequisites`, see
[extension list]({{< relref "/docs/cli/management#extension-list" >}})):

| Offering | To run it | To install it | Released |
| --- | --- | --- | --- |
| Native AOT `morphir` | Nothing from .NET | Nothing | Not yet as this offering |
| Self-contained `morphir` (carries its own .NET runtime) | Nothing from .NET | Nothing | Not yet |
| Framework-dependent `morphir` | The .NET runtime `Microsoft.NETCore.App` 10.x | Nothing | Not yet |
| `dotnet-morphir` (`Morphir.Tool` dotnet tool) | The .NET runtime `Microsoft.NETCore.App` 10.x | A .NET SDK 10.0.100 or later, for `dotnet tool install` | Yes, on NuGet |

The `morphir-<rid>` executables that releases attach today are a trimmed single-file build that carries its runtime,
so they need nothing from .NET either; no named release profile produces them, so they report no `deployment`. Building
from source needs the .NET SDK that `global.json` names.

## Installation Methods

### Global Tool Installation

Install the Morphir CLI as a global .NET tool. The command is `dotnet-morphir`:

```bash
dotnet tool install --global Morphir.Tool --prerelease
```

### Native Executable

The native executable `morphir` is one file and needs no .NET SDK. Download
`morphir-linux-x64`, `morphir-linux-arm64`, `morphir-osx-arm64` or `morphir-win-x64.exe` from the
[releases page](https://github.com/finos/morphir-dotnet/releases). To compile F# with it, also install the
F# frontend extension:

```bash
dotnet tool install --global Morphir.Frontend.FSharp.Extension --prerelease
```

The extension tool needs the .NET runtime. On a machine without .NET, use the
self-contained frontend folder instead. Releases do not publish it yet; see
[Compile without .NET installed]({{< relref "/docs/getting-started/compile-fsharp#compile-without-net-installed" >}})
for how to build it and what it supports: compiling supported F# source files
to Morphir IR, not building `.fsproj` projects. The folder is not trimmed and
is not a single file. For `win-arm64` it is about 120 MB, and its zip archive
about 47 MB. The exact file count and sizes of a build are in the log of the
build target `PublishFSharpFrontendProcess`, which prints them for the folder
and the archive.

| Platform | Self-contained F# frontend |
| --- | --- |
| Windows ARM64 (`win-arm64`) | Checked on Windows 11 ARM64 with the machine's .NET hidden from the frontend (no `dotnet` on `PATH`, no `DOTNET_ROOT`, no global install); not run on a machine with no .NET at all |
| Windows x64 (`win-x64`) | Builds; ran under x64 emulation on Windows 11 ARM64 only, not qualified |
| Linux glibc x64 (`linux-x64`) | Builds; not run, not qualified |
| Linux glibc ARM64, macOS x64, macOS ARM64 | Not qualified |

The operating system must still meet the .NET 10 runtime requirements. On
Linux these include glibc and ICU (`libicu`), which the folder does not
contain; this is not verified yet.

Both hosts generate F# with their built-in backend. A separate F# backend
extension, `Morphir.CodeGeneration.FSharp.Extension` (command
`morphir-fsharp-backend`), is for other MEP hosts that have no built-in F#
backend; `morphir generate` does not use it. See
[F# generation]({{< relref "/docs/backends/fsharp" >}}).

### The morphirup toolchain manager

Releases also attach `morphirup`, the standalone toolchain manager, as `morphirup-linux-x64`,
`morphirup-linux-arm64`, `morphirup-osx-arm64` and `morphirup-win-x64.exe`, each also in a
`morphirup-<rid>-v<version>.tar.gz` archive, with the checksum file `morphirup-v<version>.sha256`. It is one
Native AOT file that needs neither .NET nor ICU. The Linux files need glibc 2.35 or later (Ubuntu 22.04, Debian 12 or
later) and nothing else from the machine, and do not run on musl distributions such as Alpine. Each archive holds
`LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.txt` beside the executable, and the release carries the notices as
`morphirup-v<version>-THIRD-PARTY-NOTICES.txt`. This first release reads toolchain documents and prints plans; it
does not download, install, update or activate a toolchain, and it does not verify signed metadata. The files are not
code-signed, and the macOS file is not notarized. See
[Download and verify]({{< relref "/docs/cli/morphirup#download-and-verify" >}}) for the commands and for what the
release job checks.

### Local Project Installation

Add the Morphir libraries to your project:

```bash
dotnet add package Morphir.Core
```

### Build from Source

```bash
git clone https://github.com/finos/morphir-dotnet.git
cd morphir-dotnet
dotnet build
```

## Verify Installation

Check that Morphir is installed correctly:

```bash
dotnet-morphir --version
```

Then follow [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}).

## Troubleshooting

### Command Not Found

If the `dotnet-morphir` command is not found after installation:

1. Ensure the .NET tools directory is in your PATH:
   ```bash
   export PATH="$PATH:$HOME/.dotnet/tools"
   ```

2. Restart your terminal or run:
   ```bash
   source ~/.bashrc  # or ~/.zshrc
   ```

### Version Conflicts

If you encounter version conflicts, check your `global.json` file and ensure you're using the correct .NET SDK version.

