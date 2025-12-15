# moonrepo Configuration

This directory contains the moonrepo task runner configuration for the morphir-dotnet project.

## Installation via proto

**Recommended approach:** This project uses [proto](https://moonrepo.dev/docs/proto) as a toolchain manager to install and manage moonrepo. This avoids conflicts with other `moon` tools (like MoonBit's `moon` CLI).

### Quick Start

```bash
# Install proto (one-time setup)
curl -fsSL https://moonrepo.dev/install/proto.sh | bash

# proto will auto-install moonrepo based on .prototools
proto run moonrepo -- --version
```

The first time you run `proto run moonrepo`, it will automatically:
1. Read the version from [.prototools](../.prototools)
2. Download moonrepo v1.41.7 with the WASM plugin
3. Install it to `~/.proto/tools/moonrepo/1.41.7/`

### Why proto + moonrepo?

**Avoids Conflicts:** If you have MoonBit's `moon` tool installed, running `moon` directly would execute MoonBit, not moonrepo. Using `proto run moonrepo` ensures you're running the correct tool.

**Version Management:** The `.prototools` file locks the moonrepo version (1.41.7), ensuring consistency across:
- Local development
- CI/CD pipelines
- Team members

**Auto-Installation:** No manual installation needed - proto handles everything based on the config file.

## Configuration Files

- **workspace.yml** - Workspace-level configuration
  - Defines project discovery patterns (`src/*`, `tests/*`, `docs`)
  - Configures global tasks (`:test-all`, `:build-all`, `:lint`, etc.)
  - Sets up caching and VCS integration

- **Project moon.yml files** - Located in each project directory:
  - `src/Morphir.Core/moon.yml`
  - `src/Morphir.Tooling/moon.yml`
  - `src/Morphir/moon.yml`
  - `tests/Morphir.Core.Tests/moon.yml`
  - `tests/Morphir.Tooling.Tests/moon.yml`
  - `docs/moon.yml`

## Common Commands

All commands use `proto run moonrepo --` prefix to run moonrepo via proto:

```bash
# List all projects
proto run moonrepo -- query projects

# List all tasks
proto run moonrepo -- query tasks

# View project dependency graph
proto run moonrepo -- query projects --graph

# Run global tasks
proto run moonrepo -- run :build-all      # Build all .NET projects
proto run moonrepo -- run :test-all       # Run all tests
proto run moonrepo -- run :lint           # Check code formatting
proto run moonrepo -- run :format-all     # Format all code
proto run moonrepo -- run :docs-dev       # Start docs dev server
proto run moonrepo -- run :docs-build     # Build documentation

# Run project-specific tasks
proto run moonrepo -- run Morphir:build
proto run moonrepo -- run Morphir.Core.Tests:test
proto run moonrepo -- run docs:dev
```

## Task Dependencies

The task runner automatically handles dependencies:

- `Morphir.Tooling:build` depends on `Morphir.Core:build`
- `Morphir:build` depends on `Morphir.Tooling:build`
- `*.Tests:test` depends on the respective project's `build` task
- All tests run in parallel when using `:test-all`

## Caching

moonrepo provides intelligent caching:

- Task outputs are cached based on input file hashes
- Cache lifetime: 7 days (configurable in workspace.yml)
- Cache invalidation happens automatically when inputs change
- Run `proto run moonrepo -- clean` to clear all caches

## CI/CD Integration

The GitHub Actions workflows use proto to install and run moonrepo:

- `.github/workflows/development.yml` - Uses `proto run moonrepo -- run :lint` and `:test-all`
- `.github/workflows/docs.yml` - Uses `proto run moonrepo -- run docs:build`

Proto automatically installs moonrepo based on `.prototools` in CI.

## Troubleshooting

### "moonrepo not found" error

Make sure proto is installed and in your PATH:
```bash
curl -fsSL https://moonrepo.dev/install/proto.sh | bash
# Add to PATH (usually done automatically)
export PATH="$HOME/.proto/bin:$PATH"
```

### Tasks not found

Ensure you're in the repository root directory. moonrepo looks for `.moon/workspace.yml` to identify the workspace root.

### Dependencies not resolving

Run `proto run moonrepo -- query projects --graph` to visualize the dependency graph and identify any circular dependencies or missing projects.

### Caching issues

Clear the cache with `proto run moonrepo -- clean` and try again. You can also disable caching for specific tasks in the project's moon.yml file.

### Version mismatch

The moonrepo version is locked in `.prototools`. If you need a different version:
1. Update the version in `.prototools`
2. Run `proto run moonrepo -- --version` to verify

## Learn More

- [moonrepo Documentation](https://moonrepo.dev/docs)
- [proto Documentation](https://moonrepo.dev/docs/proto)
- [Task Configuration](https://moonrepo.dev/docs/config/project)
- [Workspace Configuration](https://moonrepo.dev/docs/config/workspace)
