# Literals DSL Design - Computation Expression Builder

**Status**: ✅ Implemented
**Version**: 1.0
**Date**: 2025-12-24

## Overview

The Literals DSL provides a hybrid Computation Expression builder for creating Morphir `Literal` values with both tagless (type-inferred) and tagged (explicit) syntax.

## Design Decisions

### 1. Hybrid Pattern (Tagless + Tagged)

**Decision**: Support BOTH tagless (Yield overloads) and tagged (CustomOperations) syntax.

**Rationale**:
- Tagless is ergonomic for common cases: `literal { true }`, `literal { "hello" }`
- Tagged is explicit and self-documenting: `literal { Bool true }`, `literal { String "hello" }`
- Both styles serve different use cases and aid discoverability

**User Perspective - Usage**:

```fsharp
// Tagless style (concise, type-inferred)
let boolLit = literal { true }
let stringLit = literal { "hello" }
let intLit = literal { 42L }
let decimalLit = literal { 123.45m }

// Tagged style (explicit, self-documenting)
let boolLit = literal { Bool true }
let stringLit = literal { String "hello" }
let intLit = literal { Int 42L }
let decimalLit = literal { Decimal 123.45m }

// Both produce the same result
```

**Contributor Perspective - Implementation**:

```fsharp
type LiteralBuilder() =
    // Tagless: Type inference via Yield overloads
    member _.Yield(value: bool) = BoolLiteral value
    member _.Yield(value: string) = StringLiteral value
    member _.Yield(value: int64) = WholeNumberLiteral value
    member _.Yield(value: int) = WholeNumberLiteral(int64 value)  // Auto-convert
    member _.Yield(value: float) = FloatLiteral value
    member _.Yield(value: decimal) = DecimalLiteral value
    member _.Yield(lit: Literal) = lit  // Pass-through existing Literal

    // Tagged: Explicit CustomOperations
    [<CustomOperation("Bool")>]
    member _.BoolOp(_: Literal, value: bool) = BoolLiteral value

    [<CustomOperation("String")>]
    member _.StringOp(_: Literal, value: string) = StringLiteral value

    // ... etc for all types
```

### 2. Decimal Type: .NET `decimal` (not `string`)

**Decision**: Use .NET `decimal` type instead of `string` for `DecimalLiteral`.

**Rationale**:
- Morphir-elm uses native `Decimal` type (not string)
- .NET `decimal` provides 28-29 significant digits of precision
- Type safety: compile-time validation of decimal values
- F# `m` suffix provides clean syntax: `123.45m`
- String representation only needed for serialization (handled in `toString`)

**Before**:
```fsharp
type Literal =
    | DecimalLiteral of string  // Arbitrary precision as string

let decimalLiteral (value: string) : Literal = DecimalLiteral value
```

**After**:
```fsharp
type Literal =
    | DecimalLiteral of decimal  // .NET decimal type

let decimalLiteral (value: decimal) : Literal = DecimalLiteral value
```

**Usage**:
```fsharp
// Tagless
literal { 123.456789m }  // Use m suffix

// Tagged
literal { Decimal 123.456789m }

// Direct constructor
decimalLiteral 123.456789m
```

### 3. Zero() Default: Empty String

**Decision**: `Zero()` returns `StringLiteral ""` (empty string).

**Rationale**:
- Neutral default that works for CE mechanics
- Empty string is more useful than `BoolLiteral false`
- Rarely used in practice (most CE blocks have explicit values)

**Implementation**:
```fsharp
member _.Zero() = StringLiteral ""
```

**Usage**:
```fsharp
let empty = literal { () }  // Returns StringLiteral ""
```

### 4. Int32 Conversion Support

**Decision**: Support automatic `int32` → `int64` conversion.

**Rationale**:
- F# defaults integer literals to `int32`
- Morphir IR uses `int64` for whole numbers
- Auto-conversion improves ergonomics
- Both `42` (int32) and `42L` (int64) work

**Implementation**:
```fsharp
// Yield overloads for both int32 and int64
member _.Yield(value: int64) = WholeNumberLiteral value
member _.Yield(value: int) = WholeNumberLiteral(int64 value)

// CustomOperation overloads for both
[<CustomOperation("Int")>]
member _.IntOp(_: Literal, value: int64) = WholeNumberLiteral value

[<CustomOperation("Int")>]
member _.IntOp(_: Literal, value: int) = WholeNumberLiteral(int64 value)
```

**Usage**:
```fsharp
literal { 42 }    // int32 auto-converted to int64
literal { 42L }   // int64 used directly
literal { Int 42 }   // Tagged, auto-converted
literal { Int 42L }  // Tagged, direct
```

### 5. Complete Literal Type Coverage

**Decision**: Implement all 6 literal types from morphir-elm spec.

**Verification**: Checked against [finos/morphir-elm/src/Morphir/IR/Literal.elm](https://github.com/finos/morphir-elm/blob/main/src/Morphir/IR/Literal.elm)

**Supported Types**:
1. ✅ `BoolLiteral` - Boolean values
2. ✅ `CharLiteral` - Single characters
3. ✅ `StringLiteral` - Text strings
4. ✅ `WholeNumberLiteral` - Integers (int64)
5. ✅ `FloatLiteral` - Floating-point numbers (float/double)
6. ✅ `DecimalLiteral` - Arbitrary precision decimals (decimal type)

**No additional types needed** - complete coverage of morphir-elm specification.

### 6. Removal of Helpers Module

**Decision**: Remove `Helpers` module from DSL.

**Rationale**:
- Constructor functions already exist in `Literal` module (`boolLiteral`, `stringLiteral`, etc.)
- No need for duplicate helper functions
- Single canonical approach: CE for composition, constructors for direct use

**Before**:
```fsharp
module Helpers =
    let boolLiteral (value: bool) = BoolLiteral value
    let stringLiteral (value: string) = StringLiteral value
    // ... etc
```

**After**:
Use constructors from `Literal` module directly:
```fsharp
open Morphir.IR.Classic.Literal

// Direct constructors
let lit1 = boolLiteral true
let lit2 = stringLiteral "hello"
let lit3 = wholeNumberLiteral 42L

// CE syntax
let lit4 = literal { true }
let lit5 = literal { "hello" }
let lit6 = literal { 42L }
```

## BDD Scenarios

```gherkin
Feature: Literal DSL Builder - Hybrid Pattern
  As a morphir-dotnet user
  I want to create Literal IR values using clean, flexible syntax
  So that I can build Morphir expressions ergonomically

  Scenario: Create boolean literal with tagless syntax
    Given I want to represent the boolean value true
    When I write "literal { true }"
    Then the result should be "BoolLiteral true"

  Scenario: Create boolean literal with tagged syntax
    Given I want to explicitly document I'm creating a boolean
    When I write "literal { Bool true }"
    Then the result should be "BoolLiteral true"

  Scenario: Create string literal with tagless syntax
    Given I want to represent the string "hello"
    When I write "literal { \"hello\" }"
    Then the result should be "StringLiteral \"hello\""

  Scenario: Create integer literal with tagless syntax (int32)
    Given I want to represent the number 42 as int32
    When I write "literal { 42 }"
    Then the int32 should be auto-converted to int64
    And the result should be "WholeNumberLiteral 42L"

  Scenario: Create integer literal with tagless syntax (int64)
    Given I want to represent the number 42 as int64
    When I write "literal { 42L }"
    Then the result should be "WholeNumberLiteral 42L"

  Scenario: Create float literal with tagless syntax
    Given I want to represent the number 3.14
    When I write "literal { 3.14 }"
    Then the result should be "FloatLiteral 3.14"

  Scenario: Create character literal with tagless syntax
    Given I want to represent the character 'a'
    When I write "literal { 'a' }"
    Then the result should be "CharLiteral 'a'"

  Scenario: Create decimal literal with tagless syntax
    Given I want to represent a precise decimal 123.45
    When I write "literal { 123.45m }"
    Then the result should be "DecimalLiteral 123.45m"
    And the precision should use .NET decimal type

  Scenario: Create decimal literal with tagged syntax
    Given I want to explicitly create a decimal literal
    When I write "literal { Decimal 123.45m }"
    Then the result should be "DecimalLiteral 123.45m"

  Scenario: Pass through existing Literal value
    Given I have an existing "BoolLiteral true" value
    When I write "literal { existingLiteral }"
    Then the result should be the same literal instance

  Scenario: Empty literal block returns default
    Given I have an empty CE block
    When I write "literal { () }"
    Then the result should be "StringLiteral \"\""
```

## Usage Examples

### User Guide: How to Use

```fsharp
open Morphir.IR.Classic.DSL.Literals
open Morphir.IR.Classic.Literal

// Basic tagless usage (recommended for most cases)
let myBool = literal { true }
let myString = literal { "Hello, Morphir!" }
let myInt = literal { 42 }      // Auto-converted to int64
let myLong = literal { 42L }    // Explicit int64
let myFloat = literal { 3.14159 }
let myChar = literal { 'X' }
let myDecimal = literal { 999.99m }

// Tagged usage (when you want to be explicit)
let explicitBool = literal { Bool false }
let explicitString = literal { String "World" }
let explicitInt = literal { Int 100 }
let explicitDecimal = literal { Decimal 12.34m }

// Direct constructor usage (outside CE)
let constructedBool = boolLiteral true
let constructedString = stringLiteral "Direct"
let constructedDecimal = decimalLiteral 456.78m

// Composing with other DSLs
open Morphir.IR.Classic.DSL.Values

let boolValue = value { literal { true } }
let stringValue = value { literal { "embedded" } }
```

### Contributor Guide: How to Extend

#### Adding a New Literal Type

If Morphir adds a new literal type (e.g., `ByteLiteral`):

1. **Update Literal discriminated union** (`Literal.fs`):
```fsharp
type Literal =
    | BoolLiteral of bool
    | ByteLiteral of byte  // New!
    // ... existing cases
```

2. **Add constructor function** (`Literal.fs`):
```fsharp
let byteLiteral (value: byte) : Literal = ByteLiteral value
```

3. **Add Yield overload for tagless** (`Literals.fs`):
```fsharp
member _.Yield(value: byte) = ByteLiteral value
```

4. **Add CustomOperation for tagged** (`Literals.fs`):
```fsharp
[<CustomOperation("Byte")>]
member _.ByteOp(_state: Literal, value: byte) = ByteLiteral value
```

5. **Update `toString`** (`Literal.fs`):
```fsharp
let toString (literal: Literal) : string =
    match literal with
    | ByteLiteral value -> value.ToString()
    // ... existing cases
```

6. **Add tests** (`LiteralsTests.fs`):
```fsharp
testCase "Creates ByteLiteral with tagless syntax"
<| fun _ ->
    let result = literal { 42uy }
    let expected = ByteLiteral 42uy
    result |> Expect.equal expected
```

## References

- **Implementation**: [src/Morphir.Models/IR/Classic/DSL/Literals.fs](../../src/Morphir.Models/IR/Classic/DSL/Literals.fs)
- **Tests**: [tests/Morphir.Models.Tests/IR/DSL/LiteralsTests.fs](../../tests/Morphir.Models.Tests/IR/DSL/LiteralsTests.fs)
- **Literal Types**: [src/Morphir.Models/IR/Classic/Literal.fs](../../src/Morphir.Models/IR/Classic/Literal.fs)
- **Morphir Spec**: [finos/morphir-elm/src/Morphir/IR/Literal.elm](https://github.com/finos/morphir-elm/blob/main/src/Morphir/IR/Literal.elm)
- **F# CE Guide**: [docs/contributing/fsharp-coding-guide.md#computation-expressions-and-dsls](../contributing/fsharp-coding-guide.md#computation-expressions-and-dsls)

## Migration Guide

If you have existing code using the old `Helpers` module or `string`-based `DecimalLiteral`:

### Before (Old API):
```fsharp
open Morphir.IR.Classic.DSL.Literals

// Old Helpers module
let lit1 = Helpers.boolLiteral true
let lit2 = Helpers.decimalLiteral "123.45"  // String

// Old tagged syntax only
let lit3 = literal { Bool true }
let lit4 = literal { Decimal "123.45" }  // String
```

### After (New API):
```fsharp
open Morphir.IR.Classic.DSL.Literals
open Morphir.IR.Classic.Literal

// Use constructors from Literal module
let lit1 = boolLiteral true
let lit2 = decimalLiteral 123.45m  // decimal type now

// Tagless syntax (new!)
let lit3 = literal { true }
let lit4 = literal { 123.45m }  // decimal type

// Tagged syntax still works
let lit5 = literal { Bool true }
let lit6 = literal { Decimal 123.45m }  // decimal type
```

## Future Considerations

1. **Performance**: Consider adding `[<InlineIfLambda>]` if Literals become a hot path
2. **Additional Types**: Monitor morphir-elm for new literal types (Date/Time, Binary, etc.)
3. **Validation**: Consider adding range/format validation for specific literal types
4. **Documentation**: Add IDE tooltips showing both tagless and tagged syntax options

---

**Design Review Completed**: 2025-12-24
**Reviewed By**: Design session with user (Damian)
**Next Builder**: Types or Patterns DSL
