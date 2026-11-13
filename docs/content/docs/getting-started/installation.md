---
title: "Installation"
linkTitle: "Installation"
weight: 1
description: "Install morphirup, the Morphir toolchain manager, then the older routes to the morphir CLI"
aliases:
  - /getting-started/installation/
---

Start with `morphirup`, the Morphir toolchain manager. It is one file and needs nothing else installed. It sets up
the Morphir toolchain for your project, which then compiles and generates. When it is installed, follow
[Your first model](../first-model/).

## Install morphirup

On macOS (Apple silicon) and Linux (x64, ARM64):

```sh
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
```

On Windows (x64, ARM64), in PowerShell:

```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```

The script installs the newest release for your machine and verifies the download first:

- On macOS and Linux it installs `~/.local/bin/morphirup`. If that folder is not on your `PATH`, the script prints
  the line to add to your shell profile.
- On Windows it installs `%LOCALAPPDATA%\Programs\morphirup\morphirup.exe` and adds the folder to your user `PATH`.

Open a new terminal and check that it runs:

```sh
morphirup --version
```

It prints the version you installed.

### Script options

| To | macOS and Linux | Windows |
| --- | --- | --- |
| Install a given release | `--version <version>` | `-Version <version>` |
| Install into another folder | `--install-dir <dir>` | `-InstallDir <dir>` |
| Leave `PATH` alone | The script never changes it | `-NoModifyPath` |

The versions are on the [releases page](https://github.com/finos/morphir-dotnet/releases). To pass an option:

```sh
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh -s -- --version <version>
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1))) -Version <version>
```

The environment variables `MORPHIRUP_VERSION` and `MORPHIRUP_INSTALL_DIR` work too, on every platform.

### Behind a proxy or firewall

The scripts need to reach `raw.githubusercontent.com` (for the script itself), `github.com` and
`release-assets.githubusercontent.com`. Without `--version`, they also ask `api.github.com` for the newest release,
and read the release list on `github.com` if that host is blocked.

- **Proxy.** On macOS and Linux, set `HTTPS_PROXY` (and `NO_PROXY` for internal hosts), for example
  `export HTTPS_PROXY=http://proxy.example.com:8080`. On Windows, the script uses the Windows proxy settings, or
  `HTTPS_PROXY` when you set it, and signs in to the proxy as you.
- **TLS inspection.** On macOS and Linux, point `CURL_CA_BUNDLE` (or `SSL_CERT_FILE` for wget) at your company's CA
  bundle. On Windows, the company's root certificate must be in the Windows certificate store, which IT usually
  manages.
- **Mirror.** If your company mirrors GitHub release downloads, set `MORPHIRUP_DOWNLOAD_URL` to the mirror of
  `https://github.com/finos/morphir-dotnet/releases/download`. The mirror must serve HTTPS and keep the same layout,
  `<url>/v<version>/<file>`. Pass `--version` / `-Version` if the mirror is all you can reach.

### With mise

```sh
mise use -g 'github:finos/morphir-dotnet[matching=morphirup,version_prefix=v,prerelease=true]'
morphirup --version
```

To install a given release, add `@<version>` at the end, inside the quotes.

### Download it yourself

From the [releases page](https://github.com/finos/morphir-dotnet/releases), download the file for your machine and
the release's checksum file `morphirup-v<version>.sha256`:

| Machine | File |
| --- | --- |
| macOS, Apple silicon | `morphirup-osx-arm64` |
| Linux x64 | `morphirup-linux-x64` |
| Linux ARM64 | `morphirup-linux-arm64` |
| Windows x64 | `morphirup-win-x64.exe` |
| Windows ARM64 | `morphirup-win-arm64.exe` |

Check the file before you run it. On macOS and Linux, in the download folder (on Linux, `sha256sum -c -` works too):

```sh
f=morphirup-osx-arm64; v='<version>'
grep "  $f\$" "morphirup-v$v.sha256" | shasum -a 256 -c -
chmod +x "$f" && mkdir -p ~/.local/bin && mv "$f" ~/.local/bin/morphirup
```

It prints `morphirup-osx-arm64: OK`. If it prints anything else, delete the file.

On Windows, in PowerShell:

```powershell
$f = 'morphirup-win-x64.exe'; $v = '<version>'
$expected = (Select-String -Path "morphirup-v$v.sha256" -Pattern "  $f$").Line.Split(' ')[0]
(Get-FileHash $f -Algorithm SHA256).Hash -eq $expected
```

It prints `True`. Rename the file to `morphirup.exe` and move it into a folder on your `PATH`.

### What works on each platform

| Platform | Install morphirup | Set up, compile and generate |
| --- | --- | --- |
| macOS on Apple silicon | Yes | Yes |
| Windows x64 and ARM64 | Yes | Yes |
| Linux x64 and ARM64 | Yes | Not yet: `morphirup setup` does not support Linux yet |
| macOS on Intel | No | No |

On Linux, `morphirup` needs glibc 2.35 or later (Ubuntu 22.04, Debian 12 or later). Alpine and other musl
distributions are not supported. Until `setup` supports Linux, use the standalone `morphir` CLI below.

The [morphirup reference]({{< relref "/docs/cli/morphirup" >}}) covers the release files and how they are checked.
If something goes wrong, see [Troubleshooting](../troubleshooting/).

## Other ways to install the standalone morphir CLI

These routes install the `morphir` command line itself, without `morphirup`.

### Requirements

What morphir-dotnet needs from the machine depends on the offering. A .NET runtime is what runs a managed program; a
.NET SDK is the larger download that also builds and installs one. Each offering states its needs in
`morphir extension list --json` (`deployment` and `prerequisites`, see
[extension list]({{< relref "/docs/cli/management#extension-list" >}})):

| Offering | To run it | To install it | Released |
| --- | --- | --- | --- |
| Native AOT `morphir` | Nothing from .NET | Nothing | Not yet as this offering |
| Self-contained `morphir` (carries its own .NET runtime) | Nothing from .NET | Nothing | Not yet |
| Framework-dependent `morphir` | The .NET runtime `Microsoft.NETCore.App` 11.x | Nothing | Not yet |
| `dotnet-morphir` (`Morphir.Tool` dotnet tool) | The .NET runtime `Microsoft.NETCore.App` 11.x | A .NET SDK 11.0.100 or later, for `dotnet tool install` | Yes, on NuGet |

The `morphir-<rid>` executables that releases attach today are a trimmed single-file build that carries its runtime,
so they need nothing from .NET either; no named release profile produces them, so they report no `deployment`. Building
from source needs the .NET SDK that `global.json` names.

### Global Tool Installation

Install the Morphir CLI as a global .NET tool. The command is `dotnet-morphir`:

```bash
dotnet tool install --global Morphir.Tool --prerelease
```

### Standalone Executable

The standalone `morphir` is one file that carries its .NET runtime, so it needs no .NET SDK or runtime. It is a
managed, trimmed, self-contained single-file build without an F# frontend, not a Native AOT executable. Download
`morphir-linux-x64`, `morphir-linux-arm64`, `morphir-osx-arm64`, `morphir-win-x64.exe` or `morphir-win-arm64.exe`
from the [releases page](https://github.com/finos/morphir-dotnet/releases). The Windows ARM64 file is built on a native
Windows 11 ARM64 runner; releases before the first one that lists it have no `win-arm64` file. To compile
F# with it, also install the F# frontend extension:

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

Both hosts generate F# with their built-in backend by default. A separate F#
backend extension, `Morphir.CodeGeneration.FSharp.Extension` (command
`morphir-fsharp-backend`), is for MEP hosts that have no built-in F# backend.
`morphir generate` runs it only when you select it, with `--extension` or
`[codegen.fsharp].extension`. See
[F# generation]({{< relref "/docs/backends/fsharp" >}}).

### Standalone morphir install scripts

These scripts install the standalone `morphir` executable, not `morphirup`:

```bash
# Linux
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-linux.sh | bash

# macOS
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-macos.sh | bash
```

```powershell
# Windows
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-windows.ps1 | iex
```

See [scripts/README.md](https://github.com/finos/morphir-dotnet/blob/main/scripts/README.md) for their options.

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

### Verify the morphir CLI

Check that the dotnet tool is installed correctly:

```bash
dotnet-morphir --version
```

Then follow [Compile F# to Morphir IR]({{< relref "/docs/getting-started/compile-fsharp" >}}).

## Troubleshooting

See [Troubleshooting](../troubleshooting/).

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
