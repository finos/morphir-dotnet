---
title: "Contributing"
linkTitle: "Contributing"
weight: 40
description: "How to contribute to Morphir .NET"
---

Thank you for your interest in contributing to Morphir .NET! This document provides guidelines and instructions for contributing.

## Getting Started

1. Fork the repository
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

## Coding Standards

- Follow the existing code style
- Use C# 14 features where appropriate
- Prefer immutable data structures
- Write comprehensive tests
- Update documentation as needed

## Pull Request Process

1. Ensure all tests pass
2. Run code formatters
3. Update documentation if needed
4. Create a focused PR with a clear description
5. Follow [Conventional Commits](https://www.conventionalcommits.org/) format

## Code of Conduct

Please read and follow our [Code of Conduct](/code-of-conduct/).

## Questions?

- Open an issue on GitHub
- Join our discussions
- Check the [AGENTS.md](https://github.com/finos/morphir-dotnet/blob/main/AGENTS.md) for detailed development guidelines

