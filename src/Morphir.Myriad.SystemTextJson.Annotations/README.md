# Morphir.Myriad.SystemTextJson.Annotations

Attributes and options for [Morphir.Myriad.SystemTextJson](https://www.nuget.org/packages/Morphir.Myriad.SystemTextJson), a Myriad plugin that generates trim-safe and Native AOT-safe System.Text.Json converters and contexts for F# records, unions and enums at build time.

This package has no dependency and targets `netstandard2.0`. Nothing in it runs: the generator reads the attributes from the F# source at build time.

```fsharp
open Morphir.Myriad.SystemTextJson

[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind")>]
type Shape =
    | [<JsonName "circle">] Circle of radius: float
    | [<JsonName "square">] Square of side: float
```

| Attribute | Use |
| --- | --- |
| `JsonUnion` | The encoding of one union: base encoding and flags, tag and fields names, naming. |
| `JsonRecord` | The member rules of one record: naming, skippable options, unknown members, null fields. |
| `JsonName` | The JSON name of a case or field. More names are accepted when reading. |
| `JsonParsedValue` | A type that is written as text and read only through its parser. |
| `JsonIgnoreType` | Leave a type out of the context. |

The union encodings use the names and meanings of FSharp.SystemTextJson. See the [Morphir.Myriad.SystemTextJson guide](https://finos.github.io/morphir-dotnet/docs/guides/myriad-json/) for the full reference.
