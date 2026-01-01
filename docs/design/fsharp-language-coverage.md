# F# Language Coverage Strategy

This document defines which F# language features are supported by the F# Frontend (F# → Morphir IR parser) and provides a roadmap for incremental feature adoption.

## Executive Summary

The F# Frontend adopts an **incremental, value-driven approach** to language support. We start with a **pure functional subset** (MVP) that covers 80%+ of Morphir use cases, then expand to advanced features (P1), while explicitly excluding features incompatible with Morphir's functional paradigm.

**Strategy**: Start small, validate with real users, expand based on demand.

---

## Support Tiers

### ✅ P0: MVP (Must Have)
Features required for minimum viable product. Covers pure functional programming patterns.

### ✅ P1: Extended (Should Have)
Features that enhance expressiveness but aren't critical for MVP. Added in M4 (Production Ready).

### ⚠️ P2: Future (Could Have)
Features under consideration for future releases. Require design work and may have limitations.

### ❌ Never (Won't Have)
Features fundamentally incompatible with Morphir's functional paradigm. Explicitly not supported.

---

## P0: MVP Features (M0-M3)

### Type Definitions

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Primitive types (`int`, `string`, `bool`, `float`, `char`) | ✅ P0 | M1 | Core types |
| `unit` type | ✅ P0 | M1 | Maps to `()` |
| Tuples (`'T1 * 'T2`) | ✅ P0 | M1 | 2-7 elements |
| Records | ✅ P0 | M1 | Named fields |
| Discriminated unions | ✅ P0 | M1 | Named constructors |
| Type aliases | ✅ P0 | M1 | Simple aliases |
| `Option<'T>` | ✅ P0 | M1 | Maps to `Maybe a` |
| `Result<'T, 'E>` | ✅ P0 | M1 | Maps to `Result e a` |
| `list<'T>` | ✅ P0 | M1 | Maps to `List a` |
| Function types (`'T1 -> 'T2`) | ✅ P0 | M1 | Curried |
| Generic types (basic) | ✅ P0 | M3 | Type parameters |

**Examples:**

```fsharp
// Primitives
type Age = int
type Name = string

// Records
type Customer = {
    CustomerId: int
    Name: string
    Email: string option
}

// Discriminated unions
type OrderStatus =
    | Pending
    | Confirmed of confirmedAt: System.DateTime
    | Shipped of trackingNumber: string
    | Cancelled

// Generics (basic)
type Point<'T> = {
    X: 'T
    Y: 'T
}
```

### Expressions & Values

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Literals (int, string, bool, float, char) | ✅ P0 | M2 | All primitives |
| Tuple expressions | ✅ P0 | M2 | `(1, "hello")` |
| List literals | ✅ P0 | M2 | `[1; 2; 3]` |
| Record construction | ✅ P0 | M2 | `{ Field = value }` |
| DU construction | ✅ P0 | M2 | `Some 42`, `Ok x` |
| Variable references | ✅ P0 | M2 | `x` |
| Function application | ✅ P0 | M2 | `f x y` |
| Lambda expressions | ✅ P0 | M2 | `fun x -> x + 1` |
| Let bindings (simple) | ✅ P0 | M2 | `let x = 42` |
| If-then-else | ✅ P0 | M2 | Ternary |
| Match expressions | ✅ P0 | M2 | Pattern matching |
| Recursive functions | ✅ P0 | M2 | `let rec` |
| Mutually recursive functions | ✅ P0 | M2 | `let rec ... and ...` |

**Examples:**

```fsharp
// Simple function
let add x y = x + y

// Recursive function
let rec factorial n =
    match n with
    | 0 -> 1
    | n -> n * factorial (n - 1)

// Pattern matching
let mapOption f opt =
    match opt with
    | Some value -> Some (f value)
    | None -> None

// Lambda
let increment = fun x -> x + 1

// Let bindings
let calculate x =
    let doubled = x * 2
    let squared = doubled * doubled
    squared
```

### Pattern Matching

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Wildcard pattern (`_`) | ✅ P0 | M2 | Ignore value |
| Variable pattern (`x`) | ✅ P0 | M2 | Bind to name |
| Literal patterns | ✅ P0 | M2 | `42`, `"hello"` |
| Tuple patterns | ✅ P0 | M2 | `(x, y)` |
| Record patterns | ✅ P0 | M2 | `{ Field = x }` |
| DU patterns | ✅ P0 | M2 | `Some x`, `Ok value` |
| List patterns | ✅ P0 | M2 | `[]`, `head :: tail` |
| As-patterns | ✅ P0 | M2 | `x as value` |

**Examples:**

```fsharp
// DU pattern matching
let unwrap opt =
    match opt with
    | Some value -> value
    | None -> failwith "None"

// List pattern matching
let rec sum lst =
    match lst with
    | [] -> 0
    | head :: tail -> head + sum tail

// Tuple pattern matching
let swap (x, y) = (y, x)

// Record pattern matching
let getCustomerId { CustomerId = id } = id
```

### Module System

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Namespaces | ✅ P0 | M0 | `namespace MyDomain` |
| Simple modules | ✅ P0 | M0 | `module Calculator` |
| Top-level functions | ✅ P0 | M2 | `let add x y = ...` |
| Top-level types | ✅ P0 | M1 | Type definitions |
| Documentation comments | ✅ P0 | M1 | `/// Comment` |
| `open` statements (simple) | ✅ P0 | M3 | `open MyModule` |
| Multi-file projects | ✅ P0 | M3 | `.fsproj` support |

**Examples:**

```fsharp
namespace MyDomain

/// Calculator module
module Calculator =
    /// Add two numbers
    let add x y = x + y

    /// Subtract two numbers
    let subtract x y = x - y
```

---

## P1: Extended Features (M4)

### Advanced Type Features

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Generic constraints (basic) | ✅ P1 | M4 | `'T when 'T : comparison` |
| Recursive types | ✅ P1 | M4 | `type Tree<'T> = ...` |
| Mutually recursive types | ✅ P1 | M4 | `type ... and ...` |
| Anonymous records | ⚠️ P2 | Future | `{| X = 1; Y = 2 |}` |
| Struct tuples | ⚠️ P2 | Future | `struct (x, y)` |
| Struct records | ⚠️ P2 | Future | `[<Struct>] type ...` |

**Examples:**

```fsharp
// Generic constraints
let inline max<'T when 'T : comparison> (x: 'T) (y: 'T) =
    if x > y then x else y

// Recursive type
type Tree<'T> =
    | Leaf of 'T
    | Branch of Tree<'T> * Tree<'T>

// Mutually recursive types
type Person = {
    Name: string
    Address: Address
}
and Address = {
    Street: string
    Resident: Person option
}
```

### Advanced Expression Features

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Higher-order functions | ✅ P1 | M3 | `List.map f xs` |
| Function composition | ✅ P1 | M3 | `f >> g`, `f << g` |
| Partial application | ✅ P1 | M3 | `add 1` |
| Pipe operators | ✅ P1 | M3 | `x \|> f` |
| Record update syntax | ✅ P1 | M4 | `{ record with Field = value }` |
| List comprehensions | ✅ P1 | M4 | `[ for x in xs -> f x ]` |
| Array literals | ✅ P1 | M4 | `[| 1; 2; 3 |]` |
| Sequence expressions (simple) | ⚠️ P2 | Future | `seq { yield 1 }` |
| Object expressions | ❌ Never | - | OOP feature |

**Examples:**

```fsharp
// Higher-order functions
let doubled = List.map (fun x -> x * 2) [1; 2; 3]

// Function composition
let addThenDouble = (fun x -> x + 1) >> (fun x -> x * 2)

// Partial application
let add5 = add 5  // Returns: int -> int

// Pipe operator
let result =
    [1; 2; 3]
    |> List.map (fun x -> x * 2)
    |> List.filter (fun x -> x > 2)
    |> List.sum

// Record update
let updatedCustomer = { customer with Email = Some "new@example.com" }

// List comprehension
let squares = [ for x in 1..10 -> x * x ]
```

### Advanced Pattern Features

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| When guards | ✅ P1 | M4 | `match x with \| p when cond -> ...` |
| OR patterns | ✅ P1 | M4 | `\| A \| B -> ...` |
| AND patterns | ⚠️ P2 | Future | `\| (A & B) -> ...` |
| Array patterns | ⚠️ P2 | Future | `[| x; y |]` |
| Active patterns (simple) | ⚠️ P2 | Future | `(|Even|Odd|)` |
| Active patterns (parameterized) | ❌ Never | - | Too complex |
| Active patterns (partial) | ❌ Never | - | Too complex |

**Examples:**

```fsharp
// When guards
let categorize n =
    match n with
    | x when x < 0 -> "Negative"
    | 0 -> "Zero"
    | x when x > 0 -> "Positive"

// OR patterns
let isWeekend day =
    match day with
    | "Saturday" | "Sunday" -> true
    | _ -> false
```

### Advanced Module Features

| Feature | Support | Milestone | Notes |
|---------|---------|-----------|-------|
| Nested modules | ✅ P1 | M4 | `module Inner = ...` |
| Module type annotations | ✅ P1 | M4 | `module M : sig ... end` |
| Qualified opens | ✅ P1 | M4 | `open type MyModule` |
| Auto-open modules | ⚠️ P2 | Future | `[<AutoOpen>]` |
| Module abbreviations | ⚠️ P2 | Future | `module M = LongModule` |
| Signature files (`.fsi`) | ❌ Never | - | Not needed for IR |

**Examples:**

```fsharp
// Nested modules
module Outer =
    module Inner =
        let value = 42

// Module type annotation
module Calculator : sig
    val add : int -> int -> int
end = struct
    let add x y = x + y
end
```

---

## P2: Future Consideration

These features are **not** part of the initial roadmap but may be considered for future releases based on user demand.

| Feature | Status | Rationale |
|---------|--------|-----------|
| Anonymous records | ⚠️ P2 | Can be represented as tuples or named records |
| Struct types | ⚠️ P2 | Performance optimization, not semantic difference |
| Simple active patterns | ⚠️ P2 | Can be desugared to match expressions |
| Sequence expressions (simple) | ⚠️ P2 | Can be represented as lists (lazy evaluation lost) |
| Units of measure | ⚠️ P2 | Type-level feature, could be preserved as metadata |
| Quotations | ⚠️ P2 | Code-as-data, limited Morphir use case |
| Inline functions | ⚠️ P2 | Optimization, not semantic difference |
| Auto-open modules | ⚠️ P2 | Convenience feature, can be explicit |
| Module abbreviations | ⚠️ P2 | Convenience feature, can be explicit |

**Decision Criteria for P2 → P1 Promotion**:
- User demand (5+ requests from different organizations)
- Clear mapping to Morphir IR semantics
- Implementation complexity ≤ 2 weeks
- Doesn't compromise AOT compatibility or performance

---

## ❌ Never Supported

These features are **fundamentally incompatible** with Morphir's functional paradigm and will **never** be supported. The Ionide analyzer will detect and report these as errors.

### Mutation & State

| Feature | Status | Rationale |
|---------|--------|-----------|
| `let mutable` | ❌ Never | Mutability incompatible with Morphir |
| `ref` cells | ❌ Never | Mutable references |
| Arrays (mutable) | ❌ Never | Use immutable lists instead |
| `<-` assignment | ❌ Never | Mutation operator |
| `while` loops | ❌ Never | Imperative construct, use recursion |
| `for` loops (imperative) | ❌ Never | Use `for ... in ... -> ...` (comprehension) instead |

**Why**: Morphir IR is purely functional. Mutation violates referential transparency and prevents many optimizations.

**Alternative**: Use immutable data structures and functional patterns (recursion, fold, map).

### Object-Oriented Features

| Feature | Status | Rationale |
|---------|--------|-----------|
| Classes | ❌ Never | OOP, use records/DUs instead |
| Interfaces | ❌ Never | OOP, use type aliases/DUs instead |
| Inheritance | ❌ Never | OOP, use composition instead |
| Object expressions | ❌ Never | OOP, use records instead |
| Member methods | ❌ Never | OOP, use module functions instead |
| Properties (get/set) | ❌ Never | Mutation, use record fields instead |
| Indexers | ❌ Never | OOP, use functions instead |

**Why**: Morphir models business logic using ADTs (algebraic data types), not OOP hierarchies.

**Alternative**: Use records (for data), discriminated unions (for variants), and module functions (for behavior).

### Computation Expressions

| Feature | Status | Rationale |
|---------|--------|-----------|
| `async { }` | ❌ Never | Side effects (IO, concurrency) |
| `seq { }` | ❌ Never | Lazy evaluation (side effects possible) |
| `task { }` | ❌ Never | Side effects (IO, concurrency) |
| `query { }` | ❌ Never | LINQ-style queries (not in Morphir) |
| Custom builders | ❌ Never | Syntactic sugar for monads (too complex) |

**Why**: Computation expressions often involve side effects (async IO, lazy evaluation). Morphir IR is effect-free.

**Alternative**: Use `Option`, `Result`, and explicit recursion. For async/task, model workflows as pure functions.

### Advanced Features

| Feature | Status | Rationale |
|---------|--------|-----------|
| Type providers | ❌ Never | Compile-time code generation (meta-programming) |
| Reflection | ❌ Never | Runtime introspection (not AOT-compatible) |
| Quotations (advanced) | ❌ Never | Meta-programming, limited use case |
| Parameterized active patterns | ❌ Never | Too complex, unclear IR mapping |
| Partial active patterns | ❌ Never | Too complex, unclear IR mapping |
| Byref/inref/outref | ❌ Never | Low-level mutation (C# interop) |
| Nullable reference types | ❌ Never | Use `Option<'T>` instead |
| Span/Memory | ❌ Never | Low-level performance (not in Morphir) |
| `fixed` keyword | ❌ Never | Unsafe code (pinning) |

**Why**: These features involve meta-programming, low-level optimizations, or C# interop that don't map to Morphir's high-level functional IR.

**Alternative**: Use Morphir's core functional features.

### Side Effects

| Feature | Status | Rationale |
|---------|--------|-----------|
| `printfn`, `printf` | ❌ Never | IO side effect |
| File IO | ❌ Never | IO side effect |
| Network IO | ❌ Never | IO side effect |
| Database access | ❌ Never | IO side effect |
| Exceptions (throw) | ❌ Never | Side effect, use `Result` instead |
| `failwith`, `failwithf` | ❌ Never | Exception-based error handling |
| `raise` | ❌ Never | Exception-based error handling |

**Why**: Morphir IR models pure business logic. Side effects are handled at the boundaries (not in the IR).

**Alternative**: Use `Result<'T, 'E>` for errors, model IO as function signatures (caller provides effects).

---

## Detection & Guidance (Ionide Analyzer)

The **Ionide Analyzer** (`Morphir.Frontends.FSharp.Analyzer`) detects unsupported features in real-time and provides actionable guidance.

### Error Diagnostics (Block Morphir Compatibility)

| Feature Detected | Severity | Message | Suggested Fix |
|-----------------|----------|---------|---------------|
| `let mutable` | ❌ Error | Mutable bindings not supported | Use immutable `let` with function parameters |
| Classes/OOP | ❌ Error | Classes not supported | Use records or discriminated unions |
| Computation expressions | ❌ Error | Computation expressions not supported | Use explicit recursion or `Result`/`Option` |
| `while`/`for` loops | ❌ Error | Imperative loops not supported | Use recursion or list comprehensions |
| `printfn`, IO | ❌ Error | Side effects not supported | Model IO as function signatures |
| Exceptions | ❌ Error | Exceptions not supported | Use `Result<'T, 'E>` for error handling |

### Warning Diagnostics (Future Support Possible)

| Feature Detected | Severity | Message | Suggested Fix |
|-----------------|----------|---------|---------------|
| Active patterns | ⚠️ Warning | Active patterns not yet supported | Use `match` expressions instead |
| Anonymous records | ⚠️ Warning | Anonymous records not yet supported | Use named records or tuples |
| Sequence expressions | ⚠️ Warning | Sequence expressions not yet supported | Use lists instead |

### Info Diagnostics (Guidance)

| Feature Detected | Severity | Message |
|-----------------|----------|---------|
| Type providers | ℹ️ Info | Type providers cannot be converted to Morphir IR |
| Reflection | ℹ️ Info | Reflection not supported in Morphir |

**Example VS Code Experience**:

```fsharp
module Calculator =
    let mutable counter = 0  // ❌ ERROR: Mutable bindings not supported by Morphir
                             //    Suggestion: Use immutable state + function parameters
                             //    Example: let increment count = count + 1

    let add x y = x + y      // ✅ OK

    let (|Even|Odd|) n =     // ⚠️ WARNING: Active patterns not yet supported
        if n % 2 = 0 then Even else Odd
                             //    Suggestion: Use match expression instead
```

---

## Migration Patterns

### Pattern: Mutable State → Immutable State

**Before (Unsupported)**:
```fsharp
let mutable counter = 0
let increment () = counter <- counter + 1
```

**After (Supported)**:
```fsharp
let increment counter = counter + 1

// Usage:
let counter = 0
let newCounter = increment counter
```

### Pattern: Classes → Records + Modules

**Before (Unsupported)**:
```fsharp
type Calculator() =
    member this.Add x y = x + y
    member this.Subtract x y = x - y
```

**After (Supported)**:
```fsharp
// Data (if needed)
type CalculatorState = {
    LastResult: int option
}

// Behavior
module Calculator =
    let add x y = x + y
    let subtract x y = x - y
```

### Pattern: Exceptions → Result

**Before (Unsupported)**:
```fsharp
let divide x y =
    if y = 0 then
        failwith "Division by zero"
    else
        x / y
```

**After (Supported)**:
```fsharp
let divide x y =
    if y = 0 then
        Error "Division by zero"
    else
        Ok (x / y)

// Usage:
match divide 10 0 with
| Ok result -> printfn "Result: %d" result
| Error msg -> printfn "Error: %s" msg
```

### Pattern: Async → Pure Functions

**Before (Unsupported)**:
```fsharp
let fetchCustomer id = async {
    let! customer = Database.getCustomer id
    return customer
}
```

**After (Supported)**:
```fsharp
// Model as function signature (caller provides implementation)
type ICustomerRepository =
    abstract GetCustomer : int -> Customer option

let processCustomer (repo: ICustomerRepository) customerId =
    match repo.GetCustomer customerId with
    | Some customer -> Ok customer
    | None -> Error "Customer not found"
```

### Pattern: Imperative Loops → Recursion

**Before (Unsupported)**:
```fsharp
let sumList lst =
    let mutable total = 0
    for x in lst do
        total <- total + x
    total
```

**After (Supported)**:
```fsharp
let rec sumList lst =
    match lst with
    | [] -> 0
    | head :: tail -> head + sumList tail

// Or use library function:
let sumList lst = List.sum lst
```

---

## Testing Strategy for Language Coverage

### Unit Tests
- Test each supported feature (P0, P1) with at least 3 examples
- Test edge cases (empty lists, zero, negative numbers, etc.)
- Test combinations (nested patterns, generic DUs, etc.)

### Negative Tests
- Test that unsupported features produce clear error messages
- Verify Ionide analyzer detects all "Never" features
- Verify suggested fixes are actionable

### Snapshot Tests
- Capture IR output for 100+ example F# snippets
- Verify IR output matches expected schema
- Detect unintended changes in IR generation

### Round-Trip Tests
- F# → IR → F# → IR (equality verification)
- Test all P0 and P1 features
- Target: 95%+ round-trip success rate

### Compatibility Tests
- Test across F# versions (8, 9, 10)
- Test with different FCS versions (43.x, 44.x)
- Ensure consistent behavior

---

## Roadmap Summary

| Milestone | Features Added | Language Coverage |
|-----------|---------------|-------------------|
| M0 | Parser infrastructure | 0% (no IR generation) |
| M1 | Type definitions (records, DUs, primitives) | ~30% (types only) |
| M2 | Expressions, values, functions, patterns | ~60% (MVP complete) |
| M3 | Generics, higher-order functions, multi-file | ~75% (extended MVP) |
| M4 | Advanced features (P1), Ionide analyzer | ~85% (production ready) |
| Future | P2 features (based on demand) | ~90%+ |

**Coverage Metric**: % of F# language features relevant to pure functional programming (excludes OOP, mutation, side effects by design).

---

## References

- [F# Language Reference](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/)
- [PRD: F# Frontend](./PRD-fsharp-frontend.md) - Complete requirements
- [F# Frontend Maturity Milestones](./fsharp-frontend-maturity-milestones.md) - Implementation roadmap
- [Morphir IR Specification](https://github.com/finos/morphir) - Target IR format
- [Elm to F# Patterns](./morphir-elm-migration-assessment.md) - Elm migration patterns

---

**Document Status**: Draft
**Last Updated**: 2025-12-31
**Next Review**: After M1 completion (update based on real usage)
