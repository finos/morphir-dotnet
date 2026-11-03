---
title: "C# SDK"
description: "Immutable C# runtime for the bounded Morphir .NET SDK contract"
weight: 10
---

`Morphir.SDK.CSharp` is an independent net10.0 runtime package. It uses the BCL
and System.Collections.Immutable, with no FSharp.Core, Morphir compiler, model,
CLI or Roslyn dependency. Existing `Morphir.SDK` consumers remain unchanged.

Install the package version published for your application, then use the
`Morphir.SDK.CSharp` namespace. Both C#14 and C#15 consumers use the same API.
The SDK does not require C#15 union syntax or a source generator.

```csharp
using Morphir.SDK.CSharp;

var optional = Maybe<int>.Just(7).Map(x => Basics.Add(x, 1));
var result = Result<string, int>.Ok(7)
    .AndThen(x => x > 0
        ? Result<string, int>.Ok(x)
        : Result<string, int>.Err("negative"));
var old = Dict<string, int>.Singleton("a", 1, MorphirComparers.String);
var updated = old.Insert("b", 2);
// old still contains only a; updated enumerates a then b.
```

Use [the API guide](api.md) for constructors, immutable collections and error
behavior. [The compatibility guide](compatibility.md) describes the bounded
contract, compiler profiles and runtime publication checks.
