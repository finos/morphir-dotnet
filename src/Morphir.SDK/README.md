# Morphir.SDK

F# runtime library providing Morphir SDK types and functions for generated F# code.

## Overview

`Morphir.SDK` is a foundational library that provides F# implementations of Morphir SDK types and functions. It serves as a runtime dependency for F# code generated from Morphir IR, enabling seamless integration between Morphir's functional modeling language and F#'s type system.

## Installation

```bash
dotnet add package Morphir.SDK --version 0.4.0-alpha
```

## Features

- **Type Aliases**: Morphir types mapped to idiomatic F# types (e.g., `Maybe<'a>` = `Option<'a>`)
- **Standard Library**: Comprehensive collection of functions mirroring morphir-elm SDK
- **Immutable Collections**: Dict (Map), Set, List operations
- **Date/Time Support**: LocalDate (DateOnly), LocalTime (TimeOnly)
- **AOT Compatible**: No reflection, ready for Native AOT compilation

## Modules

### Core Types

- **Basics**: Fundamental operations (Order type, comparison, arithmetic)
- **Maybe**: Optional values (`Option<'a>` alias with helper functions)
- **Result**: Error handling (`Result<'v, 'e>` with combinators)

### Collections

- **List**: List operations and extensions
- **Dict**: Immutable dictionaries (Map<'k, 'v> alias)
- **Set**: Set operations
- **Tuple**: Tuple helper functions

### Primitives

- **String**: String manipulation functions
- **Int**: Integer operations
- **Bool**: Boolean logic
- **Char**: Character operations
- **Decimal**: Decimal arithmetic

### Date/Time

- **LocalDate**: Date without timezone (DateOnly alias)
- **LocalTime**: Time without timezone (TimeOnly alias)

## Usage Examples

### Maybe (Option)

```fsharp
open Morphir.SDK

let user = { name = "Alice"; email = Some "alice@example.com" }

let emailDomain = 
    user.email
    |> Maybe.map (String.split "@")
    |> Maybe.andThen List.last
    |> Maybe.withDefault "unknown"
```

### Result

```fsharp
open Morphir.SDK

let validateAge age =
    if age < 0 then
        Result.err "Age cannot be negative"
    elif age > 150 then
        Result.err "Age is too high"
    else
        Result.ok age

let processAge = 
    validateAge 25
    |> Result.map (fun age -> age + 1)
    |> Result.withDefault 0
```

### List

```fsharp
open Morphir.SDK

let numbers = [1; 2; 3; 4; 5]

let result =
    numbers
    |> List.map (fun x -> x * 2)
    |> List.filter (fun x -> x > 5)
    |> List.sum
// result = 18
```

### Dict

```fsharp
open Morphir.SDK

let scores = 
    Dict.fromList [
        ("Alice", 95)
        ("Bob", 87)
        ("Charlie", 92)
    ]

let aliceScore = Dict.get "Alice" scores
// Some 95

let updatedScores = Dict.insert "David" 88 scores
```

### LocalDate

```fsharp
open Morphir.SDK

let today = LocalDate.today()
let nextWeek = LocalDate.addDays 7 today
let formatted = LocalDate.toIsoString nextWeek
// "2025-01-07"

let parsed = LocalDate.fromIsoString "2025-12-31"
// Some (LocalDate(2025, 12, 31))
```

## Relationship to morphir-elm

This library provides F# implementations of types and functions from [morphir-elm's SDK](https://github.com/finos/morphir-elm/tree/main/src/Morphir/SDK). The API is designed to maintain semantic compatibility while leveraging F#'s native types and idioms.

### Key Mappings

| morphir-elm | Morphir.SDK (F#) | F# Native Type |
|-------------|------------------|----------------|
| `Maybe a` | `Maybe<'a>` | `Option<'a>` |
| `Result v e` | `Result<'v, 'e>` | `Result<'v, 'e>` |
| `List a` | `'a list` | `List<'a>` |
| `Dict k v` | `Dict<'k, 'v>` | `Map<'k, 'v>` |
| `Set a` | `Set<'a>` | `Set<'a>` |
| `LocalDate` | `LocalDate` | `DateOnly` |
| `LocalTime` | `LocalTime` | `TimeOnly` |

## Design Principles

1. **Type Aliases Over Wrappers**: Use F# built-in types directly when possible
2. **Zero-Cost Abstractions**: No runtime overhead for type conversions
3. **Idiomatic F#**: Follow F# conventions and naming patterns
4. **AOT Compatibility**: No reflection or dynamic code generation
5. **Semantic Compatibility**: Maintain morphir-elm behavior

## Version Compatibility

- **Morphir.SDK**: 0.4.0-alpha
- **.NET**: 10.0+
- **F#**: 9.0+
- **morphir-elm**: Compatible with Morphir IR 2.x and 3.x

## Contributing

This library is part of the [morphir-dotnet](https://github.com/finos/morphir-dotnet) project. For contributions, please see the main repository's CONTRIBUTING.md.

## License

Apache License 2.0 - See LICENSE in the morphir-dotnet repository.

## Links

- [morphir-dotnet Repository](https://github.com/finos/morphir-dotnet)
- [Morphir Documentation](https://morphir.finos.org/)
- [morphir-elm SDK](https://github.com/finos/morphir-elm/tree/main/src/Morphir/SDK)
- [FINOS Morphir](https://github.com/finos/morphir)
