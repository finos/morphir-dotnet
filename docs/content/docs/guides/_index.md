---
title: "Guides"
linkTitle: "Guides"
weight: 20
description: "Comprehensive guides for using Morphir .NET"
---

Generate model code with the [Myriad F# reference]({{< relref "/docs/guides/myriad-fsharp" >}}), then follow the [car rental tutorial]({{< relref "/docs/guides/car-rental-tutorial" >}}) through a complete rental and invoice.

Explore our guides to learn how to use Morphir .NET effectively.

## Tutorials

- [Car rental pricing from F# to C#]({{< relref "/docs/guides/fsharp-pricing-tutorial" >}}) - Compile F# rules to Morphir IR, handle a refused construct, call the rules from C#, and check the IR in CI
- [Compile F# with the native executable]({{< relref "/docs/guides/compile-with-native-morphir" >}}) - Use the AOT `morphir` with the F# frontend extension

## Available Guides

- [Configuration]({{< relref "/guides/configuration" >}}) - Current support and the planned shared configuration/project system
- [IR Modeling](/docs/guides/ir-modeling/) - Learn how to model Morphir IR in .NET
- [Serialization](/docs/guides/serialization/) - Working with JSON serialization
- [Testing](/docs/guides/testing/) - Testing strategies and best practices

## Best Practices

- **Immutability First**: Always prefer immutable data structures
- **ADT Design**: Use algebraic data types to make illegal states unrepresentable
- **Type Safety**: Leverage C# 14 features for strong typing
- **Testing**: Write comprehensive tests using TUnit and Reqnroll

