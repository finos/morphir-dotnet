# Nuke Build Migration Guide

This document outlines the migration from Just + C# scripts to Nuke build orchestration for the morphir-dotnet project.

## Migration Status

### ✅ Completed Targets

All justfile targets have been migrated to Nuke. Below is the mapping:

| Justfile Recipe | Nuke Target | Status | Notes |
|----------------|-------------|--------|-------|
| `restore` | `Restore` | ✅ | Direct migration |
| `build` | `Compile` | ✅ | Default target, depends on Restore |
| `format` | `Format` | ✅ | Applies code formatting |
| `lint` | `Lint` | ✅ | Verifies formatting |
| `test` | `Test` | ✅ | Integrated run-tests.cs logic |
| `check` | `Check` | ✅ | Runs lint |
| `precommit` | `Precommit` | ✅ | Runs lint |
| `ci` | `CI` | ✅ | Full pipeline |
| `pack-libs` | `PackLibs` | ✅ | Packs Morphir.Core and Morphir.Tooling |
| `pack-tool` | `PackTool` | ✅ | Packs Morphir CLI as dotnet tool |
| `pack-all` | `PackAll` | ✅ | Depends on PackLibs + PackTool |
| `publish-executable` | `PublishExecutable` | ✅ | AOT single-file executable (requires --rid) |
| `publish-single-file` | `PublishSingleFile` | ✅ | Managed trimmed executable (requires --rid) |
| `publish-single-file-untrimmed` | `PublishSingleFileUntrimmed` | ✅ | Managed untrimmed executable (requires --rid) |
| `build-e2e-tests` | `BuildE2ETests` | ✅ | Builds E2E test project |
| `test-e2e` | `TestE2E` | ✅ | Runs E2E tests via run-e2e-tests.cs |
| `publish-libs` | `PublishLibs` | ✅ | Pushes libs to NuGet.org |
| `publish-tool` | `PublishTool` | ✅ | Pushes tool to NuGet.org |
| `publish-all` | `PublishAll` | ✅ | Depends on PublishLibs + PublishTool |
| `publish-local-libs` | `PublishLocalLibs` | ✅ | Pushes libs to local feed |
| `publish-local-tool` | `PublishLocalTool` | ✅ | Installs tool locally |
| `publish-local-all` | `PublishLocalAll` | ✅ | Depends on PublishLocalLibs + PublishLocalTool |

### Deprecated/Removed

The following targets from justfile are not migrated as they are complex edge cases or superseded:

- `pack-tool-platform` - Complex manual packaging, superseded by `PackTool`
- `build-tool-dll` - Internal helper, not needed in Nuke
- `publish-executables` - Multi-RID loop, use `PublishExecutable` with `--rid` instead
- `build-and-test` - Combination target, compose from `PublishSingleFile` + `TestE2E`

## Usage Comparison

### Basic Commands

| Task | Justfile | Nuke |
|------|----------|------|
| Build | `just build` | `./build.sh` or `./build.sh --target Compile` |
| Test | `just test` | `./build.sh --target Test` |
| Format | `just format` | `./build.sh --target Format` |
| Lint | `just lint` | `./build.sh --target Lint` |
| CI | `just ci` | `./build.sh --target CI` |

### Packaging

| Task | Justfile | Nuke |
|------|----------|------|
| Pack libraries | `just pack-libs` | `./build.sh --target PackLibs` |
| Pack tool | `just pack-tool` | `./build.sh --target PackTool` |
| Pack all | `just pack-all` | `./build.sh --target PackAll` |
| With version | `VERSION=1.0.0 just pack-all` | `./build.sh --target PackAll --version 1.0.0` |
| Custom output | `OUTPUT_DIR=./out just pack-libs` | `./build.sh --target PackLibs --output-dir ./out` |

### Publishing Executables

| Task | Justfile | Nuke |
|------|----------|------|
| Publish for linux-x64 | `just publish-single-file linux-x64` | `./build.sh --target PublishSingleFile --rid linux-x64` |
| With configuration | `CONFIGURATION=Debug just publish-single-file linux-x64` | `./build.sh --target PublishSingleFile --rid linux-x64 --configuration Debug` |
| AOT executable | `just publish-executable linux-x64` | `./build.sh --target PublishExecutable --rid linux-x64` |

### NuGet Publishing

| Task | Justfile | Nuke |
|------|----------|------|
| Publish to NuGet | `API_KEY=xxx just publish-all` | `./build.sh --target PublishAll --api-key xxx` |
| Custom source | `NUGET_SOURCE=... just publish-libs` | `./build.sh --target PublishLibs --nuget-source https://...` |
| Local install | `just publish-local-all` | `./build.sh --target PublishLocalAll` |
| Global install | `GLOBAL=true just publish-local-tool` | `./build.sh --target PublishLocalTool --global true` |

### E2E Testing

| Task | Justfile | Nuke |
|------|----------|------|
| Run E2E tests | `just test-e2e` | `./build.sh --target TestE2E` |
| Specific executable type | `just test-e2e trimmed` | `./build.sh --target TestE2E --executable-type trimmed` |

## Key Differences

### 1. Parameter Handling

**Justfile**: Environment variables
```bash
CONFIGURATION=Debug VERSION=1.0.0 just pack-libs
```

**Nuke**: Typed parameters with `--` prefix
```bash
./build.sh --target PackLibs --configuration Debug --version 1.0.0
```

### 2. Target Dependencies

**Nuke automatically handles dependency graphs**. When you run a target, all its dependencies run automatically:

```bash
# This automatically runs: Restore → Compile → PackLibs
./build.sh --target PackLibs
```

### 3. Cross-Platform Support

- **Justfile**: Required bash for many recipes
- **Nuke**: Pure C# - works identically on Windows/Linux/macOS
  - Windows: `build.cmd --target <Target>`
  - Linux/macOS: `./build.sh --target <Target>`
  - PowerShell: `./build.ps1 --target <Target>`

### 4. IDE Integration

Nuke provides first-class IDE support:
- IntelliSense for all targets and parameters
- Debugging support (set breakpoints in Build.cs)
- ReSharper/Rider plugins available

### 5. Help System

```bash
# Show all targets and parameters
./build.sh --help

# Show execution plan
./build.sh --plan --target CI
```

## Benefits of Nuke

1. **Type Safety**: Compile-time errors instead of runtime failures
2. **Discoverability**: IntelliSense shows all available targets and parameters
3. **Consistency**: Same build system across all platforms
4. **Debuggability**: Full debugging support in IDEs
5. **Maintainability**: C# code is easier to refactor than bash scripts
6. **Documentation**: Self-documenting via descriptions and `--help`
7. **Dependency Management**: Automatic dependency resolution

## Migration Path for Users

### For Local Development

Continue using familiar commands, just replace `just` with `./build.sh`:

```bash
# Before
just build
just test
just pack-all

# After
./build.sh
./build.sh --target Test
./build.sh --target PackAll
```

### For CI/CD

Update workflows to use Nuke bootstrap scripts:

```yaml
# GitHub Actions
- name: Build
  run: ./build.sh --target CI
```

```yaml
# Windows CI
- name: Build
  run: build.cmd --target CI
```

## Troubleshooting

### "Target not found"

```bash
# List all available targets
./build.sh --help
```

### "Parameter is required"

Some targets require parameters (like `--rid` for publish targets):

```bash
./build.sh --target PublishSingleFile --rid linux-x64
```

### Build Fails

```bash
# Show verbose output
./build.sh --target <Target> --verbose

# Show execution plan without running
./build.sh --plan --target <Target>
```

## Files Created/Modified

### New Files
- `build/_build.csproj` - Nuke build project
- `build/Build.cs` - Main build orchestration
- `.nuke` - Nuke configuration (references solution)
- `build.sh` - Unix bootstrap script
- `build.cmd` - Windows bootstrap script
- `build.ps1` - PowerShell bootstrap script

### Modified Files
- `Directory.Packages.props` - Added/updated Nuke dependencies:
  - `Nuke.Common` 10.1.0
  - `Octokit` 14.0.0 (upgraded)
  - `Serilog` 4.3.0 (upgraded)
  - `Serilog.Formatting.Compact` 3.0.0 (upgraded)
  - `Serilog.Sinks.Console` 6.1.1 (upgraded)
  - `YamlDotNet` 16.3.0 (upgraded)
  - `Microsoft.Build.Utilities.Core` 18.0.2 (upgraded)

### Preserved Files
- `justfile` - Kept for reference/gradual migration
- `scripts/*.cs` - Reused by Nuke targets (run-tests.cs, run-e2e-tests.cs, etc.)

## Future Enhancements

Potential improvements for future iterations:

1. **Target for publishing executables to GitHub Releases**
2. **Parallel execution of independent targets**
3. **Caching and incremental builds**
4. **Integration with local MCP servers for build notifications**
5. **Custom build reports and metrics**
6. **Version bumping automation**

## Questions?

See the official Nuke documentation: https://nuke.build/docs/introduction/
