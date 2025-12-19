# Morphir WASM Proto Plugin

A [proto](https://moonrepo.dev/proto) WASM plugin for managing platform-specific Morphir executable installations.

## Overview

This plugin enables proto to install and manage Morphir CLI executables across multiple platforms. Proto is a unified toolchain manager that uses WASM plugins to provide consistent tooling experiences.

## Features

- **Platform Support**: Automatically detects and installs the correct Morphir executable for:
  - Linux (x64, arm64)
  - macOS (x64, arm64)
  - Windows (x64)
- **Version Management**: Install and switch between different Morphir versions
- **Automatic Updates**: Easy upgrades to new Morphir releases
- **Integration**: Works seamlessly with proto's tool management

## Installation

### Install proto

If you haven't already, install proto:

```bash
# Linux/macOS
curl -fsSL https://moonrepo.dev/install/proto.sh | bash

# Windows (PowerShell)
irm https://moonrepo.dev/install/proto.ps1 | iex
```

### Install the Morphir Plugin

Add the Morphir plugin to your proto configuration:

```bash
# Using proto CLI
proto plugin add morphir "source:https://github.com/finos/morphir-dotnet/releases/latest/download/morphir_plugin.wasm"

# Or add to ~/.proto/config.toml
[plugins]
morphir = "source:https://github.com/finos/morphir-dotnet/releases/latest/download/morphir_plugin.wasm"
```

### Install Morphir

```bash
# Install latest version
proto install morphir

# Install specific version
proto install morphir 1.0.0

# Set global version
proto use morphir 1.0.0 --global
```

## Usage

Once installed via proto, use Morphir as normal:

```bash
# Verify installation
morphir --version

# Use Morphir commands
morphir ir verify my-ir-file.json
```

## Development

### Prerequisites

- [Rust](https://rustup.rs/) (latest stable)
- [proto](https://moonrepo.dev/proto) (for testing)

### Building the Plugin

```bash
# Build for WASM
cargo build --release --target wasm32-wasi

# The output will be at:
# target/wasm32-wasi/release/morphir_wasm_proto_plugin.wasm
```

### Testing Locally

```bash
# Build the plugin
cargo build --release --target wasm32-wasi

# Test with proto
proto plugin add morphir-dev "source:file://$(pwd)/target/wasm32-wasi/release/morphir_wasm_proto_plugin.wasm"
proto install morphir-dev
```

### Optimization

The plugin is configured for minimal size with:
- Link-time optimization (LTO)
- Size optimization (`opt-level = "z"`)
- Symbol stripping
- Single codegen unit

## Architecture

The plugin implements the proto PDK (Plugin Development Kit) interface:

- **`register_tool`**: Registers Morphir with proto
- **`detect_version_files`**: Detects Morphir version in projects (not used)
- **`load_versions`**: Lists available Morphir versions
- **`download_prebuilt`**: Downloads platform-specific executables from GitHub Releases
- **`locate_executables`**: Locates the morphir executable after installation
- **`resolve_version`**: Resolves version specs (latest, specific versions)
- **`post_install`**: Sets executable permissions on Unix systems

## Release Process

The plugin has its own independent release cycle from Morphir itself:

1. **Plugin Development**: Make changes to the plugin code
2. **Version Bump**: Update version in `Cargo.toml`
3. **Build & Test**: Run `cargo build` and test locally
4. **Release**: Push tag `plugin-v{version}` to trigger release workflow
5. **GitHub Release**: Workflow builds and uploads WASM artifact

The plugin release workflow builds the WASM file and uploads it to GitHub Releases, where it can be referenced by proto users.

## Integration with Morphir Releases

When a new Morphir version is released:

1. The Morphir release workflow creates GitHub releases with platform-specific executables
2. This plugin downloads those executables based on the user's platform
3. No plugin update is needed for new Morphir versions (unless the release artifact structure changes)

The plugin should only be updated when:
- Adding support for new platforms
- Changing the download URL structure
- Adding new features to the plugin itself
- Updating proto PDK version

## Platform-Specific Details

### Executable Naming

- **Linux/macOS**: `morphir`
- **Windows**: `morphir.exe`

### Download URLs

The plugin downloads from GitHub Releases using this pattern:
```
https://github.com/finos/morphir-dotnet/releases/download/v{version}/morphir-{rid}-v{version}.tar.gz
```

Where `{rid}` is one of:
- `linux-x64`
- `linux-arm64`
- `win-x64`
- `osx-x64`
- `osx-arm64`

### Archive Structure

Downloaded archives should contain the executable directly in a platform-specific directory:
```
morphir-{rid}-v{version}.tar.gz
└── {rid}/
    └── morphir[.exe]
```

## Troubleshooting

### "Unsupported platform" error

The plugin currently supports:
- Linux: x64, arm64
- macOS: x64, arm64 (Apple Silicon)
- Windows: x64

If you're on an unsupported platform, you can install Morphir using alternative methods documented in the main Morphir repository.

### Version not found

Ensure the version exists in the [Morphir releases](https://github.com/finos/morphir-dotnet/releases). The plugin downloads from GitHub Releases, so only released versions are available.

### Permission denied on Unix

The `post_install` hook should automatically set executable permissions. If this fails, manually run:

```bash
chmod +x ~/.proto/tools/morphir/*/morphir
```

## Contributing

Contributions are welcome! Please see the main [CONTRIBUTING.md](../../../CONTRIBUTING.md) for guidelines.

### Development Workflow

1. Make changes to `src/lib.rs`
2. Run `cargo fmt` to format code
3. Run `cargo clippy` to check for issues
4. Build and test locally
5. Submit a pull request

## Resources

- [Proto Documentation](https://moonrepo.dev/docs/proto)
- [Proto WASM Plugin Guide](https://moonrepo.dev/docs/guides/wasm-plugins)
- [Proto PDK Rust Crate](https://docs.rs/proto_pdk/)
- [Morphir Documentation](https://morphir.finos.org/)
- [Morphir .NET Repository](https://github.com/finos/morphir-dotnet)

## License

This plugin is part of the Morphir .NET project and is licensed under the Apache License 2.0. See [LICENSE](../../../LICENSE) for details.
