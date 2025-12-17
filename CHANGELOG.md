# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0-alpha-010] - 2025-12-16

### Changed
- Updated NuGet packages to use LICENSE.md instead of LICENSE file
- Added README.md to all NuGet packages (Morphir.Core, Morphir.Tooling, and Morphir CLI tool)
- Enhanced CLI tool package metadata with complete NuGet properties (license, authors, URLs, repository info)

### Fixed
- Fixed LICENSE file path issue in NuGet packages (now correctly placed at package root)
- Removed redundant LICENSE configuration from individual project files

## [0.2.0-alpha-009] - 2025-12-17

### Fixed
- NuGet packages now include LICENSE file and correct metadata (repository URL, project URL)

## [0.2.0-alpha-003] - 2025-12-16

### Added
- Platform-specific install scripts for Linux, macOS, and Windows that download from NuGet
- Support for installing Morphir without .NET SDK using platform-specific trimmed executables

### Changed
- Switched from AOT-compiled executables to trimmed (non-AOT) executables for publishing
- Updated deployment workflow to build trimmed single-file executables instead of AOT executables
- Updated pack-tool-platform script to handle trimmed executables with lowercase naming
- Updated installation documentation with platform-specific install script instructions

## [0.2.0-alpha-002] - 2025-12-16

### Added
- Single-file executable support for Morphir CLI (AOT-compiled, no .NET runtime required)
- Matrix build workflow for cross-platform executable generation (linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64)
- Platform-specific NuGet tool package that automatically selects the correct executable
- Build scripts for tool DLL and platform packaging (`scripts/build-tool-dll.sh`, `scripts/pack-tool-platform.sh`)
- `publish-executable` and `publish-executables` tasks for building platform-specific executables
- `pack-tool-platform` task for packaging platform-specific executables into NuGet tool package

### Changed
- Refactored complex build logic from justfile to dedicated scripts for better maintainability
- Updated deployment workflow to use matrix builds for platform-specific executables

## [0.2.0-alpha-001] - 2025-12-16

### Added
- Initial release of Morphir .NET
- Morphir.Core library with IR types
- Morphir.Tooling library with tooling infrastructure
- Morphir CLI as a dotnet tool
- Integration of just command runner for build orchestration
- Packaging and publishing tasks for NuGet packages and dotnet tools
- Local publishing workflow for testing packages
- KeepAChangelog integration for automatic versioning

[Unreleased]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-009...HEAD
[0.2.0-alpha-009]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-008...v0.2.0-alpha-009
[0.2.0-alpha-003]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-002...v0.2.0-alpha-003
[0.2.0-alpha-002]: https://github.com/finos/morphir-dotnet/compare/v0.2.0-alpha-001...v0.2.0-alpha-002
[0.2.0-alpha-001]: https://github.com/finos/morphir-dotnet/releases/tag/v0.2.0-alpha-001

