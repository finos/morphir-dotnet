---
title: "Contributing"
linkTitle: "Contributing"
weight: 100
description: "How to contribute to Morphir .NET - guidelines, setup, and design documentation"
---

Thank you for your interest in contributing to Morphir .NET! This section provides everything you need to get started as a contributor.

## Quick Start

1. Fork the [repository](https://github.com/finos/morphir-dotnet)
2. Clone your fork
3. Set up your [development environment](#development-setup)
4. Create a branch for your changes
5. Submit a pull request

## Development Setup

### Prerequisites

- .NET SDK 10.0 or higher
- Git

### Build & Test

```bash
# Build the project
dotnet build

# Run tests
dotnet test --nologo

# Format code (required before committing)
dotnet format
```

### Install Git Hooks

```bash
dotnet tool restore
dotnet husky install
```

## Coding Standards

- Follow the existing code style
- Use C# 14 / F# 9 features where appropriate
- Prefer immutable data structures
- Write comprehensive tests (TDD approach)
- Update documentation as needed
- Follow [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) for architectural guidance

## Pull Request Process

1. Ensure all tests pass: `dotnet test --nologo`
2. Run code formatters: `dotnet format`
3. Update documentation if needed
4. Create a focused PR with a clear description
5. Follow [Conventional Commits](https://www.conventionalcommits.org/) format
6. Ensure DCO is signed (see [CONTRIBUTING.md](https://github.com/finos/morphir-dotnet/blob/main/CONTRIBUTING.md))

## Need Help?

- **Questions**: Open an issue on [GitHub](https://github.com/finos/morphir-dotnet/issues)
- **Discussions**: Join our [discussions](https://github.com/finos/morphir-dotnet/discussions)
- **FINOS Slack**: `#morphir` channel on [FINOS Slack](https://finos-lf.slack.com/)

## Key Resources

| Resource | Description |
|----------|-------------|
| [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) | Comprehensive guidance for AI agents and developers |
| [CONTRIBUTING.md](https://github.com/finos/morphir-dotnet/blob/main/CONTRIBUTING.md) | DCO and legal requirements |
| [Code of Conduct](https://github.com/finos/morphir-dotnet/blob/main/CODE_OF_CONDUCT.md) | Community guidelines |
