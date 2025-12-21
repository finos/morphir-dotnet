# Custom Types → Discriminated Unions

Elm's custom types map directly to F#'s discriminated unions, with some naming convention differences.

## When to Use

**Use Cases:**
- Converting any Elm `type` declaration
- Representing sum types/tagged unions
- Encoding domain concepts with multiple variants

**Always Applicable:** This is the primary type translation pattern.

## Elm Code

```elm
-- Simple custom type
type Color
    = Red
    = Green
    = Blue

-- Custom type with associated data
type Shape
    = Circle Float
    = Rectangle Float Float
    = Triangle Float Float Float

-- Generic custom type
type Result error value
    = Ok value
    | Err error

-- Custom type with named fields (rare in Elm, uses records usually)
type User
    = Anonymous
    | Registered { id : Int, name : String }
```

## F# Translation

### Approach 1: Direct Translation (Recommended)

```fsharp
// Simple DU
type Color =
    | Red
    | Green
    | Blue

// DU with associated data
type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Triangle of side1: float * side2: float * side3: float

// Generic DU
type Result<'error, 'value> =
    | Ok of 'value
    | Error of 'error  // Note: Use 'Error' not 'Err' (F# convention)

// DU with inline records
type User =
    | Anonymous
    | Registered of id: int * name: string
    // Or: | Registered of User.RegisteredData
```

**Pros:**
- Clean, idiomatic F#
- Pattern matching support
- Type-safe
- AOT-compatible

**Cons:**
- None (this is the standard approach)

**When to Use:** Always, for direct Elm custom type translations

## Naming Conventions

| Elm | F# | Reason |
|-----|-----|--------|
| `Err` | `Error` | F# convention for Result type |
| `camelCase` type params | `'camelCase` | F# generic parameter syntax |
| No naming fields | Named fields with `of name: type` | F# best practice for clarity |

## Examples

### Example 1: Maybe/Option

**Elm:**
```elm
type Maybe a
    = Nothing
    | Just a
```

**F#:**
```fsharp
// Built-in, but if defining yourself:
type Option<'a> =
    | None
    | Some of 'a

// Better: Just use built-in Option
```

### Example 2: Tree Structure

**Elm:**
```elm
type Tree a
    = Empty
    | Node a (Tree a) (Tree a)
```

**F#:**
```fsharp
type Tree<'a> =
    | Empty
    | Node of value: 'a * left: Tree<'a> * right: Tree<'a>
```

### Example 3: Morphir IR Type

**Elm:**
```elm
type Type
    = Variable String
    | Reference QualifiedName (List Type)
    | Tuple (List Type)
    | Record (List Field)
    | Function Type Type
```

**F#:**
```fsharp
type Type =
    | Variable of name: string
    | Reference of qualifiedName: QualifiedName * typeArgs: Type list
    | Tuple of elements: Type list
    | Record of fields: Field list
    | Function of input: Type * output: Type
```

## Pattern Matching

**Elm:**
```elm
describe : Shape -> String
describe shape =
    case shape of
        Circle r ->
            "Circle with radius " ++ String.fromFloat r
        
        Rectangle w h ->
            "Rectangle " ++ String.fromFloat w ++ "x" ++ String.fromFloat h
        
        Triangle a b c ->
            "Triangle with sides " ++ String.fromFloat a ++ ", " ++ String.fromFloat b ++ ", " ++ String.fromFloat c
```

**F#:**
```fsharp
let describe (shape: Shape) : string =
    match shape with
    | Circle radius ->
        $"Circle with radius {radius}"
    
    | Rectangle (width, height) ->
        $"Rectangle {width}x{height}"
    
    | Triangle (a, b, c) ->
        $"Triangle with sides {a}, {b}, {c}"
```

## AOT Compatibility

✅ **AOT-Safe** - Discriminated unions are fully AOT-compatible with no reflection required.

## Testing Strategy

**Unit Test:**
```fsharp
[<Test>]
let ``Circle description`` () =
    let shape = Circle 5.0
    let desc = describe shape
    Assert.Equal("Circle with radius 5", desc)
```

**Property Test:**
```fsharp
[<Property>]
let ``All shapes have non-empty descriptions`` (shape: Shape) =
    describe shape |> String.IsNullOrWhiteSpace |> not
```

## Common Pitfalls

### Pitfall 1: Forgetting Named Parameters

**Problem:** F# allows unnamed tuple members, but they're less readable.

```fsharp
// ❌ BAD: Unnamed
type Rectangle of float * float

// ✅ GOOD: Named
type Rectangle of width: float * height: float
```

**Solution:** Always name tuple members in DU cases.

### Pitfall 2: Using Err Instead of Error

**Problem:** Elm uses `Err` but F# convention is `Error`.

```fsharp
// ❌ BAD: Elm naming
type Result<'a, 'e> = Ok of 'a | Err of 'e

// ✅ GOOD: F# convention
type Result<'a, 'e> = Ok of 'a | Error of 'e
```

**Solution:** Follow F# conventions unless maintaining exact compatibility is critical.

## Related Patterns

- [Type Aliases](./type-aliases.md) - For Elm `type alias`
- [Opaque Types](./opaque-types.md) - For smart constructors
- [Maybe/Result](./maybe-result.md) - Standard library types

## References

- [F# Discriminated Unions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions)
- [Elm Custom Types](https://guide.elm-lang.org/types/custom_types.html)

## History

**Version:** 1.0  
**Created:** 2025-12-21  
**Last Updated:** 2025-12-21  
**Times Used:** 0
