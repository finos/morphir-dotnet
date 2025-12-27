---
title: "Serialization"
linkTitle: "Serialization"
weight: 2
description: "Working with JSON serialization in Morphir .NET"
---

## Overview

Morphir .NET provides JSON serialization support for Morphir IR, enabling interoperability with other Morphir tooling. The serialization infrastructure supports:

- **Format Versions 1-3**: Compatible with Morphir IR produced by morphir-elm
- **Future Semantic Versioning**: Designed to support SemVer format versions (4.0.0+)
- **AOT/Trimming Friendly**: No reflection-based serialization

## F# Serialization (Morphir.Models)

The `Morphir.Models` library provides encoder/decoder modules and JSON converters for F# IR types.

### Configuration

```fsharp
open Morphir.Json
open Morphir.IR.Versioning

// Use predefined options for specific format versions
let v1Options = MorphirJsonOptions.v1  // Format version 1
let v2Options = MorphirJsonOptions.v2  // Format version 2
let v3Options = MorphirJsonOptions.v3  // Format version 3 (default)

// Or create custom options
let customOptions = {
    FormatVersion = Classic 3
    WriteIndented = true
}

// For future SemVer versions
let semVerOptions = {
    FormatVersion = SemVer { Major = 4; Minor = 0; Patch = 0; PreRelease = None; BuildMetadata = None }
    WriteIndented = false
}
```

### Encoding and Decoding

Each IR type has a codec module with `encode`, `decode`, and direct writer/reader functions:

```fsharp
open Morphir.IR
open Morphir.Json.Codecs

// Name serialization
let name = Name.fromList [ "foo"; "bar" ]
let encoded = NameCodec.encode name
// Result: ["foo", "bar"]

let decoded = NameCodec.decode encoded
// Result: Ok (Name ["foo"; "bar"])

// Path serialization
let path = Path.fromString "morphir.sdk"
let encodedPath = PathCodec.encode path
// Result: [["morphir"], ["sdk"]]

// FQName serialization
let fqName = FQName.fqNameFromPaths
    (Path.fromString "morphir.sdk")
    (Path.fromString "basics")
    (Name.fromString "int")
let encodedFQ = FQNameCodec.encode fqName
// Result: [[["morphir"], ["sdk"]], [["basics"]], ["int"]]
```

### Literal Serialization with Version-Aware Tags

Literal types use different tag formats depending on the format version:

```fsharp
open Morphir.IR.Classic.Literal
open Morphir.IR.Versioning

let literal = BoolLiteral true

// v1/v2 format: snake_case tags
let v1Encoded = LiteralCodec.encode (Classic 1) literal
// Result: ["bool_literal", true]

// v3+ format: PascalCase tags
let v3Encoded = LiteralCodec.encode (Classic 3) literal
// Result: ["BoolLiteral", true]

// Decoding accepts any format
let decoded = LiteralCodec.decode v1Encoded
// Result: Ok (BoolLiteral true)
```

### Using JsonConverters

For integration with `System.Text.Json`, use the provided JsonConverters:

```fsharp
open System.Text.Json
open Morphir.Json.Codecs

// Create options with converters
let options = JsonSerializerOptions()
options.Converters.Add(NameJsonConverter())
options.Converters.Add(PathJsonConverter())
options.Converters.Add(PackageNameJsonConverter())
options.Converters.Add(ModulePathJsonConverter())
options.Converters.Add(FQNameJsonConverter())
options.Converters.Add(LiteralJsonConverter(MorphirJsonOptions.v3))

// Serialize
let name = Name.fromList [ "test"; "name" ]
let json = JsonSerializer.Serialize(name, options)
// Result: ["test","name"]

// Deserialize
let deserialized = JsonSerializer.Deserialize<Name>(json, options)
// Result: Name ["test"; "name"]
```

### Version-Aware Tag Naming

The `TagNaming` module provides functions for generating version-appropriate tags:

```fsharp
open Morphir.Json
open Morphir.IR.Versioning

// Literal tags
TagNaming.literalTag (Classic 1) "BoolLiteral"   // "bool_literal"
TagNaming.literalTag (Classic 3) "BoolLiteral"   // "BoolLiteral"

// Type tags
TagNaming.typeTag (Classic 1) "Variable"         // "variable"
TagNaming.typeTag (Classic 2) "Variable"         // "Variable"

// Value tags
TagNaming.valueTag (Classic 1) "Apply"           // "apply"
TagNaming.valueTag (Classic 3) "Apply"           // "Apply"

// Pattern tags
TagNaming.patternTag (Classic 1) "AsPattern"     // "as_pattern"
TagNaming.patternTag (Classic 3) "AsPattern"     // "AsPattern"
```

## JSON Format Reference

### Name

All versions serialize names as arrays of lowercase strings:

```json
["first", "name", "segments"]
```

### Path

Paths are arrays of Name arrays:

```json
[["first", "name"], ["second", "name"]]
```

### FQName (Fully-Qualified Name)

FQNames are 3-element arrays: `[packagePath, modulePath, localName]`:

```json
[[["morphir"], ["sdk"]], [["basics"]], ["int"]]
```

### Literal

Literals are 2-element arrays with version-dependent tags:

| Literal Type | v1/v2 Format | v3+ Format |
|-------------|--------------|------------|
| Bool | `["bool_literal", true]` | `["BoolLiteral", true]` |
| Char | `["char_literal", "x"]` | `["CharLiteral", "x"]` |
| String | `["string_literal", "hello"]` | `["StringLiteral", "hello"]` |
| WholeNumber | `["wholenumber_literal", 42]` | `["WholeNumberLiteral", 42]` |
| Float | `["float_literal", 3.14]` | `["FloatLiteral", 3.14]` |
| Decimal | `["decimal_literal", "123.456"]` | `["DecimalLiteral", "123.456"]` |

## Format Version Differences

### Version 1 → Version 2

- **Type tags**: lowercase → PascalCase (`"variable"` → `"Variable"`)
- **Distribution tag**: `"library"` → `"Library"`
- **Access control**: `"public"/"private"` → `"Public"/"Private"`

### Version 2 → Version 3

- **Value tags**: lowercase → PascalCase (`"apply"` → `"Apply"`)
- **Pattern tags**: snake_case → PascalCase (`"as_pattern"` → `"AsPattern"`)
- **Literal tags**: snake_case → PascalCase (`"bool_literal"` → `"BoolLiteral"`)

## Roundtrip Testing

Always test roundtrip serialization to ensure compatibility:

```fsharp
open Expecto
open Morphir.Json.Codecs

[<Tests>]
let roundtripTests =
    testCase "Name roundtrips correctly" <| fun _ ->
        let original = Name.fromList [ "test"; "name" ]
        let encoded = NameCodec.encode original
        let decoded = NameCodec.decode encoded
        Expect.equal decoded (Ok original) "Should roundtrip"
```

## AOT/Trimming Compatibility

The serialization infrastructure is designed for AOT/trimming compatibility:

- All converters are explicitly registered (no reflection discovery)
- No `MakeGenericType` calls in converter factories
- Direct byte-level serialization using `Utf8JsonWriter`/`Utf8JsonReader`

To ensure AOT compatibility in your project:

```xml
<PropertyGroup>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <IsTrimmable>true</IsTrimmable>
</PropertyGroup>
```

## See Also

- [Morphir IR Specification](/docs/spec/morphir-ir-specification/)
- [JSON Schemas](/docs/spec/schemas/)
- [AOT/Trimming Guide](/docs/contributing/aot-trimming-guide/)
