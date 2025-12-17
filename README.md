# Morphir .NET

Morphir .NET is a .NET implementation of the Morphir project, providing tools and libraries for working with Morphir IR (Intermediate Representation) in .NET applications.

## Status

![Development Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/development.yml/badge.svg)
![Deployment Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/deployment.yml/badge.svg)
![FINOS Project Maturity](https://img.shields.io/badge/FINOS-Incubating-blue)
![NuGet Version](https://img.shields.io/nuget/v/Morphir?label=NuGet%20Morphir)
![NuGet Core](https://img.shields.io/nuget/v/Morphir.Core?label=NuGet%20Core)
![NuGet Tooling](https://img.shields.io/nuget/v/Morphir.Tooling?label=NuGet%20Tooling)

## Getting Started

### Quick Start

Install Morphir using one of the following methods:

**Using Platform-Specific Install Scripts (Recommended):**

```bash
# Linux
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-linux.sh | bash

# macOS
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-macos.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-windows.ps1 | iex
```

**Using .NET Tool (requires .NET SDK):**

```bash
dotnet tool install -g Morphir
```

**Verify Installation:**

```bash
morphir --version
```

For detailed installation instructions and troubleshooting, see the [Installation Guide](https://finos.github.io/morphir-dotnet/getting-started/installation/).

### Documentation

- [Installation Guide](https://finos.github.io/morphir-dotnet/getting-started/installation/)
- [Getting Started](https://finos.github.io/morphir-dotnet/getting-started/)
- [API Documentation](https://finos.github.io/morphir-dotnet/api/)

## Building

This project uses [`just`](https://github.com/casey/just) as the command orchestrator. All build commands are run through `just`.

### Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or higher
- [`just`](https://github.com/casey/just) command runner

### Basic Commands

```bash
# Restore dependencies
just restore

# Build the solution
just build

# Run tests
just test

# Run linting/formatting checks
just lint

# Format code
just format

# Run full CI pipeline (restore, build, test, lint)
just ci
```

### Configuration

You can set the build configuration using the `CONFIGURATION` environment variable:

```bash
# Build in Debug mode
CONFIGURATION=Debug just build

# Run tests in Debug mode
CONFIGURATION=Debug just test
```

By default, commands use `Release` configuration.

## Developing

### Project Structure

```
morphir-dotnet/
├── src/
│   ├── Morphir.Core/          # Core IR types and utilities
│   ├── Morphir.Tooling/       # Tooling infrastructure
│   └── Morphir/               # CLI application
├── tests/
│   ├── Morphir.Core.Tests/    # Unit tests for Core
│   ├── Morphir.Tooling.Tests/ # Unit tests for Tooling
│   └── Morphir.E2E.Tests/     # End-to-end tests (BDD/Gherkin)
├── scripts/                   # Build and utility scripts (C# scripts)
└── justfile                   # Build orchestration commands
```

### Development Workflow

1. **Restore dependencies:**
   ```bash
   just restore
   ```

2. **Build the solution:**
   ```bash
   just build
   ```

3. **Run tests:**
   ```bash
   just test
   ```

4. **Check code formatting:**
   ```bash
   just lint
   ```

5. **Format code (if needed):**
   ```bash
   just format
   ```

### Scripts

The project uses C# scripts (`.cs` files) for build automation, leveraging .NET 10's direct C# file execution. These scripts are located in the `scripts/` directory:

- `build-tool-dll.cs` - Builds the managed DLL for the dotnet tool
- `pack-tool-platform.cs` - Packages the Morphir CLI as a dotnet tool
- `publish-single-file.cs` - Publishes trimmed single-file executables
- `publish-single-file-untrimmed.cs` - Publishes untrimmed single-file executables
- `run-tests.cs` - Runs unit tests
- `run-e2e-tests.cs` - Runs end-to-end tests
- `generate-wolverine-code.cs` - Generates Wolverine code

### Build Commands

#### Building Libraries

```bash
# Pack library projects as NuGet packages
just pack-libs [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]

# Pack all projects (libraries and tool)
just pack-all [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
```

#### Building Executables

```bash
# Build a single-file executable for a specific platform
just publish-single-file <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/single-file]

# Common RIDs: linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64
# Example:
just publish-single-file linux-x64
```

#### Building the Dotnet Tool

```bash
# Build the managed DLL for the tool
just build-tool-dll [CONFIGURATION=Release] [OUTPUT_DIR=./artifacts/tool-dll]

# Pack the Morphir CLI as a dotnet tool (named 'dotnet-morphir')
just pack-tool-platform [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/packages]
```

#### Testing

```bash
# Run unit tests
just test [CONFIGURATION=Release]

# Build E2E test project
just build-e2e-tests [CONFIGURATION=Release]

# Run end-to-end tests
just test-e2e [EXECUTABLE_TYPE=all] [CONFIGURATION=Release]
# EXECUTABLE_TYPE: aot, trimmed, untrimmed, or all (default)

# Build and test a single-file executable for a specific platform
just build-and-test <RID> [CONFIGURATION=Release] [VERSION=] [OUTPUT_DIR=./artifacts/single-file]
```

#### Publishing

```bash
# Publish library packages to NuGet.org
just publish-libs [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]

# Publish the Morphir CLI tool package to NuGet.org
just publish-tool [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]

# Publish all packages
just publish-all [NUGET_SOURCE=https://api.nuget.org/v3/index.json] [API_KEY=] [OUTPUT_DIR=./artifacts/packages]

# Publish to local NuGet feed (for testing)
just publish-local-libs [LOCAL_SOURCE=./artifacts/local-feed] [OUTPUT_DIR=./artifacts/packages]

# Install tool locally from package
just publish-local-tool [OUTPUT_DIR=./artifacts/packages] [GLOBAL=false]
```

### Available Just Commands

Run `just` (without arguments) to see all available commands with descriptions.

Key commands:
- `restore` - Restore .NET dependencies
- `build` - Build the solution
- `test` - Run unit tests
- `lint` - Check code formatting
- `format` - Format code
- `ci` - Run full CI pipeline
- `pack-libs` - Pack library projects as NuGet packages
- `pack-tool-platform` - Pack the Morphir CLI as a dotnet tool
- `publish-single-file <RID>` - Publish trimmed single-file executable
- `test-e2e` - Run end-to-end tests
- `build-and-test <RID>` - Build and test executable for a platform

## Contributing

We welcome contributions! Please see our [Contributing Guide](https://finos.github.io/morphir-dotnet/contributing/) for details.

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Links

- [Documentation](https://finos.github.io/morphir-dotnet/)
- [GitHub Repository](https://github.com/finos/morphir-dotnet)
- [NuGet Packages](https://www.nuget.org/packages?q=morphir)
- [Morphir Project](https://github.com/finos/morphir)
