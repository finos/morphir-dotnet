# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Serilog logging infrastructure for CLI tools (Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.Console)
- CLI logging standards documentation in AGENTS.md and CLAUDE.md
- `.morphir/out/` directory to .gitignore
- New Morphir.Tool project as dedicated dotnet tool package (#214)
- Build.CI.cs with DevWorkflow, CILint, CITest targets for local CI simulation (#214)
- Comprehensive XML documentation for all 23+ build targets (#214)
- Morphir package now published to NuGet/GitHub releases (#214)
- Comprehensive Phase 1 test plan in docs/content/contributing/qa/
- **QA Testing Framework**: Claude Code skill with F# automation scripts
  - smoke-test.fsx - Quick 2-minute smoke test
  - regression-test.fsx - Full 10-15 minute regression suite
  - validate-packages.fsx - NuGet package structure validation
- **Cross-Agent Guidance**: `.agents/` directory for specialized topics
  - .agents/qa-testing.md - Comprehensive QA testing guidance for all AI agents
  - .agents/README.md - Navigation and contribution guidelines
- **Multi-Agent Pointer Files**: Configuration for all major AI coding assistants
  - .cursorrules - Cursor AI pointer file
  - .github/copilot-instructions.md - GitHub Copilot instructions
  - .windsurf/rules/morphir.md - Windsurf AI rules and workflows
  - .idea/ai-assistant-rules.md - JetBrains AI Assistant configuration
- **AGENTS.md Enhancements**:
  - Quick Navigation section (links to .agents/, CLAUDE.md, docs/)
  - Section 18: Specialized Guidance (detailed links to .agents/ topics)
  - Section 19: Resources and References (comprehensive resource index)

### Changed
- Configured all logging to write to stderr instead of stdout to preserve stdout for command output
- Updated E2E test runner script to use Nuke build commands instead of deprecated `just` commands
- Updated README.md with Nuke build command examples
- Split monolithic Build.cs into 5 partial classes: Packaging, Publishing, Testing, CI (#214)
- Tool command name follows dotnet convention: `dotnet-morphir` (was `morphir`) (#214)
- Morphir.Tool delegates to public Morphir.Program.Main() eliminating code duplication (#214)
- Morphir project now packable (IsPackable=true, was false) (#214)
- PackAll now builds 4 packages: Core, Tooling, Morphir, Tool (#214)
- **AGENTS.md**: Enhanced with navigation sections for better discoverability across AI tools

### Fixed
- Fixed JSON output contamination issue where log messages were being written to stdout
- CLI tools now properly separate data (stdout) and diagnostics (stderr) following Unix conventions
- E2E tests for JSON output format now pass (12/13 tests passing, up from 10/13)
- Windows build file locking issues by removing problematic MSBuild target (#214)
- Circular build dependencies causing CS2012 errors on Windows (#214)
- Directory cleaning conflicts in PackAll target (#214)

### Removed
- Deprecated scripts: pack-tool-platform.cs, build-tool-dll.cs (#214)
- Problematic GenerateWolverineCode MSBuild target, moved to Nuke (#214)
- BuildInParallel=false workaround after fixing root cause (#214)

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

