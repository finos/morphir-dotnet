---
title: "Contributing"
linkTitle: "Contributing"
weight: 30
menu:
  main:
    weight: 30
description: "How to contribute to Morphir .NET"
---

Thank you for your interest in contributing to Morphir .NET! This section provides guidelines, instructions, and design documentation for contributing to the project.

## Quick Links

- **[Design & Architecture](./design/)**: AI Skill Framework, guru philosophy, and creation guides
- **[Development Setup](#development-setup)**: Get your environment ready
- **[Contribution Workflow](#pull-request-process)**: How to submit changes
- **[Code Standards](#coding-standards)**: What we expect
- **[QA & Testing](./qa/)**: Testing practices and playbooks

## Getting Started

1. Fork the [repository](https://github.com/finos/morphir-dotnet)
2. Clone your fork
3. Create a branch for your changes
4. Make your changes
5. Submit a pull request

## Development Setup

### Prerequisites

- .NET SDK 10.0
- Git

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test --nologo
```

### Format Code

```bash
dotnet format
```

### Install Git Hooks

```bash
dotnet tool restore
dotnet husky install
```

## Coding Standards

- Follow the existing code style
- Use C# 14 features where appropriate
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

## Design & Architecture

### For New Features

Start with the **[Design Documentation](./design/)** section:
- Understand the [AI Skill Framework](./design/skill-framework-design.md) for tooling features
- Review [Guru Philosophy](./design/guru-philosophy.md) to understand continuous improvement patterns
- Follow the [Guru Creation Guide](./design/guru-creation-guide.md) for new AI skills
- Create a PRD (Product Requirements Document) in `docs/content/contributing/design/prds/`

### For Existing Features

- Check existing issues and PRs
- Review related code and tests
- Understand the architecture (see [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md))

## Testing & Quality

See **[QA & Testing](./qa/)** for comprehensive guidance on:
- Test strategies and best practices
- Regression testing
- BDD scenarios
- Test coverage requirements

## Code of Conduct

Please read and follow our [Code of Conduct](/code-of-conduct/).

## Need Help?

- **Questions**: Open an issue on [GitHub](https://github.com/finos/morphir-dotnet/issues)
- **Discussions**: Join our [discussions](https://github.com/finos/morphir-dotnet/discussions)
- **FINOS Slack**: `#morphir` channel on [FINOS Slack](https://finos-lf.slack.com/)

## Key Resources

- [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) - Comprehensive guidance for AI agents and developers
- [CONTRIBUTING.md](https://github.com/finos/morphir-dotnet/blob/main/CONTRIBUTING.md) - DCO and legal requirements
- [README.md](https://github.com/finos/morphir-dotnet/blob/main/README.md) - Project overview
- Check the [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) for detailed development guidelines

