# Morphir.Internal.CodeGeneration

Myriad code generation utilities for morphir-dotnet development.

## Overview

This package provides compile-time code generation through Myriad plugins, enabling reflection-free, AOT-compatible code for the morphir-dotnet project.

## Features

- **JSON Codec Generation**: Generate reflection-free JSON encoders and decoders
- **Visitor Pattern**: Generate type-safe visitor patterns for discriminated unions
- **Lenses**: Generate lenses for nested record updates
- **Active Patterns**: Generate active patterns from discriminated unions
- **Type-Safe Builders**: Generate fluent builder APIs

## Installation

Add the package to your F# project:

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Internal.CodeGeneration" Version="0.1.0" />
</ItemGroup>
```

## Usage

### JSON

For JSON, use the public `Morphir.Myriad.SystemTextJson` plugin. It generates trim-safe System.Text.Json
converters and a `JsonSerializerContext` for F# types; see `src/Morphir.Myriad.SystemTextJson/README.md`.

### Visitor Pattern

Mark discriminated unions with `[<GenerateVisitor>]`:

```fsharp
[<GenerateVisitor>]
type TypeExpr =
    | TInt
    | TString
    | TFunc of input: TypeExpr * output: TypeExpr
```

This generates a visitor record type and an `accept` function.

### Lenses

Mark records with `[<GenerateLenses>]`:

```fsharp
[<GenerateLenses>]
type Config = {
    Port: int
    Host: string
}
```

This generates lenses in the `Config.Lenses` module for each field.

## Configuration

Generators support configuration through attribute properties:

```fsharp
[<GenerateVisitor>]
type Expr =
    | Literal of int
    | Add of Expr * Expr
```

## AOT Compatibility

All generated code is:
- ✅ AOT-compatible (works with `PublishAot=true`)
- ✅ Trimming-friendly (works with `PublishTrimmed=true`)
- ✅ Reflection-free (no runtime reflection usage)

## Development

This is an internal package for morphir-dotnet development. For more information, see the [morphir-dotnet repository](https://github.com/finos/morphir-dotnet).

## License

Apache-2.0
