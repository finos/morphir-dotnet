# Values DSL Design Review

**Status**: 🔄 In Review
**Date**: 2025-12-26
**Reviewers**: morphir-dotnet team

## Executive Summary

The Values DSL provides computation expression builders for creating Morphir IR value expressions. Unlike Types, Patterns, and Literals which are simple data structures, Values represent complex expressions including lambdas, let bindings, pattern matching, function application, and more.

**Current State**:
- ✅ Has both generic `ValueBuilder<'typeAttributes, 'valueAttributes>` and specialized `ValueBuilder()`
- ✅ Supports CustomOperations for query-style syntax (`variable`, `literal`, `tuple`, etc.)
- ✅ Has Pascal-case methods for direct usage
- ⚠️ NOT reviewed for Fun.Blazor style (still uses Pascal-case for some methods)
- ⚠️ Missing `Yield(unit)` overload for CustomOperation initial state
- ⚠️ No tagless syntax for value literals (e.g., `value { 42 }`, `value { "hello" }`)
- ⚠️ Complex - has 16 different Value constructors vs 6 for Type

## Value Type Structure

```fsharp
type Value<'typeAttributes, 'valueAttributes> =
    // Simple values
    | Literal of 'valueAttributes * Literal
    | Constructor of 'valueAttributes * FQName
    | Variable of 'valueAttributes * Name
    | Reference of 'valueAttributes * FQName
    | Unit of 'valueAttributes

    // Collections
    | Tuple of 'valueAttributes * Value list
    | List of 'valueAttributes * Value list
    | Record of 'valueAttributes * Map<Name, Value>

    // Field access
    | Field of 'valueAttributes * Value * Name
    | FieldFunction of 'valueAttributes * Name

    // Functions
    | Lambda of 'valueAttributes * Pattern * Value
    | Apply of 'valueAttributes * Value * Value

    // Let bindings
    | LetDefinition of 'valueAttributes * Name * ValueDefinition * Value
    | LetRecursion of 'valueAttributes * Map<Name, ValueDefinition> * Value

    // Control flow
    | IfThenElse of 'valueAttributes * Value * Value * Value
    | PatternMatch of 'valueAttributes * Value * (Pattern * Value) list
    | Destructure of 'valueAttributes * Pattern * Value * Value

    // Record update
    | UpdateRecord of 'valueAttributes * Value * Map<Name, Value>
```

## Design Goals

### 1. **Consistency with Other DSLs**
Follow the patterns established in Types, Patterns, and Literals DSLs:
- Lowercase CustomOperations (Fun.Blazor style)
- `Yield(unit)` overload for CustomOperation support
- `Zero()` returns appropriate default (Unit value)
- Tagless syntax where applicable

### 2. **Support Both Declarative and Imperative Styles**
Values are complex enough to benefit from both:
- **Declarative**: `value { variable "x" }`, `value { literal (BoolLiteral true) }`
- **Imperative**: Direct method calls `value.Variable("x")`, `value.Literal(BoolLiteral true)`

### 3. **Tagless Syntax for Simple Values**
Where type inference works, support tagless:
- `value { 42 }` → `Literal ((), WholeNumberLiteral 42L)`
- `value { "hello" }` → `Literal ((), StringLiteral "hello")`
- `value { true }` → `Literal ((), BoolLiteral true)`

### 4. **Clean Function Application Syntax**
Function application is common, needs good syntax:
- Current: `value.Apply(funcExpr, argExpr)`
- Possible operator: `funcExpr @@ argExpr` or `funcExpr <| argExpr`

## Proposed Changes

### Change 1: Convert CustomOperations to Lowercase

**Current** (Pascal-case):
```fsharp
[<CustomOperation("literal")>]
member _.LiteralOp(_state: Value<unit, unit>, lit: Literal) = ...

[<CustomOperation("variable")>]
member _.VariableOp(_state: Value<unit, unit>, name: Name) = ...
```

**Proposed** (lowercase - Fun.Blazor style):
```fsharp
[<CustomOperation("literal")>]
member _.literal(_state: Value<unit, unit>, lit: Literal) = ...

[<CustomOperation("variable")>]
member _.variable(_state: Value<unit, unit>, name: Name) = ...
```

**Rationale**: Consistency with Types DSL Fun.Blazor style

### Change 2: Add Yield(unit) Overload

**Problem**: CustomOperations need initial state, causing compilation errors

**Solution**:
```fsharp
/// <summary>
/// Yields unit as Unit value (for CustomOperation initial state).
/// </summary>
member _.Yield((): unit) = Unit defaultAttrs
```

**Placement**: First Yield overload (before `Yield(value: Value)`)

### Change 3: Add Tagless Syntax for Literals

**Proposed** (add Yield overloads for primitive types):
```fsharp
/// Yields a bool as Literal value (tagless syntax)
member _.Yield(value: bool) = Literal(defaultAttrs, BoolLiteral value)

/// Yields a string as Literal value (tagless syntax)
member _.Yield(value: string) = Literal(defaultAttrs, StringLiteral value)

/// Yields an int64 as Literal value (tagless syntax)
member _.Yield(value: int64) = Literal(defaultAttrs, WholeNumberLiteral value)

/// Yields an int as Literal value (tagless syntax)
member _.Yield(value: int) = Literal(defaultAttrs, WholeNumberLiteral (int64 value))

/// Yields a float as Literal value (tagless syntax)
member _.Yield(value: float) = Literal(defaultAttrs, FloatLiteral value)

/// Yields a decimal as Literal value (tagless syntax)
member _.Yield(value: decimal) = Literal(defaultAttrs, DecimalLiteral value)

/// Yields a char as Literal value (tagless syntax)
member _.Yield(value: char) = Literal(defaultAttrs, CharLiteral value)
```

**Usage**:
```fsharp
let fortyTwo = value { 42 }  // Literal ((), WholeNumberLiteral 42L)
let greeting = value { "hello" }  // Literal ((), StringLiteral "hello")
let flag = value { true }  // Literal ((), BoolLiteral true)
```

### Change 4: Application Operator (Optional)

**Option A**: Use `@@` operator
```fsharp
let (@@) (f: Value<'t, 'v>) (arg: Value<'t, 'v>) : Value<'t, 'v> =
    Apply((), f, arg)
```

**Option B**: Use extension method `.Apply()`
```fsharp
type Value<'typeAttributes, 'valueAttributes> with
    member this.Apply(arg: Value<'typeAttributes, 'valueAttributes>) =
        Value.Apply((), this, arg)
```

**Usage**:
```fsharp
// Current
let result = value.Apply(funcExpr, argExpr)

// With operator
let result = funcExpr @@ argExpr

// With extension
let result = funcExpr.Apply(argExpr)
```

**Decision**: Extension method approach is cleaner and more discoverable

## Comparison with Elm

### Elm Syntax
```elm
-- Simple values
x = 42
name = "Alice"

-- Function application
result = add 1 2

-- Lambda
increment = \x -> x + 1

-- Let binding
let
    helper y = y * 2
in
    helper 5

-- Pattern matching
case maybe of
    Just x -> x
    Nothing -> 0
```

### Morphir-Dotnet Values DSL

```fsharp
// Simple values (tagless)
let x = value { 42 }
let name = value { "Alice" }

// Function application
let result = value.Apply(addFunc, value { 1 }).Apply(value { 2 })
// Or with extension: addFunc.Apply(value { 1 }).Apply(value { 2 })

// Lambda
let increment = value.Lambda(pattern.Variable("x"),
    value.Apply(value.Reference(fqn ["stdlib"] ["add"] ["add"]),
                value.Variable("x"),
                value { 1 }))

// Let binding (complex - needs ValueDefinition)
// Pattern matching (complex - needs pattern list)
```

**Observation**: Values DSL is more verbose than Elm, but this is expected given the nature of building ASTs programmatically vs writing code.

## Testing Strategy

### Unit Tests Needed

1. **Tagless Literal Syntax**
   ```fsharp
   testCase "Creates Literal value from int (tagless)"
   testCase "Creates Literal value from string (tagless)"
   testCase "Creates Literal value from bool (tagless)"
   ```

2. **CustomOperations with Yield(unit)**
   ```fsharp
   testCase "CustomOperation 'variable' works"
   testCase "CustomOperation 'literal' works"
   testCase "CustomOperation 'tuple' works"
   ```

3. **Complex Expressions**
   ```fsharp
   testCase "Creates Lambda value"
   testCase "Creates Apply value"
   testCase "Creates IfThenElse value"
   testCase "Creates PatternMatch value"
   ```

4. **Extension Methods**
   ```fsharp
   testCase "Apply extension method chains correctly"
   ```

## Open Questions

### Q1: Should we support F# computation expression `let!` and `do!`?

**Context**: F# CEs can support `let!` for binding, which could map to LetDefinition

**Proposal**:
```fsharp
member _.Bind(value: Value<'t, 'v>, f: Value<'t, 'v> -> Value<'t, 'v>) = ...
```

**Usage**:
```fsharp
value {
    let! x = value { 42 }
    let! y = value { 10 }
    return value.Apply(addFunc, x, y)
}
```

**Decision**: **Defer** - This is complex and may not align with Morphir semantics. LetDefinition requires a ValueDefinition, not just a Value.

### Q2: Should we support special syntax for function application?

**Options**:
1. Operator: `funcExpr @@ argExpr`
2. Extension method: `funcExpr.Apply(argExpr)`
3. Both
4. Neither (keep only `value.Apply(funcExpr, argExpr)`)

**Recommendation**: Extension method (option 2) - cleaner chaining, discoverable

### Q3: How should we handle ValueDefinition construction?

**Problem**: ValueDefinition requires `InputTypes`, `OutputType`, and `Body`. This is complex.

**Current**: Direct construction
```fsharp
let def = {
    InputTypes = [(Name.fromString "x", (), Type.variable () (Name.fromString "Int"))]
    OutputType = Type.variable () (Name.fromString "Int")
    Body = value.Variable("x")
}
```

**Proposed**: Helper function
```fsharp
let valueDef inputTypes outputType body = {
    InputTypes = inputTypes
    OutputType = outputType
    Body = body
}
```

**Decision**: Add helper function to Helpers.fs

## Implementation Plan

### Phase 1: Core Changes (High Priority) ✅ COMPLETE
1. ✅ Add `Yield(unit)` overload
2. ✅ Convert CustomOperations to lowercase
3. ✅ Add tagless Yield overloads for primitives
4. ✅ Create comprehensive ValuesTests (26 tests)

**Status**: Completed in commit 7447a7d. All 287 tests passing.

### Phase 2: Enhancements (Medium Priority)
4. ⏳ Add `.Apply()` extension method
5. ⏳ Add ValueDefinition helper to Helpers.fs

### Phase 3: Documentation (Low Priority)
6. ⏳ Add comprehensive examples
7. ⏳ Document patterns for common use cases

## Success Criteria

- [x] All Values DSL tests compile ✅
- [x] All Values DSL tests pass (287/287) ✅
- [x] Tagless syntax works for primitives ✅
- [x] CustomOperations work with lowercase names ✅
- [ ] Extension method for Apply chains correctly (Phase 2)
- [ ] Documentation includes examples of common patterns (Phase 3)

## Related Documents

- [CE DSL Types](./ce-dsl-types.md)
- [CE DSL Patterns](./ce-dsl-patterns.md)
- [CE DSL Literals](./ce-dsl-literals.md)
- [Fun.Blazor DSL Style Guide](https://github.com/slaveOftime/Fun.Blazor)

## Appendix: Value Variants Reference

| Variant | Description | Common Use Case |
|---------|-------------|-----------------|
| Literal | Constant value | `42`, `"hello"`, `true` |
| Constructor | Type constructor | `Just`, `Nothing`, `Cons` |
| Variable | Reference to bound variable | `x`, `acc`, `item` |
| Reference | Reference to defined value | Function reference |
| Unit | Empty value | `()` |
| Tuple | Multiple values | `(x, y)`, `(a, b, c)` |
| List | List of values | `[1, 2, 3]` |
| Record | Named fields | `{ name = "Alice", age = 30 }` |
| Field | Field access | `person.name` |
| FieldFunction | Field accessor function | `.name` |
| Lambda | Anonymous function | `\x -> x + 1` |
| Apply | Function application | `f x`, `add 1 2` |
| LetDefinition | Local binding | `let x = 5 in x * 2` |
| LetRecursion | Mutual recursion | `let rec f = ... and g = ...` |
| IfThenElse | Conditional | `if x > 0 then 1 else 0` |
| PatternMatch | Pattern matching | `case x of ...` |
| Destructure | Pattern destructuring | `let (a, b) = tuple in ...` |
| UpdateRecord | Record update | `{ person | age = 31 }` |
