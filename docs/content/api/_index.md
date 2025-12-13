---
title: "API Reference"
linkTitle: "API Reference"
weight: 30
description: "Complete API reference for Morphir .NET"
---

## Overview

The Morphir .NET API provides comprehensive support for working with Morphir IR (Intermediate Representation).

## Core Namespaces

### Morphir.Core

The core namespace contains the fundamental types and structures for Morphir IR:

- **IR**: Intermediate representation types
- **Types**: Type expression models
- **Values**: Value expression models
- **Names**: Name and path handling

### Morphir

The main namespace contains CLI and application-level functionality:

- **CLI**: Command-line interface
- **Serialization**: JSON serialization support

## Getting Started

To use Morphir .NET in your project:

```csharp
using Morphir.Core.IR;
using Morphir.Core.Types;

// Create a type expression
var intType = new TypeExpr.TInt();

// Create a function type
var funcType = new TypeExpr.TFunc(
    new TypeExpr.TInt(),
    new TypeExpr.TString()
);
```

## Documentation

For detailed API documentation, see the generated XML documentation comments in the source code, or explore the source directly:

- [Morphir.Core Source](https://github.com/finos/morphir-dotnet/tree/main/src/Morphir.Core)
- [Morphir Source](https://github.com/finos/morphir-dotnet/tree/main/src/Morphir)

