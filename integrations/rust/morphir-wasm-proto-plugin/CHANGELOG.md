# Changelog

All notable changes to the Morphir Proto Plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] - 2025-12-19

### Added

- Comprehensive unit tests: expanded from 2 to 19 tests (+850% coverage)
- Platform RID mapping tests for all 5 platforms (Linux x64/arm64, macOS x64/arm64, Windows x64)
- Executable naming tests (Windows `.exe` vs Unix)
- URL generation tests for GitHub Releases
- Archive naming and prefix validation tests
- Version parsing tests (stable, pre-release, build metadata, invalid)
- Unix permissions tests (0o755 verification)
- Edge case tests (special versions, error handling)
- BDD integration test documentation in new `INTEGRATION_TESTS.md`
- Gherkin-style scenarios for all plugin features
- Platform detection scenarios (7 scenarios)
- Version resolution, download URL generation, and executable location scenarios
- Post-install hook scenarios
- End-to-end workflow documentation
- Manual testing instructions

### Fixed

- Code formatting: applied `cargo fmt` to resolve all 18 formatting violations
- Standardized whitespace and line breaks throughout codebase
- Fixed multi-line formatting issues
- Consistent blank line spacing

### Changed

- Version bumped from 0.1.0 to 0.1.1

## [0.1.0] - 2025-12-18

### Added

- Proto WASM plugin for managing platform-specific Morphir CLI installations
- Support for 5 platforms: Linux (x64, arm64), macOS (x64, arm64), Windows (x64)
- Automatic platform detection and RID mapping
- GitHub Release URL generation for executables
- Post-install executable permissions setup (Unix)
- Integration with proto toolchain manager (requires proto ≥0.32.0)

[Unreleased]: https://github.com/finos/morphir-dotnet/compare/plugin-v0.1.1...HEAD
[0.1.1]: https://github.com/finos/morphir-dotnet/compare/plugin-v0.1.0...plugin-v0.1.1
[0.1.0]: https://github.com/finos/morphir-dotnet/releases/tag/plugin-v0.1.0
