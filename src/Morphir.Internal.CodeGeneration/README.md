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

### JSON Codec Generation

Mark your types with the `[<GenerateJsonCodec>]` attribute:

```fsharp
open Morphir.Internal.CodeGeneration

[<GenerateJsonCodec>]
type User = {
    Id: int
    Name: string
    Email: string
}
```

This generates a module `User.JsonCodec` with `encode` and `decode` functions:

```fsharp
open User.JsonCodec

let user = { Id = 1; Name = "John"; Email = "john@example.com" }
let json = encode user
let result = decode jsonElement
```

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
[<GenerateJsonCodec(PropertyNamingPolicy = "camelCase", Namespace = "MyApp.Generated")>]
type MyType = { Field: string }
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
