# Proposal

## Why

F# types cannot use the System.Text.Json source generator: it reads C# syntax only. So an F# project that must be trimmed or Native AOT-compiled has two choices today: write each JSON codec by hand, or use a reflection-based library (FSharp.SystemTextJson, Thoth.Json.Net auto coders), which is not trim-safe. morphir-dotnet needs JSON for many F# types now: the Morphir Extension Protocol (`add-mep-core`, 56 contract types), the extension registry, IR documents and configuration. Hand-written codecs are slow to write, easy to get wrong and drift from the contract. The broader F# community has the same gap.

## What Changes

- Add a Myriad plugin that is the F# equivalent of the System.Text.Json source generator. From annotated F# records, unions and enums, it generates at build time:
  - a `JsonConverter<T>` for each type that reads and writes with `Utf8JsonReader` and `Utf8JsonWriter`, with no reflection;
  - a context type that derives from `JsonSerializerContext`, with one `JsonTypeInfo<T>` property for each type and a `Default` instance, so callers use the normal `JsonSerializer` calls with type info, and contexts chain with `JsonTypeInfoResolver.Combine`.
- Make the encoding configurable with the vocabulary that F# users already know from FSharp.SystemTextJson: union encodings `AdjacentTag`, `ExternalTag`, `InternalTag` and `Untagged`; the flags `NamedFields`, `UnwrapFieldlessTags`, `UnwrapSingleFieldCases`, `UnwrapSingleCaseUnions`, `UnwrapRecordCases`, `UnwrapOption`, `AllowUnorderedTag` and `UnionFieldNamesFromTypes`, and case-insensitive tags; the presets `Default`, `NewtonsoftLike`, `ThothLike` and `FSharpLuLike`; tag and fields names; naming policies; skippable option fields; map formats; and per-case and per-field names. For the same options, the output equals FSharp.SystemTextJson output.
- Add what trimmed, parse-don't-validate code also needs:
  - parsed values: a type with a private representation is written through its text member and read through its `TryParse`, so a decoder never builds an invalid value;
  - untagged unions chosen by JSON token kind (for example a request id that is a number or a string, or an untyped JSON value);
  - exact `uint64` and `int64` numbers;
  - required members, refusal of `null` for non-option members, a duplicate-member rule and an unknown-member rule (ignore or refuse);
  - a converter that the user names for one type or member, for shapes that no option covers.
- Configure defaults for a context in the project file (MSBuild item metadata) and override them per type, case or member with attributes. The attributes come from a small package with no dependencies.
- Generate at build time, as `Morphir.Myriad` does, through the same packaged Myriad host. Projects in this repository use the plugin from its build output; external users use the NuGet package.
- Name the packages `Morphir.Myriad.SystemTextJson` and `Morphir.Myriad.SystemTextJson.Annotations`, and the Myriad generator `morphir-system-text-json`.
- Publish the two new packages and the existing `Morphir.Myriad` package to NuGet.org with each morphir-dotnet release. Today `Morphir.Myriad` goes only into a local feed. Its documentation and the car rental example move to the published package.
- Remove the stub `json-codec` generator and its `GenerateJsonCodec` attribute and `JsonCodecConfig` from `src/Morphir.Internal.CodeGeneration`. They return empty output and no project uses them.
- Use the generator first for the MEP types in `add-mep-core`, which no longer writes payload codecs by hand.

## Capabilities

### New Capabilities

- `myriad-json-generation`: build-time generation of trim-safe and AOT-safe System.Text.Json converters and contexts for F# types, with configurable encodings, through a Myriad plugin package.

### Modified Capabilities

- `myriad-model-generation`: the `Morphir.Myriad` package is published to NuGet.org with each release, and its documentation uses the published package. Its generator requirements do not change. Its scenario "Existing generators remain separate" refers to the internal generators; the stub `json-codec` generator leaves that set.

## Tracking

- Issue: [#511](https://github.com/finos/morphir-dotnet/issues/511).
- Related: the extension model PRD, step 1 (`add-mep-core`), which depends on this change.

## Impact

- New projects: `src/Morphir.Myriad.SystemTextJson` (the plugin and its build assets), `src/Morphir.Myriad.SystemTextJson.Annotations` (attributes and options types, no dependencies), `tests/Morphir.Myriad.SystemTextJson.Tests`, and an AOT probe.
- The packaged Myriad host and the coordinator of `Morphir.Myriad` become shared build infrastructure for both plugins.
- Test dependency: FSharp.SystemTextJson, in tests only, as the compatibility reference for the shared encodings.
- `src/Morphir.Internal.CodeGeneration`: the stub generator, attribute and configuration are removed; `IMPLEMENTATION_STATUS.md` points to the new plugin.
- `add-mep-core`: its payload codecs come from this generator. Only the JSON-RPC envelope classification stays hand-written.
- Documentation: a guide for the plugin, with the encoding table and the AOT setup.
- Release: two new NuGet packages and the first NuGet.org publication of `Morphir.Myriad`, with each morphir-dotnet release through `PackLibs`, `PublishLibs` and `deployment.yml`, with a package layout check for Myriad plugin packages.
- `Morphir.Myriad` documentation: the getting-started article, the car rental example and its README use the release version from NuGet.org instead of the local feed.
