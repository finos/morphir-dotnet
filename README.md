[![FINOS - Incubating](https://img.shields.io/badge/FINOS-Incubating-blue.svg)](https://finos.org)
[![Slack](https://img.shields.io/badge/Slack-morphir-blue.svg?logo=slack)](https://finos-lf.slack.com/messages/morphir)
![Development Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/development.yml/badge.svg)
![Deployment Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/deployment.yml/badge.svg)
![NuGet Version](https://img.shields.io/nuget/v/Morphir?label=NuGet%20Morphir)
![NuGet Core](https://img.shields.io/nuget/v/Morphir.Core?label=NuGet%20Core)
![NuGet Tooling](https://img.shields.io/nuget/v/Morphir.Tooling?label=NuGet%20Tooling)
<img src="https://github.com/finos/branding/blob/master/project-logos/active-project-logos/Morphir%20Logo/Horizontal/2020_Morphir_Logo_Horizontal.png?raw=true" width="450">

# Morphir

Morphir is a library of tools that works to capture business logic as data.

For the first time, business logic can be shared, stored, translated and visualised, all with the reliability of standardisation ensured in the Morphir framework.

## What is it?

A set of tools for integrating technologies. Morphir is composed of a library of tools that facilitate the digitisation of business logic into multiple different languages & platforms. The Morphir framework is unique too in that facilities elements of automation and conversion that were previously unavailable in the field of finance-tech.

## Why is it important?

Makes business logic portable. Business logic digitised provides distinct advantages: capacity for movement across departments and fields & the ability to be converted to new languages and applications.

## How does it work?

Defines a standard format for storing and sharing business logic. A clear set of standards and format is in-place from the input/output, allowing for coherent structure.

## What are the benefits?

### ✔️ Eliminates technical debt risk

> _Refactoring code libraries is often a harmful and time-sensitive issue for businesses, Morphir ensure the standards introduced from input eliminate delays at deployment._

### ✔️ Increases agility

> _Adaptability and usability are key concepts of the Morphir framework, business logic can now move with the code, be easily understood and adopted, in an ever-developing eco-system._

### ✔️ Ensures correctness

> _Certifying that specified functions behave as intended from input to output is assured through the Morphir library / tool chain._

### ✔️ Disseminates information through automation

> _Morphir’s automated processing helps disseminate information which otherwise may not be understood or shared at all, a useful tool when brining elements of business logic to conversation outside of its immediate audience (i.e developers)._


## Morphir .NET 

Provides tooling for the Morphir ecosystem and .NET libraries that can be used to help you work with Morphir.

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
- [Morphir Ecosystem](https://morphir.finos.org/)

## Building

This project uses [Nuke](https://nuke.build/) for build orchestration, providing a strongly-typed, cross-platform build system written in C#.

### Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or higher

### Basic Commands

```bash
# Build the solution (default target)
./build.sh

# Restore dependencies
./build.sh --target Restore

# Run tests
./build.sh --target Test

# Run linting/formatting checks
./build.sh --target Lint

# Format code
./build.sh --target Format

# Run full CI pipeline (restore, build, test, lint)
./build.sh --target CI

# Show all available targets
./build.sh --help
```

**Windows users:** Use `build.cmd` or `build.ps1` instead of `./build.sh`

### Configuration

You can set the build configuration using the `--configuration` parameter:

```bash
# Build in Debug mode
./build.sh --configuration Debug

# Run tests in Debug mode
./build.sh --target Test --configuration Debug
```

By default, commands use `Release` configuration.

### Migration from Just

This project was recently migrated from Just to Nuke. See [NUKE_MIGRATION.md](NUKE_MIGRATION.md) for the complete migration guide and command mappings. The old `justfile` is preserved for reference.

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
├── build/
│   ├── _build.csproj          # Nuke build project
│   └── Build.cs               # Build orchestration (strongly-typed)
├── scripts/                   # Build and utility scripts (C# scripts)
├── build.sh/cmd/ps1           # Nuke bootstrap scripts
└── justfile                   # Legacy build commands (preserved for reference)
```

### Development Workflow

1. **Restore dependencies:**
   ```bash
   ./build.sh --target Restore
   ```

2. **Build the solution:**
   ```bash
   ./build.sh --target Compile
   ```

3. **Run tests:**
   ```bash
   ./build.sh --target Test
   ```

4. **Check code formatting:**
   ```bash
   ./build.sh --target Lint
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
- [Morphir Docs](https://morphir.finos.org/)
