---
title: "Installation"
linkTitle: "Installation"
weight: 1
description: "Install Morphir .NET on your system"
---

## Requirements

- .NET SDK 10.0 or higher
- Mono (for Linux/macOS)

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
is not a single file. For `win-arm64` it has 239 files, 120,956,062 bytes in
all, and its zip archive is 47,382,148 bytes.

| Platform | Self-contained F# frontend |
| --- | --- |
| Windows ARM64 (`win-arm64`) | Verified on Windows 11 ARM64 without .NET installed |
| Windows x64 (`win-x64`) | Builds; ran under x64 emulation on Windows 11 ARM64 only, not qualified |
| Linux glibc x64 (`linux-x64`) | Builds; not run, not qualified |
| Linux glibc ARM64, macOS x64, macOS ARM64 | Not qualified |

The operating system must still meet the .NET 10 runtime requirements. On
Linux these include glibc and ICU (`libicu`), which the folder does not
contain; this is not verified yet.

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

