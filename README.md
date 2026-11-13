[![FINOS - Incubating](https://img.shields.io/badge/FINOS-Incubating-blue.svg)](https://finos.org)
[![Slack](https://img.shields.io/badge/Slack-morphir-blue.svg?logo=slack)](https://finos-lf.slack.com/messages/morphir)
![Development Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/development.yml/badge.svg)
![Deployment Workflow](https://github.com/finos/morphir-dotnet/actions/workflows/deployment.yml/badge.svg)
![CVE Scanning](https://github.com/finos/morphir-dotnet/actions/workflows/cve-scanning.yml/badge.svg)
![NuGet Version](https://img.shields.io/nuget/v/Morphir?label=NuGet%20Morphir)
![NuGet Core](https://img.shields.io/nuget/v/Morphir.Core?label=NuGet%20Core)
![NuGet Tooling](https://img.shields.io/nuget/v/Morphir.Tooling?label=NuGet%20Tooling)
[![Proto Plugin](https://img.shields.io/badge/proto_plugin-v0.1.1-blue.svg)](https://github.com/finos/morphir-dotnet/releases/tag/plugin-v0.1.1)
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

The additive [shared configuration libraries](docs/content/docs/configuration/resolution.md)
provide typed TOML, YAML, JSON/JSON5 documents, pure source resolution, named
profiles, validation and winning origins without constructing the CLI.
`Morphir.Configuration.IO` reads explicit files or supplied text and exposes
bounded project/workspace discovery through native and in-memory trees. See the
[discovery guide](docs/content/guides/configuration.md#discovery-library-api) for
`ProjectSystem.Discover`, per-root/member `ProjectSystem.Load`, membership/selection,
host boundaries and explicitly authorized machine sources.
Existing Git-first artifact discovery, cache-only tooling and CI overlays keep
their compatibility behavior. Shared [inspection APIs](docs/content/docs/configuration/inspection.md)
and `morphir config get/list/show/path/explain` expose typed values, source reports,
scoped queries and redacted explanations. The optional
[Microsoft adapter](docs/content/docs/configuration/microsoft.md) projects one
resolved document with explicit/source-generated Options binding. See the
[CLI reference](docs/content/docs/cli/config.md) for read controls, JSON schema
and exit codes. Shared [editing services](docs/content/docs/configuration/editing.md)
and `morphir config set/unset` provide typed, scoped TOML previews and writes,
source-preserving changes, optimistic conflicts and explicit recovery ownership.
Shared [classic project migration](docs/content/docs/configuration/project-migration.md)
and `morphir project migrate` provide loss-checked previews and guarded TOML
adoption while retaining classic bytes. Shared [workspace migration](docs/content/docs/configuration/workspace-migration.md)
adds bounded inventory, verified membership and recoverable batch application through
`morphir workspace migrate [directory]`. See the
[reader profile](src/Morphir.Configuration.IO/README.md) for syntax and provenance.

## Get started

Install `morphirup`, the Morphir toolchain manager. It is one file that needs neither .NET nor ICU.

```sh
# Linux (x64, ARM64) and macOS (Apple silicon)
curl -fsSL https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.sh | sh
```

```powershell
# Windows (x64, ARM64), PowerShell 7 or Windows PowerShell 5.1
irm https://raw.githubusercontent.com/finos/morphir-dotnet/main/scripts/install-morphirup.ps1 | iex
```

```sh
# Or with mise (give the version)
mise use -g 'github:finos/morphir-dotnet[matching=morphirup]@0.4.0-alpha.5'
```

Then open a new terminal and run `morphirup --version`. The scripts install the newest release, prereleases
included, and refuse to install a file whose SHA-256 does not match the release's checksum file.

| Platform | Install and `morphirup --version` | `morphirup setup` and `run` |
| --- | --- | --- |
| Windows x64 and ARM64 | Yes | Yes, experimental |
| Linux x64 and ARM64 (glibc 2.35 or later) | Yes | Not yet: `setup` refuses, releases carry no Linux toolchain |
| macOS on Apple silicon | Yes | Not yet: the macOS toolchain is in progress |
| macOS on Intel, Alpine and other musl Linux | No | No |

Next, build [your first model](docs/content/docs/getting-started/first-model.md). The
[installation guide](docs/content/docs/getting-started/installation.md) covers a pinned version, other folders,
manual download with checksum verification, and the standalone `morphir` CLI.

## SDK libraries

`Morphir.SDK.CSharp` is an independent `net11.0` and `net10.0` runtime package with immutable
C#/.NET public types. See the [C# SDK guide](docs/content/docs/sdk/csharp/_index.md)
for its bounded operation contract and package usage.

The in-memory [C# generator](docs/content/docs/backends/csharp/_index.md) lowers
typed classic/v4 libraries with explicit numeric C# 14/15 profiles, immutable
values, executable functions/patterns/record operations and public SDK bindings.
Original business fixtures and the bounded modern package-host/trimming checks
are verified under [#474](https://github.com/finos/morphir-dotnet/issues/474).
Implementation [#478](https://github.com/finos/morphir-dotnet/pull/478) landed.
The [main specification](openspec/specs/morphir-csharp-transpilation/spec.md) and
[archived acceptance evidence](openspec/changes/archive/2026-11-03-add-csharp-code-generation/validation.md)
landed in archive follow-up [#480](https://github.com/finos/morphir-dotnet/pull/480).
Coordinator cleanup remains pending.

Generate C# through the existing CLI, with numeric profiles and a validated
optional namespace:

```powershell
morphir generate --input model.json --target csharp --output generated --option language-version=14 --option namespace=Example.Generated --json
```

The [C# walkthrough](docs/content/docs/getting-started/morphir-to-csharp.md)
builds and runs a fresh consumer against the immutable runtime.
CLI implementation [#482](https://github.com/finos/morphir-dotnet/pull/482) landed;
its [main specification](openspec/specs/cli-csharp-generation/spec.md) and
[archived evidence](openspec/changes/archive/2026-11-03-integrate-csharp-generation-cli/validation.md) are synchronized
in this follow-up. Archive PR landing and root-verified cleanup remain pending.

## Build from source and other installs

### Building from source

The repository builds with the .NET 11 SDK and C# 15. Applications, .NET tools and tests target `net11.0`.
Packable libraries target `net11.0` and `net10.0` (.NET 10 LTS), so .NET 10 applications can use them.
.NET 8 and .NET 9 reached the end of support on 2026-11-10, so .NET 10 is the lowest target.
MSBuild tasks and Myriad plugins target `net10.0`, so the .NET 10 SDK can load them.
The `netstandard2.0` attribute packages keep that target. The properties
`MorphirCurrentTargetFramework`, `MorphirLibraryTargetFrameworks` and `MorphirHostedTargetFramework` in
[`Directory.Build.props`](./Directory.Build.props) set these targets.

Install the SDK pinned in [`global.json`](./global.json), or use `mise install` to install the tools in
[`mise.toml`](./mise.toml). CI uses the same SDK pin.

VS Code users can run **Dev Containers: Reopen in Container** to use the
Microsoft .NET SDK image in [`.devcontainer/Dockerfile`](./.devcontainer/Dockerfile).
The SDK includes the C# and F# compilers. Renovate groups the container, global.json,
and mise SDK updates together and keeps them on the .NET 11 SDK. Moving to the next SDK
major version requires an explicit baseline update.

```bash
./build.sh --target Test
./build.sh --target PackAll
./build.sh --target TestBuild
```

To compile classic Morphir model JSON into F#, see the [Myriad getting-started article](docs/content/docs/getting-started/morphir-to-fsharp.md), [plugin reference](docs/content/docs/guides/myriad-fsharp.md), and [car rental tutorial](docs/content/docs/guides/car-rental-tutorial.md). The [runnable example](examples/car-rental/README.md) covers inventory, reservation, pickup, return and billing with one generated F# file per module.

### Other ways to install the standalone morphir CLI

To install with `morphirup`, see [Get started](#get-started). These routes install the `morphir` command line
itself:

**Using proto (Recommended for multi-language toolchains):**

```bash
# Install proto (if not already installed)
curl -fsSL https://moonrepo.dev/install/proto.sh | bash  # Linux/macOS
# or: irm https://moonrepo.dev/install/proto.ps1 | iex  # Windows

# Add Morphir plugin and install
proto plugin add morphir "source:https://github.com/finos/morphir-dotnet/releases/latest/download/morphir_plugin.wasm"
proto install morphir
```

**Using Platform-Specific Install Scripts:**

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
dotnet tool install --global Morphir.Tool --prerelease
```

**Verify Installation:**

```bash
morphir --version          # native executable
dotnet-morphir --version   # .NET tool
```

**Compile F# to Morphir IR:** follow the five-minute [Compile F# guide](https://finos.github.io/morphir-dotnet/docs/getting-started/compile-fsharp/).

For detailed installation instructions and troubleshooting, see the [Installation Guide](https://finos.github.io/morphir-dotnet/docs/getting-started/installation/).

### Documentation

- [Installation Guide](https://finos.github.io/morphir-dotnet/docs/getting-started/installation/)
- [Getting Started](https://finos.github.io/morphir-dotnet/docs/getting-started/)
- [API Documentation](https://finos.github.io/morphir-dotnet/api/)
- [Morphir Ecosystem](https://morphir.finos.org/)

## Building

This project uses [Nuke](https://nuke.build/) for build orchestration, providing a strongly-typed, cross-platform build system written in C#.

### Requirements

- [.NET SDK 11.0.100](https://dotnet.microsoft.com/download) or a later patch in the same feature band, as specified in `global.json`

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

### Build system

The build runs on [Fallout](https://github.com/Fallout-build/Fallout) 10.5.0, the successor to Nuke. `./build.sh`, `build.ps1`, and `build.cmd` are the entry points. Target names are unchanged from the earlier Just-to-Nuke move, which is recorded in [NUKE_MIGRATION.md](NUKE_MIGRATION.md).

## Developing

The [shared .NET SDK contract](docs/design/dotnet-sdk-contract.md) freezes the
bounded operation signatures, scalar limits and independent expectations used by
both SDK deliveries. See the [fixture guide](tests/Fixtures/MorphirSDK/README.md)
for the build command, schema and provenance. After building its test project,
run the offline validator with:

```powershell
dotnet exec tests/Morphir.SDK.Conformance.Tests/bin/Debug/net11.0/Morphir.SDK.Conformance.Tests.dll --maximum-parallel-tests 2
```

F# runtime consumers use `Morphir.SDK.FSharp`. Its namespace and runtime assembly
remain `Morphir.SDK`, so existing generated source and runtime type identities
remain valid. The legacy `Morphir.SDK` package depends on the canonical package
and ships no runtime assembly. See the [F# runtime migration guide](src/Morphir.SDK.FSharp/README.md)
for dependency selection and covered behavior corrections.

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
│   └── Morphir.BlackBox.Tests/ # Black-box tests of the built artifacts (BDD/Gherkin)
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

The project uses C# scripts (`.cs` files) for build automation, leveraging the direct C# file execution of the .NET SDK. These scripts are located in the `scripts/` directory:

- `run-tests.cs` - Runs unit tests
- `run-e2e-tests.cs` - Runs end-to-end tests
- `generate-wolverine-code.cs` - Generates Wolverine code

**Note**: The deprecated `build-tool-dll.cs`, `pack-tool-platform.cs`, `publish-single-file.cs` and `publish-single-file-untrimmed.cs` scripts have been removed. Use the Nuke build targets (`PackTool`, `PublishTool`, `PackRelease`, `PublishSingleFile`, `PublishSingleFileUntrimmed`) instead.

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

#### Building Release Profiles

The four .NET deployment offerings each have a named release profile. `PackRelease` publishes one profile for one platform and packs it into an archive named `morphir-dotnet-<release>-<deployment>-<rid>.<zip|tar.gz>` (zip on Windows, tar.gz elsewhere, files at the archive root), or for `dotnet-tool` the `Morphir.Tool.<release>.nupkg` package. The release comes from `CHANGELOG.md`. Output goes to `artifacts/release/`; the publish folders are in `artifacts/release/publish/<deployment>/<rid>/`.

```bash
# Native AOT morphir (src/Morphir). Build on the operating system of the RID.
./build.sh --target PackRelease --deployment native-aot --rid linux-x64
build.cmd --target PackRelease --deployment native-aot --rid win-arm64

# Managed morphir with the in-process F# frontend (src/Morphir.Managed), untrimmed folder archives
./build.sh --target PackRelease --deployment self-contained --rid osx-arm64
./build.sh --target PackRelease --deployment framework-dependent --rid linux-arm64

# The Morphir.Tool package (dotnet-morphir). Portable, so no --rid.
./build.sh --target PackRelease --deployment dotnet-tool

# Publish only, without the archive
./build.sh --target PublishRelease --deployment self-contained --rid win-x64
```

Platforms: `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`. Any other RID is refused. Beside each archive, `<archive>.build-launch.json` records its launch path, execution mode, layout (`single-file`, `folder` or `tool-package`), bundled runtime, required runtime or SDK, F# frontend, prerequisites and SHA-256. The operating-system prerequisites are not declared yet; qualifying the artifact on each platform establishes them. It is a build output, not a public contract. `PackRelease` fails when the publish folder contradicts it, for example a `framework-dependent` folder that carries `coreclr`. Building a profile does not qualify a platform: no platform is release-qualified yet. [Checking the release profiles from packaged artifacts](docs/contributing/release-profile-checks.md) runs the E2E suite against the packaged archives and lists what has been run on each platform; nothing is qualified by it. `PackRelease` also writes `<archive>.build-contents.json` and `artifacts/release/size-report.md`, which list every file each artifact contains with its size.

The same publish folder always gives the same archive bytes. A publish for one RID writes its NuGet lock files under `obj/publish-lock/<rid>/`, so it leaves the committed `packages.lock.json` files alone. To check, run `git status --short` before and after `PackRelease`: it should list nothing new outside `artifacts/`, which is ignored.

On Windows, the Native AOT link needs the Visual Studio C++ build tools for the target architecture, and the ILCompiler targets call `vswhere.exe`. If the link fails with "'vswhere.exe' is not recognized", put `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` on `PATH` in that shell.

#### Building Executables

These targets keep the layouts that the workflows and E2E tests use. `PublishSingleFile` builds the legacy release asset that releases ship as `morphir-<rid>[.exe]` and `morphir-<rid>-v<version>.tar.gz`.

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
./build.sh --target PublishSingleFile --rid linux-x64 --configuration Debug --single-file-dir ./bin
build.cmd --target PublishSingleFile --rid linux-x64 --configuration Debug --single-file-dir ./bin
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

# Build the black-box test project for the published targets
./build.sh --target BuildE2ETests
build.cmd --target BuildE2ETests

# Run end-to-end tests
./build.sh --target TestE2E                                   # Unix/macOS
build.cmd --target TestE2E                                    # Windows

# Run the black-box tests on one target (a value of MORPHIR_TEST_TARGET, or tool, all or offerings)
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
- `PublishRelease` - Publish one release profile (requires `--deployment`, and `--rid` except for `dotnet-tool`)
- `PackRelease` - Pack one release profile into its named archive and launch metadata
- `PublishSingleFile` - Publish the legacy trimmed single-file release asset (requires `--rid`)
- `PublishExecutable` - Publish AOT executable (requires `--rid`)
- `PublishSingleFileUntrimmed` - Publish untrimmed single-file executable (requires `--rid`)
- `TestE2E` - Run each group of the black-box tests against a published or packaged target
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

SDK runtime delivery and archival evidence is recorded in the [runtime delivery record](docs/design/dotnet-csharp-generation/sdk-runtime-delivery.md). The canonical [C# runtime spec](openspec/specs/csharp-sdk-runtime/spec.md), [F# package spec](openspec/specs/fsharp-sdk-packaging/spec.md), and stable [C# API handoff](openspec/changes/archive/2026-11-03-add-immutable-csharp-sdk/api-handoff.md) describe the landed runtimes. SDK archive #472 landed; source SDK worktrees and their owned artifacts were removed. Empty shared archive directory cleanup remains pending with the recorded permission/policy limitations.

## OpenSpec change proposals

The [extension installation library](src/Morphir.Extensions.Installation/README.md)
documents the implemented distribution parsers, platform mappings and MEP
eligibility checks, along with the remaining installer work tracked by #558.
Extension installation commands are not implemented yet.

The [Morphir Compatibility Kit seed](conformance/README.md) contains portable
F# frontend cases, a closed step vocabulary and the deterministic library runner.
Its reader/runner tests and Native AOT smoke check are independent of the
production F# frontend, which is still being implemented.

This repository uses the shared `html-design` OpenSpec workflow. New proposals
produce capability specs, a standalone `design.html`, and implementation tasks.
See [the OpenSpec guide](docs/openspec.md) for setup, verification and updates.
