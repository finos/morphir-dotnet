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

Both hosts generate F# with their built-in backend. A separate F# backend
extension, `Morphir.CodeGeneration.FSharp.Extension` (command
`morphir-fsharp-backend`), is for other MEP hosts that have no built-in F#
backend; `morphir generate` does not use it. See
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

### "You must install .NET to run this application"

A framework-dependent `morphir`, or `dotnet-morphir`, found no .NET runtime, or none of a version it can run on. See
[When the framework-dependent `morphir` cannot find .NET](#when-the-framework-dependent-morphir-cannot-find-net).

### Version Conflicts

If you encounter version conflicts, check your `global.json` file and ensure you're using the correct .NET SDK version.

## Release archives (not published yet)

You cannot download these yet: releases do not publish them, and no release or platform is qualified for them.
This section describes what the build packs, so that the troubleshooting below has something to refer to. Use the
installation methods above.

The build can pack four offerings of the same release. Each archive holds its files at the root; extracted anywhere,
including a path with spaces, it runs as `morphir` (`morphir.exe` on Windows).

| Offering | Artifact | Needs on the machine | F# |
|---|---|---|---|
| `native-aot` | `morphir-dotnet-<release>-native-aot-<rid>.zip` or `.tar.gz` | Nothing from .NET | Through the F# frontend extension (`--extension`) |
| `self-contained` | `morphir-dotnet-<release>-self-contained-<rid>.zip` or `.tar.gz` | Nothing from .NET | Built in |
| `framework-dependent` | `morphir-dotnet-<release>-framework-dependent-<rid>.zip` or `.tar.gz` | The .NET 10 runtime (`Microsoft.NETCore.App` 10.0) for the same architecture | Built in |
| `dotnet-tool` | `Morphir.Tool.<release>.nupkg`, as this build packs it | The .NET SDK 10.0 to install it, and the .NET 10 runtime to run it | Built in |

The `dotnet-tool` row is the package that this build packs from the current source. It is not the `Morphir.Tool`
package already on NuGet that the [global tool installation](#global-tool-installation) installs.

The operating-system prerequisites of these archives are not established: the minimum glibc version on Linux,
whether ICU and OpenSSL libraries must be installed, the minimum macOS version, and what Gatekeeper does with a
downloaded, unsigned executable. Only "needs nothing from .NET" and "needs the .NET 10 runtime" have been checked, on
one win-arm64 build machine.

### When the framework-dependent `morphir` cannot find .NET

The framework-dependent `morphir`, like `dotnet-morphir`, cannot start without a .NET runtime, so it cannot explain
the problem itself. The .NET host prints the message instead. There are two cases.

**No .NET found.** The host prints "You must install .NET to run this application." This Windows example comes from
the .NET 10.0.14 host on Windows ARM64, where it exited with `0x80008083` (-2147450749):

```text
You must install .NET to run this application.

App: <folder>\morphir.exe
Architecture: arm64
App host version: 10.0.14
.NET location: Not found

The following locations were searched:
  Application directory:
    <folder>\
  Environment variable:
    DOTNET_ROOT_ARM64 = <not set>
    DOTNET_ROOT = <not set>
  Registered location:
    HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\arm64\InstallLocation = <not set>
  Default location:
    C:\Program Files\dotnet

Learn more:
https://aka.ms/dotnet/app-launch-failed

Download the .NET runtime:
https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=arm64&rid=win-arm64&os=win10&apphost_version=10.0.14
```

**Another .NET version found.** More common is a machine with .NET, but not a version this `morphir` can run on. The
host then prints "You must install or update .NET to run this application.", the framework and version it needs, and
the frameworks it found. This Windows example was made by asking for a version the machine does not have; it exited
with `0x80008096` (-2147450730):

```text
You must install or update .NET to run this application.

App: <folder>\morphir.exe
Architecture: arm64
Framework: 'Microsoft.NETCore.App', version '11.0.0' (arm64)
.NET location: <dotnet folder>

The following frameworks were found:
  10.0.13 at [<dotnet folder>\shared\Microsoft.NETCore.App]
  10.0.14 at [<dotnet folder>\shared\Microsoft.NETCore.App]

Learn more:
https://aka.ms/dotnet/app-launch-failed

To install missing framework, download:
https://aka.ms/dotnet-core-applaunch?framework=Microsoft.NETCore.App&framework_version=11.0.0&arch=arm64&rid=win-arm64&os=win10
```

The released framework-dependent `morphir` asks for `Microsoft.NETCore.App` 10.0.0 with the default roll-forward:
any installed 10.0 patch serves (the newest one is used), and so does a later 10.x minor when no 10.0 patch is
installed. .NET 9 or .NET 11 alone does not.

On Linux and macOS the messages name Unix paths, and the "Registered location" line names
`/etc/dotnet/install_location` instead of the registry. A shell there sees only the low 8 bits of the exit code:
131 for "no .NET" and 150 for "another version". These Linux and macOS details follow from the host's documented
behavior and are not verified here.

The host looks for .NET in this order: the application folder (where a self-contained application carries it), the
`DOTNET_ROOT_<ARCH>` and `DOTNET_ROOT` variables, the registered location, and the default location.

| Operating system | Registered location | Default location |
|---|---|---|
| Windows | `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` | `C:\Program Files\dotnet` |
| Linux | `/etc/dotnet/install_location`, or `/etc/dotnet/install_location_<arch>` | `/usr/share/dotnet`, or `/usr/lib/dotnet` for some distribution packages |
| macOS | `/etc/dotnet/install_location`, or `/etc/dotnet/install_location_<arch>` | `/usr/local/share/dotnet` |

An x64 `morphir` running under emulation on an ARM64 machine (Windows or macOS) looks for the x64 .NET in the `x64`
subfolder of the default location, for example `C:\Program Files\dotnet\x64` or `/usr/local/share/dotnet/x64`, and
reads `DOTNET_ROOT_X64`. Only the Windows row was observed here, in the host's own output on Windows ARM64; the Linux and macOS rows and the emulation subfolder follow the host's documented behavior and are not verified here.

To fix it, do one of these:

1. Install the .NET 10 runtime for the architecture that the message names, from the download link it prints.
2. If .NET is installed in a folder of your own, set `DOTNET_ROOT` (or `DOTNET_ROOT_ARM64`, `DOTNET_ROOT_X64`) to
   that folder.
3. Use the `self-contained` or `native-aot` archive instead; neither needs an installed .NET.
