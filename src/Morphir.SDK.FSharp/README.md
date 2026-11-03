# Morphir.SDK.FSharp

F# runtime library providing Morphir SDK types and functions for generated F# code.

## Overview

`Morphir.SDK.FSharp` provides the F# runtime used by generated Morphir code.
Its namespace and runtime assembly remain `Morphir.SDK`.

## Installation

```bash
dotnet add package Morphir.SDK.FSharp
```

## Migrating existing consumers

Replace the `Morphir.SDK` package reference with `Morphir.SDK.FSharp`, using the
same release version. Keep `open Morphir.SDK` and existing generated source
unchanged. Project-reference consumers use
`src/Morphir.SDK.FSharp/Morphir.SDK.FSharp.fsproj`.

The legacy `Morphir.SDK` package remains installable. It depends on the exact
matching canonical package version and contains no runtime assembly. Installing
either package or both resolves the same `Morphir.SDK.dll`. Do not reference
the legacy pack-only project as a runtime project dependency.

The migration preserves the `Morphir.SDK.Order` type identity, existing public
entry points and F# aliases. Aliases such as `Maybe<'a>`, `Dict<'k,'v>`,
`LocalDate` and `LocalTime` retain their FSharp.Core/BCL types. They do not
become new CLR wrapper or forwarding types.

Select the FSharp.Core version required by the package. The captured baseline
uses `11.0.100`; a compiler's lower implicit dependency can otherwise cause
NU1605 and runtime loading failures. For an SDK whose implicit version is lower:

```xml
<PropertyGroup>
  <FSharpCoreImplicitPackageVersion>11.0.100</FSharpCoreImplicitPackageVersion>
  <WarningsAsErrors>$(WarningsAsErrors);NU1605</WarningsAsErrors>
</PropertyGroup>
```

The bounded conformance contract and migration corrections are tracked in
[issue #465](https://github.com/finos/morphir-dotnet/issues/465). Packaging identity
preservation does not claim that behavior corrections replace old code already
inlined into an existing binary.

## Covered behavior changes

The package migration also corrects the cases admitted by the shared
`morphir-dotnet-sdk-v1` / `dotnet-sdk-v1` contract. It does not certify every SDK
operation or every host representation.

- `Dict.union left right` now keeps `left`'s value for a shared key. Previously
  the second dictionary won. For example, unioning `[("rate", 1)]` with
  `[("rate", 9)]` now yields `[("rate", 1)]`. Both operands remain unchanged.
- Int32 `Basics.add`, `subtract`, `multiply` and `negate` report overflow instead
  of wrapping. The generic public entry points remain available.
- `Basics.remainderBy -1 System.Int32.MinValue` and `Basics.modBy` with those
  operands return zero. Previously the host remainder instruction overflowed.
- `Basics.compare` compares direct String operands by ordinal UTF-16 code units,
  without culture or normalization. For example, decomposed `"e\u0301"` sorts
  before precomposed `"\u00e9"`; they no longer compare equal through collation.
- `Decimal.toString` uses invariant positional text and removes fractional
  trailing zeros. `1.50M` becomes `"1.5"`, and scaled zero becomes `"0"`.
  `Decimal.fromString` parses this canonical representation first under every
  culture, then tries the caller's culture for other input. For example, under
  `de-DE`, both `"1.5"` and localized `"1,5"` mean `1.5M`. Ambiguous dot-separated
  input follows canonical decimal-point semantics rather than localized grouping.
  Parsing remains a native API compatibility check outside the frozen inventory.
- Additive `Basics.round`, `floor`, `ceiling` and `truncate` convert Float to
  Int32 with non-finite and overflow checks. Round resolves halves toward
  positive infinity: `round -2.5 = -2`.
- Additive `Decimal.div` returns `None` for a zero divisor and `Some quotient`
  otherwise. Existing `Decimal.divide` still returns decimal and retains its
  existing zero-divisor exception behavior. Exact terminating Decimal96 division
  is covered; nonterminating or unrepresentable results are separate exclusions.

Already compiled inline calls can retain their old arithmetic, union or formatting
behavior. Recompile consumers to use corrected inline bodies. Installing the
canonical runtime alone preserves their type loading; it cannot replace code
embedded in another assembly.

## Features

- **Type Aliases**: Morphir types mapped to idiomatic F# types (e.g., `Maybe<'a>` = `Option<'a>`)
- **Standard Library**: F# SDK functions; the shared contract defines the bounded conformance inventory
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
| `Result e v` | F# `Result<'v, 'e>` | `Result<'v, 'e>` |
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

- **Canonical package**: `Morphir.SDK.FSharp`
- **Compatibility package**: `Morphir.SDK`, at the matching version
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
