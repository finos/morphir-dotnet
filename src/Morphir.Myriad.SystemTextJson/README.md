# Morphir.Myriad.SystemTextJson

Generate trim-safe and Native AOT-safe System.Text.Json converters and a `JsonSerializerContext` for F# records, unions and enums at build time. It is the F# counterpart of the System.Text.Json source generator, which reads only C#.

```xml
<ItemGroup>
  <PackageReference Include="Morphir.Myriad.SystemTextJson" Version="YOUR_VERSION" PrivateAssets="all" />
  <PackageReference Include="Morphir.Myriad.SystemTextJson.Annotations" Version="YOUR_VERSION" />
</ItemGroup>
<ItemGroup>
  <Compile Include="Types.fs" />
  <FSharpJsonContext Include="Types.fs" Name="AppJson" Namespace="MyApp.Json" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

The build generates `AppJson`, compiled right after `Types.fs`. Use it with the normal System.Text.Json API:

```fsharp
let json = JsonSerializer.Serialize(person, AppJson.Default.Person)
```

The package carries the Myriad 0.85.0 host and runs it during the build. No separate command runs. A generation error stops the build and names the file, line and column.

- **No reflection.** The generated code publishes with Native AOT with no trim or AOT warning.
- **FSharp.SystemTextJson encodings.** `AdjacentTag`, `ExternalTag`, `InternalTag` and `Untagged`, the unwrap flags and the presets give the same JSON as FSharp.SystemTextJson.
- **Parse, don't validate.** A `JsonParsedValue` type is read only through its parser.
- **Exact numbers, member rules, user converters,** and cases chosen by JSON token kind.

See the [guide](https://finos.github.io/morphir-dotnet/docs/guides/myriad-json/) for the settings, the attributes and the union encodings. The bundled third-party components and their licenses are listed in `ThirdPartyNotices.txt`.
