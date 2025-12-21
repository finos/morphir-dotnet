# Maybe/Result → Option/Result

Elm's `Maybe` and `Result` map to F#'s built-in `Option` and `Result` types with slight naming differences.

## Quick Reference

| Elm | F# |
|-----|-----|
| `Nothing` | `None` |
| `Just x` | `Some x` |
| `Err e` | `Error e` |
| `Ok x` | `Ok x` (same) |

## Elm → F# Mapping

**Elm Maybe:**
```elm
type Maybe a = Nothing | Just a

map : (a -> b) -> Maybe a -> Maybe b
withDefault : a -> Maybe a -> a
andThen : (a -> Maybe b) -> Maybe a -> Maybe b
```

**F# Option:**
```fsharp
// Built-in: type Option<'a> = None | Some of 'a

Option.map : ('a -> 'b) -> 'a option -> 'b option
Option.defaultValue : 'a -> 'a option -> 'a
Option.bind : ('a -> 'b option) -> 'a option -> 'b option
```

**Elm Result:**
```elm
type Result error value = Ok value | Err error

map : (a -> b) -> Result x a -> Result x b
mapError : (x -> y) -> Result x a -> Result y a
andThen : (a -> Result x b) -> Result x a -> Result x b
```

**F# Result:**
```fsharp
// Built-in: type Result<'T, 'Error> = Ok of 'T | Error of 'Error

Result.map : ('a -> 'b) -> Result<'a, 'e> -> Result<'b, 'e>
Result.mapError : ('e1 -> 'e2) -> Result<'a, 'e1> -> Result<'a, 'e2>
Result.bind : ('a -> Result<'b, 'e>) -> Result<'a, 'e> -> Result<'b, 'e>
```

## Computation Expressions

F# adds computation expressions for cleaner chaining:

```fsharp
// Option CE
type OptionBuilder() =
    member _.Bind(x, f) = Option.bind f x
    member _.Return(x) = Some x

let option = OptionBuilder()

let result =
    option {
        let! x = someOption
        let! y = anotherOption
        return x + y
    }

// Result CE (similar)
```

## Related Patterns

- [Custom Types](./custom-types.md)
- [Railway-Oriented Programming](https://fsharpforfunandprofit.com/rop/)

## History

**Version:** 1.0  
**Created:** 2025-12-21
