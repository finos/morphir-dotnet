# Myriad Basics

When and how to use Myriad for compile-time code generation in F#.

## What is Myriad?

Myriad is F#'s answer to C# source generators - compile-time code generation that produces AOT-compatible code without runtime reflection.

## When to Use Myriad

✅ **Good Use Cases:**
- JSON codecs for 5+ types
- Visitor patterns for IR traversal
- Lenses for nested record updates
- Repetitive boilerplate (equality, comparison)

❌ **Poor Use Cases:**
- One-off code (write manually)
- Simple types (< 3 fields)
- Frequently changing code

## Built-in Generators

```fsharp
[<Generator.Fields>]
type Person = { Name: string; Age: int }
// Generates: Person.Name, Person.Age accessors

[<Generator.DuCases>]
type Shape = Circle of float | Rectangle of float * float
// Generates: helper constructors

[<Generator.Lenses>]
type Config = { Port: int; Host: string }
// Generates: lenses for updates
```

## Decision Tree

```
Pattern repetitive (3+ types)?
├─ YES → Use Myriad
├─ NO → Write manually
```

## References

- [Myriad Docs](https://moiraesoftware.github.io/myriad/)

## History

**Version:** 1.0  
**Created:** 2025-12-21
