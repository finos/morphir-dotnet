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

### Standalone Executable

The standalone `morphir` is one file that carries its .NET runtime, so it needs no .NET SDK or runtime. It is a
managed, trimmed, self-contained single-file build without an F# frontend, not a Native AOT executable. Download
`morphir-linux-x64`, `morphir-linux-arm64`, `morphir-osx-arm64` or `morphir-win-x64.exe` from the
[releases page](https://github.com/finos/morphir-dotnet/releases). To compile F# with it, also install the
F# frontend extension:

```bash
dotnet tool install --global Morphir.Frontend.FSharp.Extension --prerelease
```

### Release Archives (not published yet)

The build can pack four offerings of the same release. Releases do not publish these archives yet, and no
platform is qualified yet. Each archive holds its files at the root; extract it anywhere, including a path with
spaces, and run `morphir` (`morphir.exe` on Windows) from there.

| Offering | Archive | Needs on the machine | F# |
|---|---|---|---|
| `native-aot` | `morphir-dotnet-<release>-native-aot-<rid>.zip` or `.tar.gz` | Nothing from .NET | Through the F# frontend extension (`--extension`) |
| `self-contained` | `morphir-dotnet-<release>-self-contained-<rid>.zip` or `.tar.gz` | Nothing from .NET | Built in |
| `framework-dependent` | `morphir-dotnet-<release>-framework-dependent-<rid>.zip` or `.tar.gz` | The .NET 10 runtime (`Microsoft.NETCore.App` 10.0) for the same architecture | Built in |
| `dotnet-tool` | `Morphir.Tool.<release>.nupkg` | The .NET SDK 10.0 to install it, and the .NET 10 runtime to run it | Built in |

#### When the framework-dependent `morphir` cannot find .NET

The framework-dependent `morphir` cannot start without the .NET runtime, so it cannot explain the problem itself.
The .NET host prints this instead and exits with code `0x80008083` (-2147450749):

```text
You must install .NET to run this application.

App: <folder>\morphir.exe
Architecture: arm64
App host version: 10.0.14
.NET location: Not found

Learn more:
https://aka.ms/dotnet/app-launch-failed

Download the .NET runtime:
https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=arm64&rid=win-arm64&os=win10&apphost_version=10.0.14
```

To fix it, do one of these:

1. Install the .NET 10 runtime for the architecture that the message names, from the download link it prints.
2. If .NET is installed in a folder of your own, set `DOTNET_ROOT` (or `DOTNET_ROOT_ARM64`, `DOTNET_ROOT_X64`) to
   that folder. The host also looks in the registered install location and the default one
   (`C:\Program Files\dotnet` on Windows).
3. Use the `self-contained` or `native-aot` archive instead; neither needs an installed .NET.

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

### "You must install .NET to run this application"

A framework-dependent `morphir` found no .NET 10 runtime. See
[When the framework-dependent `morphir` cannot find .NET](#when-the-framework-dependent-morphir-cannot-find-net).

### Version Conflicts

If you encounter version conflicts, check your `global.json` file and ensure you're using the correct .NET SDK version.

