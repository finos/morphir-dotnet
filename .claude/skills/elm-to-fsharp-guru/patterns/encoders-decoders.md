# JSON Encoders/Decoders

Multiple approaches for JSON serialization in F# to replace Elm's explicit encoders/decoders.

## Approaches

### 1. System.Text.Json + Source Generators (C# Interop)

```fsharp
open System.Text.Json
open System.Text.Json.Serialization

type User = { Id: int; Name: string }

[<JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)>]
[<JsonSerializable(typeof<User>)>]
type UserJsonContext() =
    inherit JsonSerializerContext()

// Usage
let json = JsonSerializer.Serialize(user, UserJsonContext.Default.User)
```

**When:** C# interop, simple types

### 2. Myriad-Generated (Pure F#, AOT-safe)

```fsharp
[<Generator.Json>]  // Custom plugin
type User = { Id: int; Name: string }

// Generated at compile-time
```

**When:** Pure F#, complex IR types, AOT required

### 3. Manual (Full Control)

```fsharp
module User =
    let encode (user: User) =
        JsonSerializer.SerializeToElement({| id = user.Id; name = user.Name |})
    
    let decode (json: JsonElement) =
        try
            Ok { Id = json.GetProperty("id").GetInt32()
                 Name = json.GetProperty("name").GetString() }
        with ex -> Error ex.Message
```

**When:** Simple types, prototyping

## Decision Matrix

| Scenario | Approach | Reason |
|----------|----------|--------|
| C# interop | Source Generators | Native .NET |
| Pure F# + Complex | Myriad | AOT-safe, consistent |
| Simple (< 5 fields) | Manual | No overhead |

## History

**Version:** 1.0  
**Created:** 2025-12-21
