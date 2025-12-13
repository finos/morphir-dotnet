---
title: "IR Modeling"
linkTitle: "IR Modeling"
weight: 1
description: "Learn how to model Morphir IR in .NET"
---

## Overview

Morphir IR (Intermediate Representation) is the core data structure that represents your business logic. In Morphir .NET, we model the IR using C# record types and algebraic data types (ADTs).

## Type Expressions

Type expressions represent the types in your Morphir model:

```csharp
public abstract record TypeExpr
{
    public sealed record TInt() : TypeExpr;
    public sealed record TString() : TypeExpr;
    public sealed record TBool() : TypeExpr;
    public sealed record TTuple(IReadOnlyList<TypeExpr> Items) : TypeExpr;
    public sealed record TRecord(IReadOnlyDictionary<string, TypeExpr> Fields) : TypeExpr;
    public sealed record TFunc(TypeExpr Input, TypeExpr Output) : TypeExpr;
}
```

## Value Expressions

Value expressions represent the actual values and computations:

```csharp
public abstract record ValueExpr
{
    public sealed record Literal(LiteralValue Value) : ValueExpr;
    public sealed record Variable(string Name) : ValueExpr;
    public sealed record Lambda(string Parameter, TypeExpr ParameterType, ValueExpr Body) : ValueExpr;
    public sealed record Apply(ValueExpr Function, ValueExpr Argument) : ValueExpr;
}
```

## Best Practices

1. **Use Records**: Prefer `record` types for immutable data structures
2. **Pattern Matching**: Use exhaustive pattern matching for ADTs
3. **Validation**: Implement smart constructors for validated types
4. **Immutability**: Keep all types immutable

## Example

Here's a complete example of modeling a simple function:

```csharp
var addFunction = new ValueExpr.Lambda(
    Parameter: "x",
    ParameterType: new TypeExpr.TInt(),
    Body: new ValueExpr.Lambda(
        Parameter: "y",
        ParameterType: new TypeExpr.TInt(),
        Body: new ValueExpr.Apply(
            Function: new ValueExpr.Variable("+"),
            Argument: new ValueExpr.Tuple(new[]
            {
                new ValueExpr.Variable("x"),
                new ValueExpr.Variable("y")
            })
        )
    )
);
```

