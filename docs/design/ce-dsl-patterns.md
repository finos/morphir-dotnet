# Patterns DSL Design - Computation Expression Builder

**Status**: ✅ Implemented
**Version**: 1.0
**Date**: 2025-12-24

## Overview

The Patterns DSL provides a Computation Expression builder for creating Morphir `Pattern` values used in pattern matching, destructuring, lambda arguments, and case expressions.

## Current Implementation Analysis

### Pattern Types (from morphir-elm spec)

Morphir IR defines 8 pattern types:

1. **WildcardPattern** - Matches any value without binding (`_` in Elm/F#)
2. **AsPattern** - Binds a name to a nested pattern (handles variable bindings)
3. **TuplePattern** - Matches tuple elements
4. **ConstructorPattern** - Matches custom type constructors
5. **EmptyListPattern** - Matches empty lists (`[]`)
6. **HeadTailPattern** - Matches list head/tail (cons pattern `::`)
7. **LiteralPattern** - Matches exact literal values
8. **UnitPattern** - Matches the unit value (`()`)

**Key Insight**: There is NO dedicated `VariablePattern` in Morphir. Variable bindings like `x` in Elm become `AsPattern(WildcardPattern, "x")`.

### Current DSL Features

The current implementation ([Patterns.fs](../../src/Morphir.Models/IR/Classic/DSL/Patterns.fs)) provides:

**Three naming styles**:
1. **Lowercase properties**: `wildcard`, `unit`
2. **Lowercase methods**: `variable(name)`, `tuple(patterns)`, `constructor(...)`
3. **Pascal case CustomOperations**: `Variable`, `Tuple`, `Constructor`, `Literal`, `Unit`
4. **Pascal case regular methods**: `Variable(name)`, `Tuple(patterns)`, etc.

**Example Usage**:
```fsharp
// Lowercase property style
pattern { wildcard }
pattern { unit }

// Lowercase method style
pattern { variable "x" }
pattern { tuple [ p1; p2 ] }

// Pascal case CustomOperation style
pattern { Variable "x" }
pattern { Tuple [ p1; p2 ] }

// Pascal case method style (callable outside CE)
pattern.Variable(Name.fromString "x")
pattern.Tuple([ p1; p2 ])
```

## Design Questions for Review

### Question 1: Naming Convention Standardization

**Current State**: Three different naming styles create flexibility but potential confusion.

**Options**:

**A. Pascal Case Only** (Consistent with Literals DSL)
```fsharp
pattern { Variable "x" }
pattern { Tuple [ p1; p2 ] }
pattern { Constructor fqName [ arg ] }
pattern { Literal (BoolLiteral true) }
pattern { Unit }
```
- ✅ Consistent with Literals DSL
- ✅ IDE autocomplete works better (shows all options)
- ✅ Clear distinction from F# keywords
- ❌ Less "DSL-like" feel

**B. Lowercase Only** (More DSL-like)
```fsharp
pattern { wildcard }
pattern { variable "x" }
pattern { tuple [ p1; p2 ] }
pattern { constructor fqName [ arg ] }
pattern { literal (BoolLiteral true) }
pattern { unit }
```
- ✅ Feels more like pattern matching syntax
- ✅ Lowercase matches Elm style
- ❌ Inconsistent with Literals DSL
- ❌ `unit` conflicts with F# `unit` type

**C. Hybrid** (Current - Maximum Flexibility)
- ✅ Users can choose their preferred style
- ✅ Both styles work
- ❌ Two ways to do the same thing
- ❌ Inconsistent across codebase

**Decision**: **Option A (Pascal Case Only)** - Implemented for consistency with Literals DSL and to avoid F# keyword conflicts.

---

### Question 2: Zero() Default Pattern

**Current**: `Zero() = WildcardPattern defaultAttrs`

**Rationale**: Wildcard matches anything, neutral default.

**Alternatives**:
- `UnitPattern` - Matches only unit
- Throw exception - Fail if pattern block is empty
- Keep `WildcardPattern` ✅ (recommended)

**Usage** (rare case):
```fsharp
let emptyPattern = pattern { () }  // Returns WildcardPattern
```

**Decision**: Keep `WildcardPattern` as Zero() - it's the most permissive and rarely used anyway.

---

### Question 3: Variable Pattern Implementation

**Current Approach**: Variable patterns are `AsPattern(WildcardPattern, name)`

This is **correct per morphir-elm spec**: "when there is just a variable name in a pattern in Elm it will be represented as a WildcardPattern wrapped in an AsPattern."

**Example**:
```fsharp
// User writes:
pattern { variable "x" }

// Creates:
AsPattern((), WildcardPattern(), Name.fromString "x")
```

**This accurately represents the Morphir IR** ✅

**No changes needed** - implementation is spec-compliant.

---

### Question 4: Property vs Method for Nullary Patterns

**Current**: `wildcard` and `unit` are lowercase properties:
```fsharp
member _.wildcard = WildcardPattern defaultAttrs
member _.unit = UnitPattern defaultAttrs
```

**Issue**: Properties work in CE but can conflict with F# types/keywords:
- `unit` property conflicts with `unit` type
- Can cause ambiguity in some contexts

**Alternative**: Make them zero-argument methods:
```fsharp
member _.Wildcard() = WildcardPattern defaultAttrs
member _.Unit() = UnitPattern defaultAttrs

// Usage (requires parens):
pattern { Wildcard() }
pattern { Unit() }
```

**Or**: Use CustomOperations only:
```fsharp
[<CustomOperation("Wildcard")>]
member _.WildcardOp(_: Pattern<'a>) = WildcardPattern defaultAttrs

// Usage (no parens needed in CE):
pattern { Wildcard }
```

**Decision**: Use CustomOperations for nullary patterns to avoid keyword conflicts and maintain consistency. **Implemented**.

---

### Question 5: Tagless Pattern Support?

Unlike Literals (where we added `literal { true }` tagless syntax), patterns don't have obvious type inference candidates.

**Could we add**:
```fsharp
// Yield overload for string → variable pattern?
member _.Yield(name: string) = AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

// Usage:
pattern { "x" }  // Creates variable pattern
```

**Analysis**:
- ❌ Ambiguous: Is `"x"` a variable or a literal string pattern?
- ❌ Patterns are structural, not value-based like literals
- ❌ Would need complex overloading for tuple/list patterns

**Decision**: **Do NOT add tagless syntax** for patterns - keep explicit CustomOperations only. **Confirmed**.

---

## Final Design (Implemented)

Based on the analysis, here's the implemented design:

### 1. Pascal Case CustomOperations Only

All lowercase properties and methods have been removed. The DSL now provides:

**CustomOperations** (for CE use):
```fsharp
[<CustomOperation("Wildcard")>]
[<CustomOperation("Variable")>]
[<CustomOperation("Tuple")>]
[<CustomOperation("Constructor")>]
[<CustomOperation("EmptyList")>]
[<CustomOperation("HeadTail")>]
[<CustomOperation("Literal")>]
[<CustomOperation("Unit")>]
[<CustomOperation("AsPattern")>]  // For advanced use
```

**Regular Methods** (for direct use outside CE):
```fsharp
member _.Wildcard() = WildcardPattern defaultAttrs
member _.Variable(name: Name) = AsPattern(defaultAttrs, WildcardPattern defaultAttrs, name)
member _.Variable(name: string) = AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)
// ... etc
```

### 2. Keep WildcardPattern as Zero()

```fsharp
member _.Zero() = WildcardPattern defaultAttrs
```

### 3. Limited Tagless Syntax

**Unit Pattern**: Added `Yield((): unit)` overload to support tagless syntax for unit patterns:
```fsharp
pattern { () }  // Creates UnitPattern
```

**Rationale**: Unit pattern is a special case where tagless syntax is unambiguous and ergonomic. The `()` literal clearly maps to `UnitPattern`.

**Tuple Patterns**: Added `Yield` overloads for 2-5 element tuples to support natural tuple syntax:
```fsharp
// Define tuple outside CE to avoid F# CE ambiguity
let tup = (pattern { Variable "x" }, pattern { Variable "y" })
pattern { tup }  // Creates TuplePattern

// Works for 2-5 tuples
let tup2 = (p1, p2)
let tup3 = (p1, p2, p3)
let tup4 = (p1, p2, p3, p4)
let tup5 = (p1, p2, p3, p4, p5)
```

**Rationale**: Tuples are a structural pattern where the F# tuple syntax `(p1, p2)` maps naturally to `TuplePattern`. However, F# computation expressions treat inline tuples specially, so the tuple must be defined outside the CE block.

**Note on CE Tuple Ambiguity**: F# computation expressions have special handling for tuple syntax `(a, b)` that makes inline tuples ambiguous. Users must define tuples outside the CE:
```fsharp
// ❌ This fails - F# sees it as CE-specific syntax
pattern { (p1, p2) }

// ✅ This works - tuple defined outside CE
let tup = (p1, p2)
pattern { tup }
```

**No other tagless patterns**: Other patterns remain explicit to avoid ambiguity.

### 4. Fluent API for AsPattern

Added `.As()` extension methods to `Pattern<'attributes>` type for ergonomic AsPattern creation:

```fsharp
// Fluent API with string name
let pattern1 = (pattern { Variable "x" }).As("value")

// Fluent API with Name type
let pattern2 = (pattern { Tuple [ p1; p2 ] }).As(Name.fromString "point")

// Fluent API with explicit attributes
let pattern3 = (pattern { Wildcard }).As((), "any")
```

**Rationale**: AsPattern (wrapping a pattern and binding it to a name) is conceptually similar to name binding. The fluent `.As()` method provides a more natural syntax than the CustomOperation, especially when composing patterns.

**Available overloads**:
```fsharp
type Pattern<'attributes> with
    member this.As(name: string) : Pattern<'attributes>
    member this.As(name: Name) : Pattern<'attributes>
    member this.As(attributes: 'attributes, name: string) : Pattern<'attributes>
    member this.As(attributes: 'attributes, name: Name) : Pattern<'attributes>
```

### 5. Complete Pattern Coverage

Support all 8 Morphir pattern types with both CustomOperations and regular methods.

---

## Usage Examples

### User Guide: How to Use

```fsharp
open Morphir.IR.Classic.DSL.Patterns
open Morphir.IR.Classic.Pattern
open Morphir.IR.Classic.Literal

// Wildcard pattern (matches anything)
let anyPattern = pattern { Wildcard }

// Variable pattern (binds name)
let xPattern = pattern { Variable "x" }
let yPattern = pattern { Variable (Name.fromString "y") }

// Tuple pattern (CustomOperation style)
let tuplePattern1 = pattern {
    Tuple [
        pattern { Variable "x" }
        pattern { Variable "y" }
    ]
}

// Tuple pattern (tagless style) - NEW in v1.0
let p1 = pattern { Variable "x" }
let p2 = pattern { Variable "y" }
let tup = (p1, p2)
let tuplePattern2 = pattern { tup }

// Nested tuple pattern
let innerTup = (p1, p2)
let inner = pattern { innerTup }
let outerTup = (inner, pattern { Variable "z" })
let nestedTuple = pattern { outerTup }

// Constructor pattern (e.g., Maybe.Just)
let justPattern = pattern {
    Constructor
        (fqName { package ["morphir"; "sdk"]; module' ["maybe"]; local "just" })
        [ pattern { Variable "value" } ]
}

// Literal pattern (exact match)
let truePattern = pattern {
    Literal (BoolLiteral true)
}

// List patterns
let emptyListPattern = pattern { EmptyList }

let consPattern = pattern {
    HeadTail
        (pattern { Variable "head" })
        (pattern { Variable "tail" })
}

// Unit pattern (CustomOperation style)
let unitPattern1 = pattern { Unit }

// Unit pattern (tagless style) - NEW in v1.0
let unitPattern2 = pattern { () }

// AsPattern (CustomOperation style - advanced)
let asPattern1 = pattern {
    AsPattern
        (pattern { Tuple [ pattern { Variable "x" }; pattern { Variable "y" } ] })
        (Name.fromString "point")
}
// Matches (x, y) and also binds the whole tuple as "point"

// AsPattern (Fluent API style) - NEW in v1.0
let asPattern2 =
    (pattern { Tuple [ pattern { Variable "x" }; pattern { Variable "y" } ] }).As("point")

// More fluent examples
let wildcardWithName = (pattern { Wildcard }).As("any")
let tupleWithName =
    (pattern {
        Tuple [
            pattern { Variable "x" }
            pattern { Variable "y" }
        ]
    }).As("coordinates")

// Direct constructor usage (outside CE)
let directWildcard = wildcardPattern ()
let directVariable = asPattern () (wildcardPattern ()) (Name.fromString "x")
```

### Real-World Example: Case Expression

```fsharp
open Morphir.IR.Classic.DSL.Values
open Morphir.IR.Classic.DSL.Patterns

// match maybeValue with
// | Just value -> value
// | Nothing -> 0

let caseExpr = value {
    PatternMatch maybeValue [
        // Just value -> value
        (pattern {
            Constructor
                (fqName { package ["morphir"; "sdk"]; module' ["maybe"]; local "just" })
                [ pattern { Variable "value" } ]
         },
         value { Variable "value" })

        // Nothing -> 0
        (pattern {
            Constructor
                (fqName { package ["morphir"; "sdk"]; module' ["maybe"]; local "nothing" })
                []
         },
         value { Literal (WholeNumberLiteral 0L) })
    ]
}
```

---

## BDD Scenarios

```gherkin
Feature: Pattern DSL Builder
  As a morphir-dotnet user
  I want to create Pattern IR values for pattern matching
  So that I can build case expressions and destructuring

  Scenario: Create wildcard pattern
    Given I want to match any value without binding
    When I write "pattern { Wildcard }"
    Then the result should be "WildcardPattern ()"

  Scenario: Create variable pattern
    Given I want to bind a value to the name "x"
    When I write "pattern { Variable \"x\" }"
    Then the result should be "AsPattern((), WildcardPattern(), Name(\"x\"))"

  Scenario: Create tuple pattern
    Given I want to match a tuple of two values
    When I write "pattern { Tuple [ pattern1; pattern2 ] }"
    Then the result should destructure both tuple elements

  Scenario: Create constructor pattern
    Given I want to match the Just constructor with argument pattern
    When I write "pattern { Constructor fqName [ argPattern ] }"
    Then the result should match that specific constructor

  Scenario: Create literal pattern
    Given I want to match the exact boolean value true
    When I write "pattern { Literal (BoolLiteral true) }"
    Then the result should only match true

  Scenario: Create empty list pattern
    Given I want to match an empty list
    When I write "pattern { EmptyList }"
    Then the result should be "EmptyListPattern ()"

  Scenario: Create head-tail pattern
    Given I want to match a non-empty list as head and tail
    When I write "pattern { HeadTail headPattern tailPattern }"
    Then the result should destructure the list

  Scenario: Create unit pattern with CustomOperation
    Given I want to match the unit value
    When I write "pattern { Unit }"
    Then the result should be "UnitPattern ()"

  Scenario: Create unit pattern with tagless syntax
    Given I want to match the unit value using tagless syntax
    When I write "pattern { () }"
    Then the result should be "UnitPattern ()"
    And it should be equivalent to the CustomOperation style

  Scenario: Create tuple pattern with tagless syntax (2-tuple)
    Given I have two patterns p1 and p2
    When I write "let tup = (p1, p2); pattern { tup }"
    Then the result should be a TuplePattern with 2 elements
    And it should be equivalent to "pattern { Tuple [ p1; p2 ] }"

  Scenario: Create tuple pattern with tagless syntax (3-tuple)
    Given I have three patterns p1, p2, and p3
    When I write "let tup = (p1, p2, p3); pattern { tup }"
    Then the result should be a TuplePattern with 3 elements

  Scenario: Create nested tuple pattern with tagless syntax
    Given I have an inner tuple pattern
    When I create an outer tuple containing the inner pattern
    Then the result should be a nested TuplePattern structure

  Scenario: Combine tuple tagless syntax with fluent As
    Given I have a tuple pattern created with tagless syntax
    When I call ".As(\"point\")" on it
    Then the result should be an AsPattern wrapping the tuple
    And the name should be "point"

  Scenario: Create as-pattern for named binding with CustomOperation
    Given I want to match a tuple and also bind it to "point"
    When I write "pattern { AsPattern tuplePattern (Name.fromString \"point\") }"
    Then the nested pattern matches AND the name is bound

  Scenario: Create as-pattern using fluent API with string name
    Given I have a tuple pattern and want to bind it to "coordinates"
    When I write "(pattern { Tuple [ p1; p2 ] }).As(\"coordinates\")"
    Then the result should be an AsPattern wrapping the tuple
    And the name should be "coordinates"

  Scenario: Create as-pattern using fluent API with Name type
    Given I have a wildcard pattern and want to bind it to a Name
    When I write "(pattern { Wildcard }).As(Name.fromString \"value\")"
    Then the result should be an AsPattern wrapping the wildcard
    And the name should be the provided Name instance

  Scenario: Create as-pattern using fluent API with explicit attributes
    Given I have a variable pattern and want to bind it with specific attributes
    When I write "(pattern { Variable \"x\" }).As((), \"item\")"
    Then the result should be an AsPattern with the specified attributes
    And the nested pattern and name should be preserved

  Scenario: Empty pattern block returns wildcard
    Given I have an empty CE block
    When I write "pattern { }"
    Then the result should be "WildcardPattern ()"
```

---

## Contributor Guide: How to Extend

### Architecture

The PatternBuilder uses the **Hybrid CE Pattern** with three builder classes:

1. **`PatternBuilder<'attributes>`** - Generic version for any attributes
2. **`PatternBuilderWithAttrs<'attributes>`** - Explicit attributes version
3. **`PatternBuilder()`** - Sealed version for `unit` attributes

```fsharp
type PatternBuilder() =
    let defaultAttrs = ()

    // Standard CE methods
    member _.Yield(pattern: Pattern<unit>) = pattern
    member _.Combine(_, pattern: Pattern<unit>) = pattern
    member _.For(items, f) = items |> Seq.map f |> Seq.last
    member _.Zero() = WildcardPattern defaultAttrs
    member _.Delay(f: unit -> Pattern<unit>) = f
    member _.Run(f: unit -> Pattern<unit>) = f()

    // CustomOperations
    [<CustomOperation("Wildcard")>]
    member _.WildcardOp(_: Pattern<unit>) = WildcardPattern defaultAttrs

    [<CustomOperation("Variable")>]
    member _.VariableOp(_: Pattern<unit>, name: string) =
        AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

    // Regular methods (callable outside CE)
    member _.Wildcard() = WildcardPattern defaultAttrs
    member _.Variable(name: string) =
        AsPattern(defaultAttrs, WildcardPattern defaultAttrs, Name.fromString name)

    // Attributes support
    member _.WithAttributes<'a>(attrs: 'a) = PatternBuilderWithAttrs<'a>(attrs)
```

### Adding a New Pattern Type

If Morphir adds a new pattern type (hypothetical `RegexPattern`):

1. **Update Pattern DU** ([Pattern.fs](../../src/Morphir.Models/IR/Classic/Pattern.fs)):
```fsharp
type Pattern<'attributes> =
    | WildcardPattern of 'attributes
    | RegexPattern of 'attributes * string  // New!
    // ... existing cases
```

2. **Add constructor function**:
```fsharp
let regexPattern<'attributes> (attributes: 'attributes) (pattern: string) : Pattern<'attributes> =
    RegexPattern(attributes, pattern)
```

3. **Add CustomOperation** ([Patterns.fs](../../src/Morphir.Models/IR/Classic/DSL/Patterns.fs)):
```fsharp
[<CustomOperation("Regex")>]
member _.RegexOp(_: Pattern<'attributes>, pattern: string) =
    RegexPattern(defaultAttrs, pattern)
```

4. **Add regular method**:
```fsharp
member _.Regex(pattern: string) = RegexPattern(defaultAttrs, pattern)
```

5. **Update toString**:
```fsharp
let rec toString (pattern: Pattern<'attributes>) : string =
    match pattern with
    | RegexPattern(_, pat) -> $"/{pat}/"
    // ... existing cases
```

6. **Add tests**:
```fsharp
testCase "Creates RegexPattern"
<| fun _ ->
    let result = pattern { Regex "\\d+" }
    let expected = regexPattern () "\\d+"
    result |> Expect.equal expected
```

---

## Migration Guide

If you have existing code using the old lowercase style:

### Before (Old Mixed Style):
```fsharp
pattern { wildcard }          // Lowercase property - REMOVED
pattern { variable "x" }      // Lowercase method - REMOVED
pattern { tuple [ p1; p2 ] }  // Lowercase method - REMOVED
pattern { unit }              // Lowercase property - REMOVED
```

### After (Pascal Case Only):
```fsharp
pattern { Wildcard }          // Pascal CustomOperation
pattern { Variable "x" }      // Pascal CustomOperation
pattern { Tuple [ p1; p2 ] }  // Pascal CustomOperation
pattern { Unit }              // Pascal CustomOperation
```

### Direct Method Usage (Outside CE):
```fsharp
// Old (still works)
pattern.Wildcard()
pattern.Variable("x")
pattern.Tuple([ p1; p2 ])

// These are unchanged
```

**Breaking Change**: Yes - lowercase properties and methods have been removed from CE usage.

**Migration**: Simple find/replace:
- `wildcard` → `Wildcard`
- `variable ` → `Variable `
- `tuple ` → `Tuple `
- `constructor ` → `Constructor `
- `emptyList` → `EmptyList`
- `headTail ` → `HeadTail `
- `literal ` → `Literal `
- `unit` → `Unit` (in pattern context)
- `asPattern ` → `AsPattern `

---

## References

- **Implementation**: [src/Morphir.Models/IR/Classic/DSL/Patterns.fs](../../src/Morphir.Models/IR/Classic/DSL/Patterns.fs)
- **Tests**: [tests/Morphir.Models.Tests/IR/DSL/PatternsTests.fs](../../tests/Morphir.Models.Tests/IR/DSL/PatternsTests.fs)
- **Pattern Types**: [src/Morphir.Models/IR/Classic/Pattern.fs](../../src/Morphir.Models/IR/Classic/Pattern.fs)
- **Morphir Spec**: [finos/morphir-elm/src/Morphir/IR/Value.elm](https://github.com/finos/morphir-elm/blob/main/src/Morphir/IR/Value.elm) (Pattern type definition)
- **F# CE Guide**: [docs/contributing/fsharp-coding-guide.md#computation-expressions-and-dsls](../contributing/fsharp-coding-guide.md#computation-expressions-and-dsls)
- **Literals DSL**: [docs/design/ce-dsl-literals.md](./ce-dsl-literals.md) (for comparison)

---

## Decision Summary

1. **✅ Naming Convention**: Standardized on Pascal Case only (consistent with Literals DSL)
2. **✅ Zero() Default**: Keep `WildcardPattern`
3. **✅ Variable Pattern**: Keep current `AsPattern(WildcardPattern, name)` approach (spec-compliant)
4. **✅ Nullary Patterns**: Use CustomOperations (avoids keyword conflicts with F# `unit` type)
5. **✅ No Tagless Syntax**: Patterns remain explicit

**Implementation Complete**: 2025-12-24
- Removed all lowercase properties and methods
- Standardized on Pascal case CustomOperations for CE use
- Added Pascal case regular methods for direct use outside CE
- Complete coverage of all 8 Morphir pattern types

---

**Design Review Completed**: 2025-12-24
**Reviewed By**: Design session with user (Damian)
**Next Builder**: Types or Values DSL
