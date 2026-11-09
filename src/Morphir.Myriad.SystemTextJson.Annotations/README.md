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
| `JsonDefaultValue` | A string, Boolean or numeric literal for an absent record member. |
| `JsonDefaultEmpty` | An empty sequence or map for an absent member. |
| `JsonDefaultValueProvider` | A direct call to an accessible static method with no arguments, returning the field's type. |
| `JsonDefaultJson` | An explicit JSON-literal default read through the generated field codec. |
| `JsonOmitEmpty` | Leave an empty record collection out on writing. |
| `JsonVersionAlias` | Read one exact legacy member/version pair and normalize it to the current form. |
| `JsonKnownPaths` | Require every critical path in a string collection to match the exact allowlist. |

Scalar strings are ordinary values, without JSON quoting. Decimal and domain defaults can use `JsonDefaultValueProvider(typeof<Defaults>, nameof Defaults.Create)`; provider code runs only for an absent member at deserialization, and the generated call is checked by the F# compiler. It does not use reflection. Each field can declare only one default. Explicit null retains the field's normal rules.

The generator also supports System.Text.Json's `JsonPropertyName` and `JsonExtensionData` attributes; extension data requires a string-keyed `JsonElement` map record field. Unknown members are preserved beside the named members without a wrapper. New member rules are opt-in; unannotated fields retain their encodings and required-member checks.

The union encodings use the names and meanings of FSharp.SystemTextJson. See the [Morphir.Myriad.SystemTextJson guide](https://finos.github.io/morphir-dotnet/docs/guides/myriad-json/) for the full reference.
