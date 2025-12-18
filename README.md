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
# First-time setup: Restore tools and dependencies
dotnet tool restore
./build.sh --target Restore

# Build the solution (default target)
./build.sh

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
   # Unix/macOS
   ./build.sh --target Format

   # Windows
   build.cmd --target Format
   # or
   ./build.ps1 --target Format
   ```

### Scripts

The project uses C# scripts (`.cs` files) for build automation, leveraging .NET 10's direct C# file execution. These scripts are located in the `scripts/` directory:

- `publish-single-file.cs` - Publishes trimmed single-file executables
- `publish-single-file-untrimmed.cs` - Publishes untrimmed single-file executables
- `run-tests.cs` - Runs unit tests
- `run-e2e-tests.cs` - Runs end-to-end tests
- `generate-wolverine-code.cs` - Generates Wolverine code

**Note**: The deprecated `build-tool-dll.cs` and `pack-tool-platform.cs` scripts have been removed. Use the Nuke build targets (`PackTool`, `PublishTool`) instead.

### Build Commands

All Nuke build commands support both Unix/macOS (`./build.sh`) and Windows (`build.cmd` or `./build.ps1`) bootstrap scripts. Examples below show both formats.

#### Building Libraries

```bash
# Unix/macOS: Pack library projects as NuGet packages
./build.sh --target PackLibs

# Windows
build.cmd --target PackLibs
# or
./build.ps1 --target PackLibs

# Pack all projects (libraries and tool)
./build.sh --target PackAll                  # Unix/macOS
build.cmd --target PackAll                   # Windows

# Specify configuration and version
./build.sh --target PackLibs --configuration Debug --version 1.2.3
build.cmd --target PackLibs --configuration Debug --version 1.2.3

# Custom output directory
./build.sh --target PackLibs --output-dir ./my-packages
build.cmd --target PackLibs --output-dir ./my-packages
```

#### Building Executables

```bash
# Build a trimmed single-file executable for a specific platform
./build.sh --target PublishSingleFile --rid linux-x64        # Unix/macOS
build.cmd --target PublishSingleFile --rid linux-x64         # Windows

# Build an AOT (ahead-of-time compiled) executable
./build.sh --target PublishExecutable --rid linux-x64
build.cmd --target PublishExecutable --rid linux-x64

# Build an untrimmed executable (larger, better for debugging)
./build.sh --target PublishSingleFileUntrimmed --rid win-x64
build.cmd --target PublishSingleFileUntrimmed --rid win-x64

# Common RIDs: linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64

# With custom configuration and output directory
./build.sh --target PublishSingleFile --rid linux-x64 --configuration Debug --output-dir ./bin
build.cmd --target PublishSingleFile --rid linux-x64 --configuration Debug --output-dir ./bin
```

#### Building the Dotnet Tool

```bash
# Pack the Morphir CLI as a dotnet tool
./build.sh --target PackTool                                  # Unix/macOS
build.cmd --target PackTool                                   # Windows

# With version and output directory
./build.sh --target PackTool --version 2.0.0 --output-dir ./packages
build.cmd --target PackTool --version 2.0.0 --output-dir ./packages
```

#### Testing

```bash
# Run unit tests
./build.sh --target Test                                      # Unix/macOS
build.cmd --target Test                                       # Windows

# Run tests in Debug configuration
./build.sh --target Test --configuration Debug
build.cmd --target Test --configuration Debug

# Build E2E test project
./build.sh --target BuildE2ETests
build.cmd --target BuildE2ETests

# Run end-to-end tests
./build.sh --target TestE2E                                   # Unix/macOS
build.cmd --target TestE2E                                    # Windows

# Run E2E tests for specific executable type (aot, trimmed, untrimmed, or all)
./build.sh --target TestE2E --executable-type trimmed
build.cmd --target TestE2E --executable-type trimmed
```

#### Publishing to NuGet

```bash
# Publish library packages to NuGet.org
./build.sh --target PublishLibs --api-key YOUR_API_KEY       # Unix/macOS
build.cmd --target PublishLibs --api-key YOUR_API_KEY        # Windows

# Publish the Morphir CLI tool package to NuGet.org
./build.sh --target PublishTool --api-key YOUR_API_KEY
build.cmd --target PublishTool --api-key YOUR_API_KEY

# Publish all packages
./build.sh --target PublishAll --api-key YOUR_API_KEY
build.cmd --target PublishAll --api-key YOUR_API_KEY

# Publish to custom NuGet source
./build.sh --target PublishLibs --nuget-source https://custom-feed.com --api-key YOUR_KEY
build.cmd --target PublishLibs --nuget-source https://custom-feed.com --api-key YOUR_KEY

# Publish to local NuGet feed (for testing)
./build.sh --target PublishLocalLibs                          # Unix/macOS
build.cmd --target PublishLocalLibs                           # Windows

# With custom local source
./build.sh --target PublishLocalLibs --local-source ./my-local-feed
build.cmd --target PublishLocalLibs --local-source ./my-local-feed

# Install tool locally from package
./build.sh --target PublishLocalTool
build.cmd --target PublishLocalTool

# Install globally
./build.sh --target PublishLocalTool --global true
build.cmd --target PublishLocalTool --global true
```

### Available Nuke Targets

Run `./build.sh --help` (Unix/macOS) or `build.cmd --help` (Windows) to see all available targets with descriptions and parameters.

Key targets:
- `Restore` - Restore .NET dependencies
- `Compile` - Build the solution (default target)
- `Test` - Run unit tests
- `Lint` - Check code formatting
- `Format` - Format code
- `CI` - Run full CI pipeline (restore, build, test, lint)
- `PackLibs` - Pack library projects as NuGet packages
- `PackTool` - Pack the Morphir CLI as a dotnet tool
- `PackAll` - Pack all projects (libraries and tool)
- `PublishSingleFile` - Publish trimmed single-file executable (requires `--rid`)
- `PublishExecutable` - Publish AOT executable (requires `--rid`)
- `PublishSingleFileUntrimmed` - Publish untrimmed single-file executable (requires `--rid`)
- `TestE2E` - Run end-to-end tests
- `PublishLibs` - Publish library packages to NuGet
- `PublishTool` - Publish tool package to NuGet
- `PublishAll` - Publish all packages to NuGet
- `PublishLocalLibs` - Publish libraries to local feed
- `PublishLocalTool` - Install tool locally from package
- `PublishLocalAll` - Publish all to local feed

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
