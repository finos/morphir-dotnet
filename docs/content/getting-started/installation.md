---
title: "Installation"
linkTitle: "Installation"
weight: 1
description: "Install Morphir .NET on your system"
---

## Requirements

- .NET SDK 10.0 or higher (for dotnet tool installation)
- OR platform-specific executable (self-contained, no .NET SDK required)

## Installation Methods

### Platform-Specific Install Scripts (Recommended)

Install Morphir using platform-specific install scripts that download from NuGet:

**Linux:**
```bash
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-linux.sh | bash
```

**macOS:**
```bash
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-macos.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-windows.ps1 | iex
```

These scripts will:
- Use `dotnet tool install` if .NET SDK is available
- Otherwise, download and install the platform-specific trimmed executable directly
- Automatically detect your platform architecture
- Support version pinning: `curl ... | bash -s 1.0.0`

### Global Tool Installation

Install Morphir as a global .NET tool (requires .NET SDK):

```bash
dotnet tool install -g Morphir
```

### Local Project Installation

Add Morphir to your project:

```bash
dotnet add package Morphir
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
morphir info
```

## Troubleshooting

### Command Not Found

If the `morphir` command is not found after installation:

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


