---
title: "Serialization"
linkTitle: "Serialization"
weight: 2
description: "Working with JSON serialization in Morphir .NET"
---

## Overview

Morphir .NET provides JSON serialization support for Morphir IR, enabling interoperability with other Morphir tooling.

## Basic Usage

### Serializing IR to JSON

```csharp
using Morphir.Core.IR;
using System.Text.Json;

var typeExpr = new TypeExpr.TInt();
var json = JsonSerializer.Serialize(typeExpr, new JsonSerializerOptions
{
    WriteIndented = true
});
```

### Deserializing JSON to IR

```csharp
var json = @"{""_tag"": ""TInt""}";
var typeExpr = JsonSerializer.Deserialize<TypeExpr>(json);
```

## JSON Format

Morphir IR uses a tagged union format in JSON:

```json
{
  "_tag": "TTuple",
  "items": [
    { "_tag": "TInt" },
    { "_tag": "TString" }
  ]
}
```

## Custom Serialization

For custom serialization needs, you can implement your own converters:

```csharp
public class CustomTypeExprConverter : JsonConverter<TypeExpr>
{
    // Implementation
}
```

## Roundtrip Testing

Always test roundtrip serialization to ensure compatibility:

```csharp
var original = new TypeExpr.TInt();
var json = JsonSerializer.Serialize(original);
var deserialized = JsonSerializer.Deserialize<TypeExpr>(json);
Assert.Equal(original, deserialized);
```



