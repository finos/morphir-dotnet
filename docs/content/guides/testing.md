---
title: "Testing"
linkTitle: "Testing"
weight: 3
description: "Testing strategies and best practices for Morphir .NET"
---

## Overview

Morphir .NET supports multiple testing approaches to ensure code quality and correctness.

## Unit Testing with TUnit

TUnit is the primary unit testing framework:

```csharp
using TUnit.Assertions;
using TUnit.Core;

public class TypeExprTests
{
    [Test]
    public void TInt_Should_Be_Equal()
    {
        var type1 = new TypeExpr.TInt();
        var type2 = new TypeExpr.TInt();
        
        Assert.That(type1).IsEqualTo(type2);
    }
}
```

## Behavior-Driven Development with Reqnroll

Reqnroll enables BDD-style testing:

```gherkin
Feature: Type Expression Creation
  Scenario: Create an integer type
    Given I want to create a type expression
    When I create a TInt
    Then it should be a valid type expression
```

## Property-Based Testing

Use property-based testing for invariant validation:

```csharp
[Property]
public bool RoundtripSerialization(TypeExpr typeExpr)
{
    var json = JsonSerializer.Serialize(typeExpr);
    var deserialized = JsonSerializer.Deserialize<TypeExpr>(json);
    return typeExpr.Equals(deserialized);
}
```

## Contract Testing

Test compatibility with Morphir IR format:

```csharp
[Test]
public void Should_Roundtrip_With_Morphir_Elm()
{
    // Load canonical IR sample
    var json = File.ReadAllText("samples/canonical.json");
    var ir = JsonSerializer.Deserialize<IR>(json);
    
    // Serialize back
    var roundtrip = JsonSerializer.Serialize(ir);
    
    // Verify compatibility
    Assert.That(roundtrip).IsValidJson();
}
```

## Best Practices

1. **Exhaustive Testing**: Test all ADT cases
2. **Edge Cases**: Test boundary conditions
3. **Roundtrip Tests**: Always test serialization roundtrips
4. **Property Tests**: Use property-based testing for invariants
5. **Coverage**: Maintain >= 80% code coverage














