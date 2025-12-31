# Morphir.SDK F# Library Plan

**Purpose**: F# runtime library providing Morphir SDK types and functions for generated code
**Created**: 2025-12-31
**Status**: Planning
**Priority**: P0 (Critical dependency for F# backend)

---

## Executive Summary

**Decision**: Create a **Morphir.SDK F# library** (written in F#) that provides runtime implementations of Morphir SDK types and functions. Generated F# code will reference this library instead of using pure type mapping.

### Rationale

1. **Consistency with morphir-scala and morphir-jvm**: Follow proven pattern from other Morphir implementations
2. **Previous Work**: Leverage `main-archive` branch SDK implementation as reference
3. **Easier Compatibility**: F# library closely mirrors Elm reference implementation
4. **No Code Generation Needed**: SDK functions don't need to be generated - just referenced
5. **Versioning**: SDK library can version independently of backend
6. **Shared Across Backends**: Future C# backend can also use this library

### Key Principle

> **"Don't generate what you can reference"** - The SDK provides stable, well-tested implementations that generated code simply imports.

---

## Architecture Decision

### Option A: Pure Mapping (REJECTED)

```fsharp
// In generated code - NO SDK library
// Morphir.SDK.List.map → List.map (F# built-in)
// Morphir.SDK.Maybe.Maybe → Option<'a> (F# built-in)

let result = List.map double [1; 2; 3]  // Pure F# stdlib
```

**Pros**: Zero dependencies, simple mapping
**Cons**:
- ❌ Can't handle complex SDK types (Key, Aggregate, Rule)
- ❌ No versioning of SDK semantics
- ❌ Inconsistent with morphir-scala/morphir-jvm patterns
- ❌ Requires regenerating code for SDK changes

### Option B: Morphir.SDK Library (SELECTED) ✅

```fsharp
// In generated code - WITH SDK library
open Morphir.SDK

let result = List.map double [1; 2; 3]  // Still uses F# List
let key = Key.create "user" 123          // Uses Morphir.SDK.Key
```

**Pros**:
- ✅ Handles complex SDK types (Key, Aggregate, Rule, etc.)
- ✅ Versioning of SDK semantics
- ✅ Consistent with morphir-scala/morphir-jvm
- ✅ SDK updates don't require code regeneration
- ✅ Shared across F# and future C# backends
- ✅ Easier to test (library tested separately)

**Cons**: One additional dependency (acceptable trade-off)

---

## Project Structure

```
src/Morphir.SDK/
├── Morphir.SDK.fsproj           # F# class library
├── README.md                     # SDK documentation
│
├── Basics.fs                     # Basic types and operations
├── Bool.fs                       # Boolean operations (minimal - use F# bool)
├── Char.fs                       # Character operations
├── String.fs                     # String extensions (complement F# String)
├── Int.fs                        # Integer operations
├── Float.fs                      # Float operations
├── Decimal.fs                    # Decimal operations
│
├── Maybe.fs                      # Type alias: type Maybe<'a> = Option<'a>
├── Result.fs                     # Type alias: type Result<'v, 'e> = Result<'v, 'e>
│
├── List.fs                       # List extensions (complement F# List)
├── Dict.fs                       # Dict type: type Dict<'k, 'v> = Map<'k, 'v>
├── Set.fs                        # Set extensions (complement F# Set)
├── Tuple.fs                      # Tuple operations
│
├── LocalDate.fs                  # Type alias: type LocalDate = DateOnly
├── LocalTime.fs                  # Type alias: type LocalTime = TimeOnly
├── Instant.fs                    # Type: type Instant = DateTimeOffset
├── Month.fs                      # Month enumeration
│
├── Regex.fs                      # Regex type and operations
├── UUID.fs                       # UUID type alias
│
├── Key.fs                        # Key<'a> type for lookups
├── Aggregate.fs                  # Aggregation functions
├── Common.fs                     # Shared utilities
│
└── Internal/                     # Internal utilities (not exposed)
    └── Helpers.fs
```

---

## Implementation Strategy

### Phase 0: SDK Library Foundation (New Phase - Week 0)

**Before F# Backend Implementation**:
- [ ] Create `src/Morphir.SDK/Morphir.SDK.fsproj`
- [ ] Review `main-archive` SDK implementation for reference
- [ ] Review morphir-elm `src/Morphir/IR/SDK/` modules
- [ ] Decide which modules to implement (prioritize)
- [ ] Implement core modules (Basics, Maybe, Result, List, Dict)
- [ ] Write comprehensive tests for SDK library
- [ ] Document SDK API (XML docs)
- [ ] Publish SDK as NuGet package (alpha)

### Integration with F# Backend

**Phase 1 (Foundation)**: Update SDK.fs to reference library instead of pure mapping

```fsharp
// In Morphir.Backends.FSharp/SDK.fs

/// Maps Morphir SDK types to Morphir.SDK library types
let sdkTypeMap = Map.ofList [
    // Basics - Use F# built-ins (no library reference needed)
    (["morphir"; "s"; "d"; "k"; "basics"], ["int"]), "int"
    (["morphir"; "s"; "d"; "k"; "basics"], ["float"]), "float"
    (["morphir"; "s"; "d"; "k"; "basics"], ["bool"]), "bool"

    // Maybe - Use F# Option (Morphir.SDK provides type alias)
    (["morphir"; "s"; "d"; "k"; "maybe"], ["maybe"]), "Morphir.SDK.Maybe"
    // Or just "Option" if we don't need the alias

    // Result - Use F# Result (Morphir.SDK provides type alias)
    (["morphir"; "s"; "d"; "k"; "result"], ["result"]), "Morphir.SDK.Result"
    // Or just "Result" if we don't need the alias

    // Collections - Use F# built-ins with Morphir.SDK extensions
    (["morphir"; "s"; "d"; "k"; "list"], ["list"]), "List"  // F# List + Morphir.SDK.List extensions
    (["morphir"; "s"; "d"; "k"; "dict"], ["dict"]), "Morphir.SDK.Dict"  // Type alias for Map
    (["morphir"; "s"; "d"; "k"; "set"], ["set"]), "Set"  // F# Set + Morphir.SDK.Set extensions

    // Date/Time - Use .NET types with Morphir.SDK type aliases
    (["morphir"; "s"; "d"; "k"; "local"; "date"], ["local"; "date"]), "Morphir.SDK.LocalDate"  // DateOnly
    (["morphir"; "s"; "d"; "k"; "local"; "time"], ["local"; "time"]), "Morphir.SDK.LocalTime"  // TimeOnly
    (["morphir"; "s"; "d"; "k"; "instant"], ["instant"]), "Morphir.SDK.Instant"  // DateTimeOffset

    // Complex SDK types - Use Morphir.SDK library
    (["morphir"; "s"; "d"; "k"; "key"], ["key"]), "Morphir.SDK.Key"
    (["morphir"; "s"; "d"; "k"; "aggregate"], ["aggregate"]), "Morphir.SDK.Aggregate"
    (["morphir"; "s"; "d"; "k"; "regex"], ["regex"]), "Morphir.SDK.Regex"
]

let sdkFunctionMap = Map.ofList [
    // List functions - Use Morphir.SDK.List (which may delegate to F# List)
    (["morphir"; "s"; "d"; "k"; "list"], ["map"]), "Morphir.SDK.List.map"
    (["morphir"; "s"; "d"; "k"; "list"], ["filter"]), "Morphir.SDK.List.filter"
    // ... etc

    // Maybe functions - Use Morphir.SDK.Maybe (which may delegate to Option)
    (["morphir"; "s"; "d"; "k"; "maybe"], ["map"]), "Morphir.SDK.Maybe.map"
    // ... etc
]
```

**Generated Code** (with SDK library):

```fsharp
namespace Generated.MyApp.Model

open Morphir.SDK  // Import Morphir SDK library

type Person = {
    name: string
    age: int
    email: Maybe<string>  // Morphir.SDK.Maybe<'a> = Option<'a>
}

let validatePerson (p: Person) : Result<Person, string> =
    if String.isEmpty p.name then
        Result.err "Name cannot be empty"
    else
        Result.ok p
```

---

## Module Implementation Guidelines

### Pattern 1: Type Aliases for F# Built-ins

When Morphir SDK type is identical to F# built-in, provide type alias for semantic clarity:

```fsharp
// Morphir.SDK/Maybe.fs
namespace Morphir.SDK

/// <summary>
/// Morphir Maybe type - alias for F# Option.
/// Use this to maintain semantic alignment with Morphir IR.
/// </summary>
type Maybe<'a> = Option<'a>

module Maybe =
    /// <summary>Maps a function over a Maybe value</summary>
    let inline map f maybe = Option.map f maybe

    /// <summary>Returns the value or a default</summary>
    let inline withDefault defaultValue maybe = Option.defaultValue defaultValue maybe

    // ... other functions delegate to Option module
```

**Rationale**: Maintains Morphir naming while using F# built-in types

### Pattern 2: Extensions to F# Built-ins

When Morphir SDK provides additional functions beyond F# built-in:

```fsharp
// Morphir.SDK/List.fs
namespace Morphir.SDK

module List =
    // Delegate to F# List for existing functions
    let inline map f list = List.map f list
    let inline filter predicate list = List.filter predicate list

    // Add Morphir-specific functions not in F# List
    let sortWith : ('a -> 'a -> Order) -> 'a list -> 'a list =
        fun comparer list ->
            list |> List.sortWith (fun a b ->
                match comparer a b with
                | LT -> -1
                | EQ -> 0
                | GT -> 1
            )

    // ... other extensions
```

### Pattern 3: New Types Unique to Morphir

When Morphir SDK has types not in F#/.NET:

```fsharp
// Morphir.SDK/Key.fs
namespace Morphir.SDK

/// <summary>
/// Key type for lookups - wraps a comparable key value.
/// ADAPTED FROM: morphir-elm src/Morphir/IR/SDK/Key.elm
/// </summary>
type Key<'a when 'a : comparison> = Key of 'a

module Key =
    let create value = Key value
    let value (Key v) = v

    // Implement IComparable for use in Map/Set
    type Key<'a when 'a : comparison> with
        interface IComparable<Key<'a>> with
            member this.CompareTo(other) =
                match this, other with
                | Key a, Key b -> compare a b
```

### Pattern 4: .NET Type Aliases with Extensions

When .NET has the type but we want Morphir-aligned naming and functions:

```fsharp
// Morphir.SDK/LocalDate.fs
namespace Morphir.SDK

open System

/// <summary>
/// Morphir LocalDate - alias for System.DateOnly (.NET 6+)
/// Represents a date without time or timezone.
/// </summary>
type LocalDate = DateOnly

module LocalDate =
    let create year month day = DateOnly(year, month, day)
    let year (date: LocalDate) = date.Year
    let month (date: LocalDate) = date.Month
    let day (date: LocalDate) = date.Day
    let toISOString (date: LocalDate) = date.ToString("yyyy-MM-dd")

    // ... other Morphir-specific functions
```

---

## Morphir.SDK Module Priority

### Phase 0.1: Core Types (Week 0.5)

**Highest Priority** - Needed for basic code generation:

- [ ] **Basics.fs** - Order type, comparison functions
- [ ] **Maybe.fs** - Type alias + functions
- [ ] **Result.fs** - Type alias + functions
- [ ] **List.fs** - Extensions to F# List
- [ ] **String.fs** - Extensions to F# String
- [ ] **Int.fs** - Extensions to F# int
- [ ] **Bool.fs** - Minimal (F# bool sufficient)
- [ ] **Char.fs** - Extensions to F# char

**Deliverable**: Can generate code using basic Morphir types

### Phase 0.2: Collections & Date/Time (Week 0.5)

**High Priority** - Needed for real-world models:

- [ ] **Dict.fs** - Type alias for Map + extensions
- [ ] **Set.fs** - Extensions to F# Set
- [ ] **Tuple.fs** - Tuple helper functions
- [ ] **LocalDate.fs** - DateOnly alias + functions
- [ ] **LocalTime.fs** - TimeOnly alias + functions
- [ ] **Decimal.fs** - Decimal extensions

**Deliverable**: Can generate code with collections and dates

### Phase 0.3: Advanced Types (Week 1 - Can be done in parallel with Phase 1)

**Medium Priority** - Needed for complex models:

- [ ] **Instant.fs** - DateTimeOffset alias + functions
- [ ] **Month.fs** - Month enumeration
- [ ] **UUID.fs** - Guid alias + functions
- [ ] **Regex.fs** - Regex wrapper
- [ ] **Key.fs** - Key type for lookups
- [ ] **Aggregate.fs** - Aggregation functions

**Deliverable**: Full SDK coverage

### Phase 0.4: Documentation & Testing (Ongoing)

- [ ] XML doc comments on all public APIs
- [ ] Unit tests for all modules (≥80% coverage)
- [ ] Examples for each module
- [ ] API reference documentation

---

## Migration from main-archive

### Existing SDK Code to Review

From `main-archive` branch:
```
src/Morphir.SDK.Core/
├── Array.fs
├── Basics.fs          ✅ Can reuse/adapt
├── Bool.fs            ✅ Can reuse/adapt
├── Char.fs            ✅ Can reuse/adapt
├── Comparison.fs      ✅ Can reuse/adapt (Order type)
├── Decimal.fs         ✅ Can reuse/adapt
├── Dict.fs            ✅ Can reuse/adapt
├── Extensions.fs      ✅ Review for useful extensions
... etc
```

### Assessment Checklist

For each module in `main-archive`:
- [ ] Review implementation quality
- [ ] Check if it matches morphir-elm semantics
- [ ] Validate F# idioms used
- [ ] Ensure AOT compatibility (no reflection)
- [ ] Update to .NET 10 / F# 9 if needed
- [ ] Add comprehensive tests
- [ ] Add XML documentation

---

## Testing Strategy

### Unit Tests (Morphir.SDK.Tests)

```fsharp
module Morphir.SDK.Tests.MaybeTests

open TUnit.Core
open Morphir.SDK

[<Test>]
let ``Maybe.map should transform Some values`` () =
    let result = Maybe.map ((*) 2) (Some 5)
    result |> should equal (Some 10)

[<Test>]
let ``Maybe.map should preserve None`` () =
    let result = Maybe.map ((*) 2) None
    result |> should equal None

[<Test>]
let ``Maybe.withDefault should return value for Some`` () =
    let result = Maybe.withDefault 0 (Some 5)
    result |> should equal 5

[<Test>]
let ``Maybe.withDefault should return default for None`` () =
    let result = Maybe.withDefault 0 None
    result |> should equal 0
```

### Property-Based Tests (FsCheck)

```fsharp
module Morphir.SDK.Tests.ListPropertyTests

open FsCheck
open FsCheck.TUnit
open Morphir.SDK

[<Property>]
let ``List.map preserves length`` (list: int list) =
    let result = List.map ((*) 2) list
    List.length result = List.length list

[<Property>]
let ``List.filter is idempotent`` (list: int list) (predicate: int -> bool) =
    let once = List.filter predicate list
    let twice = List.filter predicate once
    once = twice
```

### Compatibility Tests with morphir-elm

Test that SDK behavior matches morphir-elm semantics:

```fsharp
[<Test>]
let ``List.sortWith should match Morphir elm behavior`` () =
    // morphir-elm example: sortWith compareAge people
    let people = [
        { name = "Alice"; age = 30 }
        { name = "Bob"; age = 25 }
        { name = "Charlie"; age = 35 }
    ]

    let compareAge a b =
        if a.age < b.age then LT
        elif a.age > b.age then GT
        else EQ

    let result = List.sortWith compareAge people

    result.[0].name |> should equal "Bob"    // age 25
    result.[1].name |> should equal "Alice"  // age 30
    result.[2].name |> should equal "Charlie" // age 35
```

---

## NuGet Package Strategy

### Package Metadata

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>Morphir.SDK</PackageId>
    <Version>0.1.0-alpha</Version>
    <Title>Morphir SDK for F#</Title>
    <Description>
      F# runtime library providing Morphir SDK types and functions.
      Used by Morphir F# code generator for generated code dependencies.
    </Description>
    <Authors>FINOS Morphir Team</Authors>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageProjectUrl>https://morphir.finos.org</PackageProjectUrl>
    <RepositoryUrl>https://github.com/finos/morphir-dotnet</RepositoryUrl>
    <PackageTags>morphir;fsharp;sdk;functional-programming</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FSharp.Core" Version="9.0.0" />
  </ItemGroup>
</Project>
```

### Versioning Strategy

- **0.1.0-alpha**: Initial SDK with core types (Phase 0.1-0.2)
- **0.2.0-alpha**: Full SDK coverage (Phase 0.3)
- **0.3.0-beta**: After testing with F# backend
- **1.0.0**: Production release with F# backend v1.0.0

---

## Integration with F# Backend Issues

### Updated Issue #2 (Foundation)

Add SDK library creation task:

**Phase 0: Morphir.SDK Library** (NEW - Before Phase 1)
- [ ] Create `src/Morphir.SDK/Morphir.SDK.fsproj`
- [ ] Review `main-archive` SDK code for reuse
- [ ] Implement core modules (Basics, Maybe, Result, List, Dict)
- [ ] Write unit tests (≥80% coverage)
- [ ] Publish as NuGet package (alpha)

**Phase 1: Foundation** (Updated)
- [ ] Update `SDK.fs` to reference Morphir.SDK library (not pure mapping)
- [ ] Generate code that imports `open Morphir.SDK`
- [ ] Test generated code compiles with SDK library dependency

---

## Decision Log

### SDK Library vs. Pure Mapping

**Date**: 2025-12-31

**Decision**: Create Morphir.SDK F# library

**Rationale**:
1. **Consistency**: Follows morphir-scala, morphir-jvm patterns
2. **Prior Art**: `main-archive` branch has SDK implementation to reuse
3. **Complex Types**: Key, Aggregate, Rule types need library implementations
4. **Versioning**: SDK semantics can evolve independently
5. **Testing**: Library tested separately from code generation
6. **Shared**: Can be used by future C# backend

**Trade-offs**:
- **Pros**: Better long-term maintainability, semantic alignment with Morphir
- **Cons**: Additional NuGet dependency (acceptable)

**References**:
- morphir-scala: Has `morphir-sdk` library
- morphir-jvm: Has SDK implementation
- main-archive: `src/Morphir.SDK.Core/` previous work

---

## Next Steps

1. **Create GitHub Issue**: "Phase 0: Morphir.SDK F# Library Implementation"
2. **Prioritize for Phase 0**: Complete before F# backend Phase 1
3. **Review main-archive**: Extract reusable code
4. **Implement core modules**: Basics, Maybe, Result, List (Week 0.5)
5. **Test thoroughly**: Property-based tests + morphir-elm compatibility
6. **Publish alpha**: `Morphir.SDK 0.1.0-alpha`
7. **Update F# backend design**: Reference SDK library in generated code

---

**Status**: Ready for Implementation
**Priority**: P0 (Blocks F# Backend Phase 1)
**Estimated Effort**: 1-2 weeks (parallel with Phase 1 planning)
