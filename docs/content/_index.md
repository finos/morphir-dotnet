---
title: "Morphir .NET"
linkTitle: "Home"
menu:
  main:
    weight: 1
---

Welcome to the Morphir .NET documentation!

Morphir .NET provides .NET bindings, libraries, codecs, and tooling interoperable with the Morphir IR (intermediate representation) and developer workflows.

## What is Morphir?

[Morphir](https://morphir.finos.org/) is a multi-language system that brings your business logic to life. It provides a way to model your domain as data and transform it into various formats.

## Quick Start

Get started with Morphir .NET in minutes:

```bash
# Install the Morphir CLI
dotnet tool install -g Morphir

# Get information about your workspace
morphir info

# Run the Morphir compiler
morphir run <wasm-plugin-path>
```

## Features

- **Pure Domain Models**: Immutability-first design with ADTs that make illegal states unrepresentable
- **IR Compatibility**: Full compatibility with Morphir IR and JSON formats
- **Strong Typing**: Leverage C# 14 and .NET 10 features for type-safe domain modeling
- **Comprehensive Testing**: Support for TUnit, Reqnroll, and property-based testing

## Project Structure

- **src/Morphir**: C# CLI/host application
- **src/Morphir.Core**: Core domain model and IR definition
- **tests/**: Unit, property-based, and BDD/acceptance tests

## Resources

- [Morphir Documentation](https://morphir.finos.org/)
- [GitHub Repository](https://github.com/finos/morphir-dotnet)
- [Contributing Guide](/contributing/)




