---
title: "Myriad System.Text.Json generation"
linkTitle: "Myriad JSON"
weight: 36
description: "Generate trim-safe System.Text.Json converters and contexts for F# types at build time"
---

`Morphir.Myriad.SystemTextJson` is the F# counterpart of the System.Text.Json source generator. The C# generator reads only C# code, so F# projects that must be trimmed or compiled with Native AOT had two choices: write each JSON codec by hand, or use a library that serializes through reflection. This Myriad plugin removes that choice. From your records, unions and enums it generates at build time:

- one `JsonConverter<T>` for each type, which reads and writes with `Utf8JsonReader` and `Utf8JsonWriter`;
- a context type that derives from `JsonSerializerContext`, with a `JsonTypeInfo<T>` member for each type and a `Default` instance.

The generated code uses no reflection. You call it through the normal `JsonSerializer` API, as you call a C# generated context. The union encodings use the names and the JSON of [FSharp.SystemTextJson](https://github.com/Tarmil/FSharp.SystemTextJson), so a type can move from it with no change to its JSON.

## Set up a project

Reference the plugin and the annotations package, and declare one context for an input file:

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad.SystemTextJson" Version="YOUR_VERSION" PrivateAssets="all" />
  <PackageReference Include="Morphir.Myriad.SystemTextJson.Annotations" Version="YOUR_VERSION" />
</ItemGroup>
<ItemGroup>
  <Compile Include="Guide/Types.fs" />
  <FSharpJsonContext Include="Guide/Types.fs" Name="AppJson" Namespace="Guide.Json" Naming="CamelCase" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

The input file must also be a `Compile` item. The build compiles the generated file right after it, so files after the input can use the context. The plugin package carries the Myriad 0.85.0 host; no separate command runs. The annotations package has no dependency, and nothing in it runs: the generator reads the attributes from the source.

A project can declare more than one context, also more than one for the same input file, each with its own name and settings.

## Write and read values

For these types:

```fsharp
type Person = { Name: string; Age: int; Nickname: string option }
```

the context has the member `AppJson.Default.Person`:

```fsharp
let writePerson () =
    JsonSerializer.Serialize({ Name = "Ada"; Age = 36; Nickname = None }, AppJson.Default.Person)
```

`writePerson ()` returns `{"name":"Ada","age":36,"nickname":null}`. `JsonSerializer.Deserialize(json, AppJson.Default.Person)` reads it back.

The context is a normal `IJsonTypeInfoResolver`. To use types from more than one context, combine them. `Verify` checks that the options can resolve every type that the context gets from other contexts, and names the first type that they cannot resolve:

```fsharp
let combine (other: IJsonTypeInfoResolver) =
    let options = JsonSerializerOptions(TypeInfoResolver = JsonTypeInfoResolver.Combine(AppJson.Default, other))
    AppJson.Default.Verify options
    options
```

Call `Verify` in a unit test for each set of options that you use, so that a missing context fails a test and not a production call.

## Context settings

The custom metadata of a `FSharpJsonContext` item are the context settings. An unknown setting stops the build, so a misspelled setting does not go unseen.

| Setting | Values | Default |
| --- | --- | --- |
| `Name` | One F# identifier: the context type | Required |
| `Namespace` | F# identifiers joined by dots | Required |
| `Naming` | `AsIs`, `CamelCase`, `PascalCase`, `SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`, `KebabCaseUpper` | `AsIs` |
| `SkippableOptionFields` | `Always` (leave out `None`), `Never` (write `null`) | `Never` |
| `UnknownMembers` | `Ignore`, `Refuse` | `Ignore` |
| `AllowNullFields` | `true`, `false` | `false` |
| `AllowNumbersFromStrings` | `true`, `false` | `false` |
| `MapFormat` | `ObjectOrArrayOfPairs`, `Object`, `ArrayOfPairs` | `ObjectOrArrayOfPairs` |
| `UnionEncoding` | Base encoding and flags, or a preset, joined by commas | `Default` |
| `UnionTagName` | The name of the case member | `Case` |
| `UnionFieldsName` | The name of the fields member | `Fields` |
| `UnionTagNaming` | A naming value for case names | `AsIs` |
| `UnionFieldNaming` | A naming value for case field names | The `Naming` value |
| `UnionTagCaseInsensitive` | `true`, `false` | `false` |

The defaults are those of FSharp.SystemTextJson.

## Attributes

Attributes on a type, a case or a field override the context settings for that item only.

| Attribute | Where | Use |
| --- | --- | --- |
| `JsonRecord` | Record | `Naming`, `SkippableOptionFields`, `UnknownMembers`, `AllowNullFields` |
| `JsonUnion` | Union | `BaseEncoding`, `TagName`, `FieldsName`, `TagNaming`, `FieldNaming`, `UnknownMembers`, `TagCaseInsensitive` |
| `JsonName` | Field, case | The JSON name. More names are read; the first is written. On a case, `Field = "x"` names the field `x` |
| `JsonPropertyName` | Field | The System.Text.Json attribute, for one name |
| `JsonParsedValue` | Type, abbreviation | A value that is written as text and read only through its parser |
| `JsonIgnoreType` | Type | Leave the type out of the context |
| `JsonDefaultValue` | Record field | Read this JSON literal through the generated field codec when the member is absent |
| `JsonOmitEmpty` | Record collection field | Omit an empty collection on writing |
| `JsonExtensionData` | Record map field | Capture unknown members and write them beside the named members. This is the System.Text.Json attribute |
| `JsonVersionAlias` | Record version field | Normalize an exact legacy member/version pair to the current member and version |
| `JsonKnownPaths` | Record string collection | Accept only the declared exact critical paths |
| `JsonConverter` | Type, field | The System.Text.Json attribute: use this converter |
| `JsonNumberHandling` | Number field | The System.Text.Json attribute: `AllowReadingFromString` for this field |

For example, this record uses snake case and refuses unknown members, and its colour member has two names:

```fsharp
[<JsonRecord(Naming = JsonNaming.SnakeCaseLower, UnknownMembers = JsonUnknownMembers.Refuse)>]
type Settings =
    { MaxItems: int
      [<JsonName("colour", "color")>]
      Colour: string }
```

It is written as `{"max_items":3,"colour":"red"}`, and `{"max_items":3,"color":"red"}` reads the same value.

## Records

A record is a JSON object with one member for each field.

- A field that is not an option is required unless it declares a missing-member default or extension data. A missing required member or disallowed `null` fails.
- An option is `null` for `None`, or left out with `SkippableOptionFields`. A missing member reads as `None`.
- A member that appears twice fails. An unknown member is skipped or refused, as `UnknownMembers` says.
- `AllowNullFields` accepts `null` for a field whose type accepts `null` (string, arrays, `ResizeArray`, read-only interfaces, types from other contexts).

### Defaults and empty collections

The wire-rule annotations below require a generator and annotations package built after `0.4.0-alpha.2`.

These annotations opt into wire rules for a particular record field. Other fields keep their existing required-member rules:

```fsharp
type Diagnostic =
    { Message: string
      [<JsonDefaultValue("[]"); JsonOmitEmpty>]
      Related: string list
      [<JsonDefaultValue("false")>]
      Retryable: bool }
```

An absent `Related` reads as `[]`; an empty `Related` is omitted on writing. An absent `Retryable` reads as false and is still written. `JsonDefaultValue` takes one complete JSON value as a string literal. The generated codec reads it using the field's normal encoding, including parsed-value parsers and union settings. Malformed JSON fails generation; a default of the wrong type fails through that codec when used. Explicit null never selects the default. `JsonOmitEmpty` supports sequences and maps, and does not itself make a missing member optional.

### Open objects

Mark one `Map<string, JsonElement>` or `IReadOnlyDictionary<string, JsonElement>` record field with the System.Text.Json `JsonExtensionData` attribute. Unknown members enter that map and are written directly into the record object. The map is empty when no unknown member is present. There is no wrapper named after the field. Duplicate input members and output keys colliding with named members, their aliases, or an enclosing union tag are refused. Extension data cannot be combined with `UnknownMembers.Refuse` or additional default/omission annotations on the same field.

### Versioned member migration

For a format whose version member changed its name, declare the exact legacy and current values. For example, the MEP claims contract uses:

```fsharp
type Claims =
    { [<JsonVersionAlias("statementVersion", "0.1.0-draft.1", "0.1.0-draft.2", CriticalMember = "critical")>]
      ClaimsVersion: string
      [<JsonDefaultValue("[]"); JsonOmitEmpty; JsonKnownPaths("claimsVersion", "protocolVersions", "capabilities", "capabilities.frontend")>]
      Critical: string list }
```

With camel-case context naming, a reader accepts `statementVersion` only with draft.1 and `claimsVersion` only with draft.2. Both names together fail. Legacy input normalizes to draft.2 and its exact `statementVersion` critical path becomes `claimsVersion`. A legacy list naming `claimsVersion` fails. Writers require the current version and write only `claimsVersion`.

`CriticalMember` is optional and names a JSON member, not an F# field. When set, it must name a string collection with `JsonKnownPaths` that allows the current version-member path and excludes the legacy path. The paths shown above illustrate a subset; declare every path your format understands. Known parents never permit unknown children. The version field can also be a `JsonParsedValue` type; its parser receives the normalized current text. One version alias is allowed per record. These wire annotations apply to record fields.

## Unions

The union encodings and flags have the names and meanings of FSharp.SystemTextJson. For this union:

```text
type Shape =
    | NoArgs
    | OneArg of x: int
    | TwoArgs of a: int * b: string
```

| Encoding | `NoArgs` | `OneArg 1` | `TwoArgs(3, "t")` |
| --- | --- | --- | --- |
| `AdjacentTag` | `{"Case":"NoArgs"}` | `{"Case":"OneArg","Fields":[1]}` | `{"Case":"TwoArgs","Fields":[3,"t"]}` |
| `AdjacentTag, NamedFields` | `{"Case":"NoArgs"}` | `{"Case":"OneArg","Fields":{"x":1}}` | `{"Case":"TwoArgs","Fields":{"a":3,"b":"t"}}` |
| `ExternalTag` | `{"NoArgs":[]}` | `{"OneArg":[1]}` | `{"TwoArgs":[3,"t"]}` |
| `InternalTag` | `["NoArgs"]` | `["OneArg",1]` | `["TwoArgs",3,"t"]` |
| `InternalTag, NamedFields` | `{"Case":"NoArgs"}` | `{"Case":"OneArg","x":1}` | `{"Case":"TwoArgs","a":3,"b":"t"}` |
| `Untagged` | `{}` | `{"x":1}` | `{"a":3,"b":"t"}` |

| Flag | Effect |
| --- | --- |
| `UnwrapFieldlessTags` | A case with no field is the string of its name: `"NoArgs"` |
| `UnwrapSingleFieldCases` | With `AdjacentTag` or `ExternalTag`, one field is written alone: `{"OneArg":1}` |
| `UnwrapRecordCases` | The members of a case's single record field are written in place of the field |
| `UnionFieldNamesFromTypes` | A field with no name is named after its type: `Int32`, `String`, `Point` |
| `UnwrapOption` | `None` is `null` and `Some x` is `x`. Without it, `Some x` is a union case |
| `UnwrapSingleCaseUnions` | A union with one case and one field is written as that field |
| `AllowUnorderedTag` | When reading, the case member can come after the fields |

| Preset | Flags |
| --- | --- |
| `Default` | `AdjacentTag`, `UnwrapOption`, `UnwrapSingleCaseUnions`, `AllowUnorderedTag` |
| `NewtonsoftLike` | `AdjacentTag`, `AllowUnorderedTag` |
| `ThothLike` | `InternalTag`, `UnwrapFieldlessTags`, `AllowUnorderedTag` |
| `FSharpLuLike` | `ExternalTag`, `UnwrapFieldlessTags`, `UnwrapOption`, `UnwrapSingleFieldCases`, `AllowUnorderedTag` |

A `JsonUnion` attribute sets the whole encoding of one union. This union is an object tagged by a `kind` member:

```fsharp
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.NamedFields), TagName = "kind")>]
type Shape =
    | Circle of radius: float
    | Square of side: float
```

`Circle 1.5` is written as `{"kind":"Circle","radius":1.5}`.

An untagged union must let a reader tell its cases apart. The build fails when two cases share a member name, or when two cases have no field and `UnwrapFieldlessTags` is not set.

## Additions to FSharp.SystemTextJson

### Parsed values

A type with a private representation and a parser can be read only through the parser, so a reader never builds an invalid value:

```fsharp
[<JsonParsedValue(TextMember = "Value")>]
type Email =
    private
    | Email of string

    member this.Value = let (Email value) = this in value

    static member Parse(text: string) : Result<Email, string> =
        if text.Contains "@" then Ok(Email text) else Error $"'{text}' is not an e-mail address."
```

- The writer writes the text member (`ToString` by default).
- The reader calls `Parse` (`string -> Result<T, 'E>`), or a C#-style `TryParse(string, out T)` with `UsesTryParse = true`. An error fails with the parser's text.
- A parsed value can be a map key, so `Map<Email, int>` is a JSON object.
- For a type from another assembly, put the attribute on an abbreviation in the input file: `[<JsonParsedValue(TextMember = "Value")>] type ExtensionIdText = ExtensionId`. Every use of `ExtensionId` in the context is then a parsed value.

### Cases chosen by JSON token kind

`SelectByTokenKind` with `Untagged` chooses the case by the kind of the JSON value. Each case has one field, and no two cases have the same kind (string, number, boolean, array or object):

```fsharp
[<JsonUnion(BaseEncoding = (JsonUnionEncoding.Untagged ||| JsonUnionEncoding.SelectByTokenKind))>]
type RequestId =
    | Number of uint64
    | Text of string
```

`18446744073709551615` reads as `Number`, and `"req-1"` reads as `Text`. Each one is written back as the same token.

### Exact numbers

Each integer type is read with its own `TryGet` member, so the full range of `uint64` and `int64` is kept, and a fraction or a value out of range fails. A number in a JSON string is read only with `AllowNumbersFromStrings` on the context or `JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)` on a number field.

## Supported types

- Primitives: `string`, `bool`, all integer types, `float32`, `float`, `decimal`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` and `JsonElement`, in the System.Text.Json formats.
- `option`, `voption`, `list`, arrays, `Set`, `ResizeArray`, `IReadOnlyList`, `IReadOnlyCollection`, `Map`, `IReadOnlyDictionary`, tuples, and nesting of these. A type abbreviation of one of these is a context member.
- Records, unions (also recursive), enums (as numbers), and abbreviations declared in the input file.
- A type from another file or assembly is read and written through the options (`options.GetTypeInfo`), so its context must be in the options.

Generic types that you declare (`type Page<'T> = ...`) are not supported. Declare an abbreviation of a closed type instead. A user type with the name of a built-in type (`Guid`, `Set`, `Single`) is refused, because the generator reads types by their names.

## Use with Native AOT

The generated code has no trim or AOT warning. An application that uses only generated contexts publishes with `PublishAot=true`. FSharp.Core itself gives two summary warnings (IL2104, IL3053) in every F# AOT application.

The F# compiler does not run the trim analyzers, so publish your application with Native AOT in CI to check it. morphir-dotnet does this with a probe that runs every shape and compares the native output with JIT.

## Errors

At build time, the generator reports each problem at its file, line and column, with the code `MSTJ0010`, and stops the build:

```text
Types.fs(3,6): error MSTJ0010: Page: generic types are not supported; declare an abbreviation of a closed type instead.
```

At run time, every failure is a `JsonException`. System.Text.Json adds the path and the position of the value. A failure inside a nested value names each member on the way to it:

```text
In the member 'Head' of Household: In the member 'Age' of Person: A JSON integer in the Int32 range was expected.
```

## Move from FSharp.SystemTextJson

1. Keep your options as context settings: the encoding flags go in `UnionEncoding`, the naming policies in `Naming`, `UnionTagNaming` and `UnionFieldNaming`, and `SkippableOptionFields` and `MapFormat` keep their names.
2. Replace `JsonFSharpConverter` attributes with `JsonUnion` or `JsonRecord`. `JsonName` keeps its meaning.
3. Replace `JsonSerializer.Serialize(value, options)` with the type info overload, `JsonSerializer.Serialize(value, AppJson.Default.Person)`, or set `TypeInfoResolver` on your options.
4. Compare the JSON of your existing tests. For the same options, the JSON is the same; morphir-dotnet checks this for every base encoding, flag and preset.
