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

Both hosts generate F# with their built-in backend by default. A separate F#
backend extension, `Morphir.CodeGeneration.FSharp.Extension` (command
`morphir-fsharp-backend`), is for MEP hosts that have no built-in F# backend.
`morphir generate` runs it only when you select it, with `--extension` or
`[codegen.fsharp].extension`. See
[F# generation]({{< relref "/docs/backends/fsharp" >}}).

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

